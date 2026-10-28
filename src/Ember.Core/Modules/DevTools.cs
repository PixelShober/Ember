using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ember.Core;

/// <summary>What a vendor currently offers: version, download and (where published) checksum.</summary>
public sealed record DevToolRelease(string Version, string Url, string FileName, string? Sha256 = null, string? Sha512 = null);

/// <param name="Method">"sccm" (IT deploys it in Software Center), "direct" (from the vendor) or "mail" (only via the service center).</param>
public sealed record DevToolState(
    string Id, string Name, string Description, string? InstalledVersion, string? InstalledName,
    SccmItem? Sccm, string Method, bool NeedsAdmin);

/// <summary>
/// The developer stack Ember sets up: Node.js LTS (npm), Git for Windows, VS Code, Python,
/// Visual Studio, .NET SDK and PowerShell 7. If IT deploys a tool in Software Center, that
/// package is used. Otherwise Ember downloads the official installer through px, checks the
/// published checksum and the vendor's signature, and installs per user where the vendor
/// allows it (Node.js, Git, VS Code, Python). Visual Studio, the .NET SDK and PowerShell 7
/// need admin rights (UAC).
/// </summary>
public static class DevTools
{
    sealed record Recipe(
        string Id, string Name, string[] DisplayNames, string[] SccmNames, bool NeedsAdmin, string? Signer,
        Func<HttpClient, TeamConfig, CancellationToken, Task<DevToolRelease>> Resolve,
        Func<string, DevToolRelease, TeamConfig, SetupStep, CancellationToken, Task> Install);

    const int InstallTimeoutSec = 2 * 60 * 60;   // Visual Studio can take a long time
    static readonly TimeSpan LatestCacheTime = TimeSpan.FromHours(1);
    static readonly Dictionary<string, (DateTime At, DevToolRelease Release)> LatestCache = new();

    static string NodeDir => Path.Combine(Paths.ToolsDir, "node");
    static string NpmGlobalDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");

    static readonly Recipe[] All =
    {
        new("nodejs", "Node.js LTS (npm)", new[] { "Node.js*" }, new[] { "Node.js*", "NodeJS*" }, false, null, NodeReleaseAsync, InstallNodeAsync),
        new("git", "Git for Windows", new[] { "Git", "Git version *" }, new[] { "Git for Windows*", "Git 2*", "Git" }, false, "Johannes Schindelin", GitReleaseAsync, InstallGitAsync),
        new("vscode", "Visual Studio Code", new[] { "Microsoft Visual Studio Code*" }, new[] { "*Visual Studio Code*" }, false, "Microsoft Corporation", VsCodeReleaseAsync, InstallVsCodeAsync),
        new("python", "Python 3", new[] { "Python 3.*(64-bit)" }, new[] { "Python 3*", "Python" }, false, "Python Software Foundation", PythonReleaseAsync, InstallPythonAsync),
        new("visualstudio", "Visual Studio Professional", new[] { "Visual Studio Professional 20*", "Visual Studio Enterprise 20*", "Visual Studio Community 20*" }, new[] { "*Visual Studio*Professional*" }, true, "Microsoft Corporation", VisualStudioReleaseAsync, InstallVisualStudioAsync),
        new("dotnet", ".NET SDK (LTS)", new[] { "Microsoft .NET SDK *" }, new[] { "*.NET SDK*", "*NET Core SDK*" }, true, "Microsoft Corporation", DotnetReleaseAsync, InstallDotnetAsync),
        new("pwsh", "PowerShell 7", new[] { "PowerShell 7*" }, new[] { "PowerShell 7*" }, true, "Microsoft Corporation", PwshReleaseAsync, InstallPwshAsync),
    };

    public static IReadOnlyList<string> Ids => All.Select(r => r.Id).ToList();

    static IEnumerable<Recipe> Enabled(TeamConfig team)
    {
        var wanted = team.DevTools?.Tools;
        return wanted is { Count: > 0 } ? All.Where(r => wanted.Contains(r.Id, StringComparer.OrdinalIgnoreCase)) : All;
    }

    static Recipe Get(string id) =>
        All.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new EmberException("unknownAction", ("action", id));

    public static string NameOf(string id) => Get(id).Name;

    /// <summary>How the tool shows up in "Programs and Features" (for the Software Center progress check).</summary>
    public static string DisplayPattern(string id) => Get(id).DisplayNames[0];

    // ------------------------------------------------------------------ state

    public static List<DevToolState> States(TeamConfig team, List<InstalledApp> installed, IReadOnlyList<SccmItem> sccm, bool clientPresent)
    {
        bool allowDirect = team.DevTools?.AllowDirect ?? true;
        return Enabled(team).Select(r =>
        {
            var app = FindInstalled(r, installed);
            var package = clientPresent
                ? sccm.FirstOrDefault(x => x.Kind == "package" && x.PackageId != null && x.ProgramId != null && r.SccmNames.Any(p => InstalledSoftware.Matches(x.Name, p)))
                : null;
            var method = package != null ? "sccm" : allowDirect ? "direct" : "mail";
            return new DevToolState(r.Id, r.Name, L.T($"devtools.{r.Id}.description"), app == null ? null : VersionOf(app), app?.Name, package, method,
                                    method == "direct" && NeedsAdmin(r));
        }).ToList();
    }

    /// <summary>
    /// The product version. Python and the .NET SDK store build numbers as DisplayVersion
    /// ("3.13.1150.0" for Python 3.13.1), so a version in the display name wins.
    /// </summary>
    internal static string? VersionOf(InstalledApp app)
    {
        var inName = Regex.Match(app.Name, @"\b(\d+\.\d+\.\d+)\b");
        return inName.Success ? inName.Groups[1].Value : app.Version;
    }

    internal static InstalledApp? FindInstalled(string id, List<InstalledApp> installed) => FindInstalled(Get(id), installed);

    static InstalledApp? FindInstalled(Recipe r, List<InstalledApp> installed)
    {
        var app = installed.Where(a => r.DisplayNames.Any(p => InstalledSoftware.Matches(a.Name, p)))
                           .OrderByDescending(a => a.Version ?? "0", Comparer<string>.Create(AppInfo.CompareVersions))
                           .FirstOrDefault();
        if (app != null || r.Id != "nodejs") return app;
        // Node.js installed by Ember (zip) has no Uninstall entry.
        var exe = Path.Combine(NodeDir, "node.exe");
        if (!File.Exists(exe)) return null;
        var version = FileVersionInfo.GetVersionInfo(exe).ProductVersion?.TrimStart('v');
        return new InstalledApp("Node.js (C:\\Ember\\Tools\\node)", version, "OpenJS Foundation");
    }

    /// <summary>Git installed for all users (Program Files) can only be updated with admin rights.</summary>
    static bool NeedsAdmin(Recipe r) =>
        r.NeedsAdmin ||
        (r.Id == "git" && Tools.Git is { } git && git.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase));

    /// <summary>The vendor's current version (cached for an hour, so opening the page does not hammer the proxy).</summary>
    public static async Task<DevToolRelease> LatestAsync(string id, EffectiveSettings eff, TeamConfig team, CancellationToken ct)
    {
        lock (LatestCache)
        {
            if (LatestCache.TryGetValue(id, out var hit) && DateTime.UtcNow - hit.At < LatestCacheTime) return hit.Release;
        }
        var (http, _) = await Net.SmartClientAsync(eff, TimeSpan.FromSeconds(60));
        using (http)
        {
            var release = await Get(id).Resolve(http, team, ct);
            lock (LatestCache) LatestCache[id] = (DateTime.UtcNow, release);
            return release;
        }
    }

    // ------------------------------------------------------------------ install

    /// <summary>Download, verify, install, check – for one tool straight from the vendor.</summary>
    public static List<SetupStep> DirectSteps(string id, EffectiveSettings eff, TeamConfig team, string? titlePrefix = null)
    {
        if (team.DevTools?.AllowDirect == false) throw new EmberException("devToolDirectDisabled");
        var r = Get(id);
        HttpClient? http = null;
        DevToolRelease? release = null;
        string file = "";
        string Title(string key) => (titlePrefix != null ? titlePrefix + ": " : "") + L.T(key);

        return new List<SetupStep>
        {
            new(Title("devtools.steps.resolve"), async (step, ct) =>
            {
                step.SetProgress(null);
                var (client, route) = await Net.SmartClientAsync(eff, TimeSpan.FromMinutes(60));
                http = client;
                release = await r.Resolve(http, team, ct);
                lock (LatestCache) LatestCache[id] = (DateTime.UtcNow, release);
                step.Report($"{r.Name} {release.Version}: {release.FileName} ({route})");
            }),
            new(Title("devtools.steps.download"), async (step, ct) =>
            {
                file = Path.Combine(Paths.Ensure(Paths.DownloadDir), release!.FileName);
                await Net.DownloadAsync(http!, release.Url, file, (done, total) =>
                {
                    step.Report(total > 0 ? L.T("jobs.downloadProgress", ("done", Net.Mb(done)), ("total", Net.Mb(total.Value))) : Net.Mb(done), log: false);
                    step.SetProgress(total > 0 ? 100.0 * done / total.Value : null);
                }, ct, resume: true);
                step.Report($"{release.FileName} · {Net.Mb(new FileInfo(file).Length)}");
            }),
            new(Title("devtools.steps.verify"), async (step, ct) =>
            {
                var parts = new List<string>();
                if (release!.Sha256 is { Length: > 0 } sha256)
                {
                    var actual = await Net.Sha256Async(file, ct);
                    if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase)) Reject("shaMismatch", ("expected", sha256), ("actual", actual));
                    parts.Add("SHA-256 " + actual[..12] + "…");
                }
                else if (release.Sha512 is { Length: > 0 } sha512)
                {
                    var actual = await Sha512Async(file, ct);
                    if (!actual.Equals(sha512, StringComparison.OrdinalIgnoreCase)) Reject("shaMismatch", ("expected", sha512[..16] + "…"), ("actual", actual[..16] + "…"));
                    parts.Add("SHA-512 " + actual[..12] + "…");
                }
                // The zip of Node.js is not signed as a whole; its checksum comes from nodejs.org over HTTPS.
                if (!file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var signer = Authenticode.Signer(file);
                    if (signer == null) Reject("devToolUnsigned", ("file", release.FileName));
                    if (r.Signer != null && !signer!.Contains(r.Signer, StringComparison.OrdinalIgnoreCase))
                        Reject("devToolSigner", ("file", release.FileName), ("signer", signer), ("expected", r.Signer));
                    parts.Add(L.T("devtools.signedBy", ("signer", signer!)));
                }
                if (parts.Count == 0) parts.Add(L.T("devtools.noChecksum"));
                step.Report(string.Join(" · ", parts));

                void Reject(string code, params (string, object?)[] args)
                {
                    try { File.Delete(file); } catch { }
                    throw new EmberException(code, args);
                }
            }),
            new(Title("devtools.steps.install"), async (step, ct) =>
            {
                step.SetProgress(null);
                if (NeedsAdmin(r)) step.Report(L.T("devtools.uacHint"), log: false);
                await r.Install(file, release!, team, step, ct);
                try { File.Delete(file); } catch { }
            }),
            new(Title("devtools.steps.check"), async (step, ct) =>
            {
                await Task.Delay(1500, ct);   // installers write their Uninstall entry at the very end
                var app = FindInstalled(r, InstalledSoftware.Read());
                if (app == null) throw new EmberException("devToolNotFound", ("tool", r.Name));
                step.Report(L.T("devtools.installed", ("name", app.Name), ("version", app.Version ?? "?")));
                http?.Dispose();
            }),
        };
    }

    static async Task<string> Sha512Async(string file, CancellationToken ct)
    {
        await using var fs = File.OpenRead(file);
        return Convert.ToHexString(await System.Security.Cryptography.SHA512.HashDataAsync(fs, ct)).ToLowerInvariant();
    }

    /// <summary>Runs an installer as the user (no UAC) or with admin rights; 3010/1641 mean "done, restart later".</summary>
    static async Task RunInstallerAsync(string file, string arguments, bool admin, string tool, SetupStep step, CancellationToken ct)
    {
        int code;
        if (admin)
        {
            code = await Elevation.RunAsync(file, arguments, ct, InstallTimeoutSec);
        }
        else
        {
            var psi = new ProcessStartInfo(file) { Arguments = arguments, UseShellExecute = false, CreateNoWindow = true };
            Log.Debug($"> {Path.GetFileName(file)} {arguments}");
            using var process = Process.Start(psi) ?? throw new EmberException("processStart", ("file", file));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(InstallTimeoutSec));
            await process.WaitForExitAsync(cts.Token);
            code = process.ExitCode;
            Log.Debug($"  exit code {code}");
        }
        if (code is 3010 or 1641) { step.Report(L.T("devtools.reboot")); return; }
        if (code != 0) throw new EmberException("devToolExit", ("tool", tool), ("code", code));
    }

    // ------------------------------------------------------------------ Node.js LTS (zip, per user)

    static async Task<DevToolRelease> NodeReleaseAsync(HttpClient http, TeamConfig team, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync("https://nodejs.org/dist/index.json", ct));
        var rel = NodeLts(doc.RootElement) ?? throw new EmberException("devToolNoRelease", ("tool", "Node.js"));
        var version = rel.GetProperty("version").GetString()!;
        var file = $"node-{version}-win-x64.zip";
        var baseUrl = $"https://nodejs.org/dist/{version}/";
        var sha = ShaFromSums(await http.GetStringAsync(baseUrl + "SHASUMS256.txt", ct), file)
                  ?? throw new EmberException("devToolNoChecksum", ("file", file));
        return new DevToolRelease(version.TrimStart('v'), baseUrl + file, file, sha);
    }

    /// <summary>Newest entry of nodejs.org/dist/index.json that is an LTS line and has a Windows zip.</summary>
    internal static JsonElement? NodeLts(JsonElement index)
    {
        foreach (var e in index.EnumerateArray())
            if (e.TryGetProperty("lts", out var lts) && lts.ValueKind == JsonValueKind.String &&
                e.TryGetProperty("files", out var files) && files.EnumerateArray().Any(f => f.GetString() == "win-x64-zip"))
                return e;
        return null;
    }

    /// <summary>The hash for one file from a SHASUMS256.txt ("hash  file" per line).</summary>
    internal static string? ShaFromSums(string sums, string file) =>
        sums.Split('\n').Select(l => l.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(p => p.Length == 2 && p[1].TrimStart('*').Equals(file, StringComparison.OrdinalIgnoreCase))?[0].ToLowerInvariant();

    static async Task InstallNodeAsync(string zip, DevToolRelease r, TeamConfig team, SetupStep step, CancellationToken ct)
    {
        var temp = Path.Combine(Paths.Ensure(Paths.ToolsDir), $".node-{Guid.NewGuid():N}");
        await Task.Run(() => Zip.ExtractSafe(zip, temp, ct), ct);
        var inner = Directory.GetDirectories(temp).FirstOrDefault(d => File.Exists(Path.Combine(d, "node.exe")))
                    ?? throw new EmberException("devToolNotFound", ("tool", "node.exe"));

        if (Directory.Exists(NodeDir))
        {
            var old = NodeDir + ".old-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try
            {
                Directory.Move(NodeDir, old);
            }
            catch (IOException)
            {
                Directory.Delete(temp, recursive: true);
                throw new EmberException("nodeInUse", ("path", NodeDir));
            }
            try { Directory.Delete(old, recursive: true); } catch { /* removed on the next update */ }
        }
        Directory.Move(inner, NodeDir);
        try { Directory.Delete(temp, recursive: true); } catch { }

        // Like the official MSI: global packages go to %APPDATA%\npm, so they survive Node updates.
        Directory.CreateDirectory(NpmGlobalDir);
        var builtin = Path.Combine(NodeDir, "node_modules", "npm", "npmrc");
        if (Directory.Exists(Path.GetDirectoryName(builtin)))
            await File.WriteAllTextAsync(builtin, "prefix=${APPDATA}\\npm\n", ct);

        UserEnvironment.AddToPath(NodeDir, NpmGlobalDir);
        step.Report(L.T("devtools.nodeReady", ("path", NodeDir)));
    }

    // ------------------------------------------------------------------ Git for Windows (per user unless installed for all users)

    static async Task<DevToolRelease> GitReleaseAsync(HttpClient http, TeamConfig team, CancellationToken ct)
    {
        var rel = await Net.GetLatestReleaseAsync(http, "git-for-windows/git", ct);
        var url = rel.AssetUrls.FirstOrDefault(u => Regex.IsMatch(Net.FileName(u), @"^Git-[\d.]+-64-bit\.exe$", RegexOptions.IgnoreCase))
                  ?? throw new EmberException("devToolNoRelease", ("tool", "Git for Windows"));
        var file = Net.FileName(url);
        var version = Regex.Match(file, @"^Git-([\d.]+)-64-bit", RegexOptions.IgnoreCase).Groups[1].Value;
        return new DevToolRelease(version, url, file, rel.Sha256?.GetValueOrDefault(file));
    }

    static Task InstallGitAsync(string file, DevToolRelease r, TeamConfig team, SetupStep step, CancellationToken ct)
    {
        bool admin = NeedsAdmin(Get("git"));
        var args = "/VERYSILENT /NORESTART /NOCANCEL /SP- /SUPPRESSMSGBOXES" + (admin ? "" : " /CURRENTUSER");
        return RunInstallerAsync(file, args, admin, "Git for Windows", step, ct);
    }

    // ------------------------------------------------------------------ VS Code (user setup)

    static async Task<DevToolRelease> VsCodeReleaseAsync(HttpClient http, TeamConfig team, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync("https://update.code.visualstudio.com/api/update/win32-x64-user/stable/latest", ct));
        var root = doc.RootElement;
        var url = root.Str("url") ?? throw new EmberException("devToolNoRelease", ("tool", "VS Code"));
        var version = root.Str("productVersion") ?? root.Str("name") ?? "?";
        return new DevToolRelease(version, url, Net.FileName(url), root.Str("sha256hash"));
    }

    static Task InstallVsCodeAsync(string file, DevToolRelease r, TeamConfig team, SetupStep step, CancellationToken ct) =>
        RunInstallerAsync(file, "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /MERGETASKS=!runcode,addcontextmenufiles,addcontextmenufolders,associatewithfiles,addtopath",
                          admin: false, "VS Code", step, ct);

    // ------------------------------------------------------------------ Python (per user)

    /// <summary>
    /// The newest stable version comes from the official "python" package on nuget.org (a stable
    /// JSON list); the installer itself from python.org. Versions without a Windows installer are skipped.
    /// </summary>
    static async Task<DevToolRelease> PythonReleaseAsync(HttpClient http, TeamConfig team, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.nuget.org/v3-flatcontainer/python/index.json", ct));
        foreach (var version in StableVersions(doc.RootElement.GetProperty("versions")).Take(12))
        {
            var file = $"python-{version}-amd64.exe";
            var url = $"https://www.python.org/ftp/python/{version}/{file}";
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await http.SendAsync(head, ct);
            if (response.IsSuccessStatusCode) return new DevToolRelease(version, url, file);
        }
        throw new EmberException("devToolNoRelease", ("tool", "Python"));
    }

    /// <summary>Stable versions (no "-beta" etc.), newest first.</summary>
    internal static IEnumerable<string> StableVersions(JsonElement versions) =>
        versions.EnumerateArray().Select(v => v.GetString() ?? "")
                .Where(v => Regex.IsMatch(v, @"^\d+\.\d+\.\d+$"))
                .OrderByDescending(v => v, Comparer<string>.Create(AppInfo.CompareVersions));

    static Task InstallPythonAsync(string file, DevToolRelease r, TeamConfig team, SetupStep step, CancellationToken ct) =>
        RunInstallerAsync(file, "/quiet InstallAllUsers=0 PrependPath=1 Include_test=0 Include_launcher=1 InstallLauncherAllUsers=0 SimpleInstall=1",
                          admin: false, "Python", step, ct);

    // ------------------------------------------------------------------ Visual Studio (admin)

    public const string DefaultVisualStudioBootstrapper = "https://aka.ms/vs/17/release/vs_professional.exe";

    public static readonly string[] DefaultWorkloads =
    {
        "Microsoft.VisualStudio.Workload.ManagedDesktop",
        "Microsoft.VisualStudio.Workload.NetWeb",
    };

    static Task<DevToolRelease> VisualStudioReleaseAsync(HttpClient http, TeamConfig team, CancellationToken ct)
    {
        var url = team.DevTools?.VisualStudio?.BootstrapperUrl is { Length: > 0 } u ? u : DefaultVisualStudioBootstrapper;
        return Task.FromResult(new DevToolRelease(L.T("devtools.channel"), url, "vs_professional.exe"));
    }

    static Task InstallVisualStudioAsync(string file, DevToolRelease r, TeamConfig team, SetupStep step, CancellationToken ct)
    {
        var workloads = team.DevTools?.VisualStudio?.Workloads is { Count: > 0 } w ? w : DefaultWorkloads.ToList();
        var args = "--passive --norestart --wait --includeRecommended " + string.Join(" ", workloads.Select(x => "--add " + x));
        step.Report(L.T("devtools.visualstudioHint"), log: false);
        return RunInstallerAsync(file, args, admin: true, "Visual Studio", step, ct);
    }

    // ------------------------------------------------------------------ .NET SDK LTS (admin)

    static async Task<DevToolRelease> DotnetReleaseAsync(HttpClient http, TeamConfig team, CancellationToken ct)
    {
        using var index = JsonDocument.Parse(await http.GetStringAsync("https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json", ct));
        var channel = index.RootElement.GetProperty("releases-index").EnumerateArray()
            .Where(c => c.Str("release-type") == "lts" && c.Str("support-phase") == "active")
            .OrderByDescending(c => c.Str("channel-version") ?? "0", Comparer<string>.Create(AppInfo.CompareVersions))
            .Select(c => c.Str("releases.json"))
            .FirstOrDefault() ?? throw new EmberException("devToolNoRelease", ("tool", ".NET SDK"));

        using var releases = JsonDocument.Parse(await http.GetStringAsync(channel, ct));
        return DotnetSdk(releases.RootElement) ?? throw new EmberException("devToolNoRelease", ("tool", ".NET SDK"));
    }

    /// <summary>The latest SDK installer (win-x64 .exe) from a channel's releases.json.</summary>
    internal static DevToolRelease? DotnetSdk(JsonElement releasesJson)
    {
        var latest = releasesJson.Str("latest-sdk");
        foreach (var release in releasesJson.GetProperty("releases").EnumerateArray())
        {
            var sdks = new List<JsonElement>();
            if (release.TryGetProperty("sdk", out var sdk)) sdks.Add(sdk);
            if (release.TryGetProperty("sdks", out var list)) sdks.AddRange(list.EnumerateArray());
            foreach (var s in sdks.Where(s => latest == null || s.Str("version") == latest))
            {
                if (!s.TryGetProperty("files", out var files)) continue;
                var exe = files.EnumerateArray().FirstOrDefault(f => f.Str("name") == "dotnet-sdk-win-x64.exe");
                if (exe.ValueKind != JsonValueKind.Object || exe.Str("url") is not { } url) continue;
                var version = s.Str("version") ?? latest ?? "?";
                return new DevToolRelease(version, url, $"dotnet-sdk-{version}-win-x64.exe", Sha512: exe.Str("hash"));
            }
        }
        return null;
    }

    static Task InstallDotnetAsync(string file, DevToolRelease r, TeamConfig team, SetupStep step, CancellationToken ct) =>
        RunInstallerAsync(file, "/install /quiet /norestart", admin: true, ".NET SDK", step, ct);

    // ------------------------------------------------------------------ PowerShell 7 (admin, MSI)

    static async Task<DevToolRelease> PwshReleaseAsync(HttpClient http, TeamConfig team, CancellationToken ct)
    {
        var rel = await Net.GetLatestReleaseAsync(http, "PowerShell/PowerShell", ct);
        var url = rel.AssetUrls.FirstOrDefault(u => Regex.IsMatch(Net.FileName(u), @"^PowerShell-[\d.]+-win-x64\.msi$", RegexOptions.IgnoreCase))
                  ?? throw new EmberException("devToolNoRelease", ("tool", "PowerShell 7"));
        var file = Net.FileName(url);
        var version = Regex.Match(file, @"^PowerShell-([\d.]+)-", RegexOptions.IgnoreCase).Groups[1].Value;
        return new DevToolRelease(version, url, file, rel.Sha256?.GetValueOrDefault(file));
    }

    static Task InstallPwshAsync(string file, DevToolRelease r, TeamConfig team, SetupStep step, CancellationToken ct) =>
        RunInstallerAsync(ProcessRunner.SystemExe("msiexec.exe"),
                          $"/i \"{file}\" /quiet /norestart ADD_PATH=1 REGISTER_MANIFEST=1 ENABLE_PSREMOTING=0 USE_MU=0 ENABLE_MU=0",
                          admin: true, "PowerShell 7", step, ct);
}

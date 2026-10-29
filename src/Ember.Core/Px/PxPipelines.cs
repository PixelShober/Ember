using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Ember.Core;

/// <summary>State shared by the px installation steps.</summary>
public sealed class PxContext : IDisposable
{
    public PxContext(EffectiveSettings settings) => Settings = settings;

    public EffectiveSettings Settings { get; }
    public string? PacUrl { get; set; }
    public HttpClient? Http { get; set; }
    public ReleaseInfo? Release { get; set; }
    public string? ZipUrl { get; set; }
    public string? ShaUrl { get; set; }
    public string? ZipPath { get; set; }
    public string? PxExe { get; set; }

    public HttpClient RequireHttp() => Http ?? throw new EmberException("noHttpClient");

    public void Dispose() => Http?.Dispose();
}

/// <summary>The step sequences for installing and uninstalling px.</summary>
public static class PxPipelines
{
    const string PxRepo = "genotrance/px";

    static string Disabled => L.T("jobs.disabledInSettings");

    static string? UsesLocalZip(PxContext c) =>
        string.IsNullOrWhiteSpace(c.Settings.LocalZip) ? null : L.T("px.localZipUsed");

    public static List<SetupStep> Install(PxContext c) => new()
    {
        Step("px.steps.detectProxy", (s, ct) => DetectProxy(c, s, ct)),
        Step("px.steps.resolveRelease", (s, ct) => ResolveRelease(c, s, ct), () => UsesLocalZip(c)),
        Step("px.steps.download", (s, ct) => Download(c, s, ct), () => UsesLocalZip(c)),
        Step("px.steps.verifyHash", (s, ct) => VerifyHash(c, s, ct),
             () => UsesLocalZip(c) ?? (c.ShaUrl == null ? L.T("px.noShaFile") : null)),
        Step("px.steps.stop", (s, ct) => StopPx(c, s, ct)),
        Step("px.steps.extract", (s, ct) => Extract(c, s, ct)),
        Step("px.steps.writeConfig", (s, ct) => WriteConfig(c, s, ct)),
        Step("px.steps.autostart", (s, ct) => SetupAutostart(c, s, ct), () => c.Settings.Autostart ? null : Disabled),
        Step("px.steps.start", (s, ct) => StartPx(c, s, ct)),
        Step("px.steps.test", (s, ct) => TestConnection(c, s, ct)),
        Step("px.steps.envVars", (s, ct) => Tool("env", c, s, ct), () => c.Settings.ConfigureEnvVars ? null : Disabled),
        Step("px.steps.git", (s, ct) => Tool("git", c, s, ct),
             () => !c.Settings.ConfigureGit ? Disabled : Tools.Git == null ? L.T("proxy.toolNotFound", ("tool", "git")) : null),
        Step("px.steps.npm", (s, ct) => Tool("npm", c, s, ct),
             () => !c.Settings.ConfigureNpm ? Disabled : Tools.Npm == null ? L.T("proxy.toolNotFound", ("tool", "npm")) : null),
        Step("px.steps.nuget", (s, ct) => Tool("nuget", c, s, ct), () => c.Settings.ConfigureNuGet ? null : Disabled),
        Step("px.steps.pip", (s, ct) => Tool("pip", c, s, ct), () => c.Settings.ConfigurePip ? null : Disabled),
    };

    public static List<SetupStep> Uninstall(PxContext c) => new()
    {
        Step("px.steps.stop", (s, ct) => StopPx(c, s, ct)),
        Step("px.steps.removeAutostart", (s, ct) => RemoveAutostart(c, s, ct),
             () => PxControl.FindExe(c.Settings.InstallDir, "px.exe") == null ? L.T("px.notFound") : null),
        Step("px.steps.removeEnvVars", (s, ct) => Untool("env", c, s, ct)),
        Step("px.steps.removeGit", (s, ct) => Untool("git", c, s, ct), () => Tools.Git == null ? L.T("proxy.toolNotFound", ("tool", "git")) : null),
        Step("px.steps.removeNpm", (s, ct) => Untool("npm", c, s, ct), () => Tools.Npm == null ? L.T("proxy.toolNotFound", ("tool", "npm")) : null),
        Step("px.steps.removeNuget", (s, ct) => Untool("nuget", c, s, ct)),
        Step("px.steps.removePip", (s, ct) => Untool("pip", c, s, ct)),
        Step("px.steps.removeVscode", (s, ct) => Untool("vscode", c, s, ct), () => File.Exists(Tools.VsCodeSettings) ? null : L.T("proxy.toolNotFound", ("tool", "VS Code"))),
        Step("px.steps.deleteFiles", (s, ct) => DeleteFiles(c, s, ct)),
    };

    static SetupStep Step(string key, Func<SetupStep, CancellationToken, Task> action, Func<string?>? skip = null) =>
        new(L.T(key), action, skip);

    // ======================================================================= installation

    static async Task DetectProxy(PxContext c, SetupStep step, CancellationToken ct)
    {
        var s = c.Settings;
        c.PacUrl = string.IsNullOrWhiteSpace(s.PacUrl) ? Net.ReadPacFromRegistry() : s.PacUrl.Trim();
        Log.Info(L.T("px.pacFile", ("value", c.PacUrl ?? L.T("px.noneFound"))));

        if (s.UsePac && c.PacUrl == null) throw new EmberException("pacMissing");

        if (s.UpstreamHostPort.Length > 0)
        {
            var parts = s.UpstreamHostPort.Split(':');
            if (!await Net.PortOpenAsync(parts[0], int.Parse(parts[1]), 3000))
                throw new EmberException("proxyUnreachable", ("proxy", s.UpstreamHostPort));
            Log.Info(L.T("px.tcpOk", ("proxy", s.UpstreamHostPort)));
        }

        c.Http?.Dispose();
        c.Http = Net.CreateClient(Net.UpstreamProxy(s), TimeSpan.FromMinutes(10));

        var download = s.UpstreamHostPort.Length > 0 ? s.UpstreamHostPort : L.T("net.systemProxy");
        var px = s.UsePac ? "PAC " + c.PacUrl : "Proxy " + s.UpstreamHostPort;
        step.Report(L.T("px.routeSummary", ("download", download), ("px", px)));
    }

    static async Task ResolveRelease(PxContext c, SetupStep step, CancellationToken ct)
    {
        step.SetProgress(null);
        var release = await Net.GetLatestReleaseAsync(c.RequireHttp(), PxRepo, ct);
        c.Release = release;

        c.ZipUrl = release.AssetUrls
            .Where(u => Regex.IsMatch(Net.FileName(u), @"windows.*\.zip$", RegexOptions.IgnoreCase))
            .Where(u => !Net.FileName(u).Contains("wheels", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(u => Regex.IsMatch(Net.FileName(u), "amd64|x86_64|x64", RegexOptions.IgnoreCase))
            .FirstOrDefault()
            ?? throw new EmberException("pxNoWindowsZip", ("tag", release.Tag),
                                        ("assets", string.Join(", ", release.AssetUrls.Select(Net.FileName))));

        var shaName = Net.FileName(c.ZipUrl) + ".sha256";
        c.ShaUrl = release.AssetUrls.FirstOrDefault(u => Net.FileName(u).Equals(shaName, StringComparison.OrdinalIgnoreCase));
        step.Report($"{release.Tag}: {Net.FileName(c.ZipUrl)}");
    }

    static async Task Download(PxContext c, SetupStep step, CancellationToken ct)
    {
        var url = c.ZipUrl ?? throw new EmberException("noDownloadLink");
        var target = Path.Combine(Paths.Ensure(Paths.DownloadDir), Net.FileName(url));
        step.SetProgress(0);
        var bytes = await Net.DownloadAsync(c.RequireHttp(), url, target, (done, total) =>
        {
            step.Report(total > 0 ? L.T("jobs.downloadProgress", ("done", Net.Mb(done)), ("total", Net.Mb(total.Value))) : Net.Mb(done), log: false);
            step.SetProgress(total > 0 ? 100.0 * done / total.Value : null);
        }, ct);
        c.ZipPath = target;
        step.Report(L.T("jobs.downloaded", ("size", Net.Mb(bytes)), ("path", target)));
    }

    static async Task VerifyHash(PxContext c, SetupStep step, CancellationToken ct)
    {
        var text = await c.RequireHttp().GetStringAsync(c.ShaUrl, ct);
        var expected = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        var actual = await Net.Sha256Async(c.ZipPath!, ct);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new EmberException("shaMismatch", ("expected", expected), ("actual", actual));
        step.Report(L.T("jobs.shaOk", ("hash", actual[..16] + "…")));
    }

    static async Task StopPx(PxContext c, SetupStep step, CancellationToken ct)
    {
        int count = await PxControl.StopAsync(c.Settings, ct);
        step.Report(count == 0 ? L.T("px.wasNotRunning") : L.T("px.stoppedCount", ("count", count)));
    }

    static async Task Extract(PxContext c, SetupStep step, CancellationToken ct)
    {
        var zip = c.ZipPath ?? c.Settings.LocalZip;
        if (string.IsNullOrWhiteSpace(zip) || !File.Exists(zip)) throw new EmberException("pxZipMissing", ("path", zip));

        var dir = Path.GetFullPath(c.Settings.InstallDir);
        step.SetProgress(null);
        int files = await Task.Run(() => Zip.ExtractSafe(zip, dir, ct), ct);

        c.PxExe = PxControl.FindExe(dir, "px.exe") ?? throw new EmberException("pxExeNotInZip");
        step.Report(L.T("jobs.extracted", ("count", files), ("path", dir)));
    }

    static string RequirePxExe(PxContext c) =>
        c.PxExe ?? PxControl.FindExe(c.Settings.InstallDir, "px.exe")
        ?? throw new EmberException("pxNotInstalled", ("path", c.Settings.InstallDir));

    static async Task WriteConfig(PxContext c, SetupStep step, CancellationToken ct)
    {
        var s = c.Settings;
        var px = RequirePxExe(c);
        Directory.CreateDirectory(Path.GetDirectoryName(s.ConfigIni)!);

        // Start from scratch so no leftovers (e.g. pac= next to proxy=) remain.
        if (File.Exists(s.ConfigIni))
        {
            File.Copy(s.ConfigIni, s.ConfigIni + ".bak", overwrite: true);
            File.Delete(s.ConfigIni);
            Log.Info(L.T("px.iniBackedUp"));
        }

        var args = new List<string>
        {
            "--config=" + s.ConfigIni,
            "--port=" + s.Port,
            s.UsePac ? "--pac=" + c.PacUrl : "--proxy=" + s.UpstreamHostPort,
        };
        if (s.InternalDomainList.Count > 0) args.Add("--noproxy=" + string.Join(",", NoProxy.ForPx(s.InternalDomainList)));
        args.Add("--save");

        try
        {
            var result = await ProcessRunner.RunAsync(px, args, ct, 30);
            if (!result.Ok) throw new EmberException("commandFailed", ("command", "px --save"), ("detail", result.Output));
        }
        catch (EmberException ex) when (ex.Code == "processTimeout" && File.Exists(s.ConfigIni))
        {
            Log.Warn(L.T("px.saveHung"));
        }

        if (!File.Exists(s.ConfigIni)) throw new EmberException("pxIniMissing", ("path", s.ConfigIni));
        step.Report(s.ConfigIni);
    }

    static async Task SetupAutostart(PxContext c, SetupStep step, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(RequirePxExe(c),
            new[] { "--config=" + c.Settings.ConfigIni, "--install", "--force" }, ct, 30);
        if (!result.Ok) throw new EmberException("commandFailed", ("command", "px --install"), ("detail", result.Output));
        step.Report(L.T("px.autostartOk"));
    }

    static async Task StartPx(PxContext c, SetupStep step, CancellationToken ct)
    {
        step.SetProgress(null);
        PxControl.Start(c.Settings);
        await PxControl.WaitForPortAsync(c.Settings.Port, TimeSpan.FromSeconds(30), ct);
        step.Report(L.T("px.listening", ("url", c.Settings.PxUrl)));
    }

    static async Task TestConnection(PxContext c, SetupStep step, CancellationToken ct)
    {
        step.SetProgress(null);
        using var http = Net.CreateClient(new WebProxy(c.Settings.PxUrl), TimeSpan.FromSeconds(30));
        try
        {
            using var response = await http.GetAsync("https://github.com", HttpCompletionOption.ResponseHeadersRead, ct);
            var code = (int)response.StatusCode;
            // 407/5xx come from px or the company proxy -> no connection
            if (code == 407 || code >= 500) throw new HttpRequestException($"HTTP {code} {response.ReasonPhrase}");
            step.Report(L.T("px.testOk", ("code", code)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new EmberException(ex, "pxTestFailed", ("detail", Net.Describe(ex)));
        }
    }

    static async Task Tool(string id, PxContext c, SetupStep step, CancellationToken ct) =>
        step.Report(await ProxyTools.SetAsync(id, c.Settings, ct));

    // ======================================================================= uninstall

    static async Task Untool(string id, PxContext c, SetupStep step, CancellationToken ct) =>
        step.Report(await ProxyTools.RemoveAsync(id, c.Settings, ct));

    static async Task RemoveAutostart(PxContext c, SetupStep step, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(RequirePxExe(c), new[] { "--uninstall" }, ct, 30);
        if (!result.Ok) throw new EmberException("commandFailed", ("command", "px --uninstall"), ("detail", result.Output));
        step.Report(L.T("px.autostartRemoved"));
    }

    static Task DeleteFiles(PxContext c, SetupStep step, CancellationToken ct)
    {
        var s = c.Settings;
        var dir = Path.GetFullPath(s.InstallDir);
        var deleted = new List<string>();

        if (Directory.Exists(dir))
        {
            if (Zip.IsDangerousDirectory(dir)) throw new EmberException("refuseDelete", ("path", dir));
            if (PxControl.FindExe(dir, "px.exe") == null) throw new EmberException("refuseDeleteNoPx", ("path", dir));
            Directory.Delete(dir, recursive: true);
            deleted.Add(dir);
        }

        foreach (var file in new[] { s.ConfigIni, s.ConfigIni + ".bak" })
        {
            if (!File.Exists(file)) continue;
            File.Delete(file);
            deleted.Add(file);
        }
        var configDir = Path.GetDirectoryName(s.ConfigIni)!;
        if (Directory.Exists(configDir) && !Directory.EnumerateFileSystemEntries(configDir).Any())
            Directory.Delete(configDir);

        step.Report(deleted.Count > 0 ? L.T("jobs.deleted", ("items", string.Join(", ", deleted))) : L.T("proxy.nothingToRemove"));
        return Task.CompletedTask;
    }
}

/// <summary>Zip helpers shared by px, llama.cpp and the UI packages.</summary>
public static class Zip
{
    /// <summary>Extracts a zip, refusing entries that would land outside the target folder.</summary>
    public static int ExtractSafe(string zip, string dir, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        int count = 0;
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new EmberException("zipInvalidPath", ("entry", entry.FullName));
            if (entry.Name.Length == 0) { Directory.CreateDirectory(dest); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            count++;
        }
        return count;
    }

    /// <summary>Safety net: never delete a drive root or a profile/system folder.</summary>
    public static bool IsDangerousDirectory(string dir)
    {
        var full = Path.GetFullPath(dir).TrimEnd('\\', '/');
        var forbidden = new[]
        {
            Path.GetPathRoot(full),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Paths.InstallRoot,
        };
        return forbidden.Any(f => !string.IsNullOrEmpty(f) &&
                                  string.Equals(Path.GetFullPath(f).TrimEnd('\\', '/'), full, StringComparison.OrdinalIgnoreCase));
    }
}

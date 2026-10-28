using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ember.Core;

/// <summary>
/// Who may see the advanced modules. This only hides UI – Ember is open source, so it is not a
/// security boundary. The host checks it as well so the UI and the bridge agree.
/// </summary>
/// <summary>
/// Admins (from team.json → admins) see the advanced modules KI and Energie. Without an admins
/// list nobody does – except local developer builds. This shapes the UI and is enforced by the
/// bridge, but it is no security boundary: the code is open and the actions run with the user's rights.
/// </summary>
public static class AdminCheck
{
    public static bool IsAdmin(TeamConfig team) =>
        AppInfo.Version.EndsWith("-dev", StringComparison.Ordinal) ||
        IsAdmin(team, Elevation.AccountName, Elevation.GroupNames());

    internal static bool IsAdmin(TeamConfig team, string account, IEnumerable<string> groups)
    {
        var users = team.Admins?.Users ?? new List<string>();
        var adminGroups = team.Admins?.Groups ?? new List<string>();
        if (users.Any(u => Same(u, account))) return true;
        var mine = groups.ToList();
        return adminGroups.Any(g => mine.Any(m => Same(g, m)));
    }

    /// <summary>"FIRMA\\max" matches "FIRMA\\max" and "max"; the comparison ignores case.</summary>
    static bool Same(string configured, string actual) =>
        configured.Trim().Length > 0 &&
        (configured.Trim().Equals(actual, StringComparison.OrdinalIgnoreCase) ||
         (!configured.Contains('\\') && actual.EndsWith("\\" + configured.Trim(), StringComparison.OrdinalIgnoreCase)));
}

public sealed record GpuInfo(string Name, string? Driver, string? Cuda, int? MemoryTotalMb, int? MemoryUsedMb,
                             double? PowerDraw, double? PowerLimit, int? Temperature, int? Utilization);

public static class Gpu
{
    /// <summary>NVIDIA GPUs via nvidia-smi (read only – Ember does not change power limits).</summary>
    public static async Task<List<GpuInfo>> NvidiaAsync(CancellationToken ct)
    {
        var smi = Tools.NvidiaSmi;
        if (smi == null) return new List<GpuInfo>();
        var header = await ProcessRunner.RunAsync(smi, Array.Empty<string>(), ct, 30, logOutput: false);
        var cuda = Regex.Match(header.StdOut, @"CUDA Version:\s*([\d.]+)").Groups[1].Value;
        var r = await ProcessRunner.RunAsync(smi, new[]
        {
            "--query-gpu=name,driver_version,memory.total,memory.used,power.draw,power.limit,temperature.gpu,utilization.gpu",
            "--format=csv,noheader,nounits",
        }, ct, 30, logOutput: false);
        return ParseCsv(r.StdOut, cuda.Length > 0 ? cuda : null);
    }

    internal static List<GpuInfo> ParseCsv(string csv, string? cuda)
    {
        static double? D(string s) => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        static int? I(string s) => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        return csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Select(line => line.Split(','))
                  .Where(f => f.Length >= 8)
                  .Select(f => new GpuInfo(f[0].Trim(), f[1].Trim(), cuda, I(f[2]), I(f[3]), D(f[4]), D(f[5]), I(f[6]), I(f[7])))
                  .ToList();
    }

    /// <summary>Any GPU at all (for choosing the Vulkan build when there is no NVIDIA card).</summary>
    public static async Task<List<string>> AdaptersAsync(CancellationToken ct)
    {
        try
        {
            var json = await PowerShell.RunJsonAsync("@(Get-CimInstance Win32_VideoController | ForEach-Object { \"$($_.Name)\" }) | ConvertTo-Json -Compress", ct, 30);
            return json.AsArray().Select(e => e.GetString() ?? "").Where(n => n.Length > 0).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug("Win32_VideoController: " + ex.Message);
            return new List<string>();
        }
    }
}

public sealed record LlamaInstall(string Tag, string Variant, string Path, bool Current);

public sealed record LlamaStatus(IReadOnlyList<LlamaInstall> Installed, string? Current, string RecommendedVariant,
                                 bool ServerRunning, int Port, string? ServerModel);

/// <summary>llama.cpp from github.com/ggml-org/llama.cpp: pick the right Windows build, install and update it.</summary>
public static class LlamaCpp
{
    const string Repo = "ggml-org/llama.cpp";
    static Process? _server;
    static string? _serverModel;

    public static IReadOnlyList<LlamaInstall> Installed(UserSettings s)
    {
        if (!Directory.Exists(Paths.LlamaDir)) return Array.Empty<LlamaInstall>();
        return Directory.GetDirectories(Paths.LlamaDir)
            .Select(d => (Dir: d, Name: Path.GetFileName(d)))
            .Where(x => File.Exists(Path.Combine(x.Dir, "llama-server.exe")))
            .Select(x =>
            {
                var dash = x.Name.IndexOf('-');
                var tag = dash > 0 ? x.Name[..dash] : x.Name;
                var variant = dash > 0 ? x.Name[(dash + 1)..] : "?";
                return new LlamaInstall(tag, variant, x.Dir, x.Name == s.LlamaCurrent);
            })
            .OrderByDescending(i => i.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<string> RecommendVariantAsync(UserSettings s, CancellationToken ct)
    {
        if (s.LlamaVariant is "cuda" or "vulkan" or "cpu") return s.LlamaVariant;
        if ((await Gpu.NvidiaAsync(ct)).Count > 0) return "cuda";
        var adapters = await Gpu.AdaptersAsync(ct);
        return adapters.Any(a => !a.Contains("Basic", StringComparison.OrdinalIgnoreCase) && !a.Contains("Remote", StringComparison.OrdinalIgnoreCase))
            ? "vulkan" : "cpu";
    }

    public static async Task<LlamaStatus> StatusAsync(UserSettings s, CancellationToken ct) =>
        new(Installed(s), string.IsNullOrEmpty(s.LlamaCurrent) ? null : s.LlamaCurrent, await RecommendVariantAsync(s, ct),
            _server is { HasExited: false }, s.LlamaPort, _server is { HasExited: false } ? _serverModel : null);

    /// <summary>
    /// Chooses the release asset: CUDA build with the highest CUDA version the driver supports,
    /// otherwise Vulkan or CPU. Returns (main zip, optional cudart zip).
    /// </summary>
    internal static (string Zip, string? Cudart) PickAssets(IReadOnlyList<string> assets, string variant, string? driverCuda)
    {
        var names = assets.Select(a => (Url: a, Name: Net.FileName(a))).ToList();
        IEnumerable<(string Url, string Name)> Candidates(string pattern) =>
            names.Where(n => Regex.IsMatch(n.Name, pattern, RegexOptions.IgnoreCase) && !n.Name.StartsWith("cudart", StringComparison.OrdinalIgnoreCase));

        if (variant == "cuda")
        {
            var driver = Version.TryParse(driverCuda ?? "", out var dv) ? dv : new Version(99, 0);
            var cuda = Candidates(@"^llama-.*-bin-win-cuda-?([\d.]+)-x64\.zip$")
                .Select(n => (n.Url, n.Name, Ver: Version.TryParse(Regex.Match(n.Name, @"cuda-?([\d.]+)-x64").Groups[1].Value, out var v) ? v : new Version(0, 0)))
                .Where(n => n.Ver <= driver)
                .OrderByDescending(n => n.Ver)
                .FirstOrDefault();
            if (cuda.Url != null)
            {
                var cudart = names.FirstOrDefault(n => n.Name.StartsWith("cudart", StringComparison.OrdinalIgnoreCase) &&
                                                       n.Name.Contains($"cuda-{cuda.Ver}", StringComparison.OrdinalIgnoreCase) &&
                                                       n.Name.Contains("x64", StringComparison.OrdinalIgnoreCase)).Url;
                return (cuda.Url, cudart);
            }
            variant = "vulkan";
        }
        var pick = Candidates(variant == "vulkan" ? @"^llama-.*-bin-win-vulkan-x64\.zip$" : @"^llama-.*-bin-win-(cpu|avx2)-x64\.zip$").FirstOrDefault().Url
                   ?? Candidates(@"^llama-.*-bin-win-cpu-x64\.zip$").FirstOrDefault().Url
                   ?? throw new EmberException("llamaNoAsset", ("variant", variant));
        return (pick, null);
    }

    public static List<SetupStep> InstallSteps(UserSettings settings, EffectiveSettings eff, Action<string> setCurrent)
    {
        HttpClient? http = null;
        ReleaseInfo? release = null;
        string variant = "cpu";
        string? cuda = null;
        (string Zip, string? Cudart) assets = default;
        string targetDir = "";
        var files = new List<string>();

        return new List<SetupStep>
        {
            new(L.T("ai.llama.steps.detect"), async (step, ct) =>
            {
                variant = await RecommendVariantAsync(settings, ct);
                cuda = (await Gpu.NvidiaAsync(ct)).FirstOrDefault()?.Cuda;
                step.Report(L.T("ai.llama.variant", ("variant", variant)) + (cuda != null ? $" (CUDA {cuda})" : ""));
            }),
            new(L.T("ai.llama.steps.release"), async (step, ct) =>
            {
                step.SetProgress(null);
                var (client, route) = await Net.SmartClientAsync(eff, TimeSpan.FromMinutes(30));
                http = client;
                release = await Net.GetLatestReleaseAsync(http, Repo, ct);
                assets = PickAssets(release.AssetUrls, variant, cuda);
                targetDir = Path.Combine(Paths.LlamaDir, $"{release.Tag}-{variant}");
                step.Report($"{release.Tag}: {Net.FileName(assets.Zip)}{(assets.Cudart != null ? " + " + Net.FileName(assets.Cudart) : "")} ({route})");
            }),
            new(L.T("ai.llama.steps.download"), async (step, ct) =>
            {
                foreach (var url in new[] { assets.Zip, assets.Cudart }.OfType<string>())
                {
                    var target = Path.Combine(Paths.Ensure(Paths.DownloadDir), Net.FileName(url));
                    await Net.DownloadAsync(http!, url, target, (done, total) =>
                    {
                        step.Report($"{Net.FileName(url)}: " + (total > 0 ? L.T("jobs.downloadProgress", ("done", Net.Mb(done)), ("total", Net.Mb(total.Value))) : Net.Mb(done)), log: false);
                        step.SetProgress(total > 0 ? 100.0 * done / total.Value : null);
                    }, ct, resume: true);
                    files.Add(target);
                }
                step.Report(string.Join(", ", files.Select(Path.GetFileName)));
            }),
            new(L.T("ai.llama.steps.extract"), async (step, ct) =>
            {
                step.SetProgress(null);
                int count = 0;
                foreach (var f in files) count += await Task.Run(() => Zip.ExtractSafe(f, targetDir, ct), ct);
                // Some releases put everything into a sub folder; flatten so llama-server.exe is at the top.
                var server = Directory.EnumerateFiles(targetDir, "llama-server.exe", SearchOption.AllDirectories).FirstOrDefault()
                             ?? throw new EmberException("llamaNoServer");
                var serverDir = Path.GetDirectoryName(server)!;
                if (!serverDir.Equals(targetDir, StringComparison.OrdinalIgnoreCase))
                    foreach (var file in Directory.GetFiles(serverDir)) File.Move(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
                setCurrent(Path.GetFileName(targetDir));
                foreach (var f in files) { try { File.Delete(f); } catch { } }
                step.Report(L.T("jobs.extracted", ("count", count), ("path", targetDir)));
            }),
            new(L.T("ai.llama.steps.cleanup"), (step, ct) =>
            {
                // keep the two newest builds (rollback), delete older ones
                var old = Installed(settings).Skip(2).ToList();
                foreach (var i in old)
                {
                    try { Directory.Delete(i.Path, recursive: true); } catch (Exception ex) { Log.Warn(ex.Message); }
                }
                step.Report(old.Count == 0 ? L.T("proxy.nothingToRemove") : L.T("jobs.deleted", ("items", string.Join(", ", old.Select(o => o.Tag)))));
                http?.Dispose();
                return Task.CompletedTask;
            }),
        };
    }

    public static void StartServer(UserSettings s, string modelPath)
    {
        StopServer();
        var current = Installed(s).FirstOrDefault(i => i.Current) ?? Installed(s).FirstOrDefault()
                      ?? throw new EmberException("llamaNotInstalled");
        if (!File.Exists(modelPath)) throw new EmberException("modelMissing", ("path", modelPath));
        var psi = new ProcessStartInfo(Path.Combine(current.Path, "llama-server.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = current.Path,
        };
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add(modelPath);
        psi.ArgumentList.Add("--host");
        psi.ArgumentList.Add("127.0.0.1");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(s.LlamaPort.ToString(CultureInfo.InvariantCulture));
        foreach (var a in SplitArgs(s.LlamaArgs)) psi.ArgumentList.Add(a);

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Log.Debug(e.Data, "llama"); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Log.Debug(e.Data, "llama"); };
        p.Exited += (_, _) => Log.Info(L.T("ai.server.stopped"), "llama");
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _server = p;
        _serverModel = Path.GetFileName(modelPath);
        Log.Info(L.T("ai.server.started", ("model", _serverModel), ("port", s.LlamaPort)), "llama");
    }

    public static void StopServer()
    {
        var p = _server;
        _server = null;
        if (p == null) return;
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        p.Dispose();
    }

    /// <summary>Splits "-ngl 99 -c 8192 --alias \"my model\"" like a command line.</summary>
    internal static List<string> SplitArgs(string text)
    {
        var result = new List<string>();
        foreach (Match m in Regex.Matches(text ?? "", "\"([^\"]*)\"|(\\S+)"))
            result.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        return result;
    }
}

public sealed record ModelState(string Id, string Name, string Url, string? Note, bool Downloaded, long? Size, string Path);

public static class Models
{
    public static string Dir(UserSettings s) => string.IsNullOrWhiteSpace(s.ModelsDir) ? Paths.DefaultModelsDir : s.ModelsDir;

    public static List<ModelEntry> All(UserSettings s, TeamConfig team) =>
        (team.Models ?? new List<ModelEntry>()).Concat(s.Models)
            .Where(m => !string.IsNullOrWhiteSpace(m.Url))
            .Select(m => { if (string.IsNullOrWhiteSpace(m.Id)) m.Id = Net.FileName(m.Url); return m; })
            .DistinctBy(m => m.Id)
            .ToList();

    public static List<ModelState> List(UserSettings s, TeamConfig team) =>
        All(s, team).Select(m =>
        {
            var path = System.IO.Path.Combine(Dir(s), Net.FileName(m.Url));
            var exists = File.Exists(path);
            return new ModelState(m.Id, string.IsNullOrWhiteSpace(m.Name) ? Net.FileName(m.Url) : m.Name, m.Url, m.Note, exists,
                                  exists ? new FileInfo(path).Length : null, path);
        }).ToList();

    public static List<SetupStep> DownloadSteps(ModelEntry model, string dir, EffectiveSettings eff)
    {
        var target = System.IO.Path.Combine(dir, Net.FileName(model.Url));
        return new List<SetupStep>
        {
            new(L.T("ai.models.steps.download"), async (step, ct) =>
            {
                var (http, route) = await Net.SmartClientAsync(eff, TimeSpan.FromHours(6));
                using (http)
                {
                    step.SetProgress(0);
                    var bytes = await Net.DownloadAsync(http, model.Url, target, (done, total) =>
                    {
                        step.Report(total > 0 ? L.T("jobs.downloadProgress", ("done", Net.Mb(done)), ("total", Net.Mb(total.Value))) : Net.Mb(done), log: false);
                        step.SetProgress(total > 0 ? 100.0 * done / total.Value : null);
                    }, ct, resume: true);
                    step.Report(L.T("jobs.downloaded", ("size", Net.Mb(bytes)), ("path", target)) + $" ({route})");
                }
            }),
            new(L.T("ai.models.steps.verify"), async (step, ct) =>
            {
                step.SetProgress(null);
                var actual = await Net.Sha256Async(target, ct);
                if (!actual.Equals(model.Sha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(target);
                    throw new EmberException("shaMismatch", ("expected", model.Sha256), ("actual", actual));
                }
                step.Report(L.T("jobs.shaOk", ("hash", actual[..16] + "…")));
            }, () => string.IsNullOrWhiteSpace(model.Sha256) ? L.T("ai.models.noSha") : null),
        };
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ember.Core;

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

/// <param name="Complete">false: a CUDA build without its runtime (cudart64/cublas64); it would run on the CPU and is not started.</param>
public sealed record LlamaInstall(string Tag, string Variant, string Path, bool Current, bool Complete = true);

/// <param name="Offloaded">"62/62": layers on the GPU, from the server's log ("offloaded 62/62 layers to GPU").</param>
/// <param name="Loaded">The server answers (log: "model loaded" / "listening on").</param>
/// <param name="Device">The GPU llama.cpp found ("CUDA0: NVIDIA RTX …"); null after loading means CPU only.</param>
/// <param name="PromptTps">Tokens per second while reading the prompt, from the last request.</param>
/// <param name="GenerationTps">Tokens per second while writing the answer, from the last request.</param>
public sealed record LlamaStatus(IReadOnlyList<LlamaInstall> Installed, string? Current, string RecommendedVariant,
                                 bool ServerRunning, int Port, string? ServerModel, string? ServerAlias, string? ServerProfile,
                                 int? ServerContext, string? Offloaded, string? LastError,
                                 bool Loaded = false, string? Device = null, double? PromptTps = null, double? GenerationTps = null);

/// <summary>Context size and KV cache for llama-server; the weights stay fully in VRAM in every profile.</summary>
public sealed record LlamaProfile(string Id, int Context, string KvCache, bool Mtp);

/// <summary>llama.cpp from github.com/ggml-org/llama.cpp: pick the right Windows build, install and update it.</summary>
public static class LlamaCpp
{
    const string Repo = "ggml-org/llama.cpp";
    static Process? _server;
    static string? _serverModel, _serverAlias, _serverProfile, _offloaded, _lastError, _device;
    static bool _loaded;
    static double? _promptTps, _genTps;
    static int? _serverContext;

    /// <summary>
    /// For a 27B model on 24 GB: "standard" 96K with q8_0 KV cache, "lang" 160K with q4_0, "max" 256K
    /// with q4_0 and without MTP. Values from the setup guide for the RTX PRO 5000 (24 GB).
    /// </summary>
    public static readonly IReadOnlyList<LlamaProfile> Profiles = new[]
    {
        new LlamaProfile("standard", 98304, "q8_0", true),
        new LlamaProfile("lang", 163840, "q4_0", true),
        new LlamaProfile("max", 262144, "q4_0", false),
    };

    public static LlamaProfile Profile(string? id) => Profiles.FirstOrDefault(p => p.Id == id) ?? Profiles.First(p => p.Id == "lang");

    public static IReadOnlyList<LlamaInstall> Installed(UserSettings s)
    {
        var list = new List<LlamaInstall>();
        // A folder that already works (e.g. C:\llama from the setup guide) comes first when chosen.
        if (s.LlamaCustomDir.Length > 0 && File.Exists(Path.Combine(s.LlamaCustomDir, "llama-server.exe")))
            list.Add(new LlamaInstall("custom", HasCudaRuntime(s.LlamaCustomDir) ? "cuda" : "?", s.LlamaCustomDir, s.LlamaCurrent == "custom"));
        if (!Directory.Exists(Paths.LlamaDir)) return list;
        return list.Concat(Directory.GetDirectories(Paths.LlamaDir)
            .Select(d => (Dir: d, Name: Path.GetFileName(d)))
            .Where(x => !x.Name.EndsWith(".part", StringComparison.Ordinal) && File.Exists(Path.Combine(x.Dir, "llama-server.exe")))
            // A CUDA build without its runtime (half-finished install) would run on the CPU: not offered.
            .Select(x =>
            {
                var dash = x.Name.IndexOf('-');
                var tag = dash > 0 ? x.Name[..dash] : x.Name;
                var variant = dash > 0 ? x.Name[(dash + 1)..] : "?";
                // A CUDA build without its runtime stays visible, marked, so the reason is clear.
                return new LlamaInstall(tag, variant, x.Dir, x.Name == s.LlamaCurrent, variant != "cuda" || HasCudaRuntime(x.Dir));
            })
            .OrderByDescending(i => i.Tag, StringComparer.OrdinalIgnoreCase))
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

    public static async Task<LlamaStatus> StatusAsync(UserSettings s, CancellationToken ct)
    {
        bool running = _server is { HasExited: false };
        return new(Installed(s), string.IsNullOrEmpty(s.LlamaCurrent) ? null : s.LlamaCurrent, await RecommendVariantAsync(s, ct),
                   running, s.LlamaPort, running ? _serverModel : null, running ? _serverAlias : null, running ? _serverProfile : null,
                   running ? _serverContext : null, running ? _offloaded : null, _lastError,
                   running && _loaded, running ? _device : null, running ? _promptTps : null, running ? _genTps : null);
    }

    /// <summary>
    /// Chooses the release assets: the CUDA build with the newest CUDA version within the major
    /// version the driver supports (CUDA 13.4 runs on a 13.2 driver), plus its cudart zip;
    /// otherwise Vulkan or CPU. Returns null when the release has no Windows build of that kind.
    /// </summary>
    internal static (string Zip, string? Cudart)? TryPickAssets(IReadOnlyList<string> assets, string variant, string? driverCuda)
    {
        var names = assets.Select(a => (Url: a, Name: Net.FileName(a))).ToList();
        IEnumerable<(string Url, string Name)> Candidates(string pattern) =>
            names.Where(n => Regex.IsMatch(n.Name, pattern, RegexOptions.IgnoreCase) && !n.Name.StartsWith("cudart", StringComparison.OrdinalIgnoreCase));

        if (variant == "cuda")
        {
            int driverMajor = Version.TryParse(driverCuda ?? "", out var dv) ? dv.Major : int.MaxValue;
            var cuda = Candidates(@"^llama-.*-bin-win-cuda-?([\d.]+)-x64\.zip$")
                .Select(n => (n.Url, n.Name, Ver: Version.TryParse(Regex.Match(n.Name, @"cuda-?([\d.]+)-x64").Groups[1].Value, out var v) ? v : new Version(0, 0)))
                .Where(n => n.Ver.Major <= driverMajor)
                .OrderByDescending(n => n.Ver)
                .FirstOrDefault();
            if (cuda.Url == null) return null;
            // Only the Windows zip: the same release also has "cudart-…-bin-ubuntu-cuda-13.4-x64.tar.gz".
            var cudart = names.FirstOrDefault(n => Regex.IsMatch(n.Name, $@"^cudart-.*win-cuda-{Regex.Escape(cuda.Ver.ToString())}-x64\.zip$", RegexOptions.IgnoreCase)).Url;
            return (cuda.Url, cudart);
        }
        var pick = Candidates(variant == "vulkan" ? @"^llama-.*-bin-win-vulkan-x64\.zip$" : @"^llama-.*-bin-win-(cpu|avx2)-x64\.zip$").FirstOrDefault().Url;
        return pick == null ? null : (pick, null);
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
                // The "latest" release of llama.cpp can be one without binaries; the builds (b12110 …) are pre-releases.
                release = await Net.FindReleaseAsync(http, Repo, r => TryPickAssets(r.AssetUrls, variant, cuda) != null, 15, ct)
                          ?? throw new EmberException("llamaNoAsset", ("variant", variant));
                assets = TryPickAssets(release.AssetUrls, variant, cuda)!.Value;
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
                    // GitHub publishes a SHA-256 per file; a broken or swapped download stops here.
                    if (release!.Sha256?.GetValueOrDefault(Net.FileName(url)) is { } expected)
                    {
                        var actual = await Net.Sha256Async(target, ct);
                        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(target);
                            throw new EmberException("shaMismatch", ("expected", expected), ("actual", actual));
                        }
                    }
                    files.Add(target);
                }
                step.Report(string.Join(", ", files.Select(Path.GetFileName)));
            }),
            new(L.T("ai.llama.steps.extract"), async (step, ct) =>
            {
                step.SetProgress(null);
                int count = 0;
                // Unpacked next to the target first: a half-finished build never shows up as installed.
                var temp = targetDir + ".part";
                if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
                // Program and CUDA runtime go into the same folder, next to llama-server.exe.
                foreach (var f in files) count += await Task.Run(() => Zip.ExtractSafe(f, temp, ct), ct);
                // Some releases put everything into a sub folder; flatten so llama-server.exe is at the top.
                var server = Directory.EnumerateFiles(temp, "llama-server.exe", SearchOption.AllDirectories).FirstOrDefault()
                             ?? throw new EmberException("llamaNoServer");
                var serverDir = Path.GetDirectoryName(server)!;
                if (!serverDir.Equals(temp, StringComparison.OrdinalIgnoreCase))
                    foreach (var file in Directory.GetFiles(serverDir)) File.Move(file, Path.Combine(temp, Path.GetFileName(file)), overwrite: true);
                if (variant == "cuda" && !HasCudaRuntime(temp)) throw new EmberException("llamaNoCudaRuntime", ("path", targetDir));
                if (Directory.Exists(targetDir)) Directory.Delete(targetDir, recursive: true);
                Directory.Move(temp, targetDir);
                setCurrent(Path.GetFileName(targetDir));
                foreach (var f in files) { try { File.Delete(f); } catch { } }
                step.Report(L.T("jobs.extracted", ("count", count), ("path", targetDir)));
            }),
            new(L.T("ai.llama.steps.devices"), async (step, ct) =>
            {
                // Like "llama-server --list-devices" in the guide: CUDA0 must show the NVIDIA card.
                var (devices, output) = await DevicesAsync(targetDir, ct);
                if (variant == "cuda" && !devices.Any(d => d.StartsWith("CUDA", StringComparison.Ordinal)))
                    throw new EmberException("llamaNoCuda", ("detail", output.Split('\n').LastOrDefault()?.Trim() ?? ""));
                step.Report(devices.Count > 0 ? string.Join(" · ", devices) : L.T("ai.llama.cpuOnly"));
            }),
            new(L.T("ai.llama.steps.cleanup"), (step, ct) =>
            {
                // keep the two newest builds (rollback), delete older ones
                var old = Installed(settings).Where(i => i.Tag != "custom").Skip(2).ToList();
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

    /// <summary>
    /// The llama-server command line as in the setup guide: everything on the GPU (-ngl 99), flash
    /// attention, the profile's context and KV cache, Jinja chat template, one stream; then what the
    /// model wants (sampling, chat-template kwargs), MTP when profile and model support it, then the
    /// user's extra arguments (they win, llama.cpp takes the last value).
    /// </summary>
    internal static List<string> ServerArgs(ModelState model, LlamaProfile profile, int port, string extra)
    {
        var args = new List<string>
        {
            "-m", model.Path,
            "--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture),
            "--alias", model.Alias,
            "-ngl", "99", "-fa", "on",
            "-c", profile.Context.ToString(CultureInfo.InvariantCulture),
            "-ctk", profile.KvCache, "-ctv", profile.KvCache,
            "--jinja", "--parallel", "1",
        };
        // The vision projector: the model also reads images (screenshots, diagrams).
        if (model.MmprojPresent && model.MmprojPath != null) args.AddRange(new[] { "--mmproj", model.MmprojPath });
        args.AddRange(model.Args);
        if (profile.Mtp && model.Mtp) args.AddRange(new[] { "--spec-type", "draft-mtp", "--spec-draft-n-max", "2" });
        // The old default before the profiles; it would cut the context back to 8K.
        if (extra.Trim() != "-ngl 99 -c 8192") args.AddRange(SplitArgs(extra));
        return args;
    }

    /// <summary>"llama-server --list-devices": the GPUs this build can use ("CUDA0: NVIDIA …"), and its whole output.</summary>
    public static async Task<(List<string> Devices, string Output)> DevicesAsync(string dir, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(Path.Combine(dir, "llama-server.exe"), new[] { "--list-devices" }, ct, 60, logOutput: false);
        var devices = Regex.Matches(r.Output, @"^\s*((CUDA|Vulkan)\d+):\s*(.+)$", RegexOptions.Multiline)
                           .Select(m => $"{m.Groups[1].Value}: {m.Groups[3].Value.Trim()}")
                           .ToList();
        return (devices, r.Output.Trim());
    }

    /// <summary>
    /// Before the start: with an NVIDIA card the build must see it, otherwise llama-server would
    /// quietly run on the CPU (a fifth of the speed). The log gets folder, runtime DLLs and devices.
    /// </summary>
    public static async Task CheckBeforeStartAsync(UserSettings s, CancellationToken ct)
    {
        var current = Current(s);
        var dlls = Directory.Exists(current.Path)
            ? string.Join(", ", Directory.GetFiles(current.Path, "*.dll").Select(Path.GetFileName).Where(f => Regex.IsMatch(f!, "^(cudart|cublas|ggml)", RegexOptions.IgnoreCase)))
            : "";
        var (devices, output) = await DevicesAsync(current.Path, ct);
        Log.Info(L.T("ai.server.build", ("path", current.Path), ("dlls", dlls.Length > 0 ? dlls : "–"), ("devices", devices.Count > 0 ? string.Join(" · ", devices) : "CPU")), "llama");
        if ((await Gpu.NvidiaAsync(ct)).Count > 0 && !devices.Any(d => d.StartsWith("CUDA", StringComparison.Ordinal)))
            throw new EmberException("llamaNoCudaAtStart", ("path", current.Path), ("detail", output.Split('\n').LastOrDefault()?.Trim() ?? ""));
    }

    static LlamaInstall Current(UserSettings s)
    {
        var all = Installed(s);
        var current = all.FirstOrDefault(i => i.Current) ?? all.FirstOrDefault(i => i.Complete) ?? all.FirstOrDefault() ?? throw new EmberException("llamaNotInstalled");
        if (!current.Complete) throw new EmberException("llamaNoCudaRuntime", ("path", current.Path));
        return current;
    }

    public static void StartServer(UserSettings s, ModelState model)
    {
        StopServer();
        var current = Current(s);
        if (!File.Exists(model.Path)) throw new EmberException("modelMissing", ("path", model.Path));
        if (current.Variant == "cuda" && !HasCudaRuntime(current.Path)) throw new EmberException("llamaNoCudaRuntime", ("path", current.Path));
        var profile = Profile(s.LlamaProfile);
        var psi = new ProcessStartInfo(Path.Combine(current.Path, "llama-server.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = current.Path,
        };
        foreach (var a in ServerArgs(model, profile, s.LlamaPort, s.LlamaArgs)) psi.ArgumentList.Add(a);
        Log.Debug("> llama-server " + string.Join(' ', psi.ArgumentList), "llama");

        _offloaded = _lastError = _device = null;
        _loaded = false;
        _promptTps = _genTps = null;
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        void OnLine(string? line)
        {
            if (line == null) return;
            Log.Debug(line, "llama");
            if (Offloaded(line) is { } layers) _offloaded = layers;
            if (Device(line) is { } device) _device ??= device;
            if (line.Contains("model loaded", StringComparison.OrdinalIgnoreCase) || line.Contains("listening on", StringComparison.OrdinalIgnoreCase)) _loaded = true;
            if (Speed(line) is { } speed) { if (speed.Prompt) _promptTps = speed.Tps; else _genTps = speed.Tps; }
            if (IsError(line)) _lastError = line.Trim();
        }
        p.OutputDataReceived += (_, e) => OnLine(e.Data);
        p.ErrorDataReceived += (_, e) => OnLine(e.Data);
        p.Exited += (_, _) => Log.Info(L.T("ai.server.stopped"), "llama");
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _server = p;
        _serverModel = model.Name;
        _serverAlias = model.Alias;
        _serverProfile = profile.Id;
        _serverContext = profile.Context;
        Log.Info(L.T("ai.server.started", ("model", model.Name), ("port", s.LlamaPort), ("profile", profile.Id), ("context", profile.Context)), "llama");
    }

    /// <summary>The CUDA runtime (cudart64_*.dll) next to llama-server.exe; without it the CUDA build silently runs on the CPU.</summary>
    internal static bool HasCudaRuntime(string dir) =>
        Directory.Exists(dir) && Directory.EnumerateFiles(dir, "cudart64_*.dll").Any() && Directory.EnumerateFiles(dir, "cublas64_*.dll").Any();

    /// <summary>
    /// A line worth showing as "Letzte Fehlermeldung". llama.cpp marks each line with I, W or E after
    /// the time stamp. Warnings stay in the log: "common_fit_params: failed to fit params … n_gpu_layers
    /// already set by user" only says the automatic fitting stepped aside because Ember sets -ngl 99.
    /// </summary>
    internal static bool IsError(string line)
    {
        var level = Regex.Match(line, @"^\s*[\d.:]+\s+([IWED])\s");
        if (level.Success) return level.Groups[1].Value == "E";
        return line.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
               || line.Contains("error:", StringComparison.OrdinalIgnoreCase)
               || (line.Contains("failed to", StringComparison.OrdinalIgnoreCase) && !line.Contains("common_fit_params", StringComparison.Ordinal));
    }

    /// <summary>"load_tensors: offloaded 65/65 layers to GPU" → "65/65".</summary>
    internal static string? Offloaded(string line)
    {
        var m = Regex.Match(line, @"offloaded (\d+)/(\d+) layers to GPU", RegexOptions.IgnoreCase);
        return m.Success ? $"{m.Groups[1].Value}/{m.Groups[2].Value}" : null;
    }

    /// <summary>
    /// "ggml_cuda_init: found 1 CUDA devices: Device 0: NVIDIA RTX PRO 5000 …" or
    /// "using device CUDA0 (NVIDIA …)" → the GPU; the CPU backend says nothing like that.
    /// </summary>
    internal static string? Device(string line)
    {
        var m = Regex.Match(line, @"(?:Device \d+|using device (?:CUDA|Vulkan)\d+)\s*[:(]\s*([^,()]+)", RegexOptions.IgnoreCase);
        return m.Success && !m.Groups[1].Value.Contains("CPU", StringComparison.OrdinalIgnoreCase) ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// The speeds from the server log: "prompt eval time = … ( … tokens per second)" is reading the
    /// prompt, "eval time = …" writing the answer.
    /// </summary>
    internal static (bool Prompt, double Tps)? Speed(string line)
    {
        var m = Regex.Match(line, @"(prompt eval|eval) time\s*=.*?([\d.]+) tokens per second", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tps)) return null;
        return (m.Groups[1].Value.StartsWith("prompt", StringComparison.OrdinalIgnoreCase), Math.Round(tps, 1));
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

/// <param name="Local">A file that was already there (Path in team.json or chosen on this PC): Ember neither downloads nor deletes it.</param>
/// <param name="Mine">Added on this PC (can be removed from the list); team models cannot.</param>
/// <param name="MmprojPath">Where the vision projector is (or would be); null: the model reads text only.</param>
/// <param name="MmprojPresent">The projector file is there, so llama-server starts with --mmproj and the model sees images.</param>
public sealed record ModelState(string Id, string Name, string Url, string? Note, bool Downloaded, long? Size, string Path,
                                bool Local, bool Mine, string Alias, bool Mtp, IReadOnlyList<string> Args,
                                string? MmprojPath = null, bool MmprojPresent = false, string? MmprojUrl = null);

public static class Models
{
    /// <summary>
    /// The model from the setup guide: Qwen3.8-27B as Unsloth's UD-Q4_K_XL (~17.6 GB), with tool-call
    /// parsers for MCP and an MTP head. Sampling as Qwen recommends, medium reasoning effort for agents.
    /// </summary>
    public static readonly ModelEntry Qwen = new()
    {
        Id = "qwen3.8-27b",
        Name = "Qwen3.8-27B (UD-Q4_K_XL)",
        Url = "https://huggingface.co/unsloth/Qwen3.8-27B-GGUF/resolve/main/Qwen3.8-27B-UD-Q4_K_XL.gguf",
        Note = "≈ 17,6 GB · 24 GB VRAM",
        Alias = "qwen3.8-27b",
        Mtp = true,
        Args = new() { "--temp", "1.0", "--top-p", "0.95", "--top-k", "20", "--min-p", "0.0", "--chat-template-kwargs", "{\"reasoning_effort\":\"medium\"}" },
        Mmproj = "https://huggingface.co/unsloth/Qwen3.8-27B-GGUF/resolve/main/mmproj-F16.gguf",
    };

    public static string Dir(UserSettings s) => string.IsNullOrWhiteSpace(s.ModelsDir) ? Paths.DefaultModelsDir : s.ModelsDir;

    /// <summary>Team models first, then the ones added on this PC, then the built-in one (the same id wins in that order).</summary>
    public static List<(ModelEntry Entry, bool Mine)> All(UserSettings s, TeamConfig team) =>
        new List<ModelEntry>().Select(m => (m, false))
            .Concat(s.Models.Select(m => (m, true)))
            .Append((Qwen, false))
            .Where(x => !string.IsNullOrWhiteSpace(x.Item1.Url) || !string.IsNullOrWhiteSpace(x.Item1.Path))
            .Select(x =>
            {
                if (string.IsNullOrWhiteSpace(x.Item1.Id)) x.Item1.Id = Net.FileName(string.IsNullOrWhiteSpace(x.Item1.Path) ? x.Item1.Url : x.Item1.Path!);
                return (Entry: x.Item1, Mine: x.Item2);
            })
            .DistinctBy(x => x.Entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static List<ModelState> List(UserSettings s, TeamConfig team) =>
        All(s, team).Select(x =>
        {
            var m = x.Entry;
            bool local = !string.IsNullOrWhiteSpace(m.Path);
            var path = local ? Environment.ExpandEnvironmentVariables(m.Path!.Trim().Trim('"')) : System.IO.Path.Combine(Dir(s), Net.FileName(m.Url));
            var exists = File.Exists(path);
            // The same file as a built-in model (picked on the PC, or listed in team.json without
            // settings): it gets that model's alias, MTP and arguments, so it runs as fast.
            var known = Known.FirstOrDefault(k => Net.FileName(k.Url).Equals(System.IO.Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
            var name = !string.IsNullOrWhiteSpace(m.Name) ? m.Name : known?.Name ?? System.IO.Path.GetFileNameWithoutExtension(path);
            var alias = !string.IsNullOrWhiteSpace(m.Alias) ? m.Alias! : known?.Alias ?? System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var (mmproj, mmprojUrl) = MmprojFor(m.Mmproj ?? known?.Mmproj, path);
            return new ModelState(m.Id, name, m.Url, m.Note ?? known?.Note, exists, exists ? new FileInfo(path).Length : null, path,
                                  local, x.Mine, alias, m.Mtp ?? known?.Mtp ?? false, m.Args ?? known?.Args ?? new List<string>(),
                                  mmproj, mmproj != null && File.Exists(mmproj), mmprojUrl);
        })
        // One entry per file: a local pick of the downloaded Qwen file is the same model.
        .GroupBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.OrderBy(m => m.Local ? 1 : 0).First())
        .ToList();

    static IEnumerable<ModelEntry> Known => new[] { Qwen };

    /// <summary>
    /// The vision projector for a model: a path stays as it is; a URL lands next to the model as
    /// "&lt;model&gt;.mmproj-F16.gguf" (every repo calls it "mmproj-F16.gguf", so the model name keeps them apart).
    /// Without either, an mmproj*.gguf in the model's own folder is taken.
    /// </summary>
    internal static (string? Path, string? Url) MmprojFor(string? mmproj, string modelPath)
    {
        var dir = System.IO.Path.GetDirectoryName(modelPath) ?? "";
        var stem = System.IO.Path.GetFileNameWithoutExtension(modelPath);
        if (!string.IsNullOrWhiteSpace(mmproj))
        {
            if (Uri.TryCreate(mmproj, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                return (System.IO.Path.Combine(dir, $"{stem}.{Net.FileName(mmproj)}"), mmproj);
            return (Environment.ExpandEnvironmentVariables(mmproj.Trim().Trim('"')), null);
        }
        try
        {
            var found = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*mmproj*.gguf").OrderByDescending(f => System.IO.Path.GetFileName(f).StartsWith(stem, StringComparison.OrdinalIgnoreCase)).FirstOrDefault()
                : null;
            return (found, null);
        }
        catch (IOException)
        {
            return (null, null);
        }
    }

    /// <summary>A GGUF file that is already on this PC or a share joins the list as it is.</summary>
    public static ModelEntry AddLocal(UserSettings s, string file)
    {
        if (!file.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) throw new EmberException("modelNotGguf", ("path", file));
        if (!File.Exists(file)) throw new EmberException("modelMissing", ("path", file));
        var entry = new ModelEntry { Id = System.IO.Path.GetFileName(file), Name = System.IO.Path.GetFileNameWithoutExtension(file), Path = file };
        s.Models.RemoveAll(m => m.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
        s.Models.Add(entry);
        return entry;
    }

    public static List<SetupStep> DownloadSteps(ModelEntry model, string dir, EffectiveSettings eff, ModelState? state = null)
    {
        var target = System.IO.Path.Combine(dir, Net.FileName(model.Url));
        bool haveModel = File.Exists(target) && state is { Downloaded: true };
        return new List<SetupStep>
        {
            new(L.T("ai.models.steps.download"), async (step, ct) =>
            {
                if (haveModel)
                {
                    step.Report(L.T("ai.models.alreadyThere", ("path", target)));
                    return;
                }
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
            }, () => string.IsNullOrWhiteSpace(model.Sha256) || haveModel ? L.T("ai.models.noSha") : null),
            new(L.T("ai.models.steps.mmproj"), async (step, ct) =>
            {
                var (path, url) = MmprojFor(model.Mmproj ?? (Net.FileName(Qwen.Url).Equals(Net.FileName(model.Url), StringComparison.OrdinalIgnoreCase) ? Qwen.Mmproj : null), target);
                if (url == null || path == null) { step.Report(L.T("ai.models.noMmproj")); return; }
                if (File.Exists(path)) { step.Report(L.T("ai.models.alreadyThere", ("path", path))); return; }
                var (http, route) = await Net.SmartClientAsync(eff, TimeSpan.FromHours(1));
                using (http)
                {
                    var bytes = await Net.DownloadAsync(http, url, path, (done, total) =>
                    {
                        step.Report(total > 0 ? L.T("jobs.downloadProgress", ("done", Net.Mb(done)), ("total", Net.Mb(total.Value))) : Net.Mb(done), log: false);
                        step.SetProgress(total > 0 ? 100.0 * done / total.Value : null);
                    }, ct, resume: true);
                    step.Report(L.T("jobs.downloaded", ("size", Net.Mb(bytes)), ("path", path)) + $" ({route})");
                }
            }),
        };
    }
}

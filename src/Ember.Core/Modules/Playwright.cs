using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ember.Core;

/// <param name="Package">@playwright/test, playwright or playwright-core – whichever the project uses.</param>
/// <param name="Version">Installed in node_modules (null if "npm install" has not run yet).</param>
/// <param name="Wanted">What package.json asks for (e.g. "^1.56.0").</param>
/// <param name="Config">playwright.config.* in the folder, if any.</param>
/// <param name="UsesEdge">A config already runs the tests in the installed Microsoft Edge (channel "msedge").</param>
public sealed record PlaywrightProject(string Dir, bool HasPackageJson, string? Package, string? Version, string? Wanted, bool HasLockFile,
                                       string? Config = null, bool UsesEdge = false);

public sealed record PlaywrightBrowser(string Name, string Path);

/// <param name="DefaultParent">Where a new project goes unless the user picks another folder.</param>
public sealed record PlaywrightState(string BrowsersDir, IReadOnlyList<PlaywrightBrowser> Browsers, string? DownloadHost,
                                     bool NodeCerts, bool NodeFound, bool EdgeFound, bool VsCodeFound, string DefaultParent,
                                     PlaywrightProject? Project);

/// <summary>
/// Playwright is a per-project npm package; its browsers live in a shared cache
/// (%LOCALAPPDATA%\ms-playwright), one folder per browser build. Ember runs "npx playwright install"
/// inside the project, so the browsers match exactly the Playwright version of that project, and
/// sets up what usually breaks behind a company proxy: the proxy variables (px), the company
/// certificates for Node (NODE_EXTRA_CA_CERTS) and, if IT runs one, a mirror for the downloads.
/// When the download is blocked anyway, the tests can run in the Edge that Windows already has.
/// </summary>
public static class Playwright
{
    static readonly string[] Packages = { "@playwright/test", "playwright", "playwright-core" };

    public static string BrowsersDir =>
        Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH") is { Length: > 0 } custom && custom != "0"
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright");

    public static PlaywrightState State(TeamConfig team, string? projectDir)
    {
        var project = string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir) ? null : Inspect(projectDir);
        return new(BrowsersDir, Browsers(), team.Playwright?.DownloadHost, NodeCertificates.Configured, Tools.Node != null,
                   EdgePath != null, Tools.VsCodeExe != null, DefaultParent(project?.Dir), project);
    }

    /// <summary>The Edge that Playwright starts for channel "msedge" (the same places Playwright looks).</summary>
    public static string? EdgePath =>
        new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.LocalApplicationData }
            .Select(Environment.GetFolderPath)
            .Where(d => d.Length > 0)
            .Select(d => Path.Combine(d, "Microsoft", "Edge", "Application", "msedge.exe"))
            .FirstOrDefault(File.Exists);

    /// <summary>Next to the current project, else %USERPROFILE%\source\repos (Visual Studio's default), else the profile.</summary>
    static string DefaultParent(string? projectDir)
    {
        if (projectDir != null && Path.GetDirectoryName(projectDir) is { } parent && Directory.Exists(parent)) return parent;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var repos = Path.Combine(profile, "source", "repos");
        return Directory.Exists(repos) ? repos : profile;
    }

    public static List<PlaywrightBrowser> Browsers()
    {
        var dir = BrowsersDir;
        if (!Directory.Exists(dir)) return new List<PlaywrightBrowser>();
        return Directory.GetDirectories(dir)
                        .Select(d => new PlaywrightBrowser(Path.GetFileName(d), d))
                        .Where(b => !b.Name.StartsWith('.'))
                        .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
    }

    public static PlaywrightProject Inspect(string dir)
    {
        var packageJson = Path.Combine(dir, "package.json");
        bool lockFile = File.Exists(Path.Combine(dir, "package-lock.json"));
        var config = PlaywrightTemplate.ConfigNames.FirstOrDefault(n => File.Exists(Path.Combine(dir, n)));
        bool edge = config != null && File.ReadAllText(Path.Combine(dir, config)).Contains("msedge", StringComparison.Ordinal) ||
                    PlaywrightTemplate.ConfigNames.Any(n => File.Exists(Path.Combine(dir, n.Replace("playwright.config.", "playwright.msedge.config."))));
        if (!File.Exists(packageJson)) return new PlaywrightProject(dir, false, null, null, null, lockFile, config, edge);

        string? package = null, wanted = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(packageJson));
            foreach (var section in new[] { "devDependencies", "dependencies" })
            {
                if (package != null || !doc.RootElement.TryGetProperty(section, out var deps) || deps.ValueKind != JsonValueKind.Object) continue;
                foreach (var name in Packages)
                    if (deps.TryGetProperty(name, out var v)) { package = name; wanted = v.GetString(); break; }
            }
        }
        catch (JsonException ex)
        {
            throw new EmberException("playwrightPackageJson", ("detail", ex.Message));
        }

        string? installed = null;
        if (package != null)
        {
            var modulePackage = Path.Combine(new[] { dir, "node_modules" }.Concat(package.Split('/')).Append("package.json").ToArray());
            if (File.Exists(modulePackage))
            {
                using var mod = JsonDocument.Parse(File.ReadAllText(modulePackage));
                installed = mod.RootElement.Str("version");
            }
        }
        return new PlaywrightProject(dir, true, package, installed, wanted, lockFile, config, edge);
    }

    /// <summary>
    /// Browsers for an existing project. With <paramref name="add"/>, a project without Playwright gets it
    /// first (npm init if there is no package.json yet, then @playwright/test and the template files that are missing).
    /// </summary>
    /// <param name="mode">"chromium", "all" or "msedge" (installed Edge, nothing to download).</param>
    public static List<SetupStep> InstallSteps(string dir, string mode, bool add, EffectiveSettings eff, TeamConfig team)
    {
        var run = new Run(dir, PlaywrightTemplate.Mode(mode), eff, team);
        var steps = new List<SetupStep>
        {
            new(L.T("playwright.steps.project"), (step, ct) =>
            {
                run.RequireNode();
                var project = Inspect(dir);
                if (!add && !project.HasPackageJson) throw new EmberException("playwrightNoProject", ("path", dir));
                if (!add && project.Package == null) throw new EmberException("playwrightNotInProject", ("path", dir));
                step.Report(project.Package != null ? $"{project.Package} {project.Version ?? project.Wanted ?? "?"} · {dir}" : L.T("playwright.willAdd", ("path", dir)));
                return Task.CompletedTask;
            }),
        };
        steps.AddRange(run.Steps());
        return steps;
    }

    /// <summary>A new project from the Ember template in parent\name, then npm and the browsers (or Edge).</summary>
    public static List<SetupStep> CreateSteps(string parent, string name, string mode, EffectiveSettings eff, TeamConfig team)
    {
        var dir = Path.Combine(parent, name);
        var run = new Run(dir, PlaywrightTemplate.Mode(mode), eff, team);
        var steps = new List<SetupStep>
        {
            new(L.T("playwright.steps.template"), (step, ct) =>
            {
                run.RequireNode();
                var files = PlaywrightTemplate.Write(parent, name, run.Mode);
                step.Report(L.T("playwright.created", ("files", string.Join(", ", files)), ("path", dir)));
                return Task.CompletedTask;
            }),
        };
        steps.AddRange(run.Steps());
        return steps;
    }

    /// <summary>What install and create have in common: environment, npm, browsers (or Edge), check.</summary>
    sealed class Run(string dir, string mode, EffectiveSettings eff, TeamConfig team)
    {
        public string Mode => mode;
        Dictionary<string, string> _env = new();
        bool _edge => mode == "msedge";
        readonly string? _npm = Tools.Npm, _npx = Tools.Npx;

        public void RequireNode()
        {
            if (_npm == null || _npx == null) throw new EmberException("playwrightNoNode");
        }

        public IEnumerable<SetupStep> Steps()
        {
            yield return new(L.T("playwright.steps.environment"), async (step, ct) =>
            {
                _env = await EnvironmentAsync(eff, team);
                // With Edge nothing needs downloading, also not by the older "playwright" package on install.
                if (_edge) _env["PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD"] = "1";
                step.Report(string.Join(" · ", _env.Where(e => e.Key != "NO_PROXY").Select(e => $"{e.Key}={e.Value}")));
            });
            yield return new(L.T("playwright.steps.npm"), async (step, ct) =>
            {
                RequireNode();
                step.SetProgress(null);
                var project = Inspect(dir);
                if (project.Package == null)
                {
                    if (!project.HasPackageJson)
                    {
                        var (initCode, initLines) = await RunAsync($"\"{_npm}\" init -y", dir, _env, step, ct, 120);
                        if (initCode != 0) throw Explain(initLines, "npm init");
                    }
                    var (code, lines) = await RunAsync($"\"{_npm}\" install -D @playwright/test@latest @types/node --no-fund --no-audit", dir, _env, step, ct, 30 * 60);
                    if (code != 0) throw Explain(lines, "npm install -D @playwright/test");
                    var written = PlaywrightTemplate.Scaffold(dir, mode);
                    project = Inspect(dir);
                    step.Report(L.T("playwright.added", ("version", project.Version ?? "?"), ("files", written.Count > 0 ? string.Join(", ", written) : "–")));
                    return;
                }
                if (project.Version != null)
                {
                    step.Report(L.T("playwright.alreadyInstalled", ("version", project.Version)));
                    return;
                }
                var command = project.HasLockFile ? "ci" : "install";
                var (c, l) = await RunAsync($"\"{_npm}\" {command} --no-fund --no-audit", dir, _env, step, ct, 30 * 60);
                if (c != 0) throw Explain(l, "npm " + command);
                project = Inspect(dir);
                step.Report($"{project.Package} {project.Version}");
            });
            if (_edge)
            {
                yield return new(L.T("playwright.steps.edge"), (step, ct) =>
                {
                    if (EdgePath == null) throw new EmberException("playwrightNoEdge");
                    var setup = PlaywrightTemplate.UseEdge(dir);
                    step.Report(L.T(setup.Created ? "playwright.edgeCreated" : "playwright.edgeExisting", ("file", setup.File), ("command", setup.Command)));
                    return Task.CompletedTask;
                });
                yield return new(L.T("playwright.steps.check"), (step, ct) =>
                {
                    var edge = EdgePath ?? throw new EmberException("playwrightNoEdge");
                    var version = FileVersionInfo.GetVersionInfo(edge).ProductVersion ?? "?";
                    step.Report(L.T("playwright.edgeFound", ("version", version), ("path", edge)));
                    return Task.CompletedTask;
                });
                yield break;
            }
            yield return new(L.T("playwright.steps.browsers"), async (step, ct) =>
            {
                step.SetProgress(null);
                var which = mode == "all" ? "" : " chromium";
                var (code, lines) = await RunAsync($"\"{_npx}\" --no playwright install{which}", dir, _env, step, ct, 60 * 60);
                if (code != 0) throw Explain(lines, "npx playwright install");
                step.Report(lines.LastOrDefault(l => l.Trim().Length > 0) ?? "OK");
            });
            yield return new(L.T("playwright.steps.check"), (step, ct) =>
            {
                var found = Browsers();
                if (found.Count == 0) throw new EmberException("playwrightNoBrowsers", ("path", BrowsersDir));
                step.Report(L.T("playwright.browsers", ("list", string.Join(", ", found.Select(b => b.Name))), ("path", BrowsersDir)));
                return Task.CompletedTask;
            });
        }
    }

    /// <summary>The variables npm, npx and the Playwright downloader need behind the company proxy.</summary>
    internal static async Task<Dictionary<string, string>> EnvironmentAsync(EffectiveSettings eff, TeamConfig team)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? proxy = await Net.PortOpenAsync("127.0.0.1", eff.Port, 800) ? eff.PxUrl
                        : eff.UpstreamHostPort.Length > 0 ? "http://" + eff.UpstreamHostPort : null;
        if (proxy != null)
        {
            foreach (var name in new[] { "HTTPS_PROXY", "HTTP_PROXY", "npm_config_proxy", "npm_config_https_proxy" }) env[name] = proxy;
            env["NO_PROXY"] = eff.NoProxyList;
        }
        if (UserEnvironment.Get(NodeCertificates.Variable) is { Length: > 0 } certs && File.Exists(certs)) env[NodeCertificates.Variable] = certs;
        if (team.Playwright?.DownloadHost is { Length: > 0 } host) env["PLAYWRIGHT_DOWNLOAD_HOST"] = host.TrimEnd('/');
        return env;
    }

    /// <summary>Runs an npm/npx command through cmd.exe in the project folder; every line goes to the log (source "playwright").</summary>
    static async Task<(int Code, List<string> Lines)> RunAsync(string commandLine, string dir, Dictionary<string, string> env,
                                                                SetupStep step, CancellationToken ct, int timeoutSec)
    {
        var psi = new ProcessStartInfo(ProcessRunner.SystemExe("cmd.exe"))
        {
            Arguments = "/d /s /c \"" + commandLine + "\"",
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var (k, v) in env) psi.Environment[k] = v;
        psi.Environment["FORCE_COLOR"] = "0";
        Log.Debug($"> {commandLine}  (in {dir})");

        var lines = new List<string>();
        using var process = new Process { StartInfo = psi };
        void OnLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (lines) lines.Add(line);
            Log.Info(line.Trim(), "playwright");
            var m = Regex.Match(line, @"(\d{1,3})% of ([\d.,]+\s*\w+)");
            if (m.Success)
            {
                step.SetProgress(double.Parse(m.Groups[1].Value));
                step.Report($"{m.Groups[1].Value} % · {m.Groups[2].Value}", log: false);
            }
            else if (line.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase) || line.Contains("downloaded to", StringComparison.OrdinalIgnoreCase))
            {
                step.Report(line.Trim(), log: false);
            }
        }
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            ct.ThrowIfCancellationRequested();
            throw new EmberException("processTimeout", ("file", "npx"), ("seconds", timeoutSec));
        }
        Log.Debug($"  exit code {process.ExitCode}");
        lock (lines) return (process.ExitCode, lines.ToList());
    }

    /// <summary>Turns the usual failures behind a company proxy into a message that says what to do.</summary>
    internal static EmberException Explain(IReadOnlyList<string> lines, string command)
    {
        var text = string.Join("\n", lines);
        if (Regex.IsMatch(text, "self[- ]signed certificate|unable to get local issuer certificate|UNABLE_TO_VERIFY_LEAF_SIGNATURE|SELF_SIGNED_CERT_IN_CHAIN|CERT_UNTRUSTED", RegexOptions.IgnoreCase))
            return new EmberException("playwrightCertificates");
        if (Regex.IsMatch(text, @"\b407\b|Proxy Authentication Required", RegexOptions.IgnoreCase))
            return new EmberException("playwrightProxyAuth");
        if (Regex.IsMatch(text, @"ECONNRESET|ETIMEDOUT|ECONNREFUSED|ENOTFOUND|EAI_AGAIN|socket hang up|getaddrinfo|\b403\b|Forbidden|Download failed", RegexOptions.IgnoreCase))
            return new EmberException("playwrightBlocked");
        var last = lines.LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "?";
        return new EmberException("commandFailed", ("command", command), ("detail", last));
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ember.Core;

/// <param name="Package">@playwright/test, playwright or playwright-core – whichever the project uses.</param>
/// <param name="Version">Installed in node_modules (null if "npm install" has not run yet).</param>
/// <param name="Wanted">What package.json asks for (e.g. "^1.56.0").</param>
public sealed record PlaywrightProject(string Dir, bool HasPackageJson, string? Package, string? Version, string? Wanted, bool HasLockFile);

public sealed record PlaywrightBrowser(string Name, string Path);

public sealed record PlaywrightState(string BrowsersDir, IReadOnlyList<PlaywrightBrowser> Browsers, string? DownloadHost,
                                     bool NodeCerts, bool NodeFound, PlaywrightProject? Project);

/// <summary>
/// Playwright is a per-project npm package; its browsers live in a shared cache
/// (%LOCALAPPDATA%\ms-playwright), one folder per browser build. Ember runs "npx playwright install"
/// inside the project, so the browsers match exactly the Playwright version of that project, and
/// sets up what usually breaks behind a company proxy: the proxy variables (px), the company
/// certificates for Node (NODE_EXTRA_CA_CERTS) and, if IT runs one, a mirror for the downloads.
/// </summary>
public static class Playwright
{
    static readonly string[] Packages = { "@playwright/test", "playwright", "playwright-core" };

    public static string BrowsersDir =>
        Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH") is { Length: > 0 } custom && custom != "0"
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright");

    public static PlaywrightState State(TeamConfig team, string? projectDir) =>
        new(BrowsersDir, Browsers(), team.Playwright?.DownloadHost, NodeCertificates.Configured, Tools.Node != null,
            string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir) ? null : Inspect(projectDir));

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
        if (!File.Exists(packageJson)) return new PlaywrightProject(dir, false, null, null, null, lockFile);

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
        return new PlaywrightProject(dir, true, package, installed, wanted, lockFile);
    }

    /// <param name="browsers">"chromium" or "all".</param>
    public static List<SetupStep> InstallSteps(string dir, string browsers, EffectiveSettings eff, TeamConfig team)
    {
        PlaywrightProject? project = null;
        Dictionary<string, string> env = new();
        var npm = Tools.Npm;
        var npx = Tools.Npx;

        return new List<SetupStep>
        {
            new(L.T("playwright.steps.project"), (step, ct) =>
            {
                if (npm == null || npx == null) throw new EmberException("playwrightNoNode");
                project = Inspect(dir);
                if (!project.HasPackageJson) throw new EmberException("playwrightNoProject", ("path", dir));
                if (project.Package == null) throw new EmberException("playwrightNotInProject", ("path", dir));
                step.Report($"{project.Package} {project.Version ?? project.Wanted ?? "?"} · {dir}");
                return Task.CompletedTask;
            }),
            new(L.T("playwright.steps.environment"), async (step, ct) =>
            {
                env = await EnvironmentAsync(eff, team);
                step.Report(string.Join(" · ", env.Where(e => e.Key != "NO_PROXY").Select(e => $"{e.Key}={e.Value}")));
            }),
            new(L.T("playwright.steps.npm"), async (step, ct) =>
            {
                step.SetProgress(null);
                var command = project!.HasLockFile ? "ci" : "install";
                var (code, lines) = await RunAsync($"\"{npm}\" {command} --no-fund --no-audit", dir, env, step, ct, 30 * 60);
                if (code != 0) throw Explain(lines, "npm " + command);
                project = Inspect(dir);
                step.Report($"{project.Package} {project.Version}");
            }, () => project?.Version != null ? L.T("playwright.alreadyInstalled", ("version", project.Version)) : null),
            new(L.T("playwright.steps.browsers"), async (step, ct) =>
            {
                step.SetProgress(null);
                var which = browsers == "all" ? "" : " chromium";
                var (code, lines) = await RunAsync($"\"{npx}\" --no playwright install{which}", dir, env, step, ct, 60 * 60);
                if (code != 0) throw Explain(lines, "npx playwright install");
                step.Report(lines.LastOrDefault(l => l.Trim().Length > 0) ?? "OK");
            }),
            new(L.T("playwright.steps.check"), (step, ct) =>
            {
                var found = Browsers();
                if (found.Count == 0) throw new EmberException("playwrightNoBrowsers", ("path", BrowsersDir));
                step.Report(L.T("playwright.browsers", ("list", string.Join(", ", found.Select(b => b.Name))), ("path", BrowsersDir)));
                return Task.CompletedTask;
            }),
        };
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

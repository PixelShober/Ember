using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ember.Core;

public sealed record ReleaseInfo(string Tag, IReadOnlyList<string> AssetUrls);

public sealed record ProxyDetectionResult(string? PacUrl, string? SystemProxy, IReadOnlyList<string> PacProxies);

/// <summary>HTTP, proxy detection and GitHub queries.</summary>
public static class Net
{
    public static string UserAgent => "Ember/" + AppInfo.Version;

    /// <summary>
    /// HttpClient that signs in at the proxy with the Windows login (Kerberos/NTLM via SSPI).
    /// proxy == null means: Windows system proxy (including PAC/WPAD).
    /// </summary>
    public static HttpClient CreateClient(IWebProxy? proxy, TimeSpan timeout)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true, UseProxy = true };
        if (proxy != null) handler.Proxy = proxy;
        else handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;

        var http = new HttpClient(handler) { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);   // GitHub requires a User-Agent
        return http;
    }

    public static IWebProxy? UpstreamProxy(EffectiveSettings s) =>
        s.UpstreamHostPort.Length == 0
            ? null
            : new WebProxy("http://" + s.UpstreamHostPort) { UseDefaultCredentials = true };

    /// <summary>
    /// The best way out right now: px if it is running (it handles Kerberos for us), otherwise the
    /// company proxy with the Windows login, otherwise the system proxy.
    /// </summary>
    public static async Task<(HttpClient Client, string Route)> SmartClientAsync(EffectiveSettings s, TimeSpan timeout)
    {
        if (await PortOpenAsync("127.0.0.1", s.Port, 500))
            return (CreateClient(new WebProxy(s.PxUrl), timeout), "px " + s.PxUrl);
        var upstream = UpstreamProxy(s);
        return upstream != null
            ? (CreateClient(upstream, timeout), s.UpstreamHostPort)
            : (CreateClient(null, timeout), L.T("net.systemProxy"));
    }

    public static async Task<bool> PortOpenAsync(string host, int port, int timeoutMs = 1500)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // --- Proxy detection -----------------------------------------------------------

    const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public static string? ReadPacFromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey);
        var value = key?.GetValue("AutoConfigURL") as string;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string? ReadSystemProxyFromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey);
        if (key?.GetValue("ProxyEnable") is not int enabled || enabled != 1) return null;
        var server = key.GetValue("ProxyServer") as string;
        return string.IsNullOrWhiteSpace(server) ? null : ParseProxyServer(server);
    }

    /// <summary>"http=proxy:80;https=proxy:8080" or "proxy:80" → host:port (https preferred).</summary>
    internal static string ParseProxyServer(string server)
    {
        var m = Regex.Match(server, @"https=([^;]+)");
        if (!m.Success) m = Regex.Match(server, @"http=([^;]+)");
        return Regex.Replace(m.Success ? m.Groups[1].Value : server, "^https?://", "").Trim();
    }

    /// <summary>Reads PAC URL and system proxy and extracts the PROXY entries from the PAC file.</summary>
    public static async Task<ProxyDetectionResult> DetectProxyAsync(CancellationToken ct)
    {
        var pac = ReadPacFromRegistry();
        var system = ReadSystemProxyFromRegistry();
        var entries = new List<string>();

        if (pac != null)
        {
            try
            {
                // The PAC file is internal, so it is loaded without a proxy.
                using var http = CreateClient(new WebProxy(), TimeSpan.FromSeconds(15));
                var text = await http.GetStringAsync(pac, ct);
                entries = PacProxies(text);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Log.Warn(L.T("net.pacFailed", ("detail", Describe(ex))));
            }
        }
        return new ProxyDetectionResult(pac, system, entries);
    }

    internal static List<string> PacProxies(string pacText) =>
        Regex.Matches(pacText, @"PROXY\s+([\w.\-]+:\d+)")
             .Select(m => m.Groups[1].Value)
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .ToList();

    // --- GitHub ------------------------------------------------------------------

    /// <summary>
    /// Latest (non-pre) release of a GitHub repo. Uses the API first; if its rate limit is hit
    /// (many users behind one company IP), falls back to the normal release page.
    /// </summary>
    public static async Task<ReleaseInfo> GetLatestReleaseAsync(HttpClient http, string repo, CancellationToken ct)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest"))
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                Log.Warn(L.T("net.githubRateLimit"));
            }
            else
            {
                EnsureOk(response);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "?";
                var urls = doc.RootElement.GetProperty("assets").EnumerateArray()
                              .Select(a => a.GetProperty("browser_download_url").GetString())
                              .OfType<string>()
                              .ToList();
                return new ReleaseInfo(tag, urls);
            }
        }

        // Fallback: /releases/latest redirects to /releases/tag/<tag>
        var web = "https://github.com/" + repo;
        using var latest = await http.GetAsync(web + "/releases/latest", HttpCompletionOption.ResponseHeadersRead, ct);
        EnsureOk(latest);
        var path = latest.RequestMessage?.RequestUri?.AbsolutePath ?? "";
        var tag2 = path[(path.LastIndexOf('/') + 1)..];
        if (tag2.Length == 0 || tag2 == "latest")
            throw new EmberException("releaseTagUnknown", ("repo", repo));

        var html = await http.GetStringAsync(web + "/releases/expanded_assets/" + tag2, ct);
        var assets = Regex.Matches(html, "href=\"(/" + Regex.Escape(repo) + "/releases/download/[^\"]+)\"")
                          .Select(m => "https://github.com" + WebUtility.HtmlDecode(m.Groups[1].Value))
                          .Distinct()
                          .ToList();
        return new ReleaseInfo(tag2, assets);
    }

    public static void EnsureOk(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var code = (int)response.StatusCode;
        var hint = code == 407 ? " – " + L.T("errors.proxyAuth") : "";
        throw new HttpRequestException(
            $"HTTP {code} {response.ReasonPhrase} – {response.RequestMessage?.RequestUri}{hint}",
            null, response.StatusCode);
    }

    public static string FileName(string url)
    {
        var path = url.Split('?')[0];
        return Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
    }

    /// <summary>
    /// Downloads a file with progress. With resume == true an existing "target.part" is continued
    /// via an HTTP Range request (large model files).
    /// </summary>
    public static async Task<long> DownloadAsync(HttpClient http, string url, string target,
                                                 Action<long, long?>? progress, CancellationToken ct, bool resume = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var part = target + ".part";
        long existing = resume && File.Exists(part) ? new FileInfo(part).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (existing > 0 && response.StatusCode != HttpStatusCode.PartialContent) existing = 0;   // server ignores Range
        EnsureOk(response);

        long? total = response.Content.Headers.ContentLength is { } len ? len + existing : null;
        long done = existing;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var file = new FileStream(part, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[1 << 17];
            var sw = Stopwatch.StartNew();
            var last = TimeSpan.Zero;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (sw.Elapsed - last > TimeSpan.FromMilliseconds(250))
                {
                    last = sw.Elapsed;
                    progress?.Invoke(done, total);
                }
            }
        }
        progress?.Invoke(done, total);
        File.Move(part, target, overwrite: true);
        return done;
    }

    public static async Task<string> Sha256Async(string file, CancellationToken ct)
    {
        await using var fs = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
    }

    public static string Mb(long bytes) => $"{bytes / 1048576.0:0.0} MB";

    /// <summary>Error message including all inner exceptions, without repetitions.</summary>
    public static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (!string.IsNullOrWhiteSpace(e.Message) && !parts.Contains(e.Message)) parts.Add(e.Message);
        var sb = new StringBuilder(string.Join(" | ", parts));
        if (ex is TaskCanceledException && ex.InnerException is TimeoutException)
            sb.Append(" (").Append(L.T("errors.timeoutSuffix")).Append(')');
        return sb.ToString();
    }
}

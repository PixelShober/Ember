using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ember.Core;

/// <param name="Sha256">SHA-256 per asset file name, where GitHub publishes it (asset "digest").</param>
public sealed record ReleaseInfo(string Tag, IReadOnlyList<string> AssetUrls, IReadOnlyDictionary<string, string>? Sha256 = null);

/// <param name="PacDirect">Hosts, domains and networks the PAC file sends DIRECT: the internal ones, without proxy.</param>
public sealed record ProxyDetectionResult(string? PacUrl, string? SystemProxy, IReadOnlyList<string> PacProxies, IReadOnlyList<string>? PacDirect = null);

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
        var direct = new List<string>();

        if (pac != null)
        {
            try
            {
                // The PAC file is internal, so it is loaded without a proxy.
                using var http = CreateClient(new WebProxy(), TimeSpan.FromSeconds(15));
                var text = await http.GetStringAsync(pac, ct);
                entries = PacProxies(text);
                direct = PacDirect(text);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Log.Warn(L.T("net.pacFailed", ("detail", Describe(ex))));
            }
        }
        return new ProxyDetectionResult(pac, system, entries, direct);
    }

    /// <summary>
    /// The internal destinations of a PAC file: what its conditions send "DIRECT". Reads
    /// dnsDomainIs, shExpMatch, localHostOrDomainIs, host == "…" and isInNet (as CIDR), also when
    /// the names sit in an array the condition loops over. isPlainHostName needs no entry.
    /// </summary>
    internal static List<string> PacDirect(string pacText)
    {
        var text = Regex.Replace(pacText, @"/\*.*?\*/|//[^\n]*", "", RegexOptions.Singleline);
        var arrays = Regex.Matches(text, @"\b(?:var|let|const)\s+(\w+)\s*=\s*(?:new\s+Array\s*\()?\[?([^;\]\)]*)")
                          .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
        var found = new List<string>();
        // if ( <balanced condition> ) { return "DIRECT"
        var direct = new Regex(@"if\s*\((?<c>(?:[^()]|\((?<d>)|\)(?<-d>))*(?(d)(?!)))\)\s*\{?\s*return\s*[""']DIRECT[""']");
        foreach (Match m in direct.Matches(text))
        {
            var cond = m.Groups["c"].Value;
            foreach (Match a in Regex.Matches(cond, @"\b(\w+)\s*\["))
                if (arrays.TryGetValue(a.Groups[1].Value, out var items)) cond += " " + string.Join(" ", Regex.Matches(items, @"[""']([^""']+)[""']").Select(i => $"shExpMatch(host, \"{i.Groups[1].Value}\")"));
            foreach (Match d in Regex.Matches(cond, @"(?:dnsDomainIs|localHostOrDomainIs)\s*\(\s*\w+\s*,\s*[""']([^""']+)[""']"))
                found.Add(d.Groups[1].Value);
            foreach (Match d in Regex.Matches(cond, @"shExpMatch\s*\(\s*\w+\s*,\s*[""']([^""']+)[""']"))
                found.Add(d.Groups[1].Value);
            foreach (Match d in Regex.Matches(cond, @"\bhost\s*===?\s*[""']([^""']+)[""']"))
                found.Add(d.Groups[1].Value);
            foreach (Match d in Regex.Matches(cond, @"isInNet\s*\([^,]+,\s*[""']([\d.]+)[""']\s*,\s*[""']([\d.]+)[""']"))
                if (MaskBits(d.Groups[2].Value) is { } bits) found.Add($"{d.Groups[1].Value}/{bits}");
        }
        return found.Select(CleanPacEntry).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
    }

    /// <summary>"*.city.net.ffm" / ".city.net.ffm" / "http://*.intra.de/*" → the domain; patterns with wildcards inside → null.</summary>
    static string? CleanPacEntry(string entry)
    {
        var e = entry.Trim().ToLowerInvariant();
        if (Regex.IsMatch(e, @"^[\d.]+/\d+$")) return e;
        e = Regex.Replace(e, @"^[a-z*]+://", "");
        e = e.Split('/')[0];
        e = Regex.Replace(e, @":\*?\d*$", "");
        e = e.TrimStart('*').TrimStart('.');
        if (e.Length == 0 || e.Contains('*') || e.Contains('?') || !e.Contains('.')) return null;
        return Regex.IsMatch(e, @"^[a-z0-9.\-]+$") ? e : null;
    }

    static int? MaskBits(string mask)
    {
        if (!IPAddress.TryParse(mask, out var ip)) return null;
        var bits = 0;
        foreach (var b in ip.GetAddressBytes()) bits += System.Numerics.BitOperations.PopCount(b);
        return bits;
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
                return ParseRelease(doc.RootElement);
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
        return new ReleaseInfo(tag2, await WebAssetsAsync(http, repo, tag2, ct));
    }

    /// <summary>
    /// The newest release, pre-releases included, that <paramref name="accept"/> takes, looking at the
    /// <paramref name="count"/> newest. llama.cpp, for one, publishes its Windows builds only as pre-releases.
    /// </summary>
    public static async Task<ReleaseInfo?> FindReleaseAsync(HttpClient http, string repo, Func<ReleaseInfo, bool> accept, int count, CancellationToken ct)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases?per_page={count}"))
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
                return doc.RootElement.EnumerateArray()
                          .Where(r => !(r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True))
                          .Select(ParseRelease)
                          .FirstOrDefault(accept);
            }
        }

        // Fallback: the tags on the release page, their assets one after the other
        var html = await http.GetStringAsync($"https://github.com/{repo}/releases", ct);
        var tags = Regex.Matches(html, "href=\"/" + Regex.Escape(repo) + "/releases/tag/([^\"]+)\"")
                        .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
                        .Distinct()
                        .Take(count);
        foreach (var tag in tags)
        {
            var release = new ReleaseInfo(tag, await WebAssetsAsync(http, repo, tag, ct));
            if (accept(release)) return release;
        }
        return null;
    }

    static ReleaseInfo ParseRelease(JsonElement release)
    {
        var tag = release.GetProperty("tag_name").GetString() ?? "?";
        var items = release.GetProperty("assets").EnumerateArray().ToList();
        var urls = items.Select(a => a.GetProperty("browser_download_url").GetString())
                        .OfType<string>()
                        .ToList();
        var sha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in items)
            if (a.TryGetProperty("digest", out var d) && d.GetString() is { } digest &&
                digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && a.GetProperty("name").GetString() is { } name)
                sha[name] = digest["sha256:".Length..].ToLowerInvariant();
        return new ReleaseInfo(tag, urls, sha);
    }

    static async Task<List<string>> WebAssetsAsync(HttpClient http, string repo, string tag, CancellationToken ct)
    {
        var html = await http.GetStringAsync($"https://github.com/{repo}/releases/expanded_assets/{tag}", ct);
        return Regex.Matches(html, "href=\"(/" + Regex.Escape(repo) + "/releases/download/[^\"]+)\"")
                    .Select(m => "https://github.com" + WebUtility.HtmlDecode(m.Groups[1].Value))
                    .Distinct()
                    .ToList();
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

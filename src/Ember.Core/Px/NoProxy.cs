using System.Text.RegularExpressions;

namespace Ember.Core;

/// <summary>
/// The internal domains (settings or team.json), written the way each tool understands them.
/// "firma.local", ".firma.local" and "*.firma.local" all mean: this domain and every host below it.
/// </summary>
public static class NoProxy
{
    public static string Domain(string entry) => entry.Trim().TrimStart('*').TrimStart('.').TrimEnd('.').ToLowerInvariant();

    /// <summary>IP addresses, ranges and subnets pass through unchanged.</summary>
    static bool IsAddress(string entry) => entry.Contains('/') || entry.Contains(':') || Regex.IsMatch(entry.Trim(), @"^[\d.*-]+$");

    /// <summary>curl and git, npm, Python, NuGet and VS Code: a plain domain matches itself and all subdomains.</summary>
    public static List<string> ForTools(IEnumerable<string> entries) =>
        entries.Select(e => IsAddress(e) ? e.Trim() : Domain(e))
               .Where(e => e.Length > 0)
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .ToList();

    /// <summary>px matches host names with wildcards, so it gets the domain itself and *.domain.</summary>
    public static List<string> ForPx(IEnumerable<string> entries) =>
        ForTools(entries).SelectMany(e => IsAddress(e) ? new[] { e } : new[] { e, "*." + e })
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToList();

    /// <summary>
    /// git: "http.&lt;url&gt;.proxy" set to "" switches the proxy off for that URL, so internal servers work
    /// even when px is not running. git's "*" covers one level of subdomains; deeper hosts are covered by
    /// NO_PROXY, which git also honours.
    /// </summary>
    public static List<string> GitUrls(IEnumerable<string> entries) =>
        ForTools(entries).Where(e => !IsAddress(e))
                         .SelectMany(d => new[] { $"https://{d}", $"https://*.{d}", $"http://{d}", $"http://*.{d}" })
                         .ToList();

    /// <summary>The URLs from "git config --get-regexp ^http\..+\.proxy$" whose proxy is switched off ("").</summary>
    public static HashSet<string> ParseGitExceptions(string output)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            var key = space < 0 ? line : line[..space];
            var value = space < 0 ? "" : line[(space + 1)..].Trim();
            if (value.Length > 0 || !key.StartsWith("http.", StringComparison.OrdinalIgnoreCase) || !key.EndsWith(".proxy", StringComparison.OrdinalIgnoreCase)) continue;
            var url = key["http.".Length..^".proxy".Length];
            if (url.Length > 0) urls.Add(url.TrimEnd('/'));
        }
        return urls;
    }
}

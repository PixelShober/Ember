using System.Security.Cryptography;
using System.Text;

namespace Ember.Core;

/// <summary>
/// Keys for the time before a team.json exists. Each key unlocks a fixed set of modules. Only the
/// SHA-256 of a key is in the code; the keys come from scripts/make-key.mjs (100 random bits each)
/// and are handed out by the team. As soon as a team.json is loaded, its roles decide instead.
/// </summary>
public static class StarterKeys
{
    static readonly (string Level, string Hash)[] Known =
    {
        ("proxy", "f81d49f0fcdb42538578c926bc43113f86e7f99518ca4c07767aea08957c968e"),
        ("proxyPlaywright", "45278f6982655ad5edfae1a08ec1fc26a097e5e6c8b6038b60a6d75b2bbb1387"),
        ("all", "36b0229843346a0bfe62ea57c687e22286305ed71ed6a750ee798f230deed86c"),
    };

    public static readonly IReadOnlyDictionary<string, string[]> LevelModules = new Dictionary<string, string[]>
    {
        ["proxy"] = new[] { "proxy" },
        ["proxyPlaywright"] = new[] { "proxy", "playwright" },
        ["all"] = new[] { ModuleIds.All },
    };

    /// <summary>The level a key unlocks ("proxy", "proxyPlaywright", "all"), or null.</summary>
    public static string? Level(string? key) => Level(key, Known);

    internal static string? Level(string? key, IEnumerable<(string Level, string Hash)> known)
    {
        var normalized = Normalize(key);
        if (normalized.Length != 20) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ember-key-v1:" + normalized))).ToLowerInvariant();
        // Fixed-time compare, although the hashes are public anyway.
        return known.FirstOrDefault(k => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(k.Hash), Encoding.ASCII.GetBytes(hash))).Level;
    }

    /// <summary>
    /// "ember-40dga m12qw…" → "40DGAM12QW…": upper case, no prefix, dashes or spaces, and the
    /// Crockford look-alikes O → 0, I/L → 1.
    /// </summary>
    internal static string Normalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        var s = new string(key.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (s.Length == 25 && s.StartsWith("EMBER", StringComparison.Ordinal)) s = s[5..];
        return s.Replace('O', '0').Replace('I', '1').Replace('L', '1');
    }
}

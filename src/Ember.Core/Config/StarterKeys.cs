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
        ("developer", "45278f6982655ad5edfae1a08ec1fc26a097e5e6c8b6038b60a6d75b2bbb1387"),
        ("all", "36b0229843346a0bfe62ea57c687e22286305ed71ed6a750ee798f230deed86c"),
    };

    public static readonly IReadOnlyDictionary<string, string[]> LevelModules = new Dictionary<string, string[]>
    {
        ["proxy"] = new[] { "proxy" },
        ["developer"] = new[] { "proxy", "playwright", "git", "software" },
        ["all"] = new[] { ModuleIds.All },
    };

    /// <summary>Developer tools a level does not offer (Bruno is for API testing, not for every developer).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> LevelHiddenTools = new Dictionary<string, string[]>
    {
        ["developer"] = new[] { "bruno" },
    };

    /// <summary>A new key: 100 random bits as EMBER-XXXXX-XXXXX-XXXXX-XXXXX (Crockford base32).</summary>
    public static string Generate()
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var bytes = RandomNumberGenerator.GetBytes(13);
        var bits = new System.Numerics.BigInteger(bytes, isUnsigned: true);
        var chars = new char[20];
        for (int i = 0; i < 20; i++)
        {
            chars[i] = alphabet[(int)(bits & 31)];
            bits >>= 5;
        }
        var s = new string(chars);
        return $"EMBER-{s[..5]}-{s[5..10]}-{s[10..15]}-{s[15..]}";
    }

    /// <summary>Two keys are the same, whatever dashes, spaces or case.</summary>
    public static bool Same(string? a, string? b)
    {
        var x = Normalize(a);
        var y = Normalize(b);
        return x.Length == 20 && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(x), Encoding.ASCII.GetBytes(y));
    }

    /// <summary>The level a key unlocks ("proxy", "developer", "all"), or null.</summary>
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

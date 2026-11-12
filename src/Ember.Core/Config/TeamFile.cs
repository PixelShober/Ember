using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ember.Core;

/// <param name="Payload">The team configuration as JSON text, exactly as it was signed.</param>
/// <param name="KeyId">Fingerprint of the signing key (first 16 hex digits of the SHA-256 of its public key).</param>
/// <param name="Trusted">The signature is valid and the key is one this Ember build trusts.</param>
public sealed record TeamFileContent(string Payload, string? KeyId, bool Signed, bool Trusted);

/// <summary>
/// The signed team file (team.ember). The team configuration stays readable JSON, but it travels in
/// an envelope with an ECDSA P-256 signature. Ember carries only the public keys of the admins
/// (config/team-signing.pub, built in); the private key lies with the admins, protected by a
/// password. A team file someone built or changed themselves has no valid signature and is refused.
/// The payload also names the path it belongs to and counts its version up, so an old or copied file
/// does not help either. Like the roles, this keeps the normal Ember build doing only what the admins
/// allowed; Ember still runs with the user's own Windows rights.
/// </summary>
public static class TeamFile
{
    public const string Format = "ember-team/1";
    public const string Extension = ".ember";

    // ------------------------------------------------------------------ trusted keys

    static readonly Lazy<IReadOnlyList<ECDsa>> Keys = new(LoadTrustedKeys);

    /// <summary>Public keys built into this Ember version. None: the build checks no signatures yet.</summary>
    public static IReadOnlyList<ECDsa> TrustedKeys => Keys.Value;

    /// <summary>Signatures are required as soon as a public key is built in.</summary>
    public static bool SignatureRequired => TrustedKeys.Count > 0;

    static IReadOnlyList<ECDsa> LoadTrustedKeys()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Ember.team-signing.pub");
        if (stream == null) return Array.Empty<ECDsa>();
        return ParsePublicKeys(new StreamReader(stream).ReadToEnd());
    }

    /// <summary>All PEM public keys in a text; comments and empty lines around them are fine.</summary>
    internal static IReadOnlyList<ECDsa> ParsePublicKeys(string text)
    {
        var keys = new List<ECDsa>();
        const string begin = "-----BEGIN PUBLIC KEY-----", end = "-----END PUBLIC KEY-----";
        int i = 0;
        while ((i = text.IndexOf(begin, i, StringComparison.Ordinal)) >= 0)
        {
            int j = text.IndexOf(end, i, StringComparison.Ordinal);
            if (j < 0) break;
            var key = ECDsa.Create();
            key.ImportFromPem(text.AsSpan(i, j + end.Length - i));
            keys.Add(key);
            i = j + end.Length;
        }
        return keys;
    }

    public static string KeyId(ECDsa key) =>
        Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()))[..16].ToLowerInvariant();

    static bool IsTrusted(ECDsa key, IReadOnlyList<ECDsa> trusted) =>
        trusted.Any(t => t.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(key.ExportSubjectPublicKeyInfo()));

    // ------------------------------------------------------------------ the admins' signing key

    /// <summary>
    /// A new signing key: the private part as password-protected PKCS#8 PEM (AES-256, PBKDF2 with
    /// 600,000 rounds) and the public part for config/team-signing.pub.
    /// </summary>
    public static (string PrivatePem, string PublicPem, string KeyId) NewKey(string password)
    {
        if (password.Length < 12) throw new EmberException("teamKeyPasswordShort");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pbe = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000);
        return (key.ExportEncryptedPkcs8PrivateKeyPem(password, pbe), key.ExportSubjectPublicKeyInfoPem(), KeyId(key));
    }

    /// <summary>Opens the private key; a wrong password says so instead of a crypto error.</summary>
    public static ECDsa OpenKey(string file, string password)
    {
        if (!File.Exists(file)) throw new EmberException("teamKeyMissing", ("path", file));
        var key = ECDsa.Create();
        try
        {
            key.ImportFromEncryptedPem(File.ReadAllText(file), password);
            return key;
        }
        catch (CryptographicException)
        {
            key.Dispose();
            throw new EmberException("teamKeyPassword");
        }
        catch (ArgumentException)
        {
            key.Dispose();
            throw new EmberException("teamKeyInvalid", ("path", file));
        }
    }

    // ------------------------------------------------------------------ reading and writing

    public static bool IsEnvelope(string text) =>
        text.TrimStart().StartsWith('{') && text.Contains("\"" + Format + "\"", StringComparison.Ordinal);

    /// <summary>The payload of a team file and whether its signature counts. Plain JSON is returned unsigned.</summary>
    public static TeamFileContent Open(string text) => Open(text, TrustedKeys);

    internal static TeamFileContent Open(string text, IReadOnlyList<ECDsa> trusted)
    {
        if (!IsEnvelope(text)) return new TeamFileContent(text, null, false, false);
        JsonObject env;
        try
        {
            env = JsonNode.Parse(text) as JsonObject ?? throw new EmberException("teamInvalid", ("detail", "{ … }"));
        }
        catch (JsonException ex)
        {
            throw new EmberException("teamInvalid", ("detail", ex.Message));
        }
        var payloadBytes = Convert.FromBase64String(env["payload"]?.GetValue<string>() ?? "");
        var signature = Convert.FromBase64String(env["signature"]?.GetValue<string>() ?? "");
        var payload = new UTF8Encoding(false).GetString(payloadBytes);
        var keyId = env["key"]?.GetValue<string>();

        var signer = trusted.FirstOrDefault(k => KeyId(k) == keyId && k.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256));
        return new TeamFileContent(payload, keyId, true, signer != null);
    }

    /// <summary>Wraps the payload with its signature.</summary>
    public static string Seal(string payload, ECDsa key)
    {
        var bytes = new UTF8Encoding(false).GetBytes(payload);
        var envelope = new JsonObject
        {
            ["format"] = Format,
            ["key"] = KeyId(key),
            ["payload"] = Convert.ToBase64String(bytes),
            ["signature"] = Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256)),
        };
        return Json.Format(envelope) + "\n";
    }

    /// <summary>
    /// Checks what Ember requires before it uses a team file: valid signature with a built-in key,
    /// the path it was signed for, and no older version than the last one seen there.
    /// Returns the reason as a locale key, or null when the file may be used.
    /// </summary>
    public static string? Problem(TeamFileContent content, TeamConfig config, string path, int lastSeen, bool signatureRequired)
    {
        if (!signatureRequired) return null;
        if (!content.Signed) return "teamUnsigned";
        if (!content.Trusted) return "teamSignatureInvalid";
        if (config.Location is not { Length: > 0 } location || !SamePath(location, path)) return "teamWrongPlace";
        if (config.Version < lastSeen) return "teamOlder";
        return null;
    }

    // ------------------------------------------------------------------ paths

    /// <summary>Same file, whether one side says Z:\… and the other \\server\share\….</summary>
    public static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string path)
    {
        var p = path.Trim().Replace('/', '\\');
        p = ToUnc(p);
        try
        {
            if (!p.StartsWith(@"\\", StringComparison.Ordinal)) p = Path.GetFullPath(p);
        }
        catch
        {
            // not a valid path: compare as written
        }
        return p.TrimEnd('\\');
    }

    /// <summary>Z:\Team\team.ember → \\server\share\Team\team.ember for a mapped network drive.</summary>
    public static string ToUnc(string path)
    {
        if (!OperatingSystem.IsWindows() || path.Length < 2 || path[1] != ':') return path;
        var drive = path[..2];
        var size = 512;
        var sb = new StringBuilder(size);
        try
        {
            return WNetGetConnection(drive, sb, ref size) == 0 ? sb.ToString().TrimEnd('\\') + path[2..] : path;
        }
        catch
        {
            return path;
        }
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);
}

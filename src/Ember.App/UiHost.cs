using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using Ember.Core;

namespace Ember.App;

/// <summary>
/// Decides which UI package is shown: the one embedded in the exe (always works offline) or a newer
/// downloaded one from %LOCALAPPDATA%\Ember\ui. Both are served as https://ui.ember.invalid/.
/// </summary>
public sealed class UiHost
{
    public const string HostName = "ui.ember.invalid";
    public const string Origin = "https://" + HostName;

    public string? EmbeddedDir { get; private set; }
    public string EmbeddedVersion { get; private set; } = "0.0.0";
    public string? CurrentDir { get; private set; }
    public string CurrentVersion { get; private set; } = "0.0.0";
    public bool CurrentIsEmbedded => CurrentDir != null && CurrentDir == EmbeddedDir;

    /// <summary>Extracts the embedded UI (once per build) and returns the package to start with.</summary>
    public (string Dir, string Version)? Prepare()
    {
        EmbeddedDir = ExtractEmbedded();
        if (EmbeddedDir != null) EmbeddedVersion = UiPackages.ReadManifest(EmbeddedDir)?.Version ?? AppInfo.Version;

        // Test builds show exactly the UI they were built with.
        var best = AppInfo.IsPrerelease ? null : UiPackages.BestDownloaded(EmbeddedVersion);
        if (best is { } b) return Select(b.Dir, b.Manifest.Version);
        return EmbeddedDir != null ? Select(EmbeddedDir, EmbeddedVersion) : null;
    }

    public (string Dir, string Version) Select(string dir, string version)
    {
        CurrentDir = dir;
        CurrentVersion = version;
        AppInfo.UiVersion = version;
        return (dir, version);
    }

    public (string Dir, string Version)? FallbackToEmbedded() =>
        EmbeddedDir != null ? Select(EmbeddedDir, EmbeddedVersion) : null;

    static string? ExtractEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Ember.ui.zip");
        if (stream == null) return null;

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var hash = Convert.ToHexString(SHA256.HashData(ms.ToArray()))[..12].ToLowerInvariant();
        var dir = Path.Combine(Paths.UiDir, "embedded-" + hash);
        if (File.Exists(Path.Combine(dir, "index.html"))) return dir;

        var tmp = dir + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);
        ms.Position = 0;
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
            archive.ExtractToDirectory(tmp, overwriteFiles: true);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.Move(tmp, dir);

        // Old embedded copies from previous builds are no longer needed.
        foreach (var old in Directory.GetDirectories(Paths.UiDir, "embedded-*"))
            if (!old.Equals(dir, StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(old, recursive: true); } catch { }
        return dir;
    }

    /// <summary>Shown when the exe was built without ui/dist (developer machines).</summary>
    public static string PlaceholderHtml => $$"""
        <!doctype html><html lang="{{L.Language}}"><head><meta charset="utf-8"><title>Ember</title>
        <style>body{font-family:'Segoe UI Variable Text','Segoe UI',sans-serif;margin:48px;color:#1b1b1b;background:#f6f5f3}
        code{background:#e9e6e1;padding:2px 6px;border-radius:4px}</style></head>
        <body><h1>Ember</h1><p>{{L.T("app.noUi")}}</p><p><code>cd ui &amp;&amp; npm ci &amp;&amp; npm run build</code></p></body></html>
        """;
}

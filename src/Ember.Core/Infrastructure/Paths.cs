namespace Ember.Core;

/// <summary>
/// Every location Ember reads from or writes to, in one place. Everything lives under C:\Ember, so
/// there is exactly one folder to look in (and one path for shortcuts, autostart and IT rules):
/// <code>
/// C:\Ember\App        Ember.exe            C:\Ember\Logs       ember-*.log, px-debug-*.log
/// C:\Ember\px         px                   C:\Ember\Reports    diagnostic reports
/// C:\Ember\llama.cpp  llama.cpp builds     C:\Ember\Cache      UI packages, WebView2 data
/// C:\Ember\Config     settings, team cache, backups            C:\Ember\Downloads, Models, Tools, Certs, Temp
/// </code>
/// If C:\ does not let a normal user create folders (hardened machines), Ember falls back to
/// %LOCALAPPDATA%\Ember with the same layout.
/// </summary>
public static class Paths
{
    /// <summary>Preferred install root.</summary>
    public const string InstallRoot = @"C:\Ember";

    /// <summary>C:\Ember, or %LOCALAPPDATA%\Ember when C:\Ember cannot be created or written.</summary>
    public static string Root { get; } = ResolveRoot();

    public static string AppDir => Path.Combine(Root, "App");
    public static string InstalledExe => Path.Combine(AppDir, "Ember.exe");
    public static string DefaultPxDir => Path.Combine(Root, "px");
    public static string LlamaDir => Path.Combine(Root, "llama.cpp");

    public static string ConfigDir => Path.Combine(Root, "Config");
    public static string LogDir => Path.Combine(Root, "Logs");
    public static string ReportDir => Path.Combine(Root, "Reports");
    public static string CacheDir => Path.Combine(Root, "Cache");
    public static string DownloadDir => Path.Combine(Root, "Downloads");
    public static string DefaultModelsDir => Path.Combine(Root, "Models");
    public static string ToolsDir => Path.Combine(Root, "Tools");
    public static string CertsDir => Path.Combine(Root, "Certs");
    public static string TempDir => Path.Combine(Root, "Temp");

    public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");
    public static string TeamCacheFile => Path.Combine(ConfigDir, "team.cache.json");
    public static string PowerBackupFile => Path.Combine(ConfigDir, "power-backup.json");
    public static string WolBackupFile => Path.Combine(ConfigDir, "wol-backup.json");
    public static string WindowFile => Path.Combine(ConfigDir, "window.json");

    public static string UiDir => Path.Combine(CacheDir, "ui");
    public static string WebViewDataDir => Path.Combine(CacheDir, "WebView2");

    /// <summary>px keeps its own configuration in %APPDATA%\px\px.ini.</summary>
    public static string PxConfigIni =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "px", "px.ini");

    /// <summary>Where Ember 1.0 test builds kept their files (settings are taken over once, see <see cref="MigrateOldLocations"/>).</summary>
    static string OldAppData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ember");

    public static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A unique file name in C:\Ember\Temp (created on demand).</summary>
    public static string TempFile(string prefix, string extension) =>
        Path.Combine(Ensure(TempDir), $"{prefix}-{Guid.NewGuid():N}.{extension}");

    /// <summary>
    /// Copies settings, team cache and backups from %APPDATA%\Ember once, if the new location does not
    /// have them yet. The old files stay where they are (nothing is lost if something goes wrong).
    /// </summary>
    public static void MigrateOldLocations()
    {
        try
        {
            if (!Directory.Exists(OldAppData)) return;
            foreach (var name in new[] { "settings.json", "team.cache.json", "power-backup.json", "wol-backup.json", "window.json" })
            {
                var from = Path.Combine(OldAppData, name);
                var to = Path.Combine(ConfigDir, name);
                if (!File.Exists(from) || File.Exists(to)) continue;
                Ensure(ConfigDir);
                File.Copy(from, to);
                Log.Debug($"Taken over from the old location: {from} → {to}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not take over files from %APPDATA%\\Ember: " + ex.Message);
        }
    }

    static string ResolveRoot()
    {
        if (!OperatingSystem.IsWindows()) return Path.Combine(Path.GetTempPath(), "Ember");
        try
        {
            Directory.CreateDirectory(InstallRoot);
            var probe = Path.Combine(InstallRoot, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return InstallRoot;
        }
        catch
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ember");
        }
    }
}

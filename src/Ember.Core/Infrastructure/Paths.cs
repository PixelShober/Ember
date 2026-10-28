namespace Ember.Core;

/// <summary>Every location Ember reads from or writes to, in one place.</summary>
public static class Paths
{
    /// <summary>Fixed install root: one path for shortcuts, autostart and firewall rules from IT.</summary>
    public const string InstallRoot = @"C:\Ember";

    public static string AppDir => Path.Combine(InstallRoot, "App");
    public static string InstalledExe => Path.Combine(AppDir, "Ember.exe");
    public static string DefaultPxDir => Path.Combine(InstallRoot, "px");
    public static string LlamaDir => Path.Combine(InstallRoot, "llama.cpp");

    /// <summary>%APPDATA%\Ember – settings, team cache, logs and backups (roams with the profile).</summary>
    public static string AppData { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ember");

    /// <summary>%LOCALAPPDATA%\Ember – UI packages, WebView2 data, reports and downloads (stays on the machine).</summary>
    public static string LocalData { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ember");

    public static string SettingsFile => Path.Combine(AppData, "settings.json");
    public static string TeamCacheFile => Path.Combine(AppData, "team.cache.json");
    public static string LogDir => Path.Combine(AppData, "logs");
    public static string PowerBackupFile => Path.Combine(AppData, "power-backup.json");
    public static string WolBackupFile => Path.Combine(AppData, "wol-backup.json");

    public static string UiDir => Path.Combine(LocalData, "ui");
    public static string WebViewDataDir => Path.Combine(LocalData, "WebView2");
    public static string ReportDir => Path.Combine(LocalData, "reports");
    public static string DownloadDir => Path.Combine(LocalData, "downloads");
    public static string DefaultModelsDir => Path.Combine(LocalData, "models");

    /// <summary>px keeps its own configuration in %APPDATA%\px\px.ini.</summary>
    public static string PxConfigIni =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "px", "px.ini");

    public static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}

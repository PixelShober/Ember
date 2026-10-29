using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Ember.Core;

namespace Ember.App;

/// <summary>
/// Copies Ember to C:\Ember\App on start and restarts it from there. The program then always
/// lives at one fixed path – handy for shortcuts, autostart and firewall rules from IT.
/// </summary>
public static class SelfInstaller
{
    /// <summary>Command line switch to skip the copy.</summary>
    public const string PortableSwitch = "--portable";

    public enum Result { RunHere, Relaunched }

    public static Result EnsureInstalled(string[] args)
    {
        var currentDir = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
        if (currentDir.Equals(Paths.AppDir, StringComparison.OrdinalIgnoreCase))
        {
            AppInfo.Mode = "installed";
            return Result.RunHere;
        }
#if DEBUG
        AppInfo.Mode = "dev";
        Log.Debug("Debug build: no copy to " + Paths.AppDir);
        return Result.RunHere;
#else
        AppInfo.Mode = "portable";
        if (AppInfo.IsPrerelease)
        {
            Log.Info(L.T("install.prerelease", ("version", AppInfo.Version)));
            return Result.RunHere;
        }
        if (args.Contains(PortableSwitch, StringComparer.OrdinalIgnoreCase))
        {
            Log.Info(L.T("install.portable", ("path", Paths.AppDir)));
            return Result.RunHere;
        }

        // Only the single-file release (one Ember.exe) is copied; a multi-file build would be incomplete.
        var exe = Environment.ProcessPath;
        if (exe == null || File.Exists(Path.Combine(currentDir, "Ember.dll")))
        {
            Log.Info(L.T("install.notSingleFile"));
            return Result.RunHere;
        }

        try
        {
            var ownVersion = AppInfo.Version;
            var installedVersion = InstalledVersion();
            if (installedVersion != null && AppInfo.CompareVersions(installedVersion, ownVersion) > 0)
            {
                Log.Info(L.T("install.newerInstalled", ("version", installedVersion)));
                return Launch(args);
            }

            if (!SameFile(exe, Paths.InstalledExe))
            {
                Directory.CreateDirectory(Paths.AppDir);
                File.Copy(exe, Paths.InstalledExe, overwrite: true);
                // A pointer to the team configuration travels with the exe.
                var pointer = Path.Combine(currentDir, TeamConfigService.PointerFileName);
                if (File.Exists(pointer)) File.Copy(pointer, Path.Combine(Paths.AppDir, TeamConfigService.PointerFileName), overwrite: true);
                Log.Info(L.T("install.copied", ("version", ownVersion), ("path", Paths.AppDir)));
                CreateStartMenuShortcut();
            }
            return Launch(args);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warn(L.T("install.noRights", ("path", Paths.AppDir), ("detail", ex.Message)));
        }
        catch (IOException ex)
        {
            // Typically: Ember already runs from C:\Ember and the file is locked.
            Log.Warn(L.T("install.locked", ("path", Paths.AppDir), ("detail", ex.Message)));
        }
        catch (Exception ex)
        {
            Log.Warn(L.T("install.failed", ("detail", Net.Describe(ex))));
        }
        return Result.RunHere;
#endif
    }

    static string? InstalledVersion()
    {
        if (!File.Exists(Paths.InstalledExe)) return null;
        var info = FileVersionInfo.GetVersionInfo(Paths.InstalledExe);
        var v = info.ProductVersion ?? info.FileVersion;
        if (v == null) return null;
        var plus = v.IndexOf('+');
        return plus > 0 ? v[..plus] : v;
    }

    static bool SameFile(string a, string b)
    {
        if (!File.Exists(b)) return false;
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var fa = File.OpenRead(a);
        using var fb = File.OpenRead(b);
        return SHA256.HashData(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
    }

    static Result Launch(string[] args)
    {
        var psi = new ProcessStartInfo(Paths.InstalledExe) { UseShellExecute = false, WorkingDirectory = Paths.AppDir };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
        return Result.Relaunched;
    }

    /// <summary>Start menu entry "Ember" (per user, no admin rights needed).</summary>
    public static void CreateStartMenuShortcut()
    {
        try
        {
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var link = Path.Combine(programs, "Ember.lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = Paths.InstalledExe;
            shortcut.WorkingDirectory = Paths.AppDir;
            shortcut.Description = L.T("app.shortcutDescription");
            shortcut.Save();
            Log.Info(L.T("install.shortcut", ("path", link)));
        }
        catch (Exception ex)
        {
            // e.g. when Windows Script Host is disabled by policy
            Log.Warn(L.T("install.shortcutFailed", ("detail", ex.Message)));
        }
    }
}

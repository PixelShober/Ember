using System.Diagnostics;
using Ember.Core;

namespace Ember.App;

/// <summary>
/// Replaces the running exe: Windows does not allow deleting a running exe, but it does allow
/// renaming it. So: Ember.exe → Ember.exe.old, Ember.exe.new → Ember.exe, start the new one.
/// </summary>
public static class ExeUpdate
{
    public const string AfterUpdateSwitch = "--after-update";

    static string OldPath => Paths.InstalledExe + ".old";

    public static void Apply()
    {
        var exe = Paths.InstalledExe;
        var next = Updater.ExeNewPath;
        if (AppInfo.Mode != "installed" || !File.Exists(next)) throw new EmberException("exeUpdateMissing");

        try { if (File.Exists(OldPath)) File.Delete(OldPath); } catch { }
        File.Move(exe, OldPath);
        try
        {
            File.Move(next, exe);
        }
        catch
        {
            File.Move(OldPath, exe);   // roll back
            throw;
        }
        Log.Info(L.T("update.restarting"));
        Process.Start(new ProcessStartInfo(exe, AfterUpdateSwitch) { UseShellExecute = false, WorkingDirectory = Paths.AppDir })?.Dispose();
    }

    /// <summary>Deletes Ember.exe.old once the old process is gone.</summary>
    public static void CleanupOld()
    {
        for (int i = 0; i < 10 && File.Exists(OldPath); i++)
        {
            try
            {
                File.Delete(OldPath);
            }
            catch (IOException)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }
}

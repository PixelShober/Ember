namespace Ember.Core;

/// <summary>Start menu entries for the current user (no admin rights needed).</summary>
public static class Shortcut
{
    /// <summary>Creates or replaces "&lt;name&gt;.lnk" in the user's Start menu; returns false (and logs) when it fails.</summary>
    public static bool CreateInStartMenu(string name, string target, string workingDirectory, string description)
    {
        try
        {
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var link = Path.Combine(programs, name + ".lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.Description = description;
            shortcut.Save();
            Log.Info(L.T("install.shortcut", ("path", link)));
            return true;
        }
        catch (Exception ex)
        {
            // e.g. when Windows Script Host is disabled by policy
            Log.Warn(L.T("install.shortcutFailed", ("detail", ex.Message)));
            return false;
        }
    }
}

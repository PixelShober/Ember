using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Ember.Core;

/// <summary>
/// User environment variables (HKCU\Environment). PATH is edited in the registry directly, because
/// Environment.SetEnvironmentVariable would store it as REG_SZ and expand entries like %USERPROFILE%.
/// After a change, running programs (Explorer, new terminals) are told via WM_SETTINGCHANGE.
/// </summary>
public static class UserEnvironment
{
    const string Key = "Environment";

    /// <summary>Adds folders to the end of the user PATH if they are not there yet. Returns the folders that were added.</summary>
    public static List<string> AddToPath(params string[] dirs)
    {
        var added = new List<string>();
        if (!OperatingSystem.IsWindows()) return added;
        using var key = Registry.CurrentUser.CreateSubKey(Key, writable: true);
        var raw = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        var entries = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        foreach (var dir in dirs)
        {
            var full = Environment.ExpandEnvironmentVariables(dir).TrimEnd('\\');
            bool present = entries.Any(e => Environment.ExpandEnvironmentVariables(e.Trim('"')).TrimEnd('\\')
                                                       .Equals(full, StringComparison.OrdinalIgnoreCase));
            if (present) continue;
            entries.Add(dir);
            added.Add(dir);
        }
        if (added.Count == 0) return added;

        key.SetValue("Path", string.Join(";", entries), RegistryValueKind.ExpandString);
        // Ember's own process sees the new folders right away (child processes inherit them).
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", current.TrimEnd(';') + ";" + string.Join(";", added.Select(Environment.ExpandEnvironmentVariables)));
        Broadcast();
        Log.Info(L.T("devtools.pathAdded", ("dirs", string.Join(", ", added))));
        return added;
    }

    public static string? Get(string name) =>
        OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) : null;

    /// <summary>Sets (or with null removes) a user variable and tells running programs.</summary>
    public static void Set(string name, string? value)
    {
        if (!OperatingSystem.IsWindows()) return;
        using (var key = Registry.CurrentUser.CreateSubKey(Key, writable: true))
        {
            if (value == null) key.DeleteValue(name, throwOnMissingValue: false);
            else key.SetValue(name, value, RegistryValueKind.String);
        }
        Environment.SetEnvironmentVariable(name, value);
        Broadcast();
    }

    static void Broadcast()
    {
        try
        {
            SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "Environment", 0x0002, 5000, out _);
        }
        catch
        {
            // only a courtesy for running programs
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}

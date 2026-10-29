namespace Ember.Core;

/// <summary>Finds developer tools on this machine.</summary>
public static class Tools
{
    public static string? Git => Find("git.exe", @"%ProgramFiles%\Git\cmd", @"%LocalAppData%\Programs\Git\cmd");
    public static string? Npm => Find("npm.cmd", @"%ProgramFiles%\nodejs", EmberNode, @"%AppData%\npm");
    public static string? Npx => Find("npx.cmd", @"%ProgramFiles%\nodejs", EmberNode, @"%AppData%\npm");
    public static string? Node => Find("node.exe", @"%ProgramFiles%\nodejs", EmberNode);

    /// <summary>Node.js installed by Ember (zip, per user).</summary>
    static string EmberNode => Path.Combine(Paths.ToolsDir, "node");
    public static string? Dotnet => Find("dotnet.exe", @"%ProgramFiles%\dotnet", @"%LocalAppData%\Microsoft\dotnet");
    public static string? NvidiaSmi => Find("nvidia-smi.exe", @"%SystemRoot%\System32", @"%ProgramFiles%\NVIDIA Corporation\NVSMI");

    public static string PipIni =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pip", "pip.ini");

    public static string NuGetConfig =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NuGet", "NuGet.Config");

    public static string VsCodeSettings =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "settings.json");

    public static bool VsCodeInstalled =>
        File.Exists(VsCodeSettings) ||
        Find("code.cmd", @"%LocalAppData%\Programs\Microsoft VS Code\bin", @"%ProgramFiles%\Microsoft VS Code\bin") != null;

    /// <summary>Code.exe next to the "code" command (user or machine installation).</summary>
    public static string? VsCodeExe =>
        Find("code.cmd", @"%LocalAppData%\Programs\Microsoft VS Code\bin", @"%ProgramFiles%\Microsoft VS Code\bin") is { } cmd &&
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(cmd)!, "..", "Code.exe")) is var exe && File.Exists(exe) ? exe : null;

    /// <summary>Searches PATH (process, user and machine – also freshly installed tools) and extra folders.</summary>
    public static string? Find(string exe, params string[] extraDirs)
    {
        var path = string.Join(";",
            Environment.GetEnvironmentVariable("PATH") ?? "",
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "",
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "");

        var dirs = path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Concat(extraDirs);
        foreach (var dir in dirs)
        {
            try
            {
                var full = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim('"')), exe);
                if (File.Exists(full)) return full;
            }
            catch
            {
                // invalid PATH entry
            }
        }
        return null;
    }
}

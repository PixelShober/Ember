using System.Reflection;

namespace Ember.Core;

/// <summary>Version and identity of the running Ember.</summary>
public static class AppInfo
{
    /// <summary>
    /// Version of the bridge API between exe and UI. Raise it when the UI needs a host method that
    /// older exes do not have; a UI package with a higher "requiresBridge" is then only installed
    /// together with a new exe.
    /// </summary>
    public const int BridgeVersion = 1;

    public const string GitHubRepo = "PixelShober/Ember";

    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// CI and local builds ("0.0.0-ci.12", "0.0.0-dev") run portable, use only their own UI and never
    /// auto-update – otherwise testing a pull request build would show the last release instead.
    /// </summary>
    public static bool IsPrerelease => Version.Contains('-');

    /// <summary>Set by the host once it knows which UI package is shown.</summary>
    public static string UiVersion { get; set; } = "?";

    /// <summary>"installed" (C:\Ember\App), "portable" or "dev".</summary>
    public static string Mode { get; set; } = "portable";

    static string ReadVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info)) return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = info.IndexOf('+');   // strip "+commit" source revision
        return plus > 0 ? info[..plus] : info;
    }

    /// <summary>Compares "1.2.3" style versions; pre-release suffixes ("-dev") sort before the release.</summary>
    public static int CompareVersions(string a, string b)
    {
        static (Version v, bool pre) Parse(string s)
        {
            s = s.Trim().TrimStart('v', 'V');
            var dash = s.IndexOf('-');
            var core = dash >= 0 ? s[..dash] : s;
            return (System.Version.TryParse(core, out var v) ? v : new Version(0, 0), dash >= 0);
        }
        var (va, pa) = Parse(a);
        var (vb, pb) = Parse(b);
        int c = va.CompareTo(vb);
        if (c != 0) return c;
        return pa == pb ? 0 : pa ? -1 : 1;
    }
}

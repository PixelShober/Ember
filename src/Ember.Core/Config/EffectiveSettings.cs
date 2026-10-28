using System.Text.RegularExpressions;

namespace Ember.Core;

/// <summary>
/// The settings that are actually used: user value ?? team value ?? Ember default.
/// Immutable snapshot, so a running installation is not affected by edits in the UI.
/// </summary>
public sealed record EffectiveSettings
{
    public const int DefaultPort = 3128;

    public bool UsePac { get; init; } = true;
    public string PacUrl { get; init; } = "";
    public string UpstreamProxy { get; init; } = "";
    public IReadOnlyList<string> InternalDomainList { get; init; } = Array.Empty<string>();
    public int Port { get; init; } = DefaultPort;

    public string InstallDir { get; init; } = Paths.DefaultPxDir;
    public string LocalZip { get; init; } = "";
    public bool Autostart { get; init; } = true;
    public bool ConfigureEnvVars { get; init; } = true;
    public bool ConfigureGit { get; init; } = true;
    public bool ConfigureNpm { get; init; } = true;
    public bool ConfigurePip { get; init; } = true;
    public bool ConfigureNuGet { get; init; } = true;

    public string PxUrl => $"http://127.0.0.1:{Port}";
    public string ConfigIni => Paths.PxConfigIni;
    public string UpstreamHostPort => NormalizeHostPort(UpstreamProxy);
    public string NoProxyList => string.Join(",", new[] { "localhost", "127.0.0.1", "::1" }.Concat(InternalDomainList));

    public static EffectiveSettings Build(UserSettings u, TeamConfig team)
    {
        var t = team.Proxy;
        return new EffectiveSettings
        {
            UsePac = u.UsePac ?? t?.UsePac ?? true,
            PacUrl = u.PacUrl ?? t?.PacUrl ?? "",
            UpstreamProxy = u.UpstreamProxy ?? t?.Upstream ?? "",
            InternalDomainList = u.InternalDomains != null ? SplitDomains(u.InternalDomains) : (IReadOnlyList<string>?)t?.InternalDomains ?? Array.Empty<string>(),
            Port = u.Port ?? t?.Port ?? DefaultPort,
            InstallDir = string.IsNullOrWhiteSpace(u.InstallDir) ? Paths.DefaultPxDir : u.InstallDir,
            LocalZip = u.LocalZip,
            Autostart = u.Autostart,
            ConfigureEnvVars = u.ConfigureEnvVars,
            ConfigureGit = u.ConfigureGit,
            ConfigureNpm = u.ConfigureNpm,
            ConfigurePip = u.ConfigurePip,
            ConfigureNuGet = u.ConfigureNuGet,
        };
    }

    public static IReadOnlyList<string> SplitDomains(string text) =>
        text.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string NormalizeHostPort(string value) => Regex.Replace(value.Trim(), "^https?://", "").TrimEnd('/');

    /// <summary>Returns an error (already translated) or null when everything is fine.</summary>
    public string? Validate()
    {
        if (!UsePac && UpstreamHostPort.Length == 0) return L.T("errors.fixedProxyMissing");
        if (UpstreamHostPort.Length > 0 && !Regex.IsMatch(UpstreamHostPort, @"^[\w.\-]+:\d{1,5}$"))
            return L.T("errors.proxyFormat");
        if (Port is < 1 or > 65535) return L.T("errors.portRange");
        if (string.IsNullOrWhiteSpace(InstallDir)) return L.T("errors.installDirMissing");
        try { Path.GetFullPath(InstallDir); }
        catch { return L.T("errors.installDirInvalid"); }
        if (LocalZip.Length > 0 && !File.Exists(LocalZip)) return L.T("errors.localZipMissing", ("path", LocalZip));
        return null;
    }
}

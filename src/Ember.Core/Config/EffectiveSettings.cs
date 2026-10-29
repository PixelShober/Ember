using System.Text.RegularExpressions;

namespace Ember.Core;

/// <summary>
/// The settings that are actually used. Company proxy (PAC, proxy host): user value ?? detected at start
/// ?? Ember default. Internal domains and port: user value ?? team value ?? default.
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
    /// <summary>NO_PROXY, npm, NuGet: local addresses plus the internal domains as plain suffixes.</summary>
    public string NoProxyList => string.Join(",", new[] { "localhost", "127.0.0.1", "::1" }.Concat(NoProxy.ForTools(InternalDomainList)));

    public static EffectiveSettings Build(UserSettings u, TeamConfig team, ProxyDetectionResult? detected = null)
    {
        var t = team.Proxy;
        var d = Detected.From(detected);
        return new EffectiveSettings
        {
            UsePac = u.UsePac ?? d.UsePac ?? true,
            PacUrl = u.PacUrl ?? d.PacUrl ?? "",
            UpstreamProxy = u.UpstreamProxy ?? d.Upstream ?? "",
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

    /// <summary>
    /// What Windows reported at start, as proxy values: the PAC address, the first proxy from the
    /// PAC file (or the system proxy), and whether PAC is the way.
    /// </summary>
    public sealed record Detected(bool? UsePac, string? PacUrl, string? Upstream)
    {
        public static Detected From(ProxyDetectionResult? r) =>
            r == null
                ? new Detected(null, null, null)
                : new Detected(r.PacUrl != null ? true : r.SystemProxy != null ? false : null,
                               r.PacUrl, r.PacProxies.FirstOrDefault() ?? r.SystemProxy);
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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core;

/// <summary>A text that is either a plain string or { "de": "…", "en": "…" }.</summary>
[JsonConverter(typeof(LocalizedTextConverter))]
public sealed class LocalizedText
{
    public LocalizedText(Dictionary<string, string> values) => Values = values;

    public LocalizedText(string text) => Values = new() { ["*"] = text };

    public Dictionary<string, string> Values { get; }

    /// <summary>Text in the current UI language (falls back to German, then anything).</summary>
    public string Get()
    {
        var text = Values.TryGetValue(L.Language, out var v) ? v
                 : Values.TryGetValue("*", out var any) ? any
                 : Values.TryGetValue("de", out var de) ? de
                 : Values.Values.FirstOrDefault() ?? "";
        return L.Swiss ? L.ToSwiss(text) : text;
    }

    public override string ToString() => Get();
}

public sealed class LocalizedTextConverter : JsonConverter<LocalizedText>
{
    public override LocalizedText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String) return new LocalizedText(reader.GetString() ?? "");
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options) ?? new();
        return new LocalizedText(new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase));
    }

    public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)
    {
        if (value.Values.Count == 1 && value.Values.ContainsKey("*")) writer.WriteStringValue(value.Values["*"]);
        else JsonSerializer.Serialize(writer, value.Values, options);
    }
}

/// <summary>
/// What the team shares about the network. The company proxy itself (PAC address, proxy host) is no
/// longer configured here: Ember detects it at every start from Windows.
/// </summary>
public sealed class TeamProxy
{
    public List<string>? InternalDomains { get; set; }
    public int? Port { get; set; }
}

public sealed class TeamServiceCenter
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? SubjectPrefix { get; set; }
}

public sealed class MailTemplate
{
    public LocalizedText? Subject { get; set; }
    public LocalizedText? Body { get; set; }
}

public sealed class TeamSoftware
{
    /// <summary>Stable key; defaults to the name.</summary>
    public string? Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Name in the Software Center (wildcards * and ?), defaults to Name.</summary>
    public string? SccmName { get; set; }
    /// <summary>DisplayName in "Programs and Features" (wildcards), defaults to Name + "*".</summary>
    public string? DisplayName { get; set; }
    public string? Category { get; set; }
    public LocalizedText? Note { get; set; }
    /// <summary>Deep link copied from the Software Center ("Share"), e.g. softwarecenter:SoftwareID=…</summary>
    public string? SoftwareCenterLink { get; set; }

    [JsonIgnore] public string Key => string.IsNullOrWhiteSpace(Id) ? Name : Id!;
}

public sealed class TeamPower
{
    /// <summary>Power schemes Ember may change. Default: only "Balanced".</summary>
    public List<string>? AllowedSchemes { get; set; }
    /// <summary>Profiles that replace (same id) or extend Ember's built-in profiles.</summary>
    public List<PowerProfileDefinition>? Profiles { get; set; }
}

/// <summary>Developer tools (Software tab): which ones to show and whether Ember may install them straight from the vendor.</summary>
public sealed class TeamDevTools
{
    /// <summary>Ids to show (nodejs, git, vscode, python, visualstudio, dotnet, pwsh). Default: all.</summary>
    public List<string>? Tools { get; set; }
    /// <summary>false: only through Software Center or the service center, never directly from the vendor. Default: true.</summary>
    public bool? AllowDirect { get; set; }
    public TeamVisualStudio? VisualStudio { get; set; }
}

public sealed class TeamVisualStudio
{
    /// <summary>Bootstrapper to use, e.g. https://aka.ms/vs/17/release/vs_professional.exe (default).</summary>
    public string? BootstrapperUrl { get; set; }
    /// <summary>Workload ids for --add. Default: ManagedDesktop and NetWeb.</summary>
    public List<string>? Workloads { get; set; }
}

public sealed class TeamPlaywright
{
    /// <summary>Internal mirror for the browser downloads (sets PLAYWRIGHT_DOWNLOAD_HOST), if cdn.playwright.dev is blocked.</summary>
    public string? DownloadHost { get; set; }
}

/// <summary>Who counts as admin and sees the advanced modules (KI, Energie). Accounts and AD groups, with or without domain.</summary>
public sealed class TeamAdmins
{
    public List<string>? Users { get; set; }
    public List<string>? Groups { get; set; }
}

public sealed class TeamUpdate
{
    /// <summary>Optional UNC folder with latest.json and the release files (if GitHub is blocked).</summary>
    public string? Mirror { get; set; }
}

/// <summary>
/// The shared team configuration (team.json on a network share). Everything is optional;
/// missing parts fall back to Ember's defaults. Company data lives only here, never in the repo.
/// </summary>
public sealed class TeamConfig
{
    public int Version { get; set; } = 1;
    public string? Name { get; set; }
    public TeamProxy? Proxy { get; set; }
    public TeamServiceCenter? ServiceCenter { get; set; }
    public Dictionary<string, MailTemplate>? MailTemplates { get; set; }
    public List<TeamSoftware>? Software { get; set; }
    public TeamDevTools? DevTools { get; set; }
    public TeamPlaywright? Playwright { get; set; }
    public TeamPower? Power { get; set; }
    public TeamAdmins? Admins { get; set; }
    public List<ModelEntry>? Models { get; set; }
    public TeamUpdate? Update { get; set; }
    /// <summary>Overrides the download URL of the WebView2 standalone installer.</summary>
    public string? WebView2InstallerUrl { get; set; }

    public static TeamConfig Parse(string json) =>
        Json.Deserialize<TeamConfig>(json) ?? throw new JsonException("empty");
}

public sealed record TeamStatus(
    string? Path, string? Source, DateTime? LoadedAt, bool FromCache, string? Error, int Version, string? Name)
{
    public static readonly TeamStatus None = new(null, null, null, false, null, 0, null);
}

/// <summary>Finds, loads and caches team.json.</summary>
public sealed class TeamConfigService
{
    public const string PointerFileName = "ember-team.txt";
    public const string EnvVariable = "EMBER_TEAM_CONFIG";

    sealed record Cache(string Path, DateTime LoadedAt, TeamConfig Config);

    public TeamConfig Config { get; private set; } = new();
    public TeamStatus Status { get; private set; } = TeamStatus.None;

    /// <summary>Order: settings → ember-team.txt next to the exe → %EMBER_TEAM_CONFIG%.</summary>
    public static (string? Path, string? Source) ResolvePath(UserSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.TeamConfigPath)) return (s.TeamConfigPath.Trim(), "settings");
        foreach (var dir in new[] { AppContext.BaseDirectory, Paths.AppDir })
        {
            try
            {
                var pointer = System.IO.Path.Combine(dir, PointerFileName);
                if (File.Exists(pointer))
                {
                    var line = File.ReadLines(pointer).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
                    if (!string.IsNullOrEmpty(line)) return (line, "file");
                }
            }
            catch
            {
                // unreadable pointer file: ignore
            }
        }
        var env = Environment.GetEnvironmentVariable(EnvVariable);
        return string.IsNullOrWhiteSpace(env) ? (null, null) : (env.Trim(), "env");
    }

    public async Task LoadAsync(UserSettings settings, CancellationToken ct)
    {
        var (path, source) = ResolvePath(settings);
        if (path == null)
        {
            Config = new TeamConfig();
            Status = TeamStatus.None;
            Log.Info(L.T("team.notConfigured"));
            return;
        }

        try
        {
            // Network shares can hang for a long time when the VPN is down.
            var text = await Task.Run(() => File.ReadAllText(path), ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
            var config = TeamConfig.Parse(text);
            Config = config;
            Status = new TeamStatus(path, source, DateTime.Now, false, null, config.Version, config.Name);
            SaveCache(new Cache(path, DateTime.Now, config));
            Log.Info(L.T("team.loaded", ("path", path)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var detail = ex is TimeoutException ? L.T("errors.timeoutSuffix") : Net.Describe(ex);
            var cache = LoadCache();
            if (cache != null)
            {
                Config = cache.Config;
                Status = new TeamStatus(path, source, cache.LoadedAt, true, detail, cache.Config.Version, cache.Config.Name);
                Log.Warn(L.T("team.fromCache", ("path", path), ("detail", detail), ("date", cache.LoadedAt)));
            }
            else
            {
                Config = new TeamConfig();
                Status = new TeamStatus(path, source, null, false, detail, 0, null);
                Log.Warn(L.T("team.failed", ("path", path), ("detail", detail)));
            }
        }
    }

    static void SaveCache(Cache cache)
    {
        try
        {
            Directory.CreateDirectory(Paths.ConfigDir);
            File.WriteAllText(Paths.TeamCacheFile, Json.Serialize(cache, indented: true));
        }
        catch (Exception ex)
        {
            Log.Debug("team cache: " + ex.Message);
        }
    }

    static Cache? LoadCache()
    {
        try
        {
            return File.Exists(Paths.TeamCacheFile) ? Json.Deserialize<Cache>(File.ReadAllText(Paths.TeamCacheFile)) : null;
        }
        catch
        {
            return null;
        }
    }
}

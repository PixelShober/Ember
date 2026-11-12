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
    /// <summary>First sentence of the "Problem melden" mail; Ember's own text if empty.</summary>
    public LocalizedText? ProblemIntro { get; set; }
}

/// <summary>The direct supervisor: requests for admin rights go to them, and they ask the service center.</summary>
public sealed class TeamSupervisor
{
    public string? Name { get; set; }
    /// <summary>For "Hallo {Vorname}"; defaults to the first word of Name (or the part after the comma in "Nachname, Vorname").</summary>
    public string? FirstName { get; set; }
    public string? Email { get; set; }

    public string? GreetingName()
    {
        if (!string.IsNullOrWhiteSpace(FirstName)) return FirstName.Trim();
        var name = Name?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        var comma = name.IndexOf(',');
        if (comma > 0 && comma < name.Length - 1) return name[(comma + 1)..].Trim().Split(' ')[0];
        return name.Split(' ')[0];
    }
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
    /// <summary>Folder with the team's project template (UNC); "Neues Projekt" copies it, "Vorlage" replaces it.</summary>
    public string? Template { get; set; }
}

/// <summary>Format before roles: these accounts and groups get every module (see <see cref="Permissions.Roles"/>).</summary>
public sealed class TeamAdmins
{
    public List<string>? Users { get; set; }
    public List<string>? Groups { get; set; }
}

/// <summary>Pi in the KI tab: which extensions Ember offers (replaces the built-in list).</summary>
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
    /// <summary>Counts up with every signed save; Ember never accepts an older one than it has seen.</summary>
    public int Version { get; set; } = 1;
    /// <summary>Where the signed file belongs (UNC path); a copy elsewhere is refused.</summary>
    public string? Location { get; set; }
    public string? SignedBy { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? Name { get; set; }
    public TeamProxy? Proxy { get; set; }
    public TeamServiceCenter? ServiceCenter { get; set; }
    public TeamSupervisor? Supervisor { get; set; }
    public Dictionary<string, MailTemplate>? MailTemplates { get; set; }
    public List<TeamSoftware>? Software { get; set; }
    public TeamDevTools? DevTools { get; set; }
    public TeamPlaywright? Playwright { get; set; }
    public TeamPower? Power { get; set; }
    /// <summary>Format before roles; still read, the Berechtigungen tab turns it into a role.</summary>
    public TeamAdmins? Admins { get; set; }
    /// <summary>Roles: who sees which module. Without roles nobody sees anything.</summary>
    public TeamAccess? Access { get; set; }
    public TeamUpdate? Update { get; set; }
    /// <summary>Overrides the download URL of the WebView2 standalone installer.</summary>
    public string? WebView2InstallerUrl { get; set; }

    public static TeamConfig Parse(string json) =>
        Json.Deserialize<TeamConfig>(json) ?? throw new JsonException("empty");
}

/// <param name="Trusted">Signed with a key this Ember build knows.</param>
public sealed record TeamStatus(
    string? Path, string? Source, DateTime? LoadedAt, bool FromCache, string? Error, int Version, string? Name,
    bool Trusted = false, string? KeyId = null, string? SignedBy = null, DateTime? SignedAt = null)
{
    public static readonly TeamStatus None = new(null, null, null, false, null, 0, null);
}

/// <summary>Finds, loads and caches team.json.</summary>
public sealed class TeamConfigService
{
    public const string PointerFileName = "ember-team.txt";
    public const string FileName = "team.json";
    public const string EnvVariable = "EMBER_TEAM_CONFIG";

    /// <summary>How long the local copy counts while the share cannot be reached.</summary>
    public static readonly TimeSpan OfflineLimit = TimeSpan.FromDays(14);

    /// <param name="Text">The team file as it was read (envelope included), verified again on use.</param>
    sealed record Cache(string Path, DateTime LoadedAt, string? Text);

    public TeamConfig Config { get; private set; } = new();
    /// <summary>The configuration JSON as signed (for the Berechtigungen page).</summary>
    public string? Payload { get; private set; }
    DateTime? _writeTime;

    /// <summary>The loaded team.json was changed since (time stamp on the share); false when unreachable.</summary>
    public async Task<bool> ChangedOnDiskAsync(CancellationToken ct)
    {
        var path = Status.Path;
        if (path == null) return false;
        try
        {
            var time = await Task.Run(() => File.GetLastWriteTimeUtc(path), ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
            return time.Year > 1601 && time != _writeTime;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
    public TeamStatus Status { get; private set; } = TeamStatus.None;

    /// <summary>
    /// Where the team file is, first match wins: the settings (chosen by the user) → ember-team.txt
    /// next to the exe or in C:\Ember\App → team.ember or team.json next to the exe (Ember started from
    /// the team share) → %EMBER_TEAM_CONFIG%.
    /// </summary>
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
        foreach (var name in new[] { "team" + TeamFile.Extension, FileName })
        {
            var beside = System.IO.Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(beside)) return (beside, "beside");
        }
        var env = Environment.GetEnvironmentVariable(EnvVariable);
        return string.IsNullOrWhiteSpace(env) ? (null, null) : (env.Trim(), "env");
    }

    /// <summary>Reads and parses a team.json without using it; throws with the reason (setup screen).</summary>
    public static async Task<TeamConfig> TryReadAsync(string path, CancellationToken ct)
    {
        if (path.Length == 0) throw new EmberException("missingParam", ("name", "path"));
        try
        {
            var text = await Task.Run(() => File.ReadAllText(path), ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
            var content = TeamFile.Open(text);
            var config = TeamConfig.Parse(content.Payload);
            if (TeamFile.Problem(content, config, path, 0, TeamFile.SignatureRequired) is { } problem)
                throw new EmberException(problem, ("path", path));
            return config;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not EmberException)
        {
            throw new EmberException("teamUnreadable", ("path", path), ("detail", ex is TimeoutException ? L.T("errors.timeoutSuffix") : Net.Describe(ex)));
        }
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
            var (text, written) = await Task.Run(() => (File.ReadAllText(path), File.GetLastWriteTimeUtc(path)), ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
            if (!Use(text, path, source, DateTime.Now, false, null, settings)) return;
            _writeTime = written;
            SaveCache(new Cache(path, DateTime.Now, text));
            Log.Info(L.T("team.loaded", ("path", path)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var detail = ex is TimeoutException ? L.T("errors.timeoutSuffix") : Net.Describe(ex);
            var cache = LoadCache();
            bool fresh = cache?.Text != null && TeamFile.SamePath(cache.Path, path) && DateTime.Now - cache.LoadedAt < OfflineLimit;
            if (fresh && Use(cache!.Text!, path, source, cache.LoadedAt, true, detail, settings))
            {
                Log.Warn(L.T("team.fromCache", ("path", path), ("detail", detail), ("date", cache.LoadedAt)));
                return;
            }
            Config = new TeamConfig();
            Payload = null;
            Status = new TeamStatus(path, source, null, false, cache?.Text != null && !fresh ? L.T("team.cacheExpired", ("days", OfflineLimit.Days)) : detail, 0, null);
            Log.Warn(L.T("team.failed", ("path", path), ("detail", Status.Error ?? detail)));
        }
    }

    /// <summary>
    /// Checks and takes over a team file: signature, path and version (see <see cref="TeamFile.Problem"/>).
    /// A file that fails leaves Ember without team configuration, i.e. on the setup screen with the reason.
    /// </summary>
    bool Use(string text, string path, string? source, DateTime loadedAt, bool fromCache, string? error, UserSettings settings)
    {
        var content = TeamFile.Open(text.TrimStart('\uFEFF'));
        var config = TeamConfig.Parse(content.Payload);
        var location = TeamFile.Normalize(path);
        settings.TeamVersions.TryGetValue(location, out var seen);
        if (TeamFile.Problem(content, config, path, seen, TeamFile.SignatureRequired) is { } problem)
        {
            Config = new TeamConfig();
            Payload = null;
            var reason = L.T("errors." + problem, ("path", path), ("version", config.Version), ("seen", seen));
            Status = new TeamStatus(path, source, null, false, reason, 0, null);
            Log.Warn(L.T("team.failed", ("path", path), ("detail", reason)));
            return false;
        }
        if (config.Version > seen && content.Trusted)
        {
            settings.TeamVersions[location] = config.Version;
            try { settings.Save(); } catch (Exception ex) { Log.Debug("settings: " + ex.Message); }
        }
        Config = config;
        Payload = content.Payload;
        Status = new TeamStatus(path, source, loadedAt, fromCache, error, config.Version, config.Name, content.Trusted, content.KeyId, config.SignedBy, config.SignedAt);
        return true;
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

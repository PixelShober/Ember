using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ember.Core;

/// <param name="Configured">models.json has the provider "llama-cpp" for this model, port and context.</param>
public sealed record PiState(bool Installed, bool NodeFound, bool Configured, string ConfigFile, string Command, string? Version = null);

/// <summary>A Pi extension Ember offers (skills, tools, TUI); team.json can replace the list (pi.packages).</summary>
public sealed class PiPackage
{
    public string Id { get; set; } = "";
    /// <summary>npm package name, installed with "pi install npm:&lt;name&gt;".</summary>
    public string Npm { get; set; } = "";
    public LocalizedText? Note { get; set; }
}

/// <param name="Installed">Installed version; "?" when Pi lists it but the version is unknown; null: not installed.</param>
/// <param name="Latest">Newest version on npm (null when npm could not be asked).</param>
public sealed record PiPackageState(string Id, string Npm, string? Note, string? Installed, string? Latest, bool UpdateAvailable);

/// <summary>
/// Pi, the coding agent from the setup guide: installed with npm through px, MCP through the
/// pi-mcp-adapter extension, and pointed at llama-server through the provider "llama-cpp" in
/// %USERPROFILE%\.pi\agent\models.json (OpenAI-compatible API on 127.0.0.1).
/// </summary>
public static class PiAgent
{
    public const string Package = "@earendil-works/pi-coding-agent";
    public const string McpAdapter = "npm:pi-mcp-adapter";
    public const string Provider = "llama-cpp";
    const int MaxTokens = 16384;

    public static string ConfigFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pi", "agent", "models.json");

    static string? Exe => Tools.Find("pi.cmd", @"%AppData%\npm");

    public static string Command(string alias) => $"pi --provider {Provider} --model {alias}";

    public static PiState State(string alias, int context, int port, bool vision = false) =>
        new(Exe != null, Tools.Npm != null, IsConfigured(ReadConfig(), alias, context, port, vision), ConfigFile, Command(alias),
            InstalledVersion(Package));

    // ------------------------------------------------------------------ extensions

    /// <summary>
    /// What Ember offers to install into Pi. Websearch: pi-web-access, a tool the model calls, so it
    /// works with the local llama.cpp server too. It searches without a key (Exa MCP, DuckDuckGo) and
    /// goes through px via its own "proxy" setting (see <see cref="SetWebSearchProxy"/>), because Node's
    /// fetch ignores HTTPS_PROXY. team.json pi.packages can swap it.
    /// </summary>
    public static readonly IReadOnlyList<PiPackage> DefaultPackages = new PiPackage[]
    {
        new() { Id = "mcp", Npm = "pi-mcp-adapter" },
        new() { Id = "websearch", Npm = WebSearchPackage },
        new() { Id = "pretty", Npm = "@heyhuynhgiabuu/pi-pretty" },
        new() { Id = "ponytail", Npm = "@dietrichgebert/ponytail" },
        new() { Id = "simplify", Npm = "pi-simplify" },
        new() { Id = "contextMode", Npm = "context-mode" },
        new() { Id = "todo", Npm = "@juicesharp/rpiv-todo" },
    };

    public const string WebSearchPackage = "pi-web-access";

    /// <summary>~/.pi/agent/web-search.json, the settings of pi-web-access.</summary>
    public static string WebSearchConfig => Path.Combine(AgentDir, "web-search.json");

    /// <summary>
    /// Points pi-web-access at px: "proxy" in web-search.json. A proxy the user set to something else
    /// (not localhost) stays; everything else in the file stays as well.
    /// </summary>
    public static bool SetWebSearchProxy(string pxUrl)
    {
        var path = WebSearchConfig;
        var text = File.Exists(path) ? File.ReadAllText(path) : null;
        var updated = WithWebSearchProxy(text, pxUrl);
        if (updated == null) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, updated);
        Log.Info(L.T("ai.pi.webSearchProxy", ("path", path), ("url", pxUrl)), "pi");
        return true;
    }

    /// <summary>The new file text, or null if nothing changes (already px, or the user's own proxy).</summary>
    internal static string? WithWebSearchProxy(string? json, string pxUrl)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? new JsonObject();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;   // never overwrite a file we cannot read
        }
        var current = root["proxy"]?.GetValue<string>();
        if (current == pxUrl) return null;
        if (!string.IsNullOrWhiteSpace(current) && !Regex.IsMatch(current, @"^https?://(127\.0\.0\.1|localhost)(:\d+)?/?$", RegexOptions.IgnoreCase)) return null;
        root["proxy"] = pxUrl;
        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    public static IReadOnlyList<PiPackage> Packages(TeamConfig team) =>
        DefaultPackages;

    static string AgentDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pi", "agent");

    /// <summary>The packages Pi has registered: "packages" in ~/.pi/agent/settings.json ("npm:name" or "npm:name@1.2.3").</summary>
    internal static HashSet<string> Registered(string? settingsJson)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (string.IsNullOrWhiteSpace(settingsJson) || JsonNode.Parse(settingsJson)?["packages"] is not JsonArray list) return names;
            foreach (var item in list)
            {
                var source = item is JsonObject o ? (string?)o["source"] : item?.GetValueKind() == System.Text.Json.JsonValueKind.String ? (string?)item : null;
                if (source == null || !source.StartsWith("npm:", StringComparison.OrdinalIgnoreCase)) continue;
                var spec = source[4..];
                var at = spec.IndexOf('@', 1);   // "@scope/name@1.2" keeps the scope's @
                names.Add(at > 0 ? spec[..at] : spec);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // a broken settings.json: Pi itself will complain
        }
        return names;
    }

    /// <summary>The version from the package's package.json, wherever npm or Pi put it.</summary>
    internal static string? InstalledVersion(string npm)
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules"),
            Path.Combine(AgentDir, "npm", "node_modules"),
            Path.Combine(AgentDir, "packages", "node_modules"),
            Path.Combine(AgentDir, "node_modules"),
        };
        foreach (var root in roots)
        {
            var file = Path.Combine(root, npm.Replace('/', Path.DirectorySeparatorChar), "package.json");
            try
            {
                if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file))?["version"] is JsonValue v) return (string?)v;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                // try the next place
            }
        }
        return null;
    }

    static readonly Dictionary<string, (DateTime At, string? Version)> LatestCache = new();

    /// <summary>Newest version on npm, through px; cached for an hour.</summary>
    static async Task<string?> LatestAsync(HttpClient http, string npm, CancellationToken ct)
    {
        lock (LatestCache)
            if (LatestCache.TryGetValue(npm, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromHours(1)) return hit.Version;
        string? version = null;
        try
        {
            var json = await http.GetStringAsync($"https://registry.npmjs.org/{npm.Replace("/", "%2F")}/latest", ct);
            version = (string?)JsonNode.Parse(json)?["version"];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug($"npm {npm}: {Net.Describe(ex)}");
        }
        lock (LatestCache) LatestCache[npm] = (DateTime.UtcNow, version);
        return version;
    }

    public static async Task<(PiPackageState Pi, List<PiPackageState> Packages)> PackagesAsync(EffectiveSettings eff, TeamConfig team, CancellationToken ct)
    {
        var settingsFile = Path.Combine(AgentDir, "settings.json");
        var registered = Registered(File.Exists(settingsFile) ? await File.ReadAllTextAsync(settingsFile, ct) : null);
        var (http, _) = await Net.SmartClientAsync(eff, TimeSpan.FromSeconds(20));
        using (http)
        {
            async Task<PiPackageState> One(string id, string npm, string? note, bool listed)
            {
                var installed = InstalledVersion(npm) ?? (listed ? "?" : null);
                var latest = await LatestAsync(http, npm, ct);
                bool update = installed is not (null or "?") && latest != null && AppInfo.CompareVersions(latest, installed) > 0;
                return new PiPackageState(id, npm, note, installed, latest, update);
            }
            var pi = await One("pi", Package, null, Exe != null);
            var list = new List<PiPackageState>();
            foreach (var p in Packages(team))
                list.Add(await One(p.Id, p.Npm, p.Note?.Get(), registered.Contains(p.Npm)));
            return (pi, list);
        }
    }

    /// <summary>"pi install npm:…" for a new one; for an installed one "pi update npm:…", and if Pi does not know that, install again.</summary>
    public static List<SetupStep> PackageSteps(PiPackage package, bool update, EffectiveSettings eff, TeamConfig team)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new List<SetupStep>
        {
            new(L.T(update ? "ai.pi.steps.update" : "ai.pi.steps.package", ("name", package.Npm)), async (step, ct) =>
            {
                var pi = Exe ?? throw new EmberException("piMissing");
                var env = await Playwright.EnvironmentAsync(eff, team);
                step.SetProgress(null);
                var r = update ? await Playwright.RunAsync($"\"{pi}\" update npm:{package.Npm}", home, env, step, ct, 10 * 60, "pi") : (Code: 1, Lines: new List<string>());
                if (r.Code != 0) r = await Playwright.RunAsync($"\"{pi}\" install npm:{package.Npm}", home, env, step, ct, 10 * 60, "pi");
                if (r.Code != 0) throw Playwright.Explain(r.Lines, "pi install npm:" + package.Npm);
                if (package.Npm == WebSearchPackage) SetWebSearchProxy(eff.PxUrl);
                step.Report(package.Npm + (InstalledVersion(package.Npm) is { } v ? " " + v : ""));
            }),
        };
    }

    /// <summary>Pi itself to the newest version (npm install -g …@latest, through px).</summary>
    public static List<SetupStep> UpdateSteps(EffectiveSettings eff, TeamConfig team)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new List<SetupStep>
        {
            new(L.T("ai.pi.steps.update", ("name", Package)), async (step, ct) =>
            {
                if (Tools.Npm == null) throw new EmberException("playwrightNoNode");
                var env = await Playwright.EnvironmentAsync(eff, team);
                var r = await Playwright.RunAsync($"npm install -g {Package}@latest", home, env, step, ct, 15 * 60, "pi");
                if (r.Code != 0) throw Playwright.Explain(r.Lines, "npm install -g " + Package);
                step.Report(Package + (InstalledVersion(Package) is { } v ? " " + v : ""));
            }),
        };
    }

    static JsonObject? ReadConfig()
    {
        try
        {
            return File.Exists(ConfigFile) ? JsonNode.Parse(File.ReadAllText(ConfigFile)) as JsonObject : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    internal static bool IsConfigured(JsonObject? config, string alias, int context, int port, bool vision = false)
    {
        var provider = config?["providers"]?[Provider];
        if ((string?)provider?["baseUrl"] != BaseUrl(port)) return false;
        return provider?["models"] is JsonArray models &&
               models.Any(m => (string?)m?["id"] == alias && (int?)m?["contextWindow"] == context &&
                               (m?["input"] as JsonArray)?.Any(i => (string?)i == "image") == vision);
    }

    static string BaseUrl(int port) => $"http://127.0.0.1:{port}/v1";

    /// <summary>
    /// Sets the provider "llama-cpp" in models.json; other providers stay. contextWindow must match the
    /// server's context, only then does Pi know when to summarize the conversation.
    /// </summary>
    internal static string Apply(string? json, string alias, int context, int port, bool vision = false)
    {
        JsonObject root;
        try
        {
            root = (json is { Length: > 0 } ? JsonNode.Parse(json) as JsonObject : null) ?? new JsonObject();
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new EmberException("piConfigInvalid", ("path", ConfigFile), ("detail", ex.Message));
        }
        var providers = root["providers"] as JsonObject ?? new JsonObject();
        providers[Provider] = new JsonObject
        {
            ["baseUrl"] = BaseUrl(port),
            ["api"] = "openai-completions",
            ["apiKey"] = "none",
            ["models"] = new JsonArray(new JsonObject
            {
                ["id"] = alias,
                // With the vision projector (--mmproj) the model also takes screenshots and pictures.
                ["input"] = vision ? new JsonArray("text", "image") : new JsonArray("text"),
                ["contextWindow"] = context,
                ["maxTokens"] = MaxTokens,
            }),
        };
        root["providers"] = providers;
        return json is { Length: > 0 } ? Json.WriteLike(root, json) : root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    public static string Configure(string alias, int context, int port, bool vision = false)
    {
        var existing = File.Exists(ConfigFile) ? File.ReadAllText(ConfigFile) : null;
        var updated = Apply(existing, alias, context, port, vision);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
        if (existing != null) File.Copy(ConfigFile, ConfigFile + ".bak", overwrite: true);
        File.WriteAllText(ConfigFile, updated);
        Log.Info(L.T("ai.pi.configured", ("path", ConfigFile), ("model", alias), ("context", context)), "pi");
        return ConfigFile;
    }

    /// <summary>npm install -g of Pi, then its MCP extension; npm goes through px with the company certificates.</summary>
    public static List<SetupStep> InstallSteps(EffectiveSettings eff, TeamConfig team)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Dictionary<string, string> env = new();
        return new List<SetupStep>
        {
            new(L.T("ai.pi.steps.install"), async (step, ct) =>
            {
                if (Tools.Npm == null) throw new EmberException("playwrightNoNode");
                env = await Playwright.EnvironmentAsync(eff, team);
                step.SetProgress(null);
                var r = await Playwright.RunAsync($"npm install -g {Package}", home, env, step, ct, 15 * 60, "pi");
                if (r.Code != 0) throw Playwright.Explain(r.Lines, "npm install -g " + Package);
                step.Report(Package);
            }),
            new(L.T("ai.pi.steps.mcp"), async (step, ct) =>
            {
                var pi = Exe ?? throw new EmberException("devToolNotFound", ("tool", "pi"));
                var r = await Playwright.RunAsync($"\"{pi}\" install {McpAdapter}", home, env, step, ct, 10 * 60, "pi");
                if (r.Code != 0) throw Playwright.Explain(r.Lines, "pi install " + McpAdapter);
                step.Report(McpAdapter);
            }),
        };
    }
}

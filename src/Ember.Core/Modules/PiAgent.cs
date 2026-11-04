using System.Text.Json.Nodes;

namespace Ember.Core;

/// <param name="Configured">models.json has the provider "llama-cpp" for this model, port and context.</param>
public sealed record PiState(bool Installed, bool NodeFound, bool Configured, string ConfigFile, string Command);

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

    public static PiState State(string alias, int context, int port) =>
        new(Exe != null, Tools.Npm != null, IsConfigured(ReadConfig(), alias, context, port), ConfigFile, Command(alias));

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

    internal static bool IsConfigured(JsonObject? config, string alias, int context, int port)
    {
        var provider = config?["providers"]?[Provider];
        if ((string?)provider?["baseUrl"] != BaseUrl(port)) return false;
        return provider?["models"] is JsonArray models &&
               models.Any(m => (string?)m?["id"] == alias && (int?)m?["contextWindow"] == context);
    }

    static string BaseUrl(int port) => $"http://127.0.0.1:{port}/v1";

    /// <summary>
    /// Sets the provider "llama-cpp" in models.json; other providers stay. contextWindow must match the
    /// server's context, only then does Pi know when to summarize the conversation.
    /// </summary>
    internal static string Apply(string? json, string alias, int context, int port)
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
                ["input"] = new JsonArray("text"),
                ["contextWindow"] = context,
                ["maxTokens"] = MaxTokens,
            }),
        };
        root["providers"] = providers;
        return json is { Length: > 0 } ? Json.WriteLike(root, json) : root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    public static string Configure(string alias, int context, int port)
    {
        var existing = File.Exists(ConfigFile) ? File.ReadAllText(ConfigFile) : null;
        var updated = Apply(existing, alias, context, port);
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

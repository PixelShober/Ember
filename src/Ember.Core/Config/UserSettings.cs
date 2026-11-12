using System.Text.Json;

namespace Ember.Core;

public sealed class ModelEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Direct download URL, e.g. https://huggingface.co/…/resolve/main/model.gguf</summary>
    public string Url { get; set; } = "";
    /// <summary>A GGUF file that is already there (local disk or share); then nothing is downloaded.</summary>
    public string? Path { get; set; }
    public string? Sha256 { get; set; }
    public string? Note { get; set; }
    /// <summary>Name the server announces (--alias) and Pi asks for; default: the id.</summary>
    public string? Alias { get; set; }
    /// <summary>The model has a multi-token-prediction head (--spec-type draft-mtp makes it faster).</summary>
    public bool? Mtp { get; set; }
    /// <summary>Extra llama-server arguments the model wants, one per item (sampling, chat template).</summary>
    public List<string>? Args { get; set; }
    /// <summary>The vision projector (mmproj-….gguf), as download URL or path; then the model also reads images.</summary>
    public string? Mmproj { get; set; }
}

/// <summary>
/// What the user changed locally, stored in C:\Ember\Config\settings.json.
/// Values that can also come from the team configuration are nullable: null means
/// "take the team value, or Ember's default".
/// </summary>
public sealed class UserSettings
{
    // --- Company proxy (team-managed) ---------------------------------------------
    public bool? UsePac { get; set; }
    public string? PacUrl { get; set; }
    public string? UpstreamProxy { get; set; }
    public string? InternalDomains { get; set; }
    public int? Port { get; set; }

    // --- px (local) -----------------------------------------------------------------
    public string InstallDir { get; set; } = Paths.DefaultPxDir;
    public bool Autostart { get; set; } = true;

    // --- Developer tools to point at px ----------------------------------------------
    public bool ConfigureEnvVars { get; set; } = true;
    /// <summary>px in the PowerShell profiles (irm/iwr in Windows PowerShell 5.1 and PowerShell 7).</summary>
    public bool ConfigurePowerShell { get; set; } = true;
    public bool ConfigureGit { get; set; } = true;
    public bool ConfigureNpm { get; set; } = true;
    public bool ConfigurePip { get; set; } = true;
    public bool ConfigureVsCode { get; set; } = true;
    public bool ConfigureNuGet { get; set; } = true;

    // --- App ------------------------------------------------------------------------
    /// <summary>"auto" (the Windows language), "de" or "en".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>"light" or "dark" (the UI keeps a copy for the first paint). Empty: taken once from Windows at start.</summary>
    public string? ColorScheme { get; set; }
    /// <summary>"efeu" or "glut".</summary>
    public string Design { get; set; } = "efeu";

    /// <summary>Ctrl+E hid the advanced tabs (KI, Energie, Berechtigungen); who has them comes from the roles.</summary>
    public bool HideAdvanced { get; set; }

    /// <summary>Starter key entered on the setup screen; only counts while there is no team.json.</summary>
    /// <summary>The admin's signing key file (Berechtigungen → Signieren); the password is never stored.</summary>
    public string? TeamKeyFile { get; set; }
    /// <summary>Highest team file version seen per location; an older copy is refused.</summary>
    public Dictionary<string, int> TeamVersions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Full name from the directory at the last start, so the greeting is right from the first frame.</summary>
    public string? DisplayName { get; set; }

    /// <summary>UNC path of team.json; empty = ember-team.txt next to the exe or EMBER_TEAM_CONFIG.</summary>
    public string TeamConfigPath { get; set; } = "";
    public bool AutoUpdate { get; set; } = true;
    /// <summary>Last project folder used for "Install Playwright browsers".</summary>
    public string PlaywrightProject { get; set; } = "";
    /// <summary>Own template folder, used while team.json names none.</summary>
    public string PlaywrightTemplate { get; set; } = "";

    // --- AI module ------------------------------------------------------------------
    public string ModelsDir { get; set; } = "";
    /// <summary>"auto", "cuda", "vulkan" or "cpu".</summary>
    public string LlamaVariant { get; set; } = "auto";
    public string LlamaCurrent { get; set; } = "";
    public int LlamaPort { get; set; } = 8080;
    /// <summary>Context profile for llama-server: "standard" (96K), "lang" (160K, the default) or "max" (256K).</summary>
    public string LlamaProfile { get; set; } = "lang";
    /// <summary>true once the user picked a profile when starting. Until then the default applies, also after it changed ("standard" → "lang").</summary>
    public bool LlamaProfileChosen { get; set; }
    /// <summary>Id of the model llama-server starts with (Ember, Pi and the start script); empty: the first one that is there.</summary>
    public string LlamaModel { get; set; } = "";
    /// <summary>A llama.cpp folder that is already there (e.g. C:\llama); used instead of Ember's own builds.</summary>
    public string LlamaCustomDir { get; set; } = "";
    /// <summary>Additional arguments after the profile's (empty: none).</summary>
    public string LlamaArgs { get; set; } = "";
    public List<ModelEntry> Models { get; set; } = new();

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
            {
                var s = Json.Deserialize<UserSettings>(File.ReadAllText(Paths.SettingsFile)) ?? new UserSettings();
                if (!s.LlamaProfileChosen) s.LlamaProfile = new UserSettings().LlamaProfile;
                return s;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn(L.T("settings.loadFailed", ("detail", ex.Message)));
        }
        return new UserSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Paths.ConfigDir);
        var tmp = Paths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, Json.Serialize(this, indented: true));
        File.Move(tmp, Paths.SettingsFile, overwrite: true);
    }

    public UserSettings Clone() => Json.Deserialize<UserSettings>(Json.Serialize(this))!;
}

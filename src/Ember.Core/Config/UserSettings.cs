using System.Text.Json;

namespace Ember.Core;

public sealed class ModelEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Direct download URL, e.g. https://huggingface.co/…/resolve/main/model.gguf</summary>
    public string Url { get; set; } = "";
    public string? Sha256 { get; set; }
    public string? Note { get; set; }
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
    public string LocalZip { get; set; } = "";
    public bool Autostart { get; set; } = true;

    // --- Developer tools to point at px ----------------------------------------------
    public bool ConfigureEnvVars { get; set; } = true;
    public bool ConfigureGit { get; set; } = true;
    public bool ConfigureNpm { get; set; } = true;
    public bool ConfigurePip { get; set; } = true;
    public bool ConfigureNuGet { get; set; } = true;

    // --- App ------------------------------------------------------------------------
    /// <summary>"auto" (the Windows language), "de" or "en".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>"light" or "dark" (the UI keeps a copy for the first paint). Empty: taken once from Windows at start.</summary>
    public string? ColorScheme { get; set; }
    /// <summary>"efeu" or "glut".</summary>
    public string Design { get; set; } = "efeu";
    /// <summary>
    /// Inactive window: "auto" lets the desktop shine through as much as the window manager asks for
    /// (GlazeWM/WinMarchy), "always" 90 % opaque, "off" never.
    /// </summary>
    public string BackgroundTransparency { get; set; } = "auto";

    /// <summary>Ctrl+E hid the advanced tabs (KI, Energie, Berechtigungen); who has them comes from the roles.</summary>
    public bool HideAdvanced { get; set; }

    /// <summary>Starter key entered on the setup screen; only counts while there is no team.json.</summary>
    public string? UnlockKey { get; set; }

    /// <summary>UNC path of team.json; empty = ember-team.txt next to the exe or EMBER_TEAM_CONFIG.</summary>
    public string TeamConfigPath { get; set; } = "";
    public bool AutoUpdate { get; set; } = true;
    /// <summary>Last project folder used for "Install Playwright browsers".</summary>
    public string PlaywrightProject { get; set; } = "";

    // --- AI module ------------------------------------------------------------------
    public string ModelsDir { get; set; } = "";
    /// <summary>"auto", "cuda", "vulkan" or "cpu".</summary>
    public string LlamaVariant { get; set; } = "auto";
    public string LlamaCurrent { get; set; } = "";
    public int LlamaPort { get; set; } = 8080;
    public string LlamaArgs { get; set; } = "-ngl 99 -c 8192";
    public List<ModelEntry> Models { get; set; } = new();

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
                return Json.Deserialize<UserSettings>(File.ReadAllText(Paths.SettingsFile)) ?? new UserSettings();
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

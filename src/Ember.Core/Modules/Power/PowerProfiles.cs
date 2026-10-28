using System.Text.Json;

namespace Ember.Core;

public sealed class PowerSettingTarget
{
    /// <summary>Subgroup alias (SUB_PROCESSOR) or GUID.</summary>
    public string Sub { get; set; } = "";
    /// <summary>Setting alias (PERFEPP) or GUID.</summary>
    public string Setting { get; set; } = "";
    /// <summary>Target when plugged in (null = leave alone).</summary>
    public long? Ac { get; set; }
    /// <summary>Target on battery (null = leave alone).</summary>
    public long? Dc { get; set; }
}

public sealed class PowerProfileDefinition
{
    public string Id { get; set; } = "";
    public LocalizedText? Name { get; set; }
    public LocalizedText? Description { get; set; }
    public List<PowerSettingTarget> Settings { get; set; } = new();
}

public sealed record PowerValue(long? Current, long? Target, long? Default);

public sealed record PowerSettingState(
    string Alias, string Guid, string SubGuid, string Name, string? Unit,
    PowerValue Ac, PowerValue Dc, bool Supported, bool Locked, IReadOnlyDictionary<string, string> Options);

public sealed record PowerProfileState(
    string Id, string Name, string? Description, string Status, bool AtDefault, bool HasBackup,
    IReadOnlyList<PowerSettingState> Settings);

public sealed record PowerState(
    string SchemeGuid, string SchemeName, string ActiveGuid, string ActiveName, bool ActiveIsTarget,
    bool ActiveSchemeByPolicy, IReadOnlyList<PowerProfileState> Profiles);

public sealed record PowerApplyResult(int Changed, int Skipped, bool Elevated, IReadOnlyList<string> Errors);

/// <summary>
/// Energy profiles for the "Balanced" scheme. Ember never switches the power plan and never creates
/// "High performance"/"Ultimate performance" (often forbidden by group policy); it only adjusts
/// values inside the allowed scheme and can always go back to the factory defaults.
/// </summary>
public static class PowerProfiles
{
    static PowerSettingTarget T(string sub, string setting, long? ac = null, long? dc = null) =>
        new() { Sub = sub, Setting = setting, Ac = ac, Dc = dc };

    /// <summary>Built-in profiles. Team.json can replace them (same id) or add more.</summary>
    public static List<PowerProfileDefinition> BuiltIn() => new()
    {
        new()
        {
            Id = "ai-performance",
            // Plugged in only; battery values stay untouched. All efficiency classes (P/E cores) get the same values.
            Settings =
            {
                T("SUB_PROCESSOR", "PERFBOOSTMODE", ac: 2),
                T("SUB_PROCESSOR", "PERFEPP", ac: 0),
                T("SUB_PROCESSOR", "PERFEPP1", ac: 0),
                T("SUB_PROCESSOR", "PERFEPP2", ac: 0),
                T("SUB_PROCESSOR", "PROCTHROTTLEMAX", ac: 100),
                T("SUB_PROCESSOR", "PROCTHROTTLEMAX1", ac: 100),
                T("SUB_PROCESSOR", "PROCTHROTTLEMAX2", ac: 100),
                T("SUB_PROCESSOR", "PROCTHROTTLEMIN", ac: 5),
                T("SUB_PROCESSOR", "PROCTHROTTLEMIN1", ac: 5),
                T("SUB_PROCESSOR", "PROCTHROTTLEMIN2", ac: 5),
            },
        },
        new()
        {
            Id = "sleep-ac",
            Settings =
            {
                T("SUB_SLEEP", "STANDBYIDLE", ac: 0),        // never sleep while plugged in
                T("SUB_SLEEP", "HIBERNATEIDLE", ac: 0),
                T("SUB_VIDEO", "VIDEOIDLE", ac: 900),        // display off after 15 minutes
            },
        },
        new()
        {
            Id = "lid-ac",
            Settings =
            {
                T("SUB_BUTTONS", "LIDACTION", ac: 0),        // closing the lid on the dock: do nothing
                T("SUB_BUTTONS", "PBUTTONACTION", ac: 3),    // power button: shut down
            },
        },
    };

    public static List<PowerProfileDefinition> Effective(TeamConfig team)
    {
        var list = BuiltIn();
        foreach (var p in team.Power?.Profiles ?? new List<PowerProfileDefinition>())
        {
            var i = list.FindIndex(x => x.Id == p.Id);
            if (i >= 0) list[i] = p;
            else list.Add(p);
        }
        return list;
    }

    public static string TargetScheme(TeamConfig team) =>
        PowerCfg.Resolve(team.Power?.AllowedSchemes?.FirstOrDefault() ?? "SCHEME_BALANCED");

    public static async Task<PowerState> GetStateAsync(TeamConfig team, CancellationToken ct)
    {
        var scheme = TargetScheme(team);
        var settings = await PowerCfg.QueryAsync(scheme, ct);
        var (activeGuid, activeName) = await PowerCfg.ActiveSchemeAsync(ct);
        var backups = LoadBackups();
        var schemeName = activeGuid == scheme && activeName.Length > 0 ? activeName : PowerCfg.SchemeName(scheme);

        var profiles = Effective(team).Select(p => BuildState(p, scheme, settings, backups.ContainsKey(p.Id))).ToList();
        return new PowerState(scheme, schemeName, activeGuid, activeName, activeGuid == scheme, PowerCfg.ActiveSchemeByPolicy(), profiles);
    }

    internal static PowerProfileState BuildState(PowerProfileDefinition p, string scheme, List<PowerSettingInfo> settings, bool hasBackup)
    {
        var states = new List<PowerSettingState>();
        foreach (var t in p.Settings)
        {
            var sub = PowerCfg.Resolve(t.Sub);
            var guid = PowerCfg.Resolve(t.Setting);
            var info = settings.FirstOrDefault(s => s.Guid == guid);
            var (defAc, defDc) = PowerCfg.Defaults(scheme, sub, guid);
            states.Add(new PowerSettingState(
                info?.Alias ?? t.Setting, guid, sub, info?.Name ?? t.Setting, info?.Unit,
                new PowerValue(info?.Ac, t.Ac, defAc), new PowerValue(info?.Dc, t.Dc, defDc),
                info != null, PowerCfg.LockedByPolicy(guid),
                (info?.Options ?? new Dictionary<long, string>()).ToDictionary(o => o.Key.ToString(), o => o.Value)));
        }

        var relevant = states.Where(s => s.Supported && !s.Locked).ToList();
        bool Hit(PowerValue v) => v.Target == null || v.Current == v.Target;
        int hits = relevant.Count(s => Hit(s.Ac) && Hit(s.Dc));
        var status = relevant.Count == 0 ? "unsupported" : hits == relevant.Count ? "applied" : hits == 0 ? "notApplied" : "partial";
        bool atDefault = relevant.All(s => (s.Ac.Target == null || s.Ac.Default == null || s.Ac.Current == s.Ac.Default) &&
                                           (s.Dc.Target == null || s.Dc.Default == null || s.Dc.Current == s.Dc.Default));

        var name = p.Name?.Get() ?? L.TryT($"power.profiles.{p.Id}.name") ?? p.Id;
        var description = p.Description?.Get() ?? L.TryT($"power.profiles.{p.Id}.description");
        return new PowerProfileState(p.Id, name, description, status, atDefault, hasBackup, states);
    }

    public enum Mode { Apply, Defaults, Backup }

    /// <summary>Applies a profile, restores factory defaults, or restores the values from before the first change.</summary>
    public static async Task<PowerApplyResult> ChangeAsync(TeamConfig team, string profileId, Mode mode, CancellationToken ct)
    {
        var state = await GetStateAsync(team, ct);
        var profile = state.Profiles.FirstOrDefault(p => p.Id == profileId) ?? throw new EmberException("powerProfileUnknown", ("id", profileId));
        var backups = LoadBackups();
        backups.TryGetValue(profileId, out var backup);
        if (mode == Mode.Backup && backup == null) throw new EmberException("powerNoBackup");

        var changes = new List<(string Sub, string Setting, bool Ac, long Value)>();
        int skipped = 0;
        foreach (var s in profile.Settings)
        {
            if (!s.Supported || s.Locked) { skipped++; continue; }
            foreach (var ac in new[] { true, false })
            {
                var v = ac ? s.Ac : s.Dc;
                if (v.Target == null) continue;   // the profile does not touch this side
                long? wanted = mode switch
                {
                    Mode.Apply => v.Target,
                    Mode.Defaults => v.Default,
                    _ => backup!.TryGetValue(s.Guid, out var b) ? (ac ? b[0] : b[1]) : null,
                };
                if (wanted == null || wanted == v.Current) continue;
                changes.Add((s.SubGuid, s.Guid, ac, wanted.Value));
            }
        }

        if (mode == Mode.Apply && backup == null && changes.Count > 0)
        {
            backups[profileId] = profile.Settings.Where(s => s.Supported)
                .ToDictionary(s => s.Guid, s => new[] { s.Ac.Current, s.Dc.Current });
            SaveBackups(backups);
        }

        var errors = new List<string>();
        var failed = new List<(string Sub, string Setting, bool Ac, long Value)>();
        foreach (var c in changes)
        {
            var r = await PowerCfg.SetAsync(state.SchemeGuid, c.Sub, c.Setting, c.Ac, c.Value, ct);
            if (!r.Ok) failed.Add(c);
        }

        bool elevated = false;
        if (failed.Count > 0)
        {
            // Usually "access denied": retry all failed values with one UAC prompt.
            elevated = true;
            var lines = failed.Select(c => "& powercfg.exe " + string.Join(" ", PowerCfg.SetArgs(state.SchemeGuid, c.Sub, c.Setting, c.Ac, c.Value)) +
                                          "; if ($LASTEXITCODE -ne 0) { $errors += '" + c.Setting + "' }");
            var json = await PowerShell.RunElevatedJsonAsync("$errors = @()\n" + string.Join("\n", lines) + "\n$result = @{ errors = $errors }", ct);
            if (json.TryGetProperty("errors", out var errs))
                errors.AddRange(errs.AsArray().Select(e => L.T("power.setFailed", ("setting", e.GetString()))));
        }

        await PowerCfg.ReapplyAsync(state.SchemeGuid, ct);
        if (mode != Mode.Apply && errors.Count == 0)
        {
            backups.Remove(profileId);
            SaveBackups(backups);
        }
        Log.Info(L.T("power.changed", ("profile", profile.Name), ("count", changes.Count - errors.Count)));
        return new PowerApplyResult(changes.Count - errors.Count, skipped, elevated, errors);
    }

    static Dictionary<string, Dictionary<string, long?[]>> LoadBackups()
    {
        try
        {
            if (File.Exists(Paths.PowerBackupFile))
                return Json.Deserialize<Dictionary<string, Dictionary<string, long?[]>>>(File.ReadAllText(Paths.PowerBackupFile)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Log.Debug("power backup: " + ex.Message);
        }
        return new();
    }

    static void SaveBackups(Dictionary<string, Dictionary<string, long?[]>> backups)
    {
        Directory.CreateDirectory(Paths.AppData);
        File.WriteAllText(Paths.PowerBackupFile, Json.Serialize(backups, indented: true));
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ember.Core;

public sealed class PowerSettingInfo
{
    public string SubGuid { get; init; } = "";
    public string? SubAlias { get; set; }
    public string Guid { get; init; } = "";
    public string? Alias { get; set; }
    /// <summary>Name as Windows shows it, in the Windows display language.</summary>
    public string Name { get; init; } = "";
    public long? Ac { get; set; }
    public long? Dc { get; set; }
    public long? Min { get; set; }
    public long? Max { get; set; }
    public string? Unit { get; set; }
    public Dictionary<long, string> Options { get; } = new();
}

/// <summary>
/// Wrapper around powercfg.exe. The output is localized ("Index der aktuellen Wechselstromeinstellung"
/// vs. "Current AC Power Setting Index"), so it is parsed by structure – indentation, GUIDs and hex
/// values – never by the label text.
/// </summary>
public static class PowerCfg
{
    public const string SchemeBalanced = "381b4222-f694-41f0-9685-ff5bb260df2e";

    /// <summary>Aliases Ember uses, so profiles can name settings without GUIDs.</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownGuids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SCHEME_BALANCED"] = SchemeBalanced,
        ["SCHEME_MIN"] = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
        ["SCHEME_MAX"] = "a1841308-3541-4fab-bc81-f71556f20b4a",
        ["SUB_PROCESSOR"] = "54533251-82be-4824-96c1-47b60b740d00",
        ["PERFBOOSTMODE"] = "be337238-0d82-4146-a960-4f3749d470c7",
        ["PERFEPP"] = "36687f9e-e3a5-4dbf-b1dc-15eb381c6863",
        ["PERFEPP1"] = "36687f9e-e3a5-4dbf-b1dc-15eb381c6864",
        ["PERFEPP2"] = "36687f9e-e3a5-4dbf-b1dc-15eb381c6865",
        ["PROCTHROTTLEMAX"] = "bc5038f7-23e0-4960-96da-33abaf5935ec",
        ["PROCTHROTTLEMAX1"] = "bc5038f7-23e0-4960-96da-33abaf5935ed",
        ["PROCTHROTTLEMAX2"] = "bc5038f7-23e0-4960-96da-33abaf5935ee",
        ["PROCTHROTTLEMIN"] = "893dee8e-2bef-41e0-89c6-b55d0929964c",
        ["PROCTHROTTLEMIN1"] = "893dee8e-2bef-41e0-89c6-b55d0929964d",
        ["PROCTHROTTLEMIN2"] = "893dee8e-2bef-41e0-89c6-b55d0929964e",
        ["SUB_SLEEP"] = "238c9fa8-0aad-41ed-83f4-97be242c8f20",
        ["STANDBYIDLE"] = "29f6c1db-86da-48c5-9fdb-f2b67b1f44da",
        ["HIBERNATEIDLE"] = "9d7815a6-7ee4-497e-8888-515a05f02364",
        ["SUB_VIDEO"] = "7516b95f-f776-4464-8c53-06167f40cc99",
        ["VIDEOIDLE"] = "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e",
        ["SUB_BUTTONS"] = "4f971e89-eebd-4455-a8de-9e59040e7347",
        ["LIDACTION"] = "5ca83367-6e45-459f-a27b-476b1d01c936",
        ["PBUTTONACTION"] = "7648efa3-dd9c-4e3e-b566-50f929386280",
    };

    static readonly Regex GuidRegex = new(@"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(?:\s+\((.*)\))?", RegexOptions.IgnoreCase);
    static readonly Regex HexRegex = new(@"^0x([0-9a-f]+)$", RegexOptions.IgnoreCase);

    public static string Resolve(string aliasOrGuid) =>
        KnownGuids.TryGetValue(aliasOrGuid.Trim(), out var g) ? g : aliasOrGuid.Trim().ToLowerInvariant();

    static string Exe => ProcessRunner.SystemExe("powercfg.exe");

    /// <summary>powercfg /qh: all settings of a scheme, including hidden ones (PERFBOOSTMODE, PERFEPP …).</summary>
    public static async Task<List<PowerSettingInfo>> QueryAsync(string scheme, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(Exe, new[] { "/qh", scheme }, ct, 60, OutputKind.Oem, logOutput: false);
        if (!r.Ok) throw new EmberException("commandFailed", ("command", "powercfg /qh"), ("detail", r.Output));
        return Parse(r.StdOut);
    }

    /// <summary>Parses the output of powercfg /q or /qh (any display language).</summary>
    public static List<PowerSettingInfo> Parse(string output)
    {
        var result = new List<PowerSettingInfo>();
        string? subGuid = null, subAlias = null;
        PowerSettingInfo? current = null;
        int currentIndexLines = 0, rangeLines = 0;
        long? pendingOption = null;

        foreach (var raw in output.Replace("\r", "").Split('\n'))
        {
            if (raw.Trim().Length == 0) continue;
            int indent = raw.Length - raw.TrimStart(' ').Length;
            var line = raw.Trim();
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            var value = line[(colon + 1)..].Trim();
            var label = line[..colon];
            var guid = GuidRegex.Match(value);
            var hex = HexRegex.Match(value);
            bool isAlias = label.Contains("Alias", StringComparison.OrdinalIgnoreCase);

            switch (indent)
            {
                case 2 when guid.Success && !isAlias:     // subgroup
                    subGuid = guid.Groups[1].Value.ToLowerInvariant();
                    subAlias = null;
                    current = null;
                    break;
                case 4 when isAlias && current == null:   // subgroup alias
                    subAlias = value;
                    break;
                case 4 when guid.Success && !isAlias:     // setting
                    current = new PowerSettingInfo
                    {
                        SubGuid = subGuid ?? "",
                        SubAlias = subAlias,
                        Guid = guid.Groups[1].Value.ToLowerInvariant(),
                        Name = guid.Groups[2].Value.Trim(),
                    };
                    result.Add(current);
                    currentIndexLines = 0;
                    rangeLines = 0;
                    pendingOption = null;
                    break;
                case 4 when hex.Success && current != null:   // current AC (first) / DC (second) value
                    var v = Convert.ToInt64(hex.Groups[1].Value, 16);
                    if (currentIndexLines++ == 0) current.Ac = v;
                    else current.Dc = v;
                    break;
                case 6 when current != null:
                    if (isAlias) current.Alias = value;
                    else if (hex.Success)
                    {
                        var n = Convert.ToInt64(hex.Groups[1].Value, 16);
                        if (rangeLines == 0) current.Min = n;
                        else if (rangeLines == 1) current.Max = n;
                        rangeLines++;
                    }
                    else if (Regex.IsMatch(value, @"^\d{3}$")) pendingOption = long.Parse(value, CultureInfo.InvariantCulture);
                    else if (pendingOption != null)
                    {
                        current.Options[pendingOption.Value] = value;
                        pendingOption = null;
                    }
                    else if (rangeLines >= 3) current.Unit = value;
                    break;
            }
        }
        return result;
    }

    /// <summary>GUID and name of the active scheme (powercfg /getactivescheme).</summary>
    public static async Task<(string Guid, string Name)> ActiveSchemeAsync(CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(Exe, new[] { "/getactivescheme" }, ct, 30, OutputKind.Oem, logOutput: false);
        var m = GuidRegex.Match(r.StdOut);
        return m.Success ? (m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.Trim()) : ("", "");
    }

    public static string SchemeName(string guid) => guid switch
    {
        SchemeBalanced => L.T("power.schemes.balanced"),
        _ => guid,
    };

    public static IEnumerable<string> SetArgs(string scheme, string sub, string setting, bool ac, long value) =>
        new[] { ac ? "/setacvalueindex" : "/setdcvalueindex", scheme, sub, setting, value.ToString(CultureInfo.InvariantCulture) };

    public static async Task<ProcResult> SetAsync(string scheme, string sub, string setting, bool ac, long value, CancellationToken ct) =>
        await ProcessRunner.RunAsync(Exe, SetArgs(scheme, sub, setting, ac, value), ct, 30, OutputKind.Oem);

    /// <summary>Re-applies the active scheme so changes take effect immediately (only if it is the edited one).</summary>
    public static async Task ReapplyAsync(string scheme, CancellationToken ct)
    {
        var (active, _) = await ActiveSchemeAsync(ct);
        if (active.Equals(scheme, StringComparison.OrdinalIgnoreCase))
            await ProcessRunner.RunAsync(Exe, new[] { "/setactive", scheme }, ct, 30, OutputKind.Oem);
    }

    /// <summary>Factory default of a setting for a scheme (OEM value if the manufacturer set one).</summary>
    public static (long? Ac, long? Dc) Defaults(string scheme, string sub, string setting)
    {
        if (!OperatingSystem.IsWindows()) return (null, null);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\{sub}\{setting}\DefaultPowerSchemeValues\{scheme}");
            return (ToLong(key?.GetValue("ACSettingIndex")), ToLong(key?.GetValue("DCSettingIndex")));
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>True when a group policy fixes this setting (then Ember must not touch it).</summary>
    public static bool LockedByPolicy(string setting)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Policies\Microsoft\Power\PowerSettings\{setting}");
            return key?.GetValue("ACSettingIndex") != null || key?.GetValue("DCSettingIndex") != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when a policy sets the active power scheme.</summary>
    public static bool ActiveSchemeByPolicy()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Power\PowerSettings");
            return key?.GetValue("ActivePowerScheme") != null;
        }
        catch
        {
            return false;
        }
    }

    static long? ToLong(object? value) => value switch
    {
        int i => (uint)i,
        long l => l,
        _ => null,
    };
}

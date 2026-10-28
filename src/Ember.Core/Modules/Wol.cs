using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ember.Core;

public sealed record WolProp(string Keyword, string Name, string? Value, string? Display, string Target, bool Required, bool Ok);

public sealed record WolAdapter(string Name, string Description, string Mac, string Status, bool Armed, bool Ready, IReadOnlyList<WolProp> Props);

public sealed record WolLocalState(IReadOnlyList<WolAdapter> Adapters, int? Hiberboot, bool FastStartupOff, bool Ready, bool HasBackup);

/// <summary>
/// Prepares THIS machine to be woken from "shut down" (S5). The target state comes from a
/// machine where Wake-on-LAN works (Dell, Realtek USB GbE in the dock + Intel I226):
/// magic packet and S5 wake in the driver, the adapter may wake the PC, fast startup off.
/// The BIOS (Wake on LAN, wake on dock, MAC pass-through) is outside Ember's reach.
/// </summary>
public static class WolTarget
{
    /// <summary>keyword → (target value, required for waking from S5).</summary>
    public static readonly IReadOnlyDictionary<string, (string Value, bool Required)> Targets =
        new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase)
        {
            ["*WakeOnMagicPacket"] = ("1", true),
            ["S5WakeOnLan"] = ("1", true),                 // Realtek
            ["WakeOnMagicPacketFromS5"] = ("1", true),     // Intel I225/I226
            ["EnablePME"] = ("1", true),                   // older Intel drivers
            ["*WakeOnPattern"] = ("1", false),
            ["WakeOnLinkChange"] = ("1", false),
            ["WolShutdownLinkSpeed"] = ("0", false),       // 10 Mbit/s first
        };

    const string WiredFilter =
        "Get-NetAdapter -Physical | Where-Object { $_.Status -ne 'Not Present' -and ($_.PhysicalMediaType -eq '802.3' -or $_.NdisPhysicalMedium -eq 14) }";

    static string KeysLiteral => "@(" + string.Join(",", Targets.Keys.Select(PowerShell.Quote)) + ")";

    public static async Task<WolLocalState> GetStateAsync(CancellationToken ct)
    {
        var json = await PowerShell.RunJsonAsync($$"""
            $keys = {{KeysLiteral}}
            $armed = @(powercfg.exe /devicequery wake_armed | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
            $adapters = @({{WiredFilter}} | ForEach-Object {
              $a = $_
              $props = @(Get-NetAdapterAdvancedProperty -Name $a.Name -AllProperties -ErrorAction SilentlyContinue |
                Where-Object { $keys -contains $_.RegistryKeyword } | ForEach-Object {
                  @{ keyword = "$($_.RegistryKeyword)"; value = "$($_.RegistryValue)"; display = "$($_.DisplayValue)"; name = "$($_.DisplayName)" } })
              @{ name = $a.Name; description = $a.InterfaceDescription; mac = $a.MacAddress; status = "$($a.Status)";
                 armed = ($armed -contains $a.InterfaceDescription); props = $props }
            })
            $hb = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -ErrorAction SilentlyContinue).HiberbootEnabled
            @{ adapters = $adapters; hiberboot = $hb } | ConvertTo-Json -Depth 6 -Compress
            """, ct, 60);
        return Evaluate(json, File.Exists(Paths.WolBackupFile));
    }

    internal static WolLocalState Evaluate(JsonElement json, bool hasBackup)
    {
        var adapters = new List<WolAdapter>();
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("adapters", out var list))
        {
            foreach (var a in list.AsArray().Where(x => x.ValueKind == JsonValueKind.Object))
            {
                var props = new List<WolProp>();
                if (a.TryGetProperty("props", out var ps))
                {
                    foreach (var p in ps.AsArray())
                    {
                        var keyword = p.Str("keyword") ?? "";
                        if (!Targets.TryGetValue(keyword, out var t)) continue;
                        var value = p.Str("value");
                        props.Add(new WolProp(keyword, p.Str("name") ?? keyword, value, p.Str("display"), t.Value, t.Required, value == t.Value));
                    }
                }
                bool armed = a.Bool("armed") ?? false;
                bool magic = props.Any(p => p.Keyword == "*WakeOnMagicPacket" && p.Ok);
                var s5 = props.Where(p => p.Required && p.Keyword != "*WakeOnMagicPacket").ToList();
                bool ready = armed && magic && (s5.Count == 0 || s5.All(p => p.Ok));
                adapters.Add(new WolAdapter(a.Str("name") ?? "?", a.Str("description") ?? "", FormatMac(a.Str("mac") ?? ""),
                                            a.Str("status") ?? "", armed, ready, props));
            }
        }
        int? hb = (int?)json.Num("hiberboot");
        bool fastOff = hb is null or 0;
        return new WolLocalState(adapters, hb, fastOff, fastOff && adapters.Any(a => a.Ready), hasBackup);
    }

    /// <summary>Sets the target state (one UAC prompt). The adapters restart briefly.</summary>
    public static async Task<IReadOnlyList<string>> ApplyAsync(CancellationToken ct)
    {
        var before = await GetStateAsync(ct);
        if (!File.Exists(Paths.WolBackupFile))
        {
            Directory.CreateDirectory(Paths.ConfigDir);
            await File.WriteAllTextAsync(Paths.WolBackupFile, Json.Serialize(before, indented: true), ct);
        }

        var targets = "@{" + string.Join("; ", Targets.Select(t => $"{PowerShell.Quote(t.Key)} = {PowerShell.Quote(t.Value.Value)}")) + "}";
        var json = await PowerShell.RunElevatedJsonAsync($$"""
            $targets = {{targets}}
            $changed = @(); $errors = @()
            {{WiredFilter}} | ForEach-Object {
              $a = $_; $dirty = $false
              $present = @(Get-NetAdapterAdvancedProperty -Name $a.Name -AllProperties -ErrorAction SilentlyContinue)
              foreach ($k in $targets.Keys) {
                $p = $present | Where-Object { $_.RegistryKeyword -eq $k } | Select-Object -First 1
                if ($p -and "$($p.RegistryValue)" -ne $targets[$k]) {
                  try { Set-NetAdapterAdvancedProperty -Name $a.Name -RegistryKeyword $k -RegistryValue $targets[$k] -NoRestart; $changed += "$($a.Name): $($k)"; $dirty = $true }
                  catch { $errors += "$($a.Name) $($k): $($_.Exception.Message)" }
                }
              }
              & powercfg.exe /deviceenablewake "$($a.InterfaceDescription)" | Out-Null
              if ($dirty) { Restart-NetAdapter -Name $a.Name -ErrorAction SilentlyContinue }
            }
            Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled -Value 0 -Type DWord
            $result = @{ changed = $changed; errors = $errors }
            """, ct);
        return Report(json);
    }

    /// <summary>
    /// Back to defaults: driver defaults for the adapter properties, fast startup on again, and wake
    /// permission removed only where Ember granted it.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ResetAsync(CancellationToken ct)
    {
        WolLocalState? backup = null;
        try
        {
            if (File.Exists(Paths.WolBackupFile)) backup = Json.Deserialize<WolLocalState>(await File.ReadAllTextAsync(Paths.WolBackupFile, ct));
        }
        catch (JsonException) { }

        var wasArmed = backup == null
            ? "$null"
            : "@(" + string.Join(",", backup.Adapters.Where(a => a.Armed).Select(a => PowerShell.Quote(a.Description))) + ")";
        var json = await PowerShell.RunElevatedJsonAsync($$"""
            $keys = {{KeysLiteral}}
            $wasArmed = {{wasArmed}}
            $changed = @(); $errors = @()
            {{WiredFilter}} | ForEach-Object {
              $a = $_; $dirty = $false
              $present = @(Get-NetAdapterAdvancedProperty -Name $a.Name -AllProperties -ErrorAction SilentlyContinue)
              foreach ($k in $keys) {
                if ($present | Where-Object { $_.RegistryKeyword -eq $k }) {
                  try { Reset-NetAdapterAdvancedProperty -Name $a.Name -RegistryKeyword $k -NoRestart; $changed += "$($a.Name): $($k)"; $dirty = $true }
                  catch { $errors += "$($a.Name) $($k): $($_.Exception.Message)" }
                }
              }
              if ($wasArmed -ne $null -and -not ($wasArmed -contains $a.InterfaceDescription)) {
                & powercfg.exe /devicedisablewake "$($a.InterfaceDescription)" | Out-Null
              }
              if ($dirty) { Restart-NetAdapter -Name $a.Name -ErrorAction SilentlyContinue }
            }
            Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled -Value 1 -Type DWord
            $result = @{ changed = $changed; errors = $errors }
            """, ct);
        try { File.Delete(Paths.WolBackupFile); } catch { }
        return Report(json);
    }

    static IReadOnlyList<string> Report(JsonElement json)
    {
        var errors = json.TryGetProperty("errors", out var e) ? e.AsArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
        var changed = json.TryGetProperty("changed", out var c) ? c.AsArray().Count() : 0;
        Log.Info(L.T("wol.local.changed", ("count", changed)));
        foreach (var err in errors) Log.Warn(err);
        return errors;
    }

    public static string FormatMac(string mac)
    {
        var hex = Regex.Replace(mac, "[^0-9A-Fa-f]", "").ToUpperInvariant();
        return hex.Length == 12 ? string.Join("-", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2))) : mac;
    }
}

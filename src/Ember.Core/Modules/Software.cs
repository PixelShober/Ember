using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ember.Core;

public sealed record InstalledApp(string Name, string? Version, string? Publisher);

public sealed record SccmItem(string Kind, string Name, string? PackageId, string? ProgramId, string? AppId,
                              string? Version, string? InstallState, int? EvaluationState, string? LastRunStatus);

public sealed record SoftwareItem(
    string Key, string Name, string? Category, string? Note,
    InstalledApp? Installed, SccmItem? Sccm, string? Link, bool CanInstall);

public sealed record SoftwareList(bool ClientPresent, string? ClientVersion, IReadOnlyList<DevToolState> DevTools,
                                  IReadOnlyList<SoftwareItem> Items, IReadOnlyList<string> Errors);

/// <summary>Installed programs from the Uninstall registry keys (64-bit, 32-bit, per user).</summary>
public static class InstalledSoftware
{
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<InstalledApp> Read()
    {
        var result = new List<InstalledApp>();
        if (!OperatingSystem.IsWindows()) return result;
        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                     (RegistryHive.CurrentUser, RegistryView.Default),
                 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(UninstallKey);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var app = key.OpenSubKey(sub);
                    if (app?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                    if (app.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    result.Add(new InstalledApp(name.Trim(), app.GetValue("DisplayVersion") as string, app.GetValue("Publisher") as string));
                }
            }
            catch
            {
                // a hive we may not read: ignore
            }
        }
        return result.DistinctBy(a => (a.Name, a.Version)).ToList();
    }

    /// <summary>Wildcard match (* and ?), case-insensitive.</summary>
    public static bool Matches(string text, string pattern) =>
        Regex.IsMatch(text, "^" + Regex.Escape(pattern.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

/// <summary>
/// Software Center (Configuration Manager client). Many companies deploy packages/programs
/// (CCM_Program) rather than applications (CCM_Application), so both are read.
/// Installing goes through the client (ExecuteProgram) – exactly what the "Install" button
/// in Software Center does, with IT's rights and approvals. Ember never installs around SCCM.
/// </summary>
public static class SoftwareCenter
{
    public static async Task<SoftwareList> ListAsync(TeamConfig team, CancellationToken ct)
    {
        var errors = new List<string>();
        string? clientVersion = null;
        var sccm = new List<SccmItem>();

        try
        {
            var json = await PowerShell.RunJsonAsync("""
                $r = @{ client = $null; programs = @(); apps = @(); errors = @() }
                try { $c = Get-CimInstance -Namespace root\ccm -ClassName SMS_Client; $r.client = "$($c.ClientVersion)" } catch { $r.errors += "SMS_Client: $($_.Exception.Message)" }
                if ($r.client) {
                  try {
                    $r.programs = @(Get-CimInstance -Namespace root\ccm\ClientSDK -ClassName CCM_Program | ForEach-Object {
                      @{ name = "$($_.Name)"; packageName = "$($_.PackageName)"; packageId = "$($_.PackageID)"; programId = "$($_.ProgramID)";
                         version = "$($_.Version)"; evaluationState = $_.EvaluationState; lastRunStatus = "$($_.LastRunStatus)" } })
                  } catch { $r.errors += "CCM_Program: $($_.Exception.Message)" }
                  try {
                    $r.apps = @(Get-CimInstance -Namespace root\ccm\ClientSDK -ClassName CCM_Application | ForEach-Object {
                      @{ name = "$($_.Name)"; id = "$($_.Id)"; version = "$($_.SoftwareVersion)"; installState = "$($_.InstallState)";
                         evaluationState = $_.EvaluationState } })
                  } catch { $r.errors += "CCM_Application: $($_.Exception.Message)" }
                }
                $r | ConvertTo-Json -Depth 4 -Compress
                """, ct, 60);

            clientVersion = NullIfEmpty(json.Str("client"));
            if (json.TryGetProperty("errors", out var errs)) errors.AddRange(errs.AsArray().Select(e => e.GetString() ?? "").Where(e => e.Length > 0));
            if (json.TryGetProperty("programs", out var programs))
                sccm.AddRange(programs.AsArray().Select(p => new SccmItem(
                    "package", p.Str("name") ?? p.Str("packageName") ?? "?", p.Str("packageId"), p.Str("programId"), null,
                    NullIfEmpty(p.Str("version")), null, (int?)p.Num("evaluationState"), NullIfEmpty(p.Str("lastRunStatus")))));
            if (json.TryGetProperty("apps", out var apps))
                sccm.AddRange(apps.AsArray().Select(a => new SccmItem(
                    "application", a.Str("name") ?? "?", null, null, a.Str("id"),
                    NullIfEmpty(a.Str("version")), NullIfEmpty(a.Str("installState")), (int?)a.Num("evaluationState"), null)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(Net.Describe(ex));
        }

        var installed = await Task.Run(InstalledSoftware.Read, ct);
        var items = new List<SoftwareItem>();
        foreach (var entry in team.Software ?? new List<TeamSoftware>())
        {
            var sccmPattern = string.IsNullOrWhiteSpace(entry.SccmName) ? entry.Name : entry.SccmName!;
            var match = sccm.FirstOrDefault(x => InstalledSoftware.Matches(x.Name, sccmPattern))
                        ?? sccm.FirstOrDefault(x => x.Name.Contains(entry.Name, StringComparison.OrdinalIgnoreCase));
            var displayPattern = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Name + "*" : entry.DisplayName!;
            var app = installed.Where(a => InstalledSoftware.Matches(a.Name, displayPattern))
                               .OrderByDescending(a => a.Version, Comparer<string?>.Create((x, y) => AppInfo.CompareVersions(x ?? "0", y ?? "0")))
                               .FirstOrDefault();
            items.Add(new SoftwareItem(entry.Key, entry.Name, entry.Category, entry.Note?.Get(), app, match,
                                       LinkFor(entry, match), match is { Kind: "package", PackageId: not null, ProgramId: not null }));
        }

        var devTools = DevTools.States(team, installed, sccm, clientVersion != null);
        return new SoftwareList(clientVersion != null, clientVersion, devTools, items, errors);
    }

    static string? LinkFor(TeamSoftware entry, SccmItem? sccm) =>
        !string.IsNullOrWhiteSpace(entry.SoftwareCenterLink) ? entry.SoftwareCenterLink
        : sccm?.AppId is { Length: > 0 } id ? "softwarecenter:SoftwareID=" + id
        : null;

    /// <summary>Opens the item in Software Center, or Software Center itself if there is no deep link.</summary>
    public static void Open(string? link)
    {
        var target = link is { Length: > 0 } && link.StartsWith("softwarecenter:", StringComparison.OrdinalIgnoreCase)
            ? link
            : "softwarecenter:Page=AvailableSoftware";
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            throw new EmberException(ex, "softwareCenterMissing");
        }
    }

    /// <summary>Asks the Configuration Manager client to run a deployed program (same as "Install").</summary>
    public static async Task ExecuteProgramAsync(string packageId, string programId, CancellationToken ct)
    {
        var json = await PowerShell.RunJsonAsync($$"""
            $r = Invoke-CimMethod -Namespace root\ccm\ClientSDK -ClassName CCM_ProgramsManager -MethodName ExecuteProgram -Arguments @{ PackageID = {{PowerShell.Quote(packageId)}}; ProgramID = {{PowerShell.Quote(programId)}} }
            @{ returnValue = $r.ReturnValue } | ConvertTo-Json -Compress
            """, ct, 60);
        var code = json.Num("returnValue") ?? 0;
        Log.Info($"ExecuteProgram {packageId}/{programId}: ReturnValue {code}");
        if (code != 0) throw new EmberException("sccmExecuteFailed", ("code", code));
    }

    /// <summary>Current evaluation state of one program (for progress after ExecuteProgram).</summary>
    public static async Task<(int? State, string? LastRunStatus)> ProgramStateAsync(string packageId, string programId, CancellationToken ct)
    {
        var json = await PowerShell.RunJsonAsync($$"""
            $p = Get-CimInstance -Namespace root\ccm\ClientSDK -ClassName CCM_Program | Where-Object { $_.PackageID -eq {{PowerShell.Quote(packageId)}} -and $_.ProgramID -eq {{PowerShell.Quote(programId)}} } | Select-Object -First 1
            if ($p) { @{ state = $p.EvaluationState; lastRunStatus = "$($p.LastRunStatus)" } | ConvertTo-Json -Compress } else { 'null' }
            """, ct, 60);
        return ((int?)json.Num("state"), NullIfEmpty(json.Str("lastRunStatus")));
    }

    /// <summary>
    /// EvaluationState of CCM_SoftwareBase: 12 = installed, 13 = error, 8–10 = reboot pending.
    /// </summary>
    public static string StateKey(int? state) => state switch
    {
        null or 0 or 1 => "available",
        2 or 3 or 4 => "preparing",
        5 => "downloading",
        6 or 14 => "waiting",
        7 or 11 => "installing",
        8 or 9 or 10 => "reboot",
        12 => "installed",
        13 => "error",
        _ => "unknown",
    };

    static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

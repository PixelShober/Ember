using System.Xml.Linq;
using Microsoft.Win32;

namespace Ember.Core;

public sealed record CheckAction(string Id, string Label, bool Elevated);

public sealed record CheckResult(string Id, string Title, string Status, string Value, string? Detail, IReadOnlyList<CheckAction> Actions);

/// <summary>
/// Group policy and system checks. Ember cannot change group policies; it shows what applies,
/// refreshes policies, and fixes the few things a developer may change.
/// </summary>
public static class SystemChecks
{
    const string FileSystemKey = @"SYSTEM\CurrentControlSet\Control\FileSystem";
    const int GpResultTimeoutSec = 90;

    /// <summary>All checks in display order. gpresult can take a minute on a domain machine, so each check loads on its own.</summary>
    static readonly (string Id, Func<CancellationToken, Task<CheckResult>> Run)[] All =
    {
        ("gpo", GroupPolicyAsync),
        ("executionPolicy", ExecutionPolicyAsync),
        ("gitLongPaths", GitLongPathsAsync),
        ("winLongPaths", WindowsLongPathsAsync),
        ("proxyPolicy", ProxyPolicyAsync),
        ("devMode", DeveloperModeAsync),
    };

    /// <summary>The checks with status "pending", so the page can show them right away.</summary>
    public static List<CheckResult> Pending() =>
        All.Select(c => new CheckResult(c.Id, Title(c.Id), "pending", "", null, Array.Empty<CheckAction>())).ToList();

    public static async Task<CheckResult> RunOneAsync(string id, CancellationToken ct)
    {
        var check = All.FirstOrDefault(c => c.Id == id);
        if (check.Run == null) throw new EmberException("unknownAction", ("action", id));
        try
        {
            return await check.Run(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"System check {id}: {Net.Describe(ex)}");
            return new CheckResult(id, Title(id), "error", Net.Describe(ex), null, Array.Empty<CheckAction>());
        }
    }

    public static async Task<List<CheckResult>> RunAsync(CancellationToken ct) =>
        (await Task.WhenAll(All.Select(c => RunOneAsync(c.Id, ct)))).ToList();

    static string Title(string id) => L.T($"system.checks.{id}.title");

    static CheckAction Action(string id, string action, bool elevated = false) => new(action, L.T($"system.checks.{id}.actions.{action}"), elevated);

    static async Task<CheckResult> GroupPolicyAsync(CancellationToken ct)
    {
        var xml = Paths.TempFile("ember-gpresult", "xml");
        try
        {
            ProcResult r;
            try
            {
                r = await ProcessRunner.RunAsync(ProcessRunner.SystemExe("gpresult.exe"), new[] { "/scope", "user", "/x", xml, "/f" },
                                                 ct, GpResultTimeoutSec, OutputKind.Oem, logOutput: false);
            }
            catch (EmberException ex) when (ex.Code == "processTimeout")
            {
                return new CheckResult("gpo", Title("gpo"), "warn", L.T("system.checks.gpo.timeout", ("seconds", GpResultTimeoutSec)), null,
                                       new[] { Action("gpo", "gpupdate") });
            }
            if (!File.Exists(xml))
                return new CheckResult("gpo", Title("gpo"), "warn", L.T("system.checks.gpo.noResult"), r.Output, new[] { Action("gpo", "gpupdate") });
            var names = ParseGpResult(await File.ReadAllTextAsync(xml, ct));
            return new CheckResult("gpo", Title("gpo"), "ok", L.T("system.checks.gpo.applied", ("count", names.Count)),
                                   string.Join("\n", names), new[] { Action("gpo", "gpupdate") });
        }
        finally
        {
            try { File.Delete(xml); } catch { }
        }
    }

    /// <summary>Names of the applied user GPOs from gpresult /x (XML, so no language issues).</summary>
    internal static List<string> ParseGpResult(string xml)
    {
        var doc = XDocument.Parse(xml);
        var user = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "UserResults");
        if (user == null) return new List<string>();
        return user.Elements().Where(e => e.Name.LocalName == "GPO")
                   .Where(g => !string.Equals(Child(g, "FilterAllowed"), "false", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(Child(g, "AccessDenied"), "true", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(Child(g, "IsValid"), "false", StringComparison.OrdinalIgnoreCase))
                   .Select(g => Child(g, "Name"))
                   .OfType<string>()
                   .Where(n => n.Length > 0)
                   .Distinct()
                   .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                   .ToList();

        static string? Child(XElement e, string name) => e.Elements().FirstOrDefault(c => c.Name.LocalName == name)?.Value;
    }

    static async Task<CheckResult> ExecutionPolicyAsync(CancellationToken ct)
    {
        var json = await PowerShell.RunJsonAsync(
            "Get-ExecutionPolicy -List | ForEach-Object { @{ scope = \"$($_.Scope)\"; policy = \"$($_.ExecutionPolicy)\" } } | ConvertTo-Json -Compress", ct, 30);
        var list = json.AsArray().Select(e => (Scope: e.Str("scope") ?? "", Policy: e.Str("policy") ?? "")).ToList();
        var byPolicy = list.FirstOrDefault(x => x.Scope is "MachinePolicy" or "UserPolicy" && x.Policy != "Undefined");
        var effective = byPolicy.Policy ?? list.FirstOrDefault(x => x.Policy != "Undefined").Policy ?? "Restricted";
        if (string.IsNullOrEmpty(effective)) effective = "Restricted";
        bool ok = effective is "RemoteSigned" or "Unrestricted" or "Bypass";
        var detail = string.Join("\n", list.Select(x => $"{x.Scope}: {x.Policy}"));
        if (byPolicy.Policy != null)
            return new CheckResult("executionPolicy", Title("executionPolicy"), ok ? "ok" : "locked",
                                   L.T("system.checks.executionPolicy.byPolicy", ("policy", effective)), detail, Array.Empty<CheckAction>());
        return new CheckResult("executionPolicy", Title("executionPolicy"), ok ? "ok" : "warn", effective, detail,
                               ok ? Array.Empty<CheckAction>() : new[] { Action("executionPolicy", "remoteSigned") });
    }

    static async Task<CheckResult> GitLongPathsAsync(CancellationToken ct)
    {
        var git = Tools.Git;
        if (git == null) return new CheckResult("gitLongPaths", Title("gitLongPaths"), "missing", L.T("proxy.toolMissing"), null, Array.Empty<CheckAction>());
        var value = (await ProcessRunner.RunAsync(git, new[] { "config", "--global", "--get", "core.longpaths" }, ct, logOutput: false)).LastLine;
        bool ok = value.Equals("true", StringComparison.OrdinalIgnoreCase);
        return new CheckResult("gitLongPaths", Title("gitLongPaths"), ok ? "ok" : "warn", value.Length == 0 ? "–" : value, null,
                               ok ? Array.Empty<CheckAction>() : new[] { Action("gitLongPaths", "enable") });
    }

    static Task<CheckResult> WindowsLongPathsAsync(CancellationToken ct)
    {
        int? value = null;
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.LocalMachine.OpenSubKey(FileSystemKey);
            value = key?.GetValue("LongPathsEnabled") as int?;
        }
        bool ok = value == 1;
        return Task.FromResult(new CheckResult("winLongPaths", Title("winLongPaths"), ok ? "ok" : "warn",
            ok ? L.T("common.on") : L.T("common.off"), null,
            ok ? Array.Empty<CheckAction>() : new[] { Action("winLongPaths", "enable", elevated: true) }));
    }

    static Task<CheckResult> ProxyPolicyAsync(CancellationToken ct)
    {
        var found = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var (hive, name) in new[] { (Registry.CurrentUser, "HKCU"), (Registry.LocalMachine, "HKLM") })
            {
                using var key = hive.OpenSubKey(@"Software\Policies\Microsoft\Windows\CurrentVersion\Internet Settings");
                if (key == null) continue;
                foreach (var v in key.GetValueNames()) found.Add($"{name}: {v} = {key.GetValue(v)}");
            }
        }
        return Task.FromResult(new CheckResult("proxyPolicy", Title("proxyPolicy"), "info",
            found.Count > 0 ? L.T("system.checks.proxyPolicy.managed") : L.T("system.checks.proxyPolicy.notManaged"),
            found.Count > 0 ? string.Join("\n", found) : null, Array.Empty<CheckAction>()));
    }

    static Task<CheckResult> DeveloperModeAsync(CancellationToken ct)
    {
        int? value = null;
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
            value = key?.GetValue("AllowDevelopmentWithoutDevLicense") as int?;
        }
        return Task.FromResult(new CheckResult("devMode", Title("devMode"), "info",
            value == 1 ? L.T("common.on") : L.T("common.off"), L.T("system.checks.devMode.hint"), Array.Empty<CheckAction>()));
    }

    /// <summary>Runs a fix offered by a check. Returns a report text.</summary>
    public static async Task<string> RunActionAsync(string checkId, string action, CancellationToken ct)
    {
        switch (checkId, action)
        {
            case ("gpo", "gpupdate"):
            {
                var r = await ProcessRunner.RunAsync(ProcessRunner.SystemExe("gpupdate.exe"), new[] { "/force" }, ct, 300, OutputKind.Oem);
                return r.Output;
            }
            case ("executionPolicy", "remoteSigned"):
                await PowerShell.RunAsync("Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force", ct, 30);
                return "CurrentUser: RemoteSigned";
            case ("gitLongPaths", "enable"):
            {
                var git = Tools.Git ?? throw new EmberException("toolMissing", ("tool", "git"));
                var r = await ProcessRunner.RunAsync(git, new[] { "config", "--global", "core.longpaths", "true" }, ct);
                if (!r.Ok) throw new EmberException("commandFailed", ("command", "git config core.longpaths"), ("detail", r.Output));
                return "git config --global core.longpaths true";
            }
            case ("winLongPaths", "enable"):
                await PowerShell.RunElevatedJsonAsync(
                    "Set-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\FileSystem' -Name LongPathsEnabled -Value 1 -Type DWord\n$result = @{ ok = $true }", ct);
                return "LongPathsEnabled = 1";
        }
        throw new EmberException("unknownAction", ("action", $"{checkId}/{action}"));
    }

    /// <summary>The exact commands behind an admin action, for the mail to the service center.</summary>
    public static string AdminCommands(string checkId, string action) => (checkId, action) switch
    {
        ("winLongPaths", "enable") => "reg add HKLM\\SYSTEM\\CurrentControlSet\\Control\\FileSystem /v LongPathsEnabled /t REG_DWORD /d 1 /f",
        _ => $"{checkId}: {action}",
    };
}

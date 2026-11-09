namespace Ember.Core;

/// <summary>The modules that roles and keys unlock. The start page is there as soon as any module is.</summary>
public static class ModuleIds
{
    public const string All = "*";

    public static readonly IReadOnlyList<string> Known =
        new[] { "proxy", "git", "software", "playwright", "system", "power", "ai", "access", "settings", "log" };

    /// <summary>Admins can hide these with Ctrl+E.</summary>
    public static readonly IReadOnlyList<string> Advanced = new[] { "power", "ai", "access" };

    /// <summary>What a member of the old access list (before roles) could use.</summary>
    internal static readonly IReadOnlyList<string> LegacyMembers = new[] { "proxy", "git", "software", "playwright", "system", "settings", "log" };

    public static bool IsKnown(string id) => id == All || Known.Contains(id);
}

/// <summary>A role in team.json: which modules its members see.</summary>
public sealed class TeamRole
{
    public string Name { get; set; } = "";
    /// <summary>Module ids, or "*" for all.</summary>
    public List<string>? Modules { get; set; }
    public List<string>? Users { get; set; }
    public List<string>? Groups { get; set; }
    /// <summary>
    /// Unlocks the role for anyone who enters it, also without being listed (EMBER-XXXXX-…). Stored
    /// in plain text so admins can look it up; like the roles themselves it is no security boundary.
    /// </summary>
    public string? Key { get; set; }
    /// <summary>Developer tools the role does not offer, e.g. ["bruno"].</summary>
    public List<string>? HideTools { get; set; }
}

/// <param name="Source">"team" (roles in team.json), "key" (starter key, no team.json), "dev" (development build), "preview" (an admin looks through another role) or "none".</param>
/// <param name="Locked">null, "setup" (no team.json and no key) or "notListed" (team.json has no role for this account).</param>
/// <param name="HiddenTools">Developer tools none of the account's roles offers.</param>
/// <param name="StaleKey">The account had a key that no longer opens anything (rotated or removed).</param>
public sealed record Grant(IReadOnlySet<string> Modules, string Source, string? Locked, IReadOnlyList<string> Roles, string? KeyLevel,
                          IReadOnlySet<string>? HiddenTools = null, bool StaleKey = false)
{
    public bool Has(string module) => Modules.Contains(module);
    public bool Any => Modules.Count > 0;
    public bool Hides(string tool) => HiddenTools?.Contains(tool) == true;
}

/// <summary>
/// Who sees what. With a team.json its roles decide: a module is visible when one of the account's
/// roles lists it (or "*"). Nobody in a role: nothing, Ember shows the lock screen. Without a
/// team.json a starter key decides. Like everything here it keeps Ember out of the wrong hands but is
/// no security boundary, since Ember runs with the user's own rights and its code is public.
/// </summary>
public static class Permissions
{
    public static Grant Compute(TeamConfig team, TeamStatus status, string? key, string account, IReadOnlyList<string> groups, bool dev)
    {
        if (dev) return new Grant(new HashSet<string>(ModuleIds.Known), "dev", null, Array.Empty<string>(), null);

        if (status.Path != null && status.LoadedAt != null)
        {
            var all = Roles(team);
            var byKey = all.Where(r => StarterKeys.Same(r.Key, key)).ToList();
            var mine = all.Where(r => Access.Listed(r.Users, r.Groups, account, groups)).Union(byKey).ToList();
            var modules = Expand(mine.SelectMany(r => r.Modules ?? new List<string>()));
            // A key was entered but opens no role (any more): the admin rotated it.
            bool stale = !string.IsNullOrWhiteSpace(key) && byKey.Count == 0;
            return new Grant(modules, "team", modules.Count == 0 ? "notListed" : null, mine.Select(r => r.Name).ToList(), null,
                             Hidden(mine), stale && modules.Count == 0);
        }

        if (StarterKeys.Level(key) is { } level)
            return new Grant(Expand(StarterKeys.LevelModules[level]), "key", null, Array.Empty<string>(), level,
                             StarterKeys.LevelHiddenTools.TryGetValue(level, out var hide) ? hide.ToHashSet() : null);

        return new Grant(new HashSet<string>(), "none", "setup", Array.Empty<string>(), null);
    }

    /// <summary>A tool is hidden only if every one of the roles hides it.</summary>
    static IReadOnlySet<string>? Hidden(IReadOnlyList<TeamRole> roles)
    {
        if (roles.Count == 0) return null;
        var hidden = new HashSet<string>(roles[0].HideTools ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var r in roles.Skip(1)) hidden.IntersectWith(r.HideTools ?? new List<string>());
        return hidden;
    }

    /// <summary>
    /// What another account would see, for an admin who wants to check Ember from their side:
    /// "role:&lt;name&gt;" (one role of the team.json), "key:&lt;level&gt;" (a starter key) or "none"
    /// (nobody's role: the lock screen). null for an unknown target.
    /// </summary>
    public static Grant? Preview(TeamConfig team, string target)
    {
        if (target == "none") return new Grant(new HashSet<string>(), "preview", "notListed", Array.Empty<string>(), null);
        if (target.StartsWith("key:", StringComparison.Ordinal))
        {
            var level = target[4..];
            return StarterKeys.LevelModules.TryGetValue(level, out var keyModules)
                ? new Grant(Expand(keyModules), "preview", null, Array.Empty<string>(), level,
                            StarterKeys.LevelHiddenTools.TryGetValue(level, out var hide) ? hide.ToHashSet() : null) : null;
        }
        if (target.StartsWith("role:", StringComparison.Ordinal))
        {
            var role = Roles(team).FirstOrDefault(r => r.Name == target[5..]);
            if (role == null) return null;
            var modules = Expand(role.Modules ?? new List<string>());
            return new Grant(modules, "preview", modules.Count == 0 ? "notListed" : null, new[] { role.Name }, null, Hidden(new[] { role }));
        }
        return null;
    }

    /// <summary>
    /// The roles of a team.json. Older files without roles still work: "admins" become a role with
    /// every module, the old access list a role with the everyday modules.
    /// </summary>
    public static List<TeamRole> Roles(TeamConfig team)
    {
        var roles = new List<TeamRole>(team.Access?.Roles ?? new List<TeamRole>());
        if (team.Admins is { } admins && (admins.Users?.Count ?? 0) + (admins.Groups?.Count ?? 0) > 0)
            roles.Add(new TeamRole { Name = "Admins", Modules = new() { ModuleIds.All }, Users = admins.Users, Groups = admins.Groups });
        if (team.Access is { } access && (access.Users?.Count ?? 0) + (access.Groups?.Count ?? 0) > 0)
            roles.Add(new TeamRole { Name = "Ember", Modules = ModuleIds.LegacyMembers.ToList(), Users = access.Users, Groups = access.Groups });
        return roles;
    }

    public static bool UsesLegacyFormat(TeamConfig team) =>
        team.Admins != null || team.Access?.Users != null || team.Access?.Groups != null || team.Access?.Enabled != null;

    static HashSet<string> Expand(IEnumerable<string> modules)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in modules.Select(m => m.Trim().ToLowerInvariant()))
        {
            if (m == ModuleIds.All) set.UnionWith(ModuleIds.Known);
            else if (ModuleIds.Known.Contains(m)) set.Add(m);
        }
        return set;
    }

    /// <summary>
    /// The modules a bridge method belongs to (any of them is enough). null: any account with at
    /// least one module may call it (jobs, mail, updates, the window).
    /// </summary>
    public static string[]? ModulesFor(string method) => method switch
    {
        "px.status" => null,
        "proxy.detect" => new[] { "proxy", "settings" },
        "settings.save" => new[] { "settings", "ai" },
        "settings.get" or "settings.defaults" => null,
        _ when method.StartsWith("proxy.", StringComparison.Ordinal) || method.StartsWith("px.", StringComparison.Ordinal) => new[] { "proxy" },
        _ when method.StartsWith("git.", StringComparison.Ordinal) => new[] { "git" },
        _ when method.StartsWith("software.", StringComparison.Ordinal) || method.StartsWith("devtools.", StringComparison.Ordinal) => new[] { "software" },
        _ when method.StartsWith("playwright.", StringComparison.Ordinal) => new[] { "playwright" },
        _ when method.StartsWith("system.", StringComparison.Ordinal) => new[] { "system" },
        _ when method.StartsWith("power.", StringComparison.Ordinal) => new[] { "power" },
        _ when method.StartsWith("wol.", StringComparison.Ordinal) => new[] { "ai" },   // wake-on-LAN for this machine sits in the KI tab
        _ when method.StartsWith("ai.", StringComparison.Ordinal) => new[] { "ai" },
        _ when method.StartsWith("access.", StringComparison.Ordinal) => new[] { "access" },
        _ when method.StartsWith("log.", StringComparison.Ordinal) => new[] { "log" },
        _ => null,
    };

    public static bool MayCall(Grant grant, string method) =>
        grant.Any && (ModulesFor(method) is not { } needed || needed.Any(grant.Has));
}

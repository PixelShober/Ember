using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;

namespace Ember.Core;

/// <summary>
/// Who sees which module (team.json "access"): roles with modules, users and AD groups, plus where
/// access requests go. Without roles nobody sees anything. "enabled", "users" and "groups" are the
/// format before roles; Ember still reads them (see <see cref="Permissions.Roles"/>).
/// </summary>
public sealed class TeamAccess
{
    public List<TeamRole>? Roles { get; set; }
    /// <summary>Where access requests go (mail address); default: the service center.</summary>
    public string? Contact { get; set; }

    // Format before roles.
    public bool? Enabled { get; set; }
    public List<string>? Users { get; set; }
    public List<string>? Groups { get; set; }
}

/// <param name="Kind">"user" or "group".</param>
public sealed record AccessEntry(string Name, string Kind);

/// <summary>A role as the Berechtigungen tab shows and saves it.</summary>
public sealed record AccessRole(string Name, List<string> Modules, List<AccessEntry> Members, string? Key = null, List<string>? HideTools = null);

/// <param name="Version">SHA-256 of team.json as read; saving refuses when the file changed in between.</param>
/// <param name="Writable">This account may write team.json (and it was not loaded from the offline copy).</param>
/// <param name="Legacy">The file still has "admins" or the old access list; saving turns them into roles.</param>
public sealed record AccessView(IReadOnlyList<AccessRole> Roles, string? Contact, string? TeamPath, bool Writable, string? Version,
                                string? Problem, string Account, IReadOnlyList<string> MyGroups, IReadOnlyList<string> Modules, bool Legacy);

/// <param name="Found">true: exists in the directory; false: does not exist; null: could not be checked (e.g. no VPN).</param>
public sealed record AccessLookup(string Name, string? Kind, bool? Found);

public sealed record AccessChange(List<AccessRole> Roles, string? Contact, string Version);

public static class Access
{
    static readonly Lazy<IReadOnlyList<string>> Groups = new(Elevation.GroupNames);

    /// <summary>The current account's groups (read once; the token does not change while Ember runs).</summary>
    public static IReadOnlyList<string> MyGroups => Groups.Value;

    public static bool DevBuild => AppInfo.Version.EndsWith("-dev", StringComparison.Ordinal);

    internal static bool Listed(List<string>? users, List<string>? groups, string account, IReadOnlyList<string> mine) =>
        (users ?? new List<string>()).Any(u => Same(u, account)) ||
        (groups ?? new List<string>()).Any(g => mine.Any(m => Same(g, m)));

    /// <summary>"FIRMA\\max" matches "FIRMA\\max" and "max"; the comparison ignores case.</summary>
    internal static bool Same(string configured, string actual) =>
        configured.Trim().Length > 0 &&
        (configured.Trim().Equals(actual, StringComparison.OrdinalIgnoreCase) ||
         (!configured.Contains('\\') && actual.EndsWith("\\" + configured.Trim(), StringComparison.OrdinalIgnoreCase)));

    // ------------------------------------------------------------------ the admin tab

    public static async Task<AccessView> ViewAsync(TeamConfig team, TeamStatus status, CancellationToken ct)
    {
        string? version = null, problem = null;
        bool writable = false;
        if (status.Path == null) problem = "noTeamFile";
        else if (status.FromCache) problem = "teamOffline";
        else
        {
            try
            {
                // Network shares can hang when the VPN is down.
                (version, writable) = await Task.Run(() =>
                {
                    var bytes = File.ReadAllBytes(status.Path);
                    bool canWrite;
                    try
                    {
                        using var _ = new FileStream(status.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                        canWrite = true;
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                        canWrite = false;
                    }
                    return (Hash(bytes), canWrite);
                }, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
                if (!writable) problem = "teamReadOnly";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                problem = "teamOffline";
                Log.Debug("access view: " + Net.Describe(ex));
            }
        }

        var domain = Environment.UserDomainName + "\\";
        var mine = MyGroups.Where(g => g.StartsWith(domain, StringComparison.OrdinalIgnoreCase) &&
                                       !g.StartsWith(Environment.MachineName + "\\", StringComparison.OrdinalIgnoreCase))
                           .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                           .ToList();
        var roles = Permissions.Roles(team)
            .Select(r => new AccessRole(r.Name, (r.Modules ?? new()).ToList(), Entries(r.Users, r.Groups), r.Key, r.HideTools))
            .ToList();
        return new AccessView(roles, team.Access?.Contact, status.Path, writable && problem == null, version, problem,
                              Elevation.AccountName, mine, ModuleIds.Known, Permissions.UsesLegacyFormat(team));
    }

    static List<AccessEntry> Entries(List<string>? users, List<string>? groups) =>
        (users ?? new()).Select(u => new AccessEntry(u, "user"))
                        .Concat((groups ?? new()).Select(g => new AccessEntry(g, "group")))
                        .ToList();

    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// Writes the roles back into team.json: only "access" changes (and an old "admins" section goes),
    /// everything else stays as it was (key order, indentation, line endings, BOM). A copy of the old
    /// file goes to C:\Ember\Config\team-backups.
    /// </summary>
    public static string Save(string path, AccessChange change, string account, IReadOnlyList<string> myGroups)
    {
        Validate(change);
        if (!DevBuild) EnsureStillAdmin(change, account, myGroups);
        if (change.Contact is { Length: > 0 } contact && !contact.Contains('@')) throw new EmberException("accessContactInvalid", ("value", contact));

        var bytes = File.ReadAllBytes(path);
        if (Hash(bytes) != change.Version) throw new EmberException("teamChanged", ("path", path));
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        var updated = Apply(text, change);

        Backup(path, bytes);
        File.WriteAllText(path, updated, new UTF8Encoding(bom));
        Log.Info(L.T("access.saved", ("path", path), ("roles", change.Roles.Count), ("members", change.Roles.Sum(r => r.Members.Count))));
        return updated;
    }

    internal static void Validate(AccessChange change)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in change.Roles.Where(r => !string.IsNullOrWhiteSpace(r.Key)))
            if (!keys.Add(StarterKeys.Normalize(role.Key))) throw new EmberException("accessKeyDuplicate", ("role", role.Name.Trim()));
        foreach (var role in change.Roles)
        {
            var name = role.Name.Trim();
            if (name.Length == 0) throw new EmberException("accessRoleNameMissing");
            if (!names.Add(name)) throw new EmberException("accessRoleDuplicate", ("name", name));
            if (role.Modules.FirstOrDefault(m => !ModuleIds.IsKnown(m)) is { } unknown)
                throw new EmberException("accessModuleUnknown", ("module", unknown), ("role", name));
            if (!string.IsNullOrWhiteSpace(role.Key) && StarterKeys.Normalize(role.Key).Length != 20)
                throw new EmberException("accessKeyInvalid", ("role", name));
        }
    }

    /// <summary>Nobody locks themselves out: whoever saves must still be able to open this tab afterwards.</summary>
    internal static void EnsureStillAdmin(AccessChange change, string account, IReadOnlyList<string> myGroups)
    {
        var mine = change.Roles.Where(r => Listed(Names(r.Members, "user").ToList(), Names(r.Members, "group").ToList(), account, myGroups));
        if (!mine.Any(r => r.Modules.Contains(ModuleIds.All) || r.Modules.Contains("access"))) throw new EmberException("accessSelfLockout");
    }

    static IEnumerable<string> Names(IEnumerable<AccessEntry> entries, string kind) =>
        entries.Where(e => e.Kind == kind).Select(e => e.Name.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    internal static string Apply(string json, AccessChange change)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                   ?? throw new EmberException("teamInvalid", ("detail", "{ … }"));
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new EmberException("teamInvalid", ("detail", ex.Message));
        }

        static JsonArray Array(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

        var access = root["access"] as JsonObject ?? new JsonObject();
        // The format before roles goes; its people are in the roles now.
        access.Remove("enabled");
        access.Remove("users");
        access.Remove("groups");
        access["roles"] = new JsonArray(change.Roles.Select(r => (JsonNode?)new JsonObject
        {
            ["name"] = r.Name.Trim(),
            ["modules"] = Array(r.Modules.Select(m => m.Trim()).Distinct(StringComparer.Ordinal)),
            ["users"] = Array(Names(r.Members, "user")),
            ["groups"] = Array(Names(r.Members, "group")),
        }).ToArray());
        // Key and hidden tools only where a role has them, so files without stay as they were.
        foreach (var (node, role) in ((JsonArray)access["roles"]!).Zip(change.Roles))
        {
            var o = (JsonObject)node!;
            if (!string.IsNullOrWhiteSpace(role.Key)) o["key"] = role.Key.Trim();
            if (role.HideTools is { Count: > 0 } hide) o["hideTools"] = Array(hide.Select(h => h.Trim()).Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        }
        if (string.IsNullOrWhiteSpace(change.Contact)) access.Remove("contact");
        else access["contact"] = change.Contact.Trim();
        root["access"] = access;
        root.Remove("admins");
        return Json.WriteLike(root, json);
    }

    static void Backup(string path, byte[] bytes)
    {
        try
        {
            var dir = Path.Combine(Paths.ConfigDir, "team-backups");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"team-{DateTime.Now:yyyyMMdd-HHmmss}.json"), bytes);
            foreach (var old in Directory.GetFiles(dir, "team-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(20))
                File.Delete(old);
        }
        catch (Exception ex)
        {
            Log.Warn("team.json backup: " + Net.Describe(ex));
        }
    }

    // ------------------------------------------------------------------ directory lookup

    /// <summary>Looks a name up in Windows/Active Directory: canonical "DOMAIN\Name" and whether it is a user or a group.</summary>
    public static AccessLookup Lookup(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new EmberException("missingParam", ("name", "name"));
        if (!OperatingSystem.IsWindows()) return new AccessLookup(name, null, null);

        uint sidSize = 0, domainSize = 0;
        LookupAccountNameW(null, name, null, ref sidSize, null, ref domainSize, out _);
        int error = Marshal.GetLastWin32Error();
        if (error == NoneMapped) return new AccessLookup(name, null, false);
        if (sidSize == 0) return new AccessLookup(name, null, null);

        var sid = new byte[sidSize];
        var domain = new StringBuilder((int)domainSize);
        if (!LookupAccountNameW(null, name, sid, ref sidSize, domain, ref domainSize, out var use))
            return Marshal.GetLastWin32Error() == NoneMapped ? new AccessLookup(name, null, false) : new AccessLookup(name, null, null);

        var kind = use switch
        {
            SidTypeUser => "user",
            SidTypeGroup or SidTypeAlias or SidTypeWellKnownGroup => "group",
            _ => null,
        };
        string canonical;
        try
        {
            canonical = new SecurityIdentifier(sid, 0).Translate(typeof(NTAccount)).Value;
        }
        catch
        {
            canonical = domain.Length > 0 && !name.Contains('\\') ? domain + "\\" + name : name;
        }
        return new AccessLookup(canonical, kind, kind != null);
    }

    const int NoneMapped = 1332;
    const int SidTypeUser = 1, SidTypeGroup = 2, SidTypeAlias = 4, SidTypeWellKnownGroup = 5;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool LookupAccountNameW(string? systemName, string accountName, byte[]? sid, ref uint sidSize,
                                          StringBuilder? domainName, ref uint domainSize, out int use);
}

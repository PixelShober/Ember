using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;

namespace Ember.Core;

/// <summary>
/// Who sees which module (team file "access"): roles with modules, users and AD groups, and the block
/// list that beats every role. Without roles nobody sees anything. "enabled", "users" and "groups"
/// are the format before roles; Ember still reads them (see <see cref="Permissions.Roles"/>).
/// </summary>
public sealed class TeamAccess
{
    public List<TeamRole>? Roles { get; set; }
    /// <summary>Accounts and groups that never get in, whatever a role says.</summary>
    public TeamBlocked? Blocked { get; set; }

    // Format before roles.
    public bool? Enabled { get; set; }
    public List<string>? Users { get; set; }
    public List<string>? Groups { get; set; }
}

public sealed class TeamBlocked
{
    public List<string>? Users { get; set; }
    public List<string>? Groups { get; set; }
}

/// <param name="Kind">"user" or "group".</param>
public sealed record AccessEntry(string Name, string Kind);

/// <summary>A role as the Berechtigungen tab shows and saves it.</summary>
public sealed record AccessRole(string Name, List<string> Modules, List<AccessEntry> Members, List<string>? HideTools = null);

/// <param name="Name">"DOMAIN\\sAMAccountName", as roles store it.</param>
/// <param name="Detail">Mail address (users) or description (groups), to tell similar names apart.</param>
public sealed record DirectoryHit(string Name, string Kind, string? Display, string? Detail);

/// <param name="Found">true: exists in the directory; false: does not exist; null: could not be checked (e.g. no VPN).</param>
public sealed record AccessLookup(string Name, string? Kind, bool? Found);

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

    internal static void Validate(IReadOnlyList<AccessRole> roles)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            var name = role.Name.Trim();
            if (name.Length == 0) throw new EmberException("accessRoleNameMissing");
            if (!names.Add(name)) throw new EmberException("accessRoleDuplicate", ("name", name));
            if (role.Modules.FirstOrDefault(m => !ModuleIds.IsKnown(m)) is { } unknown)
                throw new EmberException("accessModuleUnknown", ("module", unknown), ("role", name));
        }
    }

    /// <summary>Nobody locks themselves out: whoever saves must still open this tab afterwards, and not be blocked.</summary>
    internal static void EnsureStillAdmin(IReadOnlyList<AccessRole> roles, IReadOnlyList<AccessEntry> blocked, string account, IReadOnlyList<string> myGroups)
    {
        if (Listed(Names(blocked, "user").ToList(), Names(blocked, "group").ToList(), account, myGroups)) throw new EmberException("accessSelfBlocked");
        var mine = roles.Where(r => Listed(Names(r.Members, "user").ToList(), Names(r.Members, "group").ToList(), account, myGroups));
        if (!mine.Any(r => r.Modules.Contains(ModuleIds.All) || r.Modules.Contains("access"))) throw new EmberException("accessSelfLockout");
    }

    internal static IEnumerable<string> Names(IEnumerable<AccessEntry> entries, string kind) =>
        entries.Where(e => e.Kind == kind).Select(e => e.Name.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    internal static List<AccessEntry> Entries(List<string>? users, List<string>? groups) =>
        (users ?? new()).Select(u => new AccessEntry(u, "user"))
                        .Concat((groups ?? new()).Select(g => new AccessEntry(g, "group")))
                        .ToList();

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static void Backup(string path, byte[] bytes)
    {
        try
        {
            var dir = Path.Combine(Paths.ConfigDir, "team-backups");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"team-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}"), bytes);
            foreach (var old in Directory.GetFiles(dir, "team-*.*").OrderByDescending(f => f, StringComparer.Ordinal).Skip(20))
                File.Delete(old);
        }
        catch (Exception ex)
        {
            Log.Warn("team file backup: " + Net.Describe(ex));
        }
    }

    // ------------------------------------------------------------------ directory search

    /// <summary>
    /// Users and groups in the Active Directory whose name, display name, mail or group name starts
    /// with (or contains) the text, at most 20. Runs with the user's own login (LDAP via ADSI),
    /// like every directory query in Ember; without domain or VPN the list stays empty.
    /// </summary>
    public static async Task<IReadOnlyList<DirectoryHit>> SearchAsync(string query, CancellationToken ct)
    {
        var q = LdapEscape(query.Trim());
        if (q.Length < 2 || !OperatingSystem.IsWindows()) return Array.Empty<DirectoryHit>();
        var filter = $"(|(&(objectCategory=person)(objectClass=user)(|(sAMAccountName={q}*)(displayName=*{q}*)(givenName={q}*)(sn={q}*)(mail={q}*)))" +
                     $"(&(objectCategory=group)(|(sAMAccountName=*{q}*)(cn=*{q}*))))";
        var json = await PowerShell.RunJsonAsync($$"""
            $s = New-Object System.DirectoryServices.DirectorySearcher
            $s.Filter = {{PowerShell.Quote(filter)}}
            $s.SizeLimit = 20
            foreach ($p in 'sAMAccountName','displayName','mail','description','objectClass') { [void]$s.PropertiesToLoad.Add($p) }
            @($s.FindAll() | ForEach-Object {
              $o = $_.Properties
              @{ sam = "$($o['samaccountname'])"; display = "$($o['displayname'])"; mail = "$($o['mail'])";
                 description = "$($o['description'])"; group = [bool]($o['objectclass'] -contains 'group') }
            }) | ConvertTo-Json -Compress -Depth 3
            """, ct, 15);
        var domain = Environment.UserDomainName;
        var hits = new List<DirectoryHit>();
        foreach (var e in json.ValueKind == System.Text.Json.JsonValueKind.Array ? json.EnumerateArray() : json.ValueKind == System.Text.Json.JsonValueKind.Object ? new[] { json }.AsEnumerable() : Enumerable.Empty<System.Text.Json.JsonElement>())
        {
            var sam = e.Str("sam");
            if (string.IsNullOrWhiteSpace(sam)) continue;
            bool group = e.Bool("group") ?? false;
            string? Empty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v;
            hits.Add(new DirectoryHit($"{domain}\\{sam}", group ? "group" : "user", Empty(e.Str("display")), group ? Empty(e.Str("description")) : Empty(e.Str("mail"))));
        }
        // Users first, then groups, each alphabetically: the order people expect when they type a name.
        return hits.OrderBy(h => h.Kind == "user" ? 0 : 1).ThenBy(h => h.Display ?? h.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>RFC 4515: * ( ) \ and NUL lose their meaning in an LDAP filter.</summary>
    internal static string LdapEscape(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
            sb.Append(c switch { '*' => @"\2a", '(' => @"\28", ')' => @"\29", '\\' => @"\5c", '\0' => @"\00", _ => c.ToString() });
        return sb.ToString();
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

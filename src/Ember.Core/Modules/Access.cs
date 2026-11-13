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

    /// <summary>
    /// Only accounts and groups that exist in the Active Directory go into the team file: every entry that
    /// was not there before is looked up. Not found, a local account of this PC, or no answer from the
    /// directory (no VPN): the save is refused. Entries already in the file stay as they are.
    /// </summary>
    internal static void EnsureInDirectory(IReadOnlyList<AccessRole> roles, IReadOnlyList<AccessEntry> blocked, TeamConfig current, Func<string, AccessLookup> lookup)
    {
        var before = TeamAdmin.Doc(current).Access;
        var known = new HashSet<string>(before.Roles.SelectMany(r => r.Members).Concat(before.Blocked).Select(e => e.Name.Trim()), StringComparer.OrdinalIgnoreCase);
        var added = roles.SelectMany(r => r.Members).Concat(blocked).Select(e => e.Name.Trim())
                         .Where(n => n.Length > 0 && !known.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in added)
        {
            var found = lookup(name).Found;
            if (found == false) throw new EmberException("accessNotInDirectory", ("name", name));
            if (found == null) throw new EmberException("accessDirectoryUnreachable", ("name", name));
        }
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
    /// Users and groups in the Active Directory that match the text, at most 25. Each word has to match
    /// the start of first name, last name, display name, account or mail (users) or any part of the
    /// group name, in any order: "Max Muster", "Mustermann Max", "Mustermann, Max" and "mmuster" all
    /// find Max Mustermann. With directory.searchBase in the team file only that OU (and its sub-OUs)
    /// is searched, which is much faster in a large domain. Runs with the user's own login (LDAP via
    /// ADSI), like every directory query in Ember; without domain or VPN the list stays empty.
    /// </summary>
    public static async Task<IReadOnlyList<DirectoryHit>> SearchAsync(string query, TeamDirectory? directory, CancellationToken ct)
    {
        var words = Words(query);
        if (string.Concat(words).Length < 2 || !OperatingSystem.IsWindows()) return Array.Empty<DirectoryHit>();
        var userBase = Base(directory?.SearchBase);
        var groupBase = Base(directory?.GroupSearchBase) ?? userBase;
        var json = await PowerShell.RunJsonAsync($$"""
            function Find($base, $filter) {
              $s = New-Object System.DirectoryServices.DirectorySearcher
              if ($base) { $s.SearchRoot = New-Object System.DirectoryServices.DirectoryEntry($base) }
              $s.Filter = $filter
              $s.SizeLimit = 25
              $s.ServerTimeLimit = [TimeSpan]::FromSeconds(10)
              foreach ($p in 'sAMAccountName','displayName','givenName','sn','mail','description','objectClass') { [void]$s.PropertiesToLoad.Add($p) }
              @($s.FindAll() | ForEach-Object {
                $o = $_.Properties
                @{ sam = "$($o['samaccountname'])"; display = "$($o['displayname'])"; given = "$($o['givenname'])"; sn = "$($o['sn'])";
                   mail = "$($o['mail'])"; description = "$($o['description'])"; group = [bool]($o['objectclass'] -contains 'group') }
              })
            }
            $hits = @(Find {{PowerShell.Quote(userBase ?? "")}} {{PowerShell.Quote(UserFilter(words))}}) +
                    @(Find {{PowerShell.Quote(groupBase ?? "")}} {{PowerShell.Quote(GroupFilter(words))}})
            ConvertTo-Json -InputObject @($hits) -Compress -Depth 3
            """, ct, 20);
        var domain = Environment.UserDomainName;
        var found = new List<(DirectoryHit Hit, string? Given, string? Sn)>();
        foreach (var e in json.ValueKind == System.Text.Json.JsonValueKind.Array ? json.EnumerateArray() : json.ValueKind == System.Text.Json.JsonValueKind.Object ? new[] { json }.AsEnumerable() : Enumerable.Empty<System.Text.Json.JsonElement>())
        {
            var sam = e.Str("sam");
            if (string.IsNullOrWhiteSpace(sam)) continue;
            bool group = e.Bool("group") ?? false;
            string? Empty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v;
            found.Add((new DirectoryHit($"{domain}\\{sam}", group ? "group" : "user", Empty(e.Str("display")), group ? Empty(e.Str("description")) : Empty(e.Str("mail"))),
                       Empty(e.Str("given")), Empty(e.Str("sn"))));
        }
        return Rank(found, query);
    }

    /// <summary>The words of a search ("Mustermann, Max" → Mustermann, Max; "FIRMA\\max" → max), at most five.</summary>
    internal static List<string> Words(string query) =>
        query.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Select(w => w[(w.LastIndexOf('\\') + 1)..].Trim('*', '"', '\''))
             .Where(w => w.Length > 0).Take(5).ToList();

    /// <summary>"OU=…,DC=…" or "LDAP://…" as ADSI wants it; null for the whole domain.</summary>
    internal static string? Base(string? searchBase)
    {
        var b = searchBase?.Trim();
        if (string.IsNullOrEmpty(b)) return null;
        return b.StartsWith("LDAP://", StringComparison.OrdinalIgnoreCase) || b.StartsWith("GC://", StringComparison.OrdinalIgnoreCase) ? b : "LDAP://" + b;
    }

    /// <summary>
    /// Every word has to start one of the name fields (any order, so first and last name can be swapped).
    /// Prefix matches use the directory's indexes; "*word*" would scan every object.
    /// </summary>
    internal static string UserFilter(IReadOnlyList<string> words)
    {
        var parts = words.Select(LdapEscape).Select(w =>
            $"(|(givenName={w}*)(sn={w}*)(displayName={w}*)(sAMAccountName={w}*)(mail={w}*)(cn={w}*))");
        return $"(&(objectCategory=person)(objectClass=user){string.Concat(parts)})";
    }

    /// <summary>Group names are short and few: every word anywhere in the name.</summary>
    internal static string GroupFilter(IReadOnlyList<string> words)
    {
        var parts = words.Select(LdapEscape).Select(w => $"(|(cn=*{w}*)(sAMAccountName=*{w}*))");
        return $"(&(objectCategory=group){string.Concat(parts)})";
    }

    /// <summary>
    /// Best match first: the exact name ("Max Mustermann", "Mustermann, Max", the account), then names that
    /// start with what was typed, then the rest; users before groups, then alphabetically.
    /// </summary>
    internal static List<DirectoryHit> Rank(IEnumerable<(DirectoryHit Hit, string? Given, string? Sn)> found, string query)
    {
        var q = string.Join(' ', Words(query)).ToLowerInvariant();
        static string Norm(string? s) => string.Join(' ', Words(s ?? "")).ToLowerInvariant();
        int Score((DirectoryHit Hit, string? Given, string? Sn) f)
        {
            var sam = f.Hit.Name[(f.Hit.Name.LastIndexOf('\\') + 1)..].ToLowerInvariant();
            var names = new[] { Norm(f.Hit.Display), Norm($"{f.Given} {f.Sn}"), Norm($"{f.Sn} {f.Given}"), sam, Norm(f.Hit.Detail) };
            if (names.Any(n => n == q)) return 0;
            if (names.Any(n => n.StartsWith(q, StringComparison.Ordinal))) return 1;
            return 2;
        }
        return found.GroupBy(f => f.Hit.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .OrderBy(f => f.Hit.Kind == "user" ? 0 : 1)
                    .ThenBy(Score)
                    .ThenBy(f => f.Hit.Display ?? f.Hit.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(f => f.Hit).ToList();
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

        // Only the Active Directory counts: a local account or group of this PC is not "in the directory".
        if (domain.ToString().Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return new AccessLookup(name, null, false);

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

using Ember.Core;
using Xunit;

public class AccessTests
{
    static readonly TeamStatus Loaded = new(@"\\srv\team\team.ember", "settings", DateTime.Now, false, null, 1, "Team");

    static Grant Grant(TeamConfig team, string account, string[]? groups = null, TeamStatus? status = null) =>
        Permissions.Compute(team, status ?? Loaded, account, groups ?? Array.Empty<string>(), dev: false);

    static readonly TeamConfig Roles = TeamConfig.Parse("""
        {
          "access": {
            "roles": [
              { "name": "Admins", "modules": ["*"], "users": ["FIRMA\\anna.admin"] },
              { "name": "Entwickler", "modules": ["proxy", "playwright", "git", "software"], "groups": ["FIRMA\\GG-Dev"], "hideTools": ["bruno"] },
              { "name": "Test", "modules": ["proxy", "playwright"], "users": ["max.muster"], "groups": ["FIRMA\\GG-QA"] },
              { "name": "Logs", "modules": ["log", "gibtsnicht"], "groups": ["FIRMA\\GG-QA"] }
            ],
            "blocked": { "users": ["FIRMA\\ex.mitarbeiter"], "groups": ["FIRMA\\GG-Gesperrt"] }
          }
        }
        """);

    [Fact]
    public void Roles_decide_which_modules_someone_sees()
    {
        var admin = Grant(Roles, @"FIRMA\Anna.Admin");
        Assert.Equal(ModuleIds.Known.Count, admin.Modules.Count);
        Assert.Null(admin.Locked);
        Assert.Equal(new[] { "Admins" }, admin.Roles);

        var tester = Grant(Roles, @"FIRMA\max.muster");
        Assert.Equal(new[] { "playwright", "proxy" }, tester.Modules.OrderBy(m => m));

        // Roles add up; unknown module names are ignored.
        var qa = Grant(Roles, @"FIRMA\jmoessner", new[] { @"FIRMA\gg-qa" });
        Assert.Equal(new[] { "log", "playwright", "proxy" }, qa.Modules.OrderBy(m => m));
    }

    [Fact]
    public void The_block_list_beats_every_role()
    {
        var blocked = Grant(Roles, @"FIRMA\ex.mitarbeiter", new[] { @"FIRMA\GG-Dev" });
        Assert.False(blocked.Any);
        Assert.Equal("blocked", blocked.Locked);

        var byGroup = Grant(Roles, @"FIRMA\anna.admin", new[] { @"FIRMA\GG-Gesperrt" });
        Assert.Equal("blocked", byGroup.Locked);
    }

    [Fact]
    public void Bruno_stays_hidden_only_if_every_role_hides_it()
    {
        Assert.True(Grant(Roles, @"FIRMA\x", new[] { @"FIRMA\GG-Dev" }).Hides("bruno"));
        Assert.False(Grant(Roles, @"FIRMA\x", new[] { @"FIRMA\GG-Dev", @"FIRMA\GG-QA" }).Hides("bruno"));
    }

    [Fact]
    public void Without_a_role_or_team_file_nothing_is_visible()
    {
        var nobody = Grant(Roles, @"FIRMA\gast", new[] { @"FIRMA\Vertrieb" });
        Assert.False(nobody.Any);
        Assert.Equal("notListed", nobody.Locked);
        Assert.Equal("notListed", Grant(new TeamConfig(), @"FIRMA\anna.admin").Locked);
        Assert.Equal("setup", Grant(Roles, @"FIRMA\anna.admin", status: TeamStatus.None).Locked);
    }

    [Fact]
    public void Old_admins_and_access_lists_still_work()
    {
        var old = TeamConfig.Parse("""{ "admins": { "users": ["lead"] }, "access": { "enabled": true, "groups": ["FIRMA\\Entwicklung"] } }""");
        Assert.True(Permissions.UsesLegacyFormat(old));
        Assert.Equal(ModuleIds.Known.Count, Grant(old, @"FIRMA\lead").Modules.Count);
        var member = Grant(old, @"FIRMA\x", new[] { @"FIRMA\Entwicklung" });
        Assert.True(member.Has("proxy") && member.Has("settings"));
        Assert.False(member.Has("access") || member.Has("ai") || member.Has("power"));
    }

    [Theory]
    [InlineData("proxy.set", "proxy", true)]
    [InlineData("px.start", "playwright", false)]
    [InlineData("px.status", "playwright", true)]
    [InlineData("wol.apply", "ai", true)]
    [InlineData("settings.save", "ai", true)]
    [InlineData("settings.save", "proxy", false)]
    [InlineData("jobs.list", "proxy", true)]
    [InlineData("team.save", "settings", false)]
    [InlineData("team.save", "access", true)]
    [InlineData("team.newKey", "proxy", false)]
    public void Bridge_methods_need_their_module(string method, string module, bool allowed)
    {
        var grant = new Grant(new HashSet<string> { module }, "team", null, Array.Empty<string>());
        Assert.Equal(allowed, Permissions.MayCall(grant, method));
        Assert.False(Permissions.MayCall(new Grant(new HashSet<string>(), "none", "setup", Array.Empty<string>()), "jobs.list"));
    }

    static List<AccessRole> AdminRoles => new()
    {
        new("Admins", new() { "*" }, new() { new(@"FIRMA\lead", "user") }),
        new("Test", new() { "proxy" }, new() { new(@"FIRMA\Entwicklung", "group") }),
    };

    [Fact]
    public void Nobody_removes_their_own_permissions_tab_or_blocks_themselves()
    {
        Access.EnsureStillAdmin(AdminRoles, new List<AccessEntry>(), @"FIRMA\lead", Array.Empty<string>());
        Assert.Equal("accessSelfLockout", Assert.Throws<EmberException>(() =>
            Access.EnsureStillAdmin(AdminRoles, new List<AccessEntry>(), @"FIRMA\x", new[] { @"FIRMA\Entwicklung" })).Code);
        Assert.Equal("accessSelfBlocked", Assert.Throws<EmberException>(() =>
            Access.EnsureStillAdmin(AdminRoles, new List<AccessEntry> { new("lead", "user") }, @"FIRMA\lead", Array.Empty<string>())).Code);
    }

    [Fact]
    public void Roles_need_unique_names_and_known_modules()
    {
        Assert.Equal("accessRoleDuplicate", Assert.Throws<EmberException>(() => Access.Validate(new List<AccessRole>
            { new("Test", new(), new()), new("test", new(), new()) })).Code);
        Assert.Equal("accessModuleUnknown", Assert.Throws<EmberException>(() => Access.Validate(new List<AccessRole>
            { new("Test", new() { "gibtsnicht" }, new()) })).Code);
        Assert.Equal("accessRoleNameMissing", Assert.Throws<EmberException>(() => Access.Validate(new List<AccessRole>
            { new(" ", new(), new()) })).Code);
    }

    [Fact]
    public void Admins_preview_a_role_or_nobody()
    {
        var test = Permissions.Preview(Roles, "role:Test")!;
        Assert.Equal("preview", test.Source);
        Assert.True(test.Has("proxy") && !test.Has("access"));
        Assert.True(Permissions.Preview(Roles, "role:Entwickler")!.Hides("bruno"));
        Assert.Equal("notListed", Permissions.Preview(Roles, "none")!.Locked);
        Assert.Null(Permissions.Preview(Roles, "role:gibt es nicht"));
        Assert.Null(Permissions.Preview(Roles, "key:all"));
    }

    [Fact]
    public void Lookup_without_windows_cannot_check()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Null(Access.Lookup("jmoessner").Found);
    }

    [Fact]
    public void Directory_search_cannot_be_turned_into_another_filter()
    {
        Assert.Equal(@"m\2a\29\28x\5c", Access.LdapEscape("m*)(x\\"));
        Assert.Equal("müller", Access.LdapEscape("müller"));
    }
    [Fact]
    public void Directory_search_takes_first_and_last_name_in_any_order()
    {
        Assert.Equal(new[] { "Max", "Mustermann" }, Access.Words("  Max   Mustermann "));
        Assert.Equal(new[] { "Mustermann", "Max" }, Access.Words("Mustermann, Max"));
        Assert.Equal(new[] { "max" }, Access.Words(@"FIRMA\max"));
        var filter = Access.UserFilter(Access.Words("Max Muster"));
        Assert.Contains("(|(givenName=Max*)(sn=Max*)", filter);
        Assert.Contains("(|(givenName=Muster*)(sn=Muster*)", filter);
        Assert.StartsWith("(&(objectCategory=person)(objectClass=user)", filter);
        Assert.DoesNotContain("=*Max", filter);   // prefix only: uses the directory's indexes
        Assert.Contains("(cn=*Test*)", Access.GroupFilter(Access.Words("Test")));
    }

    [Fact]
    public void Directory_search_base_is_an_ldap_path()
    {
        Assert.Null(Access.Base("  "));
        Assert.Equal("LDAP://OU=Mitarbeiter,DC=firma,DC=local", Access.Base("OU=Mitarbeiter,DC=firma,DC=local"));
        Assert.Equal("LDAP://dc01/OU=X,DC=firma", Access.Base("LDAP://dc01/OU=X,DC=firma"));
    }

    [Fact]
    public void Exact_names_come_first()
    {
        DirectoryHit User(string sam, string display) => new(@"FIRMA\" + sam, "user", display, null);
        var ranked = Access.Rank(new (DirectoryHit, string?, string?)[]
        {
            (User("amax", "Albers, Maxi"), "Maxi", "Albers"),
            (User("mmustermann", "Mustermann, Max"), "Max", "Mustermann"),
            (new DirectoryHit(@"FIRMA\GG-Max", "group", null, null), null, null),
            (User("mmustermann", "Mustermann, Max"), "Max", "Mustermann"),
        }, "Max Mustermann");
        Assert.Equal(new[] { @"FIRMA\mmustermann", @"FIRMA\amax", @"FIRMA\GG-Max" }, ranked.Select(h => h.Name));
    }

    [Fact]
    public void Only_names_from_the_directory_go_into_the_team_file()
    {
        var current = new TeamConfig { Access = new TeamAccess { Roles = new() { new TeamRole { Name = "Test", Modules = new() { "proxy" }, Users = new() { @"FIRMA\alt" } } } } };
        AccessLookup Lookup(string n) => new(n, "user", n.EndsWith("neu") ? true : n.EndsWith("offline") ? null : n.EndsWith("alt") ? false : false);
        List<AccessRole> With(params string[] users) => new() { new("Test", new() { "proxy" }, users.Select(u => new AccessEntry(u, "user")).ToList()) };

        // Already in the file: not looked up again, even if the directory no longer knows it.
        Access.EnsureInDirectory(With(@"FIRMA\alt", @"FIRMA\neu"), new List<AccessEntry>(), current, Lookup);
        Assert.Equal("accessNotInDirectory", Assert.Throws<EmberException>(() =>
            Access.EnsureInDirectory(With(@"FIRMA\gibtsnicht"), new List<AccessEntry>(), current, Lookup)).Code);
        Assert.Equal("accessDirectoryUnreachable", Assert.Throws<EmberException>(() =>
            Access.EnsureInDirectory(With(), new List<AccessEntry> { new(@"FIRMA\offline", "user") }, current, Lookup)).Code);
    }
}

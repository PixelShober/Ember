using System.Text.Json.Nodes;
using Ember.Core;
using Xunit;

public class AccessTests
{
    static readonly TeamStatus Loaded = new(@"\\srv\team\team.json", "settings", DateTime.Now, false, null, 1, "Team");
    static readonly TeamStatus NoTeam = TeamStatus.None;

    static Grant Grant(TeamConfig team, string account, string[]? groups = null, TeamStatus? status = null, string? key = null) =>
        Permissions.Compute(team, status ?? Loaded, key, account, groups ?? Array.Empty<string>(), dev: false);

    internal static TeamConfig RolesForPreview => Roles;

    static readonly TeamConfig Roles = TeamConfig.Parse("""
        {
          "access": {
            "roles": [
              { "name": "Admins", "modules": ["*"], "users": ["FIRMA\\anna.admin"] },
              { "name": "Test", "modules": ["proxy", "playwright"], "users": ["max.muster"], "groups": ["FIRMA\\GG-QA"] },
              { "name": "Logs", "modules": ["log", "gibtsnicht"], "groups": ["FIRMA\\GG-QA"] }
            ]
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
    public void Without_a_role_nothing_is_visible()
    {
        var nobody = Grant(Roles, @"FIRMA\gast", new[] { @"FIRMA\Vertrieb" });
        Assert.False(nobody.Any);
        Assert.Equal("notListed", nobody.Locked);
        Assert.Equal("notListed", Grant(new TeamConfig(), @"FIRMA\anna.admin").Locked);   // team.json without roles
    }

    [Fact]
    public void Without_team_json_a_starter_key_decides()
    {
        var none = Grant(new TeamConfig(), @"FIRMA\max.muster", status: NoTeam);
        Assert.Equal("setup", none.Locked);
        Assert.Equal("none", none.Source);
        // A key does not count once a team.json is loaded.
        Assert.Equal("notListed", Grant(new TeamConfig(), @"FIRMA\x", key: "EMBER-00000-00000-00000-00000").Locked);
        // The offline copy counts as loaded.
        var cached = Loaded with { FromCache = true, Error = "offline" };
        Assert.True(Grant(Roles, @"FIRMA\max.muster", status: cached).Has("proxy"));
    }

    [Fact]
    public void Starter_keys_are_compared_by_hash_and_forgive_typing()
    {
        // Test table: SHA-256("ember-key-v1:" + "0123456789ABCDEFGHJK")
        var known = new[] { ("proxyPlaywright", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("ember-key-v1:0123456789ABCDEFGHJK"))).ToLowerInvariant()) };
        Assert.Equal("proxyPlaywright", StarterKeys.Level("EMBER-01234-56789-ABCDE-FGHJK", known));
        Assert.Equal("proxyPlaywright", StarterKeys.Level(" ember-o1234 56789-abcde-fghjk ", known));   // O for 0, lower case, spaces
        Assert.Equal("proxyPlaywright", StarterKeys.Level("0123456789ABCDEFGHJK", known));
        Assert.Null(StarterKeys.Level("EMBER-01234-56789-ABCDE-FGHJX", known));
        Assert.Null(StarterKeys.Level("", known));
        Assert.Equal(new[] { "proxy", "playwright" }, StarterKeys.LevelModules["proxyPlaywright"]);
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
    [InlineData("access.save", "settings", false)]
    public void Bridge_methods_need_their_module(string method, string module, bool allowed)
    {
        var grant = new Grant(new HashSet<string> { module }, "team", null, Array.Empty<string>(), null);
        Assert.Equal(allowed, Permissions.MayCall(grant, method));
        Assert.False(Permissions.MayCall(new Grant(new HashSet<string>(), "none", "setup", Array.Empty<string>(), null), "jobs.list"));
    }

    static AccessChange Change(string version = "v") => new(new List<AccessRole>
        {
            new("Admins", new() { "*" }, new() { new(@"FIRMA\lead", "user"), new(@"FIRMA\Ember-Admins", "group") }),
            new("Test", new() { "proxy", "playwright" }, new() { new(@"FIRMA\Entwicklung", "group"), new("jmoessner", "user"), new("jmoessner", "user") }),
        }, "ember@firma.example", version);

    [Fact]
    public void Only_access_changes_in_team_json_and_old_lists_become_roles()
    {
        var json = "{\r\n    // Team Entwicklung\r\n    \"name\": \"Team Entwicklung – Jürgen\",\r\n    \"admins\": { \"users\": [\"alt\"] },\r\n    \"access\": { \"enabled\": true, \"users\": [\"x\"] },\r\n    \"proxy\": { \"internalDomains\": [\"firma.local\"] }\r\n}\r\n";
        var result = Access.Apply(json, Change());
        var root = JsonNode.Parse(result)!;
        Assert.Equal("Team Entwicklung – Jürgen", (string?)root["name"]);
        Assert.Equal("firma.local", (string?)root["proxy"]!["internalDomains"]![0]);
        Assert.Null(root["admins"]);
        Assert.Null(root["access"]!["enabled"]);
        Assert.Null(root["access"]!["users"]);
        var roles = root["access"]!["roles"]!.AsArray();
        Assert.Equal(new[] { "Admins", "Test" }, roles.Select(r => (string?)r!["name"]));
        Assert.Equal(new[] { "jmoessner" }, roles[1]!["users"]!.AsArray().Select(n => (string?)n)); // duplicates dropped
        Assert.Equal(new[] { @"FIRMA\Entwicklung" }, roles[1]!["groups"]!.AsArray().Select(n => (string?)n));
        Assert.Equal(new[] { "proxy", "playwright" }, roles[1]!["modules"]!.AsArray().Select(n => (string?)n));
        Assert.Equal("ember@firma.example", (string?)root["access"]!["contact"]);
        Assert.Contains("\r\n    \"name\"", result); // indentation and CRLF stay
        Assert.Contains("Jürgen", result);
    }

    [Fact]
    public void Nobody_removes_their_own_permissions_tab()
    {
        Access.EnsureStillAdmin(Change(), @"FIRMA\lead", Array.Empty<string>());
        Access.EnsureStillAdmin(Change(), @"FIRMA\neu", new[] { @"FIRMA\ember-admins" });
        Assert.Equal("accessSelfLockout", Assert.Throws<EmberException>(() => Access.EnsureStillAdmin(Change(), @"FIRMA\jmoessner", Array.Empty<string>())).Code);
    }

    [Fact]
    public void Roles_need_unique_names_and_known_modules()
    {
        var change = Change();
        change.Roles.Add(new AccessRole(" admins ", new() { "proxy" }, new()));
        Assert.Equal("accessRoleDuplicate", Assert.Throws<EmberException>(() => Access.Validate(change)).Code);
        var unknown = new AccessChange(new() { new("X", new() { "proxi" }, new()) }, null, "v");
        Assert.Equal("accessModuleUnknown", Assert.Throws<EmberException>(() => Access.Validate(unknown)).Code);
        var noName = new AccessChange(new() { new(" ", new() { "proxy" }, new()) }, null, "v");
        Assert.Equal("accessRoleNameMissing", Assert.Throws<EmberException>(() => Access.Validate(noName)).Code);
    }

    [Fact]
    public void Saving_refuses_when_team_json_changed_in_between()
    {
        var dir = Directory.CreateTempSubdirectory("ember-access-").FullName;
        try
        {
            var path = Path.Combine(dir, "team.json");
            File.WriteAllText(path, "{ \"name\": \"Team\" }");
            Assert.Equal("teamChanged", Assert.Throws<EmberException>(() => Access.Save(path, Change("stale"), @"FIRMA\lead", Array.Empty<string>())).Code);
            Assert.Equal("{ \"name\": \"Team\" }", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Lookup_without_windows_cannot_check()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Null(Access.Lookup("jmoessner").Found);
    }
}

public class AccessMailTests
{
    [Fact]
    public void Access_requests_go_to_the_contact_and_leave_no_gap()
    {
        var team = new TeamConfig
        {
            ServiceCenter = new TeamServiceCenter { Email = "servicecenter@firma.example" },
            Access = new TeamAccess { Contact = "ember-admins@firma.example" },
        };
        Assert.Equal("ember-admins@firma.example", Mail.Recipient(team, "accessRequest"));
        Assert.Equal("servicecenter@firma.example", Mail.Recipient(team, "problem"));
        var (_, body) = Mail.Render(team, "accessRequest", new Dictionary<string, string?> { ["details"] = "" });
        Assert.DoesNotContain("\n\n\n", body);
    }

    [Fact]
    public void Admins_preview_a_role_a_key_or_nobody()
    {
        var test = Permissions.Preview(AccessTests.RolesForPreview, "role:" + Permissions.Roles(AccessTests.RolesForPreview)[0].Name);
        Assert.NotNull(test);
        Assert.Equal("preview", test!.Source);

        var key = Permissions.Preview(AccessTests.RolesForPreview, "key:proxyPlaywright")!;
        Assert.True(key.Has("proxy") && key.Has("playwright"));
        Assert.False(key.Has("access"));

        var none = Permissions.Preview(AccessTests.RolesForPreview, "none")!;
        Assert.False(none.Any);
        Assert.Equal("notListed", none.Locked);

        Assert.Null(Permissions.Preview(AccessTests.RolesForPreview, "role:gibt es nicht"));
        Assert.Null(Permissions.Preview(AccessTests.RolesForPreview, "key:root"));
    }
}

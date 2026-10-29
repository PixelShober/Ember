using System.Text.Json.Nodes;
using Ember.Core;
using Xunit;

public class AccessTests
{
    static TeamConfig Team(bool? enabled = null, string[]? users = null, string[]? groups = null, string[]? admins = null) => new()
    {
        Access = users == null && groups == null && enabled == null ? null : new TeamAccess { Enabled = enabled, Users = users?.ToList(), Groups = groups?.ToList() },
        Admins = admins == null ? null : new TeamAdmins { Users = admins.ToList() },
    };

    [Fact]
    public void Without_a_list_everyone_may()
    {
        Assert.True(Access.Allowed(new TeamConfig(), @"FIRMA\jmoessner", Array.Empty<string>()));
        Assert.True(Access.Allowed(Team(enabled: false, users: new[] { "someone" }), @"FIRMA\jmoessner", Array.Empty<string>()));
        Assert.True(Access.Allowed(Team(users: Array.Empty<string>(), groups: Array.Empty<string>()), @"FIRMA\jmoessner", Array.Empty<string>()));
    }

    [Fact]
    public void Users_groups_and_admins_get_in()
    {
        var team = Team(users: new[] { "jmoessner" }, groups: new[] { @"FIRMA\Entwicklung" }, admins: new[] { @"FIRMA\lead" });
        Assert.True(Access.Allowed(team, @"FIRMA\JMoessner", Array.Empty<string>()));
        Assert.True(Access.Allowed(team, @"FIRMA\andere", new[] { @"FIRMA\entwicklung" }));
        Assert.True(Access.Allowed(team, @"FIRMA\lead", Array.Empty<string>()));
        Assert.False(Access.Allowed(team, @"FIRMA\gast", new[] { @"FIRMA\Vertrieb" }));
    }

    static AccessChange Change(string version = "v") => new(true,
        new List<AccessEntry> { new(@"FIRMA\Entwicklung", "group"), new("jmoessner", "user"), new("jmoessner", "user") },
        new List<AccessEntry> { new(@"FIRMA\lead", "user"), new(@"FIRMA\Ember-Admins", "group") },
        "ember@firma.example", version);

    [Fact]
    public void Only_access_and_admins_change_in_team_json()
    {
        var json = "{\r\n    // Team Entwicklung\r\n    \"name\": \"Team Entwicklung – Jürgen\",\r\n    \"admins\": { \"users\": [\"alt\"] },\r\n    \"proxy\": { \"internalDomains\": [\"firma.local\"] }\r\n}\r\n";
        var result = Access.Apply(json, Change());
        var root = JsonNode.Parse(result)!;
        Assert.Equal("Team Entwicklung – Jürgen", (string?)root["name"]);
        Assert.Equal("firma.local", (string?)root["proxy"]!["internalDomains"]![0]);
        Assert.True((bool)root["access"]!["enabled"]!);
        Assert.Equal(new[] { "jmoessner" }, root["access"]!["users"]!.AsArray().Select(n => (string?)n)); // duplicates dropped
        Assert.Equal(new[] { @"FIRMA\Entwicklung" }, root["access"]!["groups"]!.AsArray().Select(n => (string?)n));
        Assert.Equal(new[] { @"FIRMA\lead" }, root["admins"]!["users"]!.AsArray().Select(n => (string?)n));
        Assert.Equal("ember@firma.example", (string?)root["access"]!["contact"]);
        Assert.Contains("\r\n    \"name\"", result); // indentation and CRLF stay
        Assert.Contains("Jürgen", result);
        Assert.True(result.IndexOf("\"name\"", StringComparison.Ordinal) < result.IndexOf("\"admins\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Nobody_removes_themselves_as_admin()
    {
        Access.EnsureStillAdmin(Change(), @"FIRMA\lead", Array.Empty<string>());
        Access.EnsureStillAdmin(Change(), @"FIRMA\neu", new[] { @"FIRMA\ember-admins" });
        Assert.Equal("accessSelfLockout", Assert.Throws<EmberException>(() => Access.EnsureStillAdmin(Change(), @"FIRMA\jmoessner", Array.Empty<string>())).Code);
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
}

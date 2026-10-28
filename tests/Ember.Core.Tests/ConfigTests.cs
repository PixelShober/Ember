namespace Ember.Core.Tests;

[Collection("language")]
public class ConfigTests
{
    static TeamConfig Example => TeamConfig.Parse(File.ReadAllText(TestFiles.Fixture("team.example.json")));

    [Fact]
    public void Example_team_config_parses_completely()
    {
        var team = Example;
        Assert.Equal("Team Entwicklung", team.Name);
        Assert.Equal("http://pac.firma.example/proxy.pac", team.Proxy!.PacUrl);
        Assert.Equal(2, team.Proxy.InternalDomains!.Count);
        Assert.Equal("servicecenter@firma.example", team.ServiceCenter!.Email);
        Assert.Contains(team.Software!, s => s.Name == "Alfaview" && s.SccmName == "alfaview*");
        Assert.Single(team.Admins!.Groups!);
    }

    [Fact]
    public void Effective_settings_prefer_user_then_team_then_default()
    {
        var team = Example;
        var user = new UserSettings();
        var eff = EffectiveSettings.Build(user, team);
        Assert.True(eff.UsePac);
        Assert.Equal("proxy.firma.example:8080", eff.UpstreamHostPort);
        Assert.Equal("localhost,127.0.0.1,::1,firma.local,intranet.firma.example", eff.NoProxyList);
        Assert.Equal("http://127.0.0.1:3128", eff.PxUrl);

        user.UpstreamProxy = "http://anderer.proxy.example:3129/";
        user.InternalDomains = "a.local; b.local  a.local";
        user.Port = 3130;
        eff = EffectiveSettings.Build(user, team);
        Assert.Equal("anderer.proxy.example:3129", eff.UpstreamHostPort);
        Assert.Equal(new[] { "a.local", "b.local" }, eff.InternalDomainList);
        Assert.Equal("http://127.0.0.1:3130", eff.PxUrl);

        var defaults = EffectiveSettings.Build(new UserSettings(), new TeamConfig());
        Assert.Equal(3128, defaults.Port);
        Assert.Equal("", defaults.UpstreamHostPort);
    }

    [Fact]
    public void Validation_messages_are_translated()
    {
        L.Configure("de");
        var bad = EffectiveSettings.Build(new UserSettings { UsePac = false }, new TeamConfig());
        Assert.Equal("Bei „fester Proxy“ muss ein Proxy (host:port) eingetragen sein.", bad.Validate());
        Assert.Contains("host:port", EffectiveSettings.Build(new UserSettings { UpstreamProxy = "kein proxy" }, new TeamConfig()).Validate());
        Assert.Null(EffectiveSettings.Build(new UserSettings(), Example).Validate());
    }

    [Fact]
    public void User_settings_round_trip_with_umlauts_unescaped()
    {
        var s = new UserSettings { InternalDomains = "müller.local", ModelsDir = @"D:\Modelle für Jürgen" };
        var json = Json.Serialize(s, indented: true);
        Assert.Contains("Modelle für Jürgen", json);   // readable in an editor, not "f\u00FCr"
        var back = Json.Deserialize<UserSettings>(json)!;
        Assert.Equal("müller.local", back.InternalDomains);
        Assert.Equal(@"D:\Modelle für Jürgen", back.ModelsDir);
        Assert.Null(back.UsePac);
    }

    [Fact]
    public void Advanced_modules_only_for_admins_from_the_team_config()
    {
        var groups = new[] { @"FIRMA\Domänen-Benutzer", @"FIRMA\Entwicklung-Admins" };
        Assert.False(AdminCheck.IsAdmin(new TeamConfig(), @"FIRMA\jmoessner", groups));   // no admins list: nobody
        var byUser = TeamConfig.Parse("""{ "admins": { "users": ["jmoessner"] } }""");
        Assert.True(AdminCheck.IsAdmin(byUser, @"FIRMA\JMoessner", Array.Empty<string>()));
        Assert.False(AdminCheck.IsAdmin(byUser, @"FIRMA\someone", Array.Empty<string>()));
        var byGroup = TeamConfig.Parse("""{ "admins": { "groups": ["FIRMA\\entwicklung-admins"] } }""");
        Assert.True(AdminCheck.IsAdmin(byGroup, @"FIRMA\someone", groups));
        Assert.False(AdminCheck.IsAdmin(byGroup, @"FIRMA\someone", new[] { @"FIRMA\Domänen-Benutzer" }));
    }
}

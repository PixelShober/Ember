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
        Assert.Single(team.Modules!.Ai!.AllowGroups!);
        Assert.Equal(2, team.AgentTools!.Count);
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
        var s = new UserSettings { InternalDomains = "müller.local", WolDevices = { new WolDevice { Name = "Büro-PC", Mac = "02-00-5E-10-00-01" } } };
        var json = Json.Serialize(s, indented: true);
        Assert.Contains("\"Büro-PC\"", json);   // readable in an editor, not "Büro"
        var back = Json.Deserialize<UserSettings>(json)!;
        Assert.Equal("müller.local", back.InternalDomains);
        Assert.Equal("Büro-PC", back.WolDevices[0].Name);
        Assert.Null(back.UsePac);
    }

    [Fact]
    public void Ai_module_access_by_user_and_group()
    {
        Assert.True(ModuleAccessCheck.AiAllowed(new TeamConfig()));
        var restricted = TeamConfig.Parse("""{ "modules": { "ai": { "allowUsers": ["NIEMAND\\niemand"] } } }""");
        Assert.False(ModuleAccessCheck.AiAllowed(restricted));
        var me = TeamConfig.Parse($$"""{ "modules": { "ai": { "allowUsers": ["{{Environment.UserName}}"] } } }""");
        Assert.True(ModuleAccessCheck.AiAllowed(me));
    }
}

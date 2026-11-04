using System.Text;

namespace Ember.Core.Tests;

public class PowerTests
{
    [Fact]
    public void Parses_german_powercfg_output_by_structure()
    {
        var settings = PowerCfg.Parse(TestFiles.Read("powercfg-qh-de.txt").Replace("\n", "\r\n"));

        var epp = settings.Single(s => s.Alias == "PERFEPP");
        Assert.Equal("36687f9e-e3a5-4dbf-b1dc-15eb381c6863", epp.Guid);
        Assert.Equal("54533251-82be-4824-96c1-47b60b740d00", epp.SubGuid);
        Assert.Equal("SUB_PROCESSOR", epp.SubAlias);
        Assert.Equal("Richtlinie für die bevorzugte Prozessorenergieeffizienz", epp.Name);
        Assert.Equal(0, epp.Ac);
        Assert.Equal(50, epp.Dc);
        Assert.Equal(0, epp.Min);
        Assert.Equal(100, epp.Max);
        Assert.Equal("%", epp.Unit);

        Assert.Equal(25, settings.Single(s => s.Alias == "PERFEPP1").Ac);

        var boost = settings.Single(s => s.Alias == "PERFBOOSTMODE");
        Assert.Equal(2, boost.Ac);
        Assert.Equal("Hoch", boost.Options[2]);
        Assert.Equal("Drosselungszustände zulassen", settings.Single(s => s.Alias == "THROTTLING").Name);

        // hidden setting without range lines
        Assert.Equal(254, settings.Single(s => s.Alias == "HETEROINCREASETHRESHOLD").Ac);

        var standby = settings.Single(s => s.Alias == "STANDBYIDLE");
        Assert.Equal("SUB_SLEEP", standby.SubAlias);
        Assert.Equal(1800, standby.Ac);
        Assert.Equal("Sekunden", standby.Unit);
    }

    [Fact]
    public void Parses_english_powercfg_output_the_same_way()
    {
        var settings = PowerCfg.Parse(TestFiles.Read("powercfg-qh-en.txt"));
        var epp = settings.Single(s => s.Guid == "36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
        Assert.Equal("PERFEPP", epp.Alias);
        Assert.Equal(33, epp.Ac);
        Assert.Equal(50, epp.Dc);
        Assert.Equal("Aggressive", settings.Single(s => s.Alias == "PERFBOOSTMODE").Options[2]);
        Assert.Equal(1, settings.Single(s => s.Alias == "PERFBOOSTMODE").Dc);
    }

    [Fact]
    public void Umlauts_survive_the_oem_code_page()
    {
        // powercfg writes CP850 on a German Windows; decoding it as UTF-8 would break "für".
        var text = TestFiles.Read("powercfg-qh-de.txt");
        var bytes = ConsoleEncoding.ForCodePage(850).GetBytes(text);
        Assert.NotEqual(text, Encoding.UTF8.GetString(bytes));
        var parsed = PowerCfg.Parse(ConsoleEncoding.Decode(bytes, 850));
        Assert.Contains(parsed, s => s.Name == "Leistungssteigerungsmodus für Prozessoren");
        const string sample = "Höchstleistung, Größe, Übersicht, äöüß";
        Assert.Equal(sample, ConsoleEncoding.Decode(ConsoleEncoding.ForCodePage(850).GetBytes(sample), 850));
    }

    [Fact]
    public void Ai_profile_targets_all_efficiency_classes_on_ac_only()
    {
        var profile = PowerProfiles.BuiltIn().Single(p => p.Id == "ai-performance");
        Assert.All(profile.Settings, s => Assert.Null(s.Dc));
        foreach (var alias in new[] { "PERFEPP", "PERFEPP1", "PERFEPP2" })
            Assert.Equal(0, profile.Settings.Single(s => s.Setting == alias).Ac);
        Assert.Equal(2, profile.Settings.Single(s => s.Setting == "PERFBOOSTMODE").Ac);
        Assert.All(profile.Settings, s => Assert.True(PowerCfg.KnownGuids.ContainsKey(s.Setting)));
    }

    [Fact]
    public void Profile_state_reports_partial_and_skips_missing_settings()
    {
        var parsed = PowerCfg.Parse(TestFiles.Read("powercfg-qh-de.txt"));
        var profile = PowerProfiles.BuiltIn().Single(p => p.Id == "ai-performance");
        var state = PowerProfiles.BuildState(profile, PowerCfg.SchemeBalanced, parsed, hasBackup: false);

        Assert.Equal("partial", state.Status);   // PERFEPP is 0, PERFEPP1 is still 25
        Assert.False(state.Settings.Single(s => s.Alias == "PERFEPP2").Supported);
        Assert.Equal(0, state.Settings.Single(s => s.Alias == "PERFEPP").Ac.Current);
        Assert.Equal(25, state.Settings.Single(s => s.Alias == "PERFEPP1").Ac.Current);
    }

    [Fact]
    public void Team_profiles_replace_builtin_ones_by_id()
    {
        var team = TeamConfig.Parse(File.ReadAllText(TestFiles.Fixture("team.example.json")));
        var list = PowerProfiles.Effective(team);
        var sleep = list.Single(p => p.Id == "sleep-ac");
        Assert.Equal(1200, sleep.Settings.Single(s => s.Setting == "VIDEOIDLE").Ac);
        Assert.Contains(list, p => p.Id == "ai-performance");
        Assert.Equal(PowerCfg.SchemeBalanced, PowerProfiles.TargetScheme(team));
    }
}

using System.Text.Json;

namespace Ember.Core.Tests;

[Collection("language")]
public class ModuleTests
{
    [Fact]
    public void Mailto_encodes_umlauts_as_utf8()
    {
        var uri = Mail.MailtoUri("service@firma.example", "Bitte Größe prüfen", "Grüße\nJürgen");
        Assert.StartsWith("mailto:service@firma.example?subject=", uri);
        Assert.Contains("Gr%C3%B6%C3%9Fe", uri);            // Größe
        Assert.Contains("Gr%C3%BC%C3%9Fe%0D%0AJ%C3%BCrgen", uri);   // Grüße\r\nJürgen
        Assert.DoesNotContain(" ", uri);
    }

    [Fact]
    public void Mailto_cuts_long_bodies()
    {
        var uri = Mail.MailtoUri("a@b.example", "x", new string('a', 5000), maxBody: 100);
        Assert.True(uri.Length < 400);
    }

    [Fact]
    public void Mail_templates_from_team_and_builtin_with_placeholders()
    {
        L.Configure("de");
        var team = TeamConfig.Parse(File.ReadAllText(TestFiles.Fixture("team.example.json")));
        var vars = new Dictionary<string, string?> { ["app"] = "Alfaview", ["details"] = "Für Schulungen", ["user"] = "FIRMA\\jmoessner" };
        var (subject, body) = Mail.Render(team, "softwareRequest", vars);
        Assert.Equal("[Ember] Softwarebereitstellung: Alfaview für FIRMA\\jmoessner", subject);
        Assert.Contains("Für Schulungen", body);
        Assert.Contains(Environment.MachineName, body);

        var (s2, b2) = Mail.Render(team, "problem", new Dictionary<string, string?> { ["details"] = "px startet nicht" });
        Assert.StartsWith("[Ember] Problem auf ", s2);
        Assert.Contains("px startet nicht", b2);
        Assert.Contains("Diagnosebericht", b2);
    }

    [Fact]
    public void Wol_state_is_evaluated_for_waking_from_s5()
    {
        using var doc = JsonDocument.Parse(TestFiles.Read("wol-state.json"));
        var state = WolTarget.Evaluate(doc.RootElement, hasBackup: false);

        Assert.True(state.FastStartupOff);
        Assert.True(state.Ready);
        var dock = state.Adapters.Single(a => a.Name == "Dock-LAN");
        Assert.True(dock.Ready);
        Assert.DoesNotContain(dock.Props, p => p.Keyword == "*EEE");   // energy saving is not part of the target
        Assert.True(dock.Props.Single(p => p.Keyword == "S5WakeOnLan").Required);
        Assert.False(dock.Props.Single(p => p.Keyword == "*WakeOnPattern").Required);

        var intel = state.Adapters.Single(a => a.Name == "Ethernet-LAN-Port");   // props came as a single object
        Assert.Equal("02-00-50-E1-00-02", intel.Mac);
        Assert.True(intel.Ready);

        var usb = state.Adapters.Single(a => a.Name == "USB-LAN");
        Assert.False(usb.Ready);
        Assert.Equal("02-00-5E-10-00-03", usb.Mac);
    }

    [Fact]
    public void Llama_picks_highest_cuda_build_the_driver_supports()
    {
        var assets = new[]
        {
            "https://github.com/ggml-org/llama.cpp/releases/download/b6890/llama-b6890-bin-win-cpu-x64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b6890/llama-b6890-bin-win-cuda-12.4-x64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b6890/llama-b6890-bin-win-cuda-13.1-x64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b6890/llama-b6890-bin-win-vulkan-x64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b6890/cudart-llama-bin-win-cuda-12.4-x64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b6890/cudart-llama-bin-win-cuda-13.1-x64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b6890/llama-b6890-bin-macos-arm64.zip",
        };
        var (zip, cudart) = LlamaCpp.PickAssets(assets, "cuda", "13.0");
        Assert.EndsWith("cuda-12.4-x64.zip", zip);
        Assert.EndsWith("cudart-llama-bin-win-cuda-12.4-x64.zip", cudart);

        (zip, cudart) = LlamaCpp.PickAssets(assets, "cuda", "13.2");
        Assert.EndsWith("cuda-13.1-x64.zip", zip);

        (zip, cudart) = LlamaCpp.PickAssets(assets, "vulkan", null);
        Assert.EndsWith("vulkan-x64.zip", zip);
        Assert.Null(cudart);

        Assert.EndsWith("cpu-x64.zip", LlamaCpp.PickAssets(assets, "cpu", null).Zip);
    }

    [Fact]
    public void Llama_args_are_split_like_a_command_line()
    {
        Assert.Equal(new[] { "-ngl", "99", "-c", "8192", "--alias", "mein Modell" },
                     LlamaCpp.SplitArgs("-ngl 99  -c 8192 --alias \"mein Modell\""));
    }

    [Fact]
    public void Gpu_csv_from_nvidia_smi()
    {
        var gpus = Gpu.ParseCsv("NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, 581.42, 24463, 1830, 18.40, 175.00, 46, 3\n", "13.0");
        var g = Assert.Single(gpus);
        Assert.Equal(24463, g.MemoryTotalMb);
        Assert.Equal(175.0, g.PowerLimit);
        Assert.Equal("13.0", g.Cuda);
    }

    [Theory]
    [InlineData(null, null, "Mustermann, Max", "Max Mustermann")]
    [InlineData(null, null, "Mößner, Jürgen (IT-Entwicklung)", "Jürgen Mößner")]
    [InlineData("Jürgen", "Mößner", "irrelevant", "Jürgen Mößner")]
    [InlineData(null, null, "Jürgen Mößner", "Jürgen Mößner")]
    [InlineData(null, null, null, null)]
    public void Git_name_from_ad_display_name(string? given, string? surname, string? display, string? expected) =>
        Assert.Equal(expected, GitIdentity.BuildName(given, surname, display));

    [Fact]
    public void Gpresult_xml_lists_only_applied_gpos()
    {
        var names = SystemChecks.ParseGpResult(TestFiles.Read("gpresult-user.xml"));
        Assert.Equal(new[] { "GPO-Benutzer-Basis", "GPO-Laufwerkszuordnung Ü-Laufwerk" }, names);
    }

    [Theory]
    [InlineData("Visual Studio Professional 2022", "Visual Studio Professional*", true)]
    [InlineData("alfaview 9.21", "Alfaview*", true)]
    [InlineData("Microsoft Visual Studio Code", "Visual Studio*", false)]
    [InlineData("App (x64)", "App (x??)", true)]
    public void Wildcards_match_software_names(string name, string pattern, bool expected) =>
        Assert.Equal(expected, InstalledSoftware.Matches(name, pattern));

    [Theory]
    [InlineData(12, "installed")]
    [InlineData(13, "error")]
    [InlineData(5, "downloading")]
    [InlineData(9, "reboot")]
    [InlineData(null, "available")]
    public void Sccm_states(int? state, string key) => Assert.Equal(key, SoftwareCenter.StateKey(state));

    [Fact]
    public void Proxy_server_and_pac_parsing()
    {
        Assert.Equal("proxy:8080", Net.ParseProxyServer("http=proxy:80;https=proxy:8080"));
        Assert.Equal("proxy:80", Net.ParseProxyServer("http://proxy:80"));
        Assert.Equal(new[] { "p1.firma.example:8080", "p2.firma.example:3128" },
                     Net.PacProxies("return 'PROXY p1.firma.example:8080; PROXY p2.firma.example:3128; PROXY p1.firma.example:8080';"));
    }

    [Theory]
    [InlineData("1.2.0", "1.10.0", -1)]
    [InlineData("v2.0.0", "1.9.9", 1)]
    [InlineData("1.0.0-dev", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("0.0.0-dev", "0.0.0-dev", 0)]
    public void Versions_compare_numerically(string a, string b, int sign) => Assert.Equal(sign, Math.Sign(AppInfo.CompareVersions(a, b)));
}

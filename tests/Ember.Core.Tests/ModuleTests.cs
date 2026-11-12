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
        var vars = new Dictionary<string, string?> { ["app"] = "Alfaview", ["user"] = "FIRMA\\jmoessner", ["sender"] = "Jürgen Mößner" };
        var (subject, body) = Mail.Render(team, "softwareRequest", vars);
        Assert.Equal("[Ember] Softwarebereitstellung: Alfaview für FIRMA\\jmoessner", subject);
        Assert.StartsWith("Sehr geehrte Kolleginnen und Kollegen,", body);
        Assert.Contains("die Software Alfaview über das Software Center", body);
        Assert.Contains(Environment.MachineName, body);
        // The team's own template has no {sender}: Ember adds the closing line with the full name.
        Assert.EndsWith("Mit freundlichen Grüßen,\nJürgen Mößner", body);

        var (s2, b2) = Mail.Render(team, "problem", new Dictionary<string, string?> { ["details"] = "px startet nicht", ["sender"] = "Jürgen Mößner" });
        Assert.StartsWith("[Ember] Problem auf ", s2);
        Assert.Contains("vor folgendem Problem in meinem Entwicklungs- bzw. Arbeitsprozess", b2);
        Assert.Contains("px startet nicht", b2);
        Assert.DoesNotContain("Diagnosebericht", b2);
        Assert.EndsWith("Mit freundlichen Grüßen,\nJürgen Mößner", b2);

        team.ServiceCenter!.ProblemIntro = new LocalizedText("Bei uns hakt es:");
        Assert.Contains("Bei uns hakt es:", Mail.Render(team, "problem", vars).Body);
    }

    [Fact]
    public void Admin_rights_go_to_the_supervisor_with_their_first_name()
    {
        L.Configure("de");
        var team = TeamConfig.Parse(File.ReadAllText(TestFiles.Fixture("team.example.json")));
        Assert.Equal("petra.lindner@firma.example", Mail.Recipient(team, "adminAction"));
        Assert.Equal("servicecenter@firma.example", Mail.Recipient(team, "problem"));
        var (_, body) = Mail.Render(team, "adminAction", new Dictionary<string, string?> { ["sender"] = "Jürgen Mößner" });
        Assert.StartsWith("Hallo Petra,", body);
        Assert.Contains("Admin-Rechte für meinen Rechner", body);
        Assert.Contains("Dank dir im Voraus!", body);

        Assert.Equal("Petra", new TeamSupervisor { Name = "Lindner, Petra" }.GreetingName());
        Assert.Equal("Pe", new TeamSupervisor { Name = "Petra Lindner", FirstName = "Pe" }.GreetingName());
        Assert.Null(Mail.Recipient(new TeamConfig(), "adminAction"));
    }

    [Theory]
    [InlineData("0.00.778.896 W common_fit_params: failed to fit params to free device memory: n_gpu_layers already set by user to 99, abort", false)]
    [InlineData("common_fit_params: failed to fit params to free device memory: n_gpu_layers already set by user to 99, abort", false)]
    [InlineData("0.01.200.000 E llama_model_load: error loading model: out of memory", true)]
    [InlineData("ggml_backend_cuda_buffer_type_alloc_buffer: allocating 9000 MiB on device 0: cudaMalloc failed: out of memory", true)]
    [InlineData("0.00.100.000 I main: model loaded", false)]
    public void Llama_warnings_are_no_errors(string line, bool error) => Assert.Equal(error, LlamaCpp.IsError(line));

    [Fact]
    public void Web_search_goes_through_px_unless_the_user_set_another_proxy()
    {
        const string px = "http://127.0.0.1:3128";
        Assert.Contains("\"proxy\": \"http://127.0.0.1:3128\"", PiAgent.WithWebSearchProxy(null, px));
        var kept = PiAgent.WithWebSearchProxy("{ \"braveApiKey\": \"x\", \"proxy\": \"http://localhost:3129\" }", px)!;
        Assert.Contains("braveApiKey", kept);
        Assert.Contains("127.0.0.1:3128", kept);
        Assert.Null(PiAgent.WithWebSearchProxy("{ \"proxy\": \"http://127.0.0.1:3128\" }", px));
        Assert.Null(PiAgent.WithWebSearchProxy("{ \"proxy\": \"http://mcr:4444\" }", px));   // the user's own proxy stays
        Assert.Null(PiAgent.WithWebSearchProxy("{ kaputt", px));
    }

    [Theory]
    [InlineData("chromium-1194", "Chromium")]
    [InlineData("firefox-1495", "Firefox")]
    [InlineData("webkit-2215", "WebKit")]
    [InlineData("chromium_headless_shell-1194", null)]
    [InlineData("ffmpeg-1013", null)]
    [InlineData("winldd-1007", null)]
    public void Playwright_cache_folders_get_browser_names(string folder, string? label) =>
        Assert.Equal(label, Playwright.LabelOf(folder));

    [Fact]
    public void Vs_code_settings_sync_leaves_the_proxy_alone()
    {
        Assert.Equal(new[] { "http.proxy", "http.noProxy" }, ProxyTools.WithSyncIgnore(null));
        Assert.Equal(new[] { "editor.fontSize", "http.proxy", "http.noProxy" }, ProxyTools.WithSyncIgnore(new[] { "editor.fontSize" }));
        Assert.Equal(new[] { "HTTP.PROXY", "http.noProxy" }, ProxyTools.WithSyncIgnore(new[] { "HTTP.PROXY" }));
        Assert.Equal(new[] { "http.proxy", "http.noProxy" }, ProxyTools.WithSyncIgnore(new[] { "http.proxy", "http.noProxy" }));
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
    public void Llama_takes_the_newest_cuda_build_within_the_drivers_major_version()
    {
        var assets = new[]
        {
            "https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-win-cpu-x64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-win-cuda-12.4-x64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-win-cuda-13.4-x64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-win-cuda-13.4-arm64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-win-vulkan-x64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b12110/cudart-llama-b12110-bin-ubuntu-cuda-13.4-x64.tar.gz",
            "https://github.com/ggml-org/llama.cpp/releases/download/b12110/cudart-llama-bin-win-cuda-12.4-x64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b12110/cudart-llama-bin-win-cuda-13.4-x64.zip",
            "https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-macos-arm64.zip",
        };
        // Driver with CUDA 13.2: the 13.4 build runs (minor version compatibility), with its runtime.
        var (zip, cudart) = LlamaCpp.TryPickAssets(assets, "cuda", "13.2")!.Value;
        Assert.EndsWith("cuda-13.4-x64.zip", zip);
        Assert.EndsWith("cudart-llama-bin-win-cuda-13.4-x64.zip", cudart);

        // Older driver (CUDA 12.x): the 12.4 build.
        (zip, cudart) = LlamaCpp.TryPickAssets(assets, "cuda", "12.8")!.Value;
        Assert.EndsWith("cuda-12.4-x64.zip", zip);
        Assert.EndsWith("cudart-llama-bin-win-cuda-12.4-x64.zip", cudart);

        (zip, cudart) = LlamaCpp.TryPickAssets(assets, "vulkan", null)!.Value;
        Assert.EndsWith("vulkan-x64.zip", zip);
        Assert.Null(cudart);
        Assert.EndsWith("cpu-x64.zip", LlamaCpp.TryPickAssets(assets, "cpu", null)!.Value.Zip);

        // A release without Windows builds (like llama.cpp's "latest" v0.9.0) is skipped, not mistaken for Vulkan.
        Assert.Null(LlamaCpp.TryPickAssets(new[] { "https://github.com/ggml-org/llama.cpp/releases/download/v0.9.0/nightly-tag.txt" }, "cuda", "13.2"));
    }

    [Fact]
    public void Llama_server_starts_as_in_the_setup_guide()
    {
        var models = Models.List(new UserSettings { ModelsDir = @"C:\models" }, new TeamConfig());
        var qwen = models.Single(m => m.Id == "qwen3.8-27b");
        Assert.EndsWith("Qwen3.8-27B-UD-Q4_K_XL.gguf", qwen.Path);

        var args = LlamaCpp.ServerArgs(qwen, LlamaCpp.Profile("standard"), 8080, "");
        string Next(string flag) => args[args.IndexOf(flag) + 1];
        Assert.Equal("qwen3.8-27b", Next("--alias"));
        Assert.Equal("99", Next("-ngl"));
        Assert.Equal("98304", Next("-c"));
        Assert.Equal("q8_0", Next("-ctk"));
        Assert.Equal("draft-mtp", Next("--spec-type"));
        Assert.Equal("{\"reasoning_effort\":\"medium\"}", Next("--chat-template-kwargs"));
        Assert.Contains("--jinja", args);

        var max = LlamaCpp.ServerArgs(qwen, LlamaCpp.Profile("max"), 8080, "--threads 8");
        Assert.Equal("262144", max[max.IndexOf("-c") + 1]);
        Assert.DoesNotContain("--spec-type", max);   // no MTP at 256K
        Assert.Equal("8", max[^1]);
        // The old default would cut the context to 8K, so it is ignored.
        Assert.DoesNotContain("8192", LlamaCpp.ServerArgs(qwen, LlamaCpp.Profile("lang"), 8080, "-ngl 99 -c 8192"));
        Assert.Equal("62/62", LlamaCpp.Offloaded("load_tensors: offloaded 62/62 layers to GPU"));
    }

    [Fact]
    public void Server_log_tells_gpu_and_speed()
    {
        Assert.Equal("NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU",
                     LlamaCpp.Device("  Device 0: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, compute capability 12.0, VMM: yes"));
        Assert.Null(LlamaCpp.Device("0.04.426.603 I cmn          init: llama threadpool init, n_threads = 24"));
        Assert.Equal((true, 1840.2), LlamaCpp.Speed("prompt eval time =     195.61 ms /   360 tokens (    0.54 ms per token,  1840.24 tokens per second)"));
        Assert.Equal((false, 78.4), LlamaCpp.Speed("       eval time =    6380.11 ms /   500 tokens (   12.76 ms per token,    78.37 tokens per second)"));
        // The progress lines while reading the prompt are no measurement.
        Assert.Null(LlamaCpp.Speed("prompt processing, n_tokens =    360, progress = 0.74, t =  10.12 s / 35.59 tokens per second"));
    }

    [Fact]
    public void Picked_qwen_file_runs_with_the_guide_settings()
    {
        var dir = Directory.CreateTempSubdirectory("ember-qwen-").FullName;
        try
        {
            var file = Path.Combine(dir, "Qwen3.8-27B-UD-Q4_K_XL.gguf");
            File.WriteAllText(file, "GGUF");
            var s = new UserSettings { ModelsDir = dir };
            Models.AddLocal(s, file);
            var qwen = Models.List(s, new TeamConfig()).Single(m => m.Path.Equals(file, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("qwen3.8-27b", qwen.Alias);
            Assert.True(qwen.Mtp);
            Assert.Contains("--chat-template-kwargs", qwen.Args);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Local_models_are_used_where_they_are()
    {
        var dir = Directory.CreateTempSubdirectory("ember-models-").FullName;
        try
        {
            var file = Path.Combine(dir, "Mein-Modell-Q4.gguf");
            File.WriteAllText(file, "GGUF");
            var s = new UserSettings();
            Models.AddLocal(s, file);
            var local = Models.List(s, new TeamConfig()).Single(m => m.Local);
            Assert.Equal(file, local.Path);
            Assert.True(local.Downloaded && local.Mine);
            Assert.Equal("mein-modell-q4", local.Alias);
            Assert.Equal("modelNotGguf", Assert.Throws<EmberException>(() => Models.AddLocal(s, Path.Combine(dir, "x.bin"))).Code);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Vision_projector_comes_with_the_model()
    {
        var dir = Directory.CreateTempSubdirectory("ember-mmproj-").FullName;
        try
        {
            var s = new UserSettings { ModelsDir = dir };
            var qwen = Models.List(s, new TeamConfig()).Single(m => m.Id == "qwen3.8-27b");
            Assert.Equal(Path.Combine(dir, "Qwen3.8-27B-UD-Q4_K_XL.mmproj-F16.gguf"), qwen.MmprojPath);
            Assert.EndsWith("/mmproj-F16.gguf", qwen.MmprojUrl);
            Assert.False(qwen.MmprojPresent);
            Assert.DoesNotContain("--mmproj", LlamaCpp.ServerArgs(qwen, LlamaCpp.Profile("standard"), 8080, ""));

            File.WriteAllText(qwen.MmprojPath!, "GGUF");
            qwen = Models.List(s, new TeamConfig()).Single(m => m.Id == "qwen3.8-27b");
            var args = LlamaCpp.ServerArgs(qwen, LlamaCpp.Profile("standard"), 8080, "");
            Assert.Equal(qwen.MmprojPath, args[args.IndexOf("--mmproj") + 1]);

            // A model picked on the PC finds the mmproj next to it.
            var other = Path.Combine(dir, "eigen");
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "Mein-Q4.gguf"), "GGUF");
            File.WriteAllText(Path.Combine(other, "mmproj-F16.gguf"), "GGUF");
            Models.AddLocal(s, Path.Combine(other, "Mein-Q4.gguf"));
            var mine = Models.List(s, new TeamConfig()).Single(m => m.Id == "Mein-Q4.gguf");
            Assert.True(mine.MmprojPresent);
            Assert.Equal(Path.Combine(other, "mmproj-F16.gguf"), mine.MmprojPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Pi_extensions_are_read_from_its_settings()
    {
        var registered = PiAgent.Registered("""{ "packages": ["npm:pi-mcp-adapter", "npm:@juicesharp/rpiv-todo@2.15.0", { "source": "npm:context-mode" }, "git:github.com/x/y"] }""");
        Assert.Equal(new[] { "@juicesharp/rpiv-todo", "context-mode", "pi-mcp-adapter" }, registered.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Empty(PiAgent.Registered("kaputt"));
        Assert.Equal(new[] { "mcp", "websearch", "pretty", "ponytail", "simplify", "contextMode", "todo" }, PiAgent.Packages(new TeamConfig()).Select(p => p.Id));
    }

    [Fact]
    public void Pi_model_takes_images_with_the_projector()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(PiAgent.Apply(null, "qwen3.8-27b", 98304, 8080, vision: true))!.AsObject();
        Assert.Equal(new[] { "text", "image" }, root["providers"]!["llama-cpp"]!["models"]![0]!["input"]!.AsArray().Select(n => (string?)n));
        Assert.True(PiAgent.IsConfigured(root, "qwen3.8-27b", 98304, 8080, vision: true));
        Assert.False(PiAgent.IsConfigured(root, "qwen3.8-27b", 98304, 8080, vision: false));
    }

    [Fact]
    public void Pi_gets_the_llama_provider_and_keeps_the_others()
    {
        var json = "{\n  \"providers\": {\n    \"openai\": { \"apiKey\": \"x\" }\n  }\n}\n";
        var result = PiAgent.Apply(json, "qwen3.8-27b", 98304, 8080);
        var root = System.Text.Json.Nodes.JsonNode.Parse(result)!.AsObject();
        Assert.Equal("x", (string?)root["providers"]!["openai"]!["apiKey"]);
        Assert.True(PiAgent.IsConfigured(root, "qwen3.8-27b", 98304, 8080));
        Assert.False(PiAgent.IsConfigured(root, "qwen3.8-27b", 163840, 8080));   // other profile: context must match
        Assert.Equal("http://127.0.0.1:8080/v1", (string?)root["providers"]!["llama-cpp"]!["baseUrl"]);
        Assert.True(PiAgent.IsConfigured(System.Text.Json.Nodes.JsonNode.Parse(PiAgent.Apply(null, "a", 1, 9000))!.AsObject(), "a", 1, 9000));
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

    [Fact]
    public void Pac_direct_rules_become_internal_domains()
    {
        const string pac = """
            // Firmen-PAC
            var intern = new Array("*.city.net.ffm", "*.stadt-frankfurt.de");
            function FindProxyForURL(url, host) {
              if (isPlainHostName(host) || dnsDomainIs(host, ".intranet.example") || host == "wiki.firma.example")
                return "DIRECT";
              for (var i = 0; i < intern.length; i++) {
                if (shExpMatch(host, intern[i])) { return "DIRECT"; }
              }
              if (isInNet(dnsResolve(host), "10.0.0.0", "255.0.0.0")) return "DIRECT";
              /* if (dnsDomainIs(host, ".alt.example")) return "DIRECT"; */
              if (shExpMatch(url, "http://*.extern.example/*")) return "PROXY proxy.example:8080";
              if (shExpMatch(host, "*intra*")) return "DIRECT";
              return "PROXY proxy.example:8080; DIRECT";
            }
            """;
        Assert.Equal(new[] { "intranet.example", "wiki.firma.example", "city.net.ffm", "stadt-frankfurt.de", "10.0.0.0/8" },
                     Net.PacDirect(pac));
        // Without own value or team.json the PAC entries are the internal domains.
        var eff = EffectiveSettings.Build(new UserSettings(), new TeamConfig(),
                                          new ProxyDetectionResult("http://pac/proxy.pac", null, new[] { "proxy.example:8080" }, Net.PacDirect(pac)));
        Assert.Contains("city.net.ffm", eff.NoProxyList);
        Assert.Contains("10.0.0.0/8", eff.InternalDomainList);
    }

    [Theory]
    [InlineData("1.2.0", "1.10.0", -1)]
    [InlineData("v2.0.0", "1.9.9", 1)]
    [InlineData("1.0.0-dev", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("0.0.0-dev", "0.0.0-dev", 0)]
    public void Versions_compare_numerically(string a, string b, int sign) => Assert.Equal(sign, Math.Sign(AppInfo.CompareVersions(a, b)));
}

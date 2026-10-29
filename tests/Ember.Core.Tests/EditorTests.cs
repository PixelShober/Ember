namespace Ember.Core.Tests;

public class EditorTests
{
    const string VsCode = """
        {
            // Mein Theme – bitte nicht anfassen
            "workbench.colorTheme": "Default Dark Modern",
            "http.proxy": "http://alt.proxy:8080",
            "editor.fontSize": 14
        }
        """;

    [Fact]
    public void Jsonc_replaces_value_and_keeps_comments()
    {
        var result = JsoncEditor.SetString(VsCode, "http.proxy", "http://127.0.0.1:3128");
        Assert.Contains("// Mein Theme – bitte nicht anfassen", result);
        Assert.Equal("http://127.0.0.1:3128", JsoncEditor.GetString(result, "http.proxy"));
        Assert.Contains("\"editor.fontSize\": 14", result);
    }

    [Fact]
    public void Jsonc_inserts_into_existing_and_empty_objects()
    {
        var inserted = JsoncEditor.SetString("{\n  \"a\": 1\n}\n", "http.proxy", "http://127.0.0.1:3128");
        Assert.Equal("http://127.0.0.1:3128", JsoncEditor.GetString(inserted, "http.proxy"));
        Assert.Contains("\"http.proxy\": \"http://127.0.0.1:3128\",", inserted);

        var empty = JsoncEditor.SetString("{}", "http.proxy", "x");
        Assert.Equal("{\n    \"http.proxy\": \"x\"\n}", empty);
        Assert.Equal("x", JsoncEditor.GetString(JsoncEditor.SetString("", "http.proxy", "x"), "http.proxy"));
    }

    [Fact]
    public void Jsonc_removes_middle_and_last_member_without_breaking_json()
    {
        var middle = JsoncEditor.SetString(VsCode, "http.proxy", null);
        Assert.Null(JsoncEditor.GetString(middle, "http.proxy"));
        Assert.Contains("\"workbench.colorTheme\": \"Default Dark Modern\",\n    \"editor.fontSize\": 14", middle);

        var last = JsoncEditor.SetString("{\n    \"a\": 1,\n    \"http.proxy\": \"p\"\n}", "http.proxy", null);
        Assert.Equal("{\n    \"a\": 1\n}", last);
    }

    [Fact]
    public void NuGet_config_gets_http_proxy_and_keeps_sources()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ember-nuget-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
        try
        {
            NuGetConfigEditor.Set(path, "http_proxy", "http://127.0.0.1:3128");
            Assert.Equal("http://127.0.0.1:3128", NuGetConfigEditor.Get(path, "http_proxy"));
            Assert.Contains("nuget.org", File.ReadAllText(path));
            NuGetConfigEditor.Set(path, "http_proxy", null);
            Assert.Null(NuGetConfigEditor.Get(path, "http_proxy"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Ini_set_get_and_remove()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ember-pip-{Guid.NewGuid():N}.ini");
        File.WriteAllText(path, "# Kommentar\n[global]\ntimeout = 60\n\n[install]\nuser = true\n");
        try
        {
            IniFile.Set(path, "global", "proxy", "http://127.0.0.1:3128");
            Assert.Equal("http://127.0.0.1:3128", IniFile.Get(path, "global", "proxy"));
            Assert.Equal("60", IniFile.Get(path, "global", "timeout"));
            Assert.Equal("true", IniFile.Get(path, "install", "user"));
            IniFile.Set(path, "global", "proxy", null);
            Assert.Null(IniFile.Get(path, "global", "proxy"));
            Assert.StartsWith("# Kommentar", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("http://127.0.0.1:3128", true)]
    [InlineData("http://127.0.0.1:3128/", true)]
    [InlineData("http://localhost:3128", true)]
    [InlineData("127.0.0.1:3128", true)]
    [InlineData("http://proxy.firma.example:8080", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Recognizes_values_that_point_to_px(string? value, bool ours)
    {
        var s = EffectiveSettings.Build(new UserSettings(), new TeamConfig());
        Assert.Equal(ours, ProxyTools.IsOurs(value, s));
    }
}

public class NoProxyTests
{
    static readonly string[] Domains = { "*.firma.local", ".intranet.firma.example", "git.Werk.de", "10.0.0.0/8" };

    [Fact]
    public void Tools_get_plain_domain_suffixes()
    {
        Assert.Equal(new[] { "firma.local", "intranet.firma.example", "git.werk.de", "10.0.0.0/8" }, NoProxy.ForTools(Domains));
        Assert.Equal("localhost,127.0.0.1,::1,firma.local", EffectiveSettings.Build(new UserSettings { InternalDomains = "*.firma.local, firma.local" }, new TeamConfig()).NoProxyList);
    }

    [Fact]
    public void Px_gets_the_domain_and_its_wildcard()
    {
        Assert.Equal(new[] { "firma.local", "*.firma.local", "10.0.0.0/8" }, NoProxy.ForPx(new[] { "*.firma.local", "10.0.0.0/8" }));
    }

    [Fact]
    public void Git_gets_url_exceptions_for_both_schemes()
    {
        Assert.Equal(new[] { "https://firma.local", "https://*.firma.local", "http://firma.local", "http://*.firma.local" },
                     NoProxy.GitUrls(new[] { "*.firma.local", "10.0.0.0/8" }));
    }

    [Fact]
    public void Git_exceptions_are_read_from_get_regexp()
    {
        var output = "http.proxy http://127.0.0.1:3128\nhttp.https://*.firma.local.proxy \nhttp.https://firma.local/.proxy\nhttp.https://extern.example.proxy http://other:8080\n";
        var urls = NoProxy.ParseGitExceptions(output);
        Assert.Equal(2, urls.Count);
        Assert.Contains("https://*.firma.local", urls);
        Assert.Contains("https://firma.local", urls);
    }
}

public class JsoncArrayTests
{
    [Fact]
    public void String_arrays_are_set_replaced_and_removed_without_touching_comments()
    {
        var text = "{\n    // Proxy über px\n    \"http.proxy\": \"http://127.0.0.1:3128\",\n    \"editor.fontSize\": 14\n}\n";
        var set = JsoncEditor.SetStringArray(text, "http.noProxy", new[] { "localhost", "firma.local" });
        Assert.Contains("// Proxy über px", set);
        Assert.Equal(new[] { "localhost", "firma.local" }, JsoncEditor.GetStringArray(set, "http.noProxy"));

        var replaced = JsoncEditor.SetStringArray(set, "http.noProxy", new[] { "intranet.firma.example" });
        Assert.Equal(new[] { "intranet.firma.example" }, JsoncEditor.GetStringArray(replaced, "http.noProxy"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(replaced, "http.noProxy"));

        var removed = JsoncEditor.SetStringArray(replaced, "http.noProxy", null);
        Assert.Null(JsoncEditor.GetStringArray(removed, "http.noProxy"));
        Assert.Equal(text, removed);
    }
}

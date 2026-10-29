using System.Globalization;
using System.Text.RegularExpressions;

namespace Ember.Core.Tests;

[Collection("language")]
public class LocalizationTests
{
    [Theory]
    [InlineData("auto", "de-DE", "de", false)]
    [InlineData("auto", "de-AT", "de", false)]
    [InlineData("auto", "de-CH", "de", true)]
    [InlineData("auto", "en-US", "en", false)]
    [InlineData("auto", "fr-FR", "en", false)]
    [InlineData("de-CH", "en-US", "de", true)]
    [InlineData("en", "de-DE", "en", false)]
    public void Resolves_the_language(string setting, string culture, string language, bool swiss)
    {
        var (l, s) = L.Resolve(setting, CultureInfo.GetCultureInfo(culture));
        Assert.Equal(language, l);
        Assert.Equal(swiss, s);
    }

    [Fact]
    public void Translates_with_umlauts_placeholders_and_swiss_spelling()
    {
        try
        {
            L.Configure("de");
            Assert.Equal("px läuft · http://127.0.0.1:3128", L.T("proxy.px.running", ("url", "http://127.0.0.1:3128")));
            Assert.Equal("Schließen", L.T("common.close"));
            L.Configure("de-CH");
            Assert.Equal("Schliessen", L.T("common.close"));
            L.Configure("en");
            Assert.Equal("Close", L.T("common.close"));
            Assert.Equal("does.not.exist", L.T("does.not.exist"));
        }
        finally
        {
            L.Configure("de");
        }
    }

    [Fact]
    public void Both_locale_files_are_embedded_with_the_same_keys()
    {
        Assert.Contains("de", L.Languages);
        Assert.Contains("en", L.Languages);
        var de = L.Table("de").Keys.ToHashSet();
        var en = L.Table("en").Keys.ToHashSet();
        Assert.Empty(de.Except(en));
        Assert.Empty(en.Except(de));
    }

    /// <summary>Every key and error code used in C# must exist in both languages.</summary>
    [Fact]
    public void All_keys_used_in_the_host_exist()
    {
        var sources = Directory.EnumerateFiles(Path.Combine(TestFiles.RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
                               .Select(File.ReadAllText).ToList();
        var keys = new HashSet<string>();
        foreach (var text in sources)
        {
            foreach (Match m in Regex.Matches(text, "L\\.(?:T|TryT)\\(\"([a-zA-Z0-9.\\-]+)\""))
                if (!m.Groups[1].Value.EndsWith('.')) keys.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, "EmberException\\((?:ex, )?\"([a-zA-Z0-9]+)\""))
                keys.Add("errors." + m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, "Step\\(\"(px\\.steps\\.[a-zA-Z]+)\""))
                keys.Add(m.Groups[1].Value);
        }
        Assert.NotEmpty(keys);
        foreach (var lang in new[] { "de", "en" })
        {
            var missing = keys.Where(k => !L.Table(lang).ContainsKey(k)).OrderBy(k => k).ToList();
            Assert.True(missing.Count == 0, $"{lang}: missing {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void Localized_text_accepts_string_or_object()
    {
        var team = TeamConfig.Parse("""
            { "software": [ { "name": "A", "note": "nur Text" }, { "name": "B", "note": { "de": "Größe", "en": "Size" } } ] }
            """);
        try
        {
            L.Configure("de");
            Assert.Equal("nur Text", team.Software![0].Note!.Get());
            Assert.Equal("Größe", team.Software[1].Note!.Get());
            L.Configure("de-CH");
            Assert.Equal("Grösse", team.Software[1].Note!.Get());
            L.Configure("en");
            Assert.Equal("Size", team.Software[1].Note!.Get());
        }
        finally
        {
            L.Configure("de");
        }
    }
}

[CollectionDefinition("language", DisableParallelization = true)]
public class LanguageCollection;

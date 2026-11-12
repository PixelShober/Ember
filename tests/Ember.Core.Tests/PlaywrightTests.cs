using System.Text.Json;
using System.Text.Json.Nodes;
using Ember.Core;
using Xunit;

public class PlaywrightTests
{
    [Fact]
    public void Project_version_comes_from_node_modules_and_package_json()
    {
        var dir = Directory.CreateTempSubdirectory("ember-pw-Jürgen-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "package.json"), """{ "name": "app", "devDependencies": { "@playwright/test": "^1.56.0" } }""");
            var before = Playwright.Inspect(dir);
            Assert.Equal("@playwright/test", before.Package);
            Assert.Equal("^1.56.0", before.Wanted);
            Assert.Null(before.Version);
            Assert.False(before.HasLockFile);

            var module = Path.Combine(dir, "node_modules", "@playwright", "test");
            Directory.CreateDirectory(module);
            File.WriteAllText(Path.Combine(module, "package.json"), """{ "name": "@playwright/test", "version": "1.56.1" }""");
            File.WriteAllText(Path.Combine(dir, "package-lock.json"), "{}");
            var after = Playwright.Inspect(dir);
            Assert.Equal("1.56.1", after.Version);
            Assert.True(after.HasLockFile);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Project_without_playwright_is_recognised()
    {
        var dir = Directory.CreateTempSubdirectory("ember-pw-").FullName;
        try
        {
            Assert.False(Playwright.Inspect(dir).HasPackageJson);
            File.WriteAllText(Path.Combine(dir, "package.json"), """{ "dependencies": { "svelte": "5.0.0" } }""");
            var p = Playwright.Inspect(dir);
            Assert.True(p.HasPackageJson);
            Assert.Null(p.Package);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("Error: self-signed certificate in certificate chain", "playwrightCertificates")]
    [InlineData("Error: unable to get local issuer certificate", "playwrightCertificates")]
    [InlineData("Error: Download failed: server returned code 407. URL: https://cdn.playwright.dev/...", "playwrightProxyAuth")]
    [InlineData("Error: getaddrinfo ENOTFOUND cdn.playwright.dev", "playwrightBlocked")]
    [InlineData("Error: socket hang up", "playwrightBlocked")]
    [InlineData("Something else entirely", "commandFailed")]
    public void Failures_are_explained(string line, string code) =>
        Assert.Equal(code, Playwright.Explain(new[] { "Downloading Chromium 140.0 …", line }, "npx playwright install").Code);

    static string TempProject(string prefix = "ember-pw-") => Directory.CreateTempSubdirectory(prefix).FullName;

    [Theory]
    [InlineData("kundenportal-tests", true)]
    [InlineData("e2e.web_2", true)]
    [InlineData("Kundenportal", false)]
    [InlineData("mein projekt", false)]
    [InlineData("-start", false)]
    [InlineData(".hidden", false)]
    [InlineData("ende.", false)]
    [InlineData("node_modules", false)]
    [InlineData("con", false)]
    [InlineData("lpt1.tests", false)]
    [InlineData("prüfung", false)]
    [InlineData("", false)]
    public void Project_names_follow_npm_and_windows_rules(string name, bool valid) =>
        Assert.Equal(valid, PlaywrightTemplate.ValidName(name));

    [Fact]
    public void Template_has_the_standard_layout_and_follows_the_browser_choice()
    {
        var files = PlaywrightTemplate.NewProject("kundenportal-tests", "chromium");
        Assert.Equal(new[] { "package.json", "playwright.config.ts", "tests/example.spec.ts", ".gitignore", ".vscode/extensions.json", "README.md" }, files.Keys);
        var package = JsonNode.Parse(files["package.json"])!;
        Assert.Equal("kundenportal-tests", (string?)package["name"]);
        Assert.Equal("playwright test", (string?)package["scripts"]!["test"]);
        Assert.Contains("devices['Desktop Chrome']", files["playwright.config.ts"]);
        Assert.DoesNotContain("msedge", files["playwright.config.ts"]);
        Assert.Contains("ms-playwright.playwright", files[".vscode/extensions.json"]);

        var edge = PlaywrightTemplate.NewProject("edge-tests", "msedge");
        Assert.Contains("channel: 'msedge'", edge["playwright.config.ts"]);
        Assert.Equal("playwright codegen --channel msedge", (string?)JsonNode.Parse(edge["package.json"])!["scripts"]!["codegen"]);

        var all = PlaywrightTemplate.NewProject("alle", "all")["playwright.config.ts"];
        Assert.Contains("Desktop Firefox", all);
        Assert.Contains("Desktop Safari", all);
    }

    [Fact]
    public void Template_is_written_only_into_a_new_or_empty_folder()
    {
        var parent = TempProject();
        try
        {
            var written = PlaywrightTemplate.Write(parent, "neu", "chromium");
            Assert.Contains("tests/example.spec.ts", written);
            Assert.True(File.Exists(Path.Combine(parent, "neu", "tests", "example.spec.ts")));
            Assert.True(File.Exists(Path.Combine(parent, "neu", ".vscode", "extensions.json")));
            var project = Playwright.Inspect(Path.Combine(parent, "neu"));
            Assert.Equal("playwright.config.ts", project.Config);
            Assert.Null(project.Package); // npm adds @playwright/test afterwards

            var ex = Assert.Throws<EmberException>(() => PlaywrightTemplate.Write(parent, "neu", "chromium"));
            Assert.Equal("playwrightFolderNotEmpty", ex.Code);
            Assert.Equal("playwrightNameInvalid", Assert.Throws<EmberException>(() => PlaywrightTemplate.Write(parent, "Neu", "chromium")).Code);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void Scripts_are_added_without_touching_the_rest_of_package_json()
    {
        var json = "{\r\n    \"name\": \"app\",\r\n    \"description\": \"Prüfstand für Jürgen\",\r\n    \"scripts\": {\r\n        \"build\": \"vite build\",\r\n        \"test\": \"echo \\\"Error: no test specified\\\" && exit 1\"\r\n    }\r\n}\r\n";
        var result = PlaywrightTemplate.WithScripts(json, new JsonObject { ["test"] = "playwright test", ["build"] = "other", ["report"] = "playwright show-report" });
        Assert.Contains("\r\n        \"build\": \"vite build\"", result); // own script stays, indentation (4) and CRLF stay
        Assert.Contains("\"test\": \"playwright test\"", result); // npm placeholder replaced
        Assert.Contains("\"report\": \"playwright show-report\"", result);
        Assert.Contains("Prüfstand für Jürgen", result); // no \u00fc escapes
        Assert.EndsWith("}\r\n", result);
        Assert.True(result.IndexOf("\"name\"", StringComparison.Ordinal) < result.IndexOf("\"scripts\"", StringComparison.Ordinal));

        var own = """{ "scripts": { "test": "vitest" } }""";
        Assert.Equal(own, PlaywrightTemplate.WithScripts(own, new JsonObject { ["test"] = "playwright test" }));
    }

    [Fact]
    public void Gitignore_gets_only_the_missing_entries()
    {
        Assert.StartsWith("node_modules/\n/test-results/", PlaywrightTemplate.MergeIgnore(null));
        var merged = PlaywrightTemplate.MergeIgnore("node_modules\n/dist\n")!;
        Assert.Equal(1, merged.Split('\n').Count(l => l.Trim('/') == "node_modules"));
        Assert.Contains("# Playwright\n/test-results/", merged);
        Assert.Null(PlaywrightTemplate.MergeIgnore(merged));
    }

    [Fact]
    public void Edge_without_config_gets_a_new_config()
    {
        var dir = TempProject();
        try
        {
            File.WriteAllText(Path.Combine(dir, "package.json"), """{ "name": "app", "devDependencies": { "@playwright/test": "^1.56.0" } }""");
            var setup = PlaywrightTemplate.UseEdge(dir);
            Assert.Equal(new PlaywrightEdgeSetup("playwright.config.ts", "npm test", true), setup);
            Assert.True(Playwright.Inspect(dir).UsesEdge);
            Assert.False(PlaywrightTemplate.UseEdge(dir).Created); // second time: nothing to do
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("playwright.config.ts", "playwright.msedge.config.ts", "from './playwright.config'")]
    [InlineData("playwright.config.mjs", "playwright.msedge.config.mjs", "from './playwright.config.mjs'")]
    [InlineData("playwright.config.js", "playwright.msedge.config.mjs", "from './playwright.config.js'")]
    public void Edge_with_existing_config_keeps_it_and_adds_a_second_one(string existing, string expected, string import)
    {
        var dir = TempProject();
        try
        {
            File.WriteAllText(Path.Combine(dir, "package.json"), """{ "name": "app", "devDependencies": { "@playwright/test": "^1.56.0" } }""");
            File.WriteAllText(Path.Combine(dir, existing), "export default { projects: [{ name: 'chromium' }] };");
            var setup = PlaywrightTemplate.UseEdge(dir);
            Assert.Equal(expected, setup.File);
            Assert.Equal("npm run test:edge", setup.Command);
            var content = File.ReadAllText(Path.Combine(dir, expected));
            Assert.Contains(import, content);
            Assert.Contains("channel: 'msedge'", content);
            Assert.Equal("export default { projects: [{ name: 'chromium' }] };", File.ReadAllText(Path.Combine(dir, existing)));
            using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "package.json")));
            Assert.Equal("playwright test -c " + expected, package.RootElement.GetProperty("scripts").GetProperty("test:edge").GetString());
            Assert.True(Playwright.Inspect(dir).UsesEdge);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Edge_needs_the_test_runner()
    {
        var dir = TempProject();
        try
        {
            File.WriteAllText(Path.Combine(dir, "package.json"), """{ "dependencies": { "playwright": "1.56.0" } }""");
            Assert.Equal("playwrightEdgeLibrary", Assert.Throws<EmberException>(() => PlaywrightTemplate.UseEdge(dir)).Code);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

namespace Ember.Core.Tests;

public class TemplateTests
{
    [Theory]
    [InlineData("tests/login.spec.ts", "drop", "test")]
    [InlineData("tests/helpers/login.ts", "keep", null)]
    [InlineData(".env", "drop", "secret")]
    [InlineData(".env.example", "keep", null)]
    [InlineData("playwright/.auth/user.json", "keep", null)]   // the folder decides, see below
    [InlineData("storageState.json", "drop", "secret")]
    [InlineData("data/kunden.csv", "drop", "data")]
    [InlineData("package-lock.json", "skip", "generated")]
    [InlineData("playwright.config.ts", "keep", null)]
    public void Files_get_a_suggestion(string path, string action, string? reason) =>
        Assert.Equal((action, reason), PlaywrightTemplates.SuggestFile(path));

    [Theory]
    [InlineData("node_modules", "skip")]
    [InlineData(".git", "skip")]
    [InlineData("playwright-report", "skip")]
    [InlineData("playwright/.auth", "drop")]
    [InlineData("tests/login.spec.ts-snapshots", "drop")]
    [InlineData("tests/pages", "keep")]
    public void Folders_get_a_suggestion(string path, string action) =>
        Assert.Equal(action, PlaywrightTemplates.SuggestDir(path).Action);

    [Fact]
    public void Scrub_removes_the_project_identity()
    {
        var pkg = PlaywrightTemplates.Scrub("package.json", "{\n  \"name\": \"kundenportal\",\n  \"version\": \"3.2.0\",\n  \"author\": \"Max\",\n  \"devDependencies\": { \"@playwright/test\": \"^1.56.0\" }\n}\n", "kundenportal");
        Assert.Contains("\"name\": \"$projectname$\"", pkg);
        Assert.Contains("\"version\": \"1.0.0\"", pkg);
        Assert.DoesNotContain("Max", pkg);
        Assert.Contains("@playwright/test", pkg);

        var config = PlaywrightTemplates.Scrub("playwright.config.ts", "use: { baseURL: 'https://portal.intern.example/app', trace: 'on' }", null);
        Assert.Contains("baseURL: process.env.BASE_URL", config);
        Assert.DoesNotContain("intern.example", config);
    }

    [Fact]
    public void Template_round_trip_keeps_the_structure_and_fills_the_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "ember-tpl-" + Guid.NewGuid().ToString("N")[..8]);
        var project = Path.Combine(root, "kundenportal");
        var template = Path.Combine(root, "vorlage");
        try
        {
            void Write(string rel, string text)
            {
                var p = Path.Combine(project, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, text);
            }
            Write("package.json", "{ \"name\": \"kundenportal\", \"version\": \"2.0.0\", \"devDependencies\": { \"@playwright/test\": \"^1.56.0\" } }");
            Write("playwright.config.ts", "export default { use: { baseURL: 'https://portal.example' } };");
            Write("tests/login.spec.ts", "test('login', () => {});");
            Write("tests/pages/login.page.ts", "export class LoginPage {}");
            Write(".env", "PASSWORD=geheim");
            Write("node_modules/x/index.js", "");
            Write("README.md", "# kundenportal");

            var preview = PlaywrightTemplates.Preview(project);
            Assert.DoesNotContain(preview, e => e.Path.StartsWith("node_modules/", StringComparison.Ordinal));
            var keep = preview.Where(e => !e.Dir && e.Action == "keep").Select(e => e.Path).ToList();
            Assert.Equal(4, PlaywrightTemplates.Save(project, template, keep, "Team-Vorlage", null, "FIRMA\\max"));

            Assert.True(File.Exists(Path.Combine(template, "tests", ".gitkeep")) || File.Exists(Path.Combine(template, "tests", "pages", "login.page.ts")));
            Assert.False(File.Exists(Path.Combine(template, "tests", "login.spec.ts")));
            Assert.False(File.Exists(Path.Combine(template, ".env")));
            Assert.True(File.Exists(Path.Combine(template, PlaywrightTemplates.ManifestName)));

            var files = PlaywrightTemplates.Instantiate(template, root, "neues-projekt");
            var created = Path.Combine(root, "neues-projekt");
            Assert.Contains("\"name\": \"neues-projekt\"", File.ReadAllText(Path.Combine(created, "package.json")));
            Assert.Equal("# neues-projekt", File.ReadAllText(Path.Combine(created, "README.md")));
            Assert.False(File.Exists(Path.Combine(created, PlaywrightTemplates.ManifestName)));
            Assert.Contains("tests/pages/login.page.ts", files);

            // Making a template again replaces the old one (after a backup).
            Assert.Equal(4, PlaywrightTemplates.Save(project, template, keep, null, null, "FIRMA\\max"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

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
}

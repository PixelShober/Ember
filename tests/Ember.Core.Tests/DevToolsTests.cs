using System.Text.Json;
using Ember.Core;
using Xunit;

public class DevToolsTests
{
    [Fact]
    public void Node_lts_is_the_newest_lts_line_with_a_windows_zip()
    {
        using var doc = JsonDocument.Parse("""
            [
              { "version": "v27.0.0", "lts": false, "files": ["win-x64-zip", "linux-x64"] },
              { "version": "v26.13.0", "lts": "Lithium", "files": ["linux-x64"] },
              { "version": "v26.12.1", "lts": "Lithium", "files": ["win-x64-msi", "win-x64-zip"] },
              { "version": "v24.11.0", "lts": "Krypton", "files": ["win-x64-zip"] }
            ]
            """);
        Assert.Equal("v26.12.1", DevTools.NodeLts(doc.RootElement)!.Value.GetProperty("version").GetString());
    }

    [Fact]
    public void Checksum_is_read_from_shasums()
    {
        const string sums = "aaa111  node-v26.12.1-x64.msi\ncbc74b5e0982  node-v26.12.1-win-x64.zip\r\nddd444 *other.zip\n";
        Assert.Equal("cbc74b5e0982", DevTools.ShaFromSums(sums, "node-v26.12.1-win-x64.zip"));
        Assert.Equal("ddd444", DevTools.ShaFromSums(sums, "other.zip"));
        Assert.Null(DevTools.ShaFromSums(sums, "missing.zip"));
    }

    [Fact]
    public void Python_versions_skip_prereleases_and_sort_numerically()
    {
        using var doc = JsonDocument.Parse("""["3.9.13", "3.13.9", "3.15.0-a1", "3.14.2", "3.14.10", "3.15.0-rc2"]""");
        Assert.Equal(new[] { "3.14.10", "3.14.2", "3.13.9", "3.9.13" }, DevTools.StableVersions(doc.RootElement));
    }

    [Fact]
    public void Dotnet_sdk_installer_comes_from_the_latest_sdk_entry()
    {
        using var doc = JsonDocument.Parse("""
            {
              "channel-version": "10.0",
              "latest-sdk": "10.0.105",
              "releases": [
                { "release-version": "10.0.5",
                  "sdk": { "version": "10.0.105", "files": [
                    { "name": "dotnet-sdk-win-x64.zip", "url": "https://builds.example/sdk.zip", "hash": "zzz" },
                    { "name": "dotnet-sdk-win-x64.exe", "url": "https://builds.example/sdk.exe", "hash": "ABC123" } ] } },
                { "release-version": "10.0.4",
                  "sdk": { "version": "10.0.104", "files": [ { "name": "dotnet-sdk-win-x64.exe", "url": "https://old", "hash": "old" } ] } }
              ]
            }
            """);
        var sdk = DevTools.DotnetSdk(doc.RootElement)!;
        Assert.Equal("10.0.105", sdk.Version);
        Assert.Equal("https://builds.example/sdk.exe", sdk.Url);
        Assert.Equal("ABC123", sdk.Sha512);
        Assert.Equal("dotnet-sdk-10.0.105-win-x64.exe", sdk.FileName);
    }

    [Fact]
    public void Installed_versions_are_found_by_display_name()
    {
        var installed = new List<InstalledApp>
        {
            new("Git", "2.47.1", "The Git Development Community"),
            new("GitHub CLI", "2.80.0", "GitHub"),
            new("Python 3.13.1 (64-bit)", "3.13.1150.0", "Python Software Foundation"),
            new("Python Launcher", "3.13.1150.0", "Python Software Foundation"),
            new("Microsoft .NET SDK 8.0.404 (x64)", "8.4.424.56", "Microsoft Corporation"),
            new("Microsoft .NET SDK 10.0.100 (x64)", "10.1.25.52411", "Microsoft Corporation"),
        };
        Assert.Equal("2.47.1", DevTools.FindInstalled("git", installed)!.Version);
        Assert.Equal("Python 3.13.1 (64-bit)", DevTools.FindInstalled("python", installed)!.Name);
        Assert.Equal("Microsoft .NET SDK 10.0.100 (x64)", DevTools.FindInstalled("dotnet", installed)!.Name);
        Assert.Null(DevTools.FindInstalled("vscode", installed));
        Assert.Equal("3.13.1", DevTools.VersionOf(installed[2]));
        Assert.Equal("10.0.100", DevTools.VersionOf(installed[5]));
        Assert.Equal("2.47.1", DevTools.VersionOf(installed[0]));
    }

    [Fact]
    public void Software_center_package_wins_over_the_vendor()
    {
        var sccm = new List<SccmItem>
        {
            new("package", "Git for Windows 2.47", "FIR00042", "Install", null, "2.47", null, 1, null),
            new("package", "Visual Studio Code", "FIR00043", null, null, null, null, 1, null),   // no program: not installable
        };
        var states = DevTools.States(new TeamConfig(), new List<InstalledApp>(), sccm, clientPresent: true);
        Assert.Equal("sccm", states.Single(s => s.Id == "git").Method);
        Assert.Equal("direct", states.Single(s => s.Id == "vscode").Method);
        Assert.True(states.Single(s => s.Id == "dotnet").NeedsAdmin);

        var locked = TeamConfig.Parse("""{ "devTools": { "allowDirect": false, "tools": ["git", "python"] } }""");
        var only = DevTools.States(locked, new List<InstalledApp>(), sccm, clientPresent: false);
        Assert.Equal(new[] { "git", "python" }, only.Select(s => s.Id));
        Assert.All(only, s => Assert.Equal("mail", s.Method));
    }
}

public class SoftwareTilesTests
{
    static SccmItem Package(string name, string id, string program, int? state = 1) =>
        new("package", name, id, program, null, null, null, state, null);

    [Fact]
    public void One_tile_per_package_with_its_install_program()
    {
        var list = SoftwareCenter.OnePerPackage(new[]
        {
            Package("7-Zip Uninstall", "FIR001", "Uninstall"),
            Package("7-Zip", "FIR001", "Install"),
            Package("Notepad++ Reparieren", "FIR002", "Reparieren"),
            Package("Notepad++", "FIR002", "Setup"),
        });
        Assert.Equal(new[] { "Install", "Setup" }, list.Select(s => s.ProgramId));
    }

    [Theory]
    [InlineData("alfaview 9", "alfaview", true)]
    [InlineData("Mozilla Firefox (x64 de)", "Mozilla Firefox (x64 de)", true)]
    [InlineData("Notepad++ 8.6", "Notepad++ (64-bit x64)", true)]
    [InlineData("Git", "GitHub CLI", false)]
    [InlineData("Teams", "Microsoft Teams", false)]
    public void Installed_programs_are_matched_without_versions(string sccmName, string displayName, bool found) =>
        Assert.Equal(found, SoftwareCenter.FindInstalled(sccmName, new List<InstalledApp> { new(displayName, "1.0", null) }) != null);

    [Fact]
    public void Everything_from_software_center_becomes_a_tile_but_nothing_twice()
    {
        var team = TeamConfig.Parse("""{ "software": [ { "name": "Alfaview", "sccmName": "alfaview*", "category": "Kommunikation" } ] }""");
        var sccm = new List<SccmItem>
        {
            Package("alfaview 9", "FIR010", "Install"),
            Package("Git for Windows 2.47", "FIR011", "Install"),
            Package("7-Zip – Install", "FIR012", "Install"),
        };
        var devTools = DevTools.States(new TeamConfig(), new List<InstalledApp>(), sccm, clientPresent: true);
        var items = SoftwareCenter.BuildItems(team, sccm, new List<InstalledApp> { new("7-Zip 24.08 (x64)", "24.08", null) }, devTools);

        Assert.Equal(new[] { "Alfaview", "7-Zip" }, items.Select(i => i.Name));          // git is a developer-tool tile
        Assert.Equal("team", items[0].Source);
        Assert.Equal("sccm", items[1].Source);
        Assert.Equal("24.08", items[1].Installed!.Version);
        Assert.True(items[1].CanInstall);
    }
}

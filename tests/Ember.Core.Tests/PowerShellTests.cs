using Ember.Core;
using Xunit;

public class PowerShellTests
{
    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\Ember\", "\"C:\\Ember\\\\\"")]
    [InlineData(@"a\""b", "\"a\\\\\\\"b\"")]
    [InlineData("Jürgen Mößner", "\"Jürgen Mößner\"")]
    public void Arguments_are_quoted_by_the_windows_rules(string value, string expected) =>
        Assert.Equal(expected, PowerShell.QuoteArgument(value));

    [Fact]
    public void Wrapped_script_stores_its_output_in_the_result_file()
    {
        var wrapped = PowerShell.Wrap("Get-Date", @"C:\Ember\Temp\it's.json");
        Assert.Contains("$__out = & {\nGet-Date\n}", wrapped);
        Assert.Contains(@"Set-Content -LiteralPath 'C:\Ember\Temp\it''s.json'", wrapped);
        Assert.StartsWith("$ErrorActionPreference='Stop'", wrapped);
    }

    [Theory]
    [InlineData("At line:1 char:5\n+ ... Unexpected token '}' in expression or statement.\n    + CategoryInfo : ParserError", true)]
    [InlineData("Die Datei kann nicht geladen werden. Die Datei ist nicht digital signiert.", true)]
    [InlineData("File C:\\x.ps1 cannot be loaded because running scripts is disabled on this system.", true)]
    [InlineData("Get-CimInstance : Ungültiger Namespace \"root\\ccm\"", false)]
    public void Launch_problems_are_told_apart_from_script_errors(string stderr, bool launchProblem) =>
        Assert.Equal(launchProblem, PowerShell.IsLaunchProblem(stderr));

    [Fact]
    public void Oem_code_page_is_never_utf8() =>
        Assert.NotEqual(65001, ConsoleEncoding.SystemOemCodePage());
}

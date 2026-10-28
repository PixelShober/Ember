using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Ember.Core;

/// <summary>How powershell.exe receives the script.</summary>
public enum PsLaunch
{
    /// <summary>-Command with the script as plain text (the command line is UTF-16, so umlauts survive).</summary>
    Command,

    /// <summary>-File with a temporary .ps1 (UTF-8 with BOM) and -ExecutionPolicy Bypass.</summary>
    File,

    /// <summary>-EncodedCommand (Base64 of UTF-16).</summary>
    Encoded,
}

/// <summary>
/// Runs Windows PowerShell 5.1 scripts. Every script writes its output into a UTF-8 result file
/// (Set-Content works in every language mode, unlike setting the console encoding). The file is
/// also the proof that the script really ran: security software on company machines sometimes
/// stops one way of starting PowerShell (typically -EncodedCommand) without any error. Ember then
/// tries the next way and remembers the one that works on this machine.
/// </summary>
public static class PowerShell
{
    const string Prelude =
        "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" +
        "try{[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding $false}catch{};\n";

    static readonly PsLaunch[] Order = { PsLaunch.Command, PsLaunch.File, PsLaunch.Encoded };

    /// <summary>The way of starting PowerShell that produced a result on this machine (null until the first run).</summary>
    public static PsLaunch? Launch { get; private set; }

    public static string Exe => ProcessRunner.SystemExe(@"WindowsPowerShell\v1.0\powershell.exe");

    /// <summary>Single-quoted PowerShell string literal.</summary>
    public static string Quote(string? value) => "'" + (value ?? "").Replace("'", "''") + "'";

    internal static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    /// <summary>The script plus the lines that store its output in <paramref name="resultFile"/>.</summary>
    internal static string Wrap(string script, string resultFile) =>
        Prelude +
        "$__out = & {\n" + script + "\n}\n" +
        $"Set-Content -LiteralPath {Quote(resultFile)} -Value ($__out -join \"`n\") -Encoding UTF8\n";

    /// <summary>Runs a script; <see cref="ProcResult.StdOut"/> holds what the script returned.</summary>
    public static async Task<ProcResult> RunAsync(string script, CancellationToken ct, int timeoutSec = 60, bool logOutput = true)
    {
        var (result, output) = await RunCoreAsync(script, ct, timeoutSec);
        if (logOutput && output.Trim().Length > 0) Log.Debug(output.Trim());
        return result with { StdOut = output.Trim() };
    }

    /// <summary>Runs a script whose output is one JSON string (ConvertTo-Json) and returns the parsed result.</summary>
    public static async Task<JsonElement> RunJsonAsync(string script, CancellationToken ct, int timeoutSec = 60)
    {
        var (_, output) = await RunCoreAsync(script, ct, timeoutSec);
        LogResult(output);
        var json = ParseJson(output);
        if (json.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) throw new EmberException("powershellEmpty");
        return json;
    }

    static async Task<(ProcResult Result, string Output)> RunCoreAsync(string script, CancellationToken ct, int timeoutSec)
    {
        Log.Debug("PowerShell:\n" + script.Trim());
        var ways = Launch is { } known ? new[] { known } : Order;
        EmberException? failure = null;
        foreach (var way in ways)
        {
            var resultFile = Paths.TempFile("ember-ps", "json");
            string? scriptFile = null;
            try
            {
                var wrapped = Wrap(script, resultFile);
                var args = new List<string> { "-NoProfile", "-NonInteractive" };
                string shown;
                switch (way)
                {
                    case PsLaunch.Command:
                        args.Add("-Command");
                        args.Add(wrapped);
                        shown = "-Command …";
                        break;
                    case PsLaunch.File:
                        scriptFile = Paths.TempFile("ember-ps", "ps1");
                        File.WriteAllText(scriptFile, wrapped, new UTF8Encoding(true));
                        args.AddRange(new[] { "-ExecutionPolicy", "Bypass", "-File", scriptFile });
                        shown = "-ExecutionPolicy Bypass -File " + scriptFile;
                        break;
                    default:
                        args.Add("-EncodedCommand");
                        args.Add(Encode(wrapped));
                        shown = "-EncodedCommand …";
                        break;
                }

                var result = await ProcessRunner.RunAsync(Exe, args, ct, timeoutSec, OutputKind.Utf8, displayArgs: shown, logOutput: false);
                if (result.StdErr.Length > 0) Log.Warn("PowerShell stderr: " + Shorten(result.StdErr, 3000));

                if (File.Exists(resultFile))
                {
                    if (Launch == null)
                    {
                        Launch = way;
                        Log.Info($"PowerShell runs via {Name(way)} on this machine.");
                    }
                    if (!result.Ok) throw new EmberException("powershell", ("detail", FirstErrorLine(result)));
                    return (result, await File.ReadAllTextAsync(resultFile, ct));
                }

                // No result file: the script never reached its last line.
                if (result.StdErr.Length > 0 && !IsLaunchProblem(result.StdErr))
                    throw new EmberException("powershell", ("detail", FirstErrorLine(result)));
                var detail = result.Output.Length > 0 ? FirstErrorLine(result) : $"exit code {result.ExitCode}";
                Log.Warn($"PowerShell via {Name(way)} did not run the script ({detail}).");
                failure = new EmberException("powershellNoResult", ("way", Name(way)), ("detail", detail));
            }
            finally
            {
                TryDelete(resultFile);
                if (scriptFile != null) TryDelete(scriptFile);
            }
        }
        throw failure ?? new EmberException("powershellNoResult", ("way", "?"), ("detail", "?"));
    }

    /// <summary>
    /// Start info for a visible PowerShell console window (no redirection), started the way that
    /// works on this machine. A script file, if one was needed, is returned so the caller can delete it.
    /// </summary>
    public static (ProcessStartInfo Psi, string? ScriptFile) VisibleStartInfo(string script)
    {
        var psi = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = false };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NoLogo");
        switch (Launch ?? PsLaunch.Command)
        {
            case PsLaunch.File:
                var file = Paths.TempFile("ember-console", "ps1");
                File.WriteAllText(file, script, new UTF8Encoding(true));
                foreach (var a in new[] { "-ExecutionPolicy", "Bypass", "-File", file }) psi.ArgumentList.Add(a);
                return (psi, file);
            case PsLaunch.Encoded:
                psi.ArgumentList.Add("-EncodedCommand");
                psi.ArgumentList.Add(Encode(script));
                return (psi, null);
            default:
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(script);
                return (psi, null);
        }
    }

    /// <summary>
    /// Runs a script elevated (UAC prompt). The script fills <c>$result</c>; Ember writes it to a
    /// JSON file in C:\Ember\Temp, because an elevated process cannot redirect its output to us
    /// (and may even run under a different admin account).
    /// </summary>
    public static async Task<JsonElement> RunElevatedJsonAsync(string script, CancellationToken ct, int timeoutSec = 300)
    {
        Log.Debug("PowerShell (elevated):\n" + script.Trim());
        // The way that works unelevated comes first. -File is the default: the command line of an
        // elevated start (ShellExecute) is best kept short.
        var preferred = Launch == PsLaunch.Encoded ? PsLaunch.Encoded : PsLaunch.File;
        var ways = new[] { preferred }.Concat(new[] { PsLaunch.File, PsLaunch.Command, PsLaunch.Encoded }.Where(w => w != preferred)).ToArray();

        foreach (var way in ways)
        {
            var resultFile = Paths.TempFile("ember-elevated", "json");
            string? scriptFile = null;
            try
            {
                var wrapped =
                    Prelude +
                    "$result = $null\n" +
                    "try {\n" + script + "\n} catch { $result = @{ error = $_.Exception.Message } }\n" +
                    $"Set-Content -LiteralPath {Quote(resultFile)} -Value ($result | ConvertTo-Json -Depth 6 -Compress) -Encoding UTF8\n";
                string arguments;
                switch (way)
                {
                    case PsLaunch.File:
                        scriptFile = Paths.TempFile("ember-elevated", "ps1");
                        File.WriteAllText(scriptFile, wrapped, new UTF8Encoding(true));
                        arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptFile}\"";
                        break;
                    case PsLaunch.Command:
                        arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command " + QuoteArgument(wrapped);
                        break;
                    default:
                        arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + Encode(wrapped);
                        break;
                }

                var exitCode = await StartElevatedAsync(arguments, ct, timeoutSec);
                if (!File.Exists(resultFile))
                {
                    Log.Warn($"PowerShell (elevated) via {Name(way)} did not run the script (exit code {exitCode}).");
                    continue;
                }
                var text = await File.ReadAllTextAsync(resultFile, ct);
                LogResult(text);
                var json = ParseJson(text);
                if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var err))
                    throw new EmberException("powershell", ("detail", err.GetString()));
                return json;
            }
            finally
            {
                TryDelete(resultFile);
                if (scriptFile != null) TryDelete(scriptFile);
            }
        }
        throw new EmberException("elevatedNoResult");
    }

    static async Task<int> StartElevatedAsync(string arguments, CancellationToken ct, int timeoutSec)
    {
        var psi = new ProcessStartInfo(Exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = arguments,
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new EmberException("elevationCancelled");
        }
        if (process == null) throw new EmberException("processStart", ("file", Exe));

        using (process)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                throw new EmberException("processTimeout", ("file", "powershell.exe"), ("seconds", timeoutSec));
            }
            try { return process.ExitCode; } catch { return -1; }
        }
    }

    /// <summary>Errors that mean "this way of starting PowerShell does not work here", not "the script failed".</summary>
    internal static bool IsLaunchProblem(string stderr) =>
        new[]
        {
            "ParserError", "ParseException", "Unexpected token", "Unerwartetes Token", "Missing closing", "fehlende schließende",
            "PSSecurityException", "UnauthorizedAccess", "about_Execution_Policies", "not digitally signed", "nicht digital signiert",
            "running scripts is disabled", "Ausführung von Skripts", "is not recognized as", "wird nicht als Name",
        }.Any(p => stderr.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>One command-line argument, quoted by the usual Windows rules (for ShellExecute).</summary>
    internal static string QuoteArgument(string value)
    {
        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') sb.Append('\\', backslashes * 2 + 1);
            else sb.Append('\\', backslashes);
            backslashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    static string Name(PsLaunch way) => way switch
    {
        PsLaunch.Command => "-Command",
        PsLaunch.File => "-File",
        _ => "-EncodedCommand",
    };

    static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { }
    }

    static string Shorten(string text, int max) => text.Length > max ? text[..max] + " …" : text;

    /// <summary>What Windows answered, so a log from a test machine shows the actual data (debug level).</summary>
    static void LogResult(string text)
    {
        text = text.Trim().TrimStart('\uFEFF');
        Log.Debug("PowerShell result: " + Shorten(text, 6000));
    }

    internal static JsonElement ParseJson(string text)
    {
        text = text.Trim().TrimStart('\uFEFF');
        if (text.Length == 0) return JsonDocument.Parse("null").RootElement.Clone();
        // Warnings printed before the JSON (rare) are skipped.
        int start = text.IndexOfAny(new[] { '{', '[' });
        if (start > 0) text = text[start..];
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    static string FirstErrorLine(ProcResult r) =>
        (r.StdErr.Length > 0 ? r.StdErr : r.StdOut).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? $"exit code {r.ExitCode}";

    /// <summary>A JSON array from PowerShell may also come as a single object or null.</summary>
    public static IEnumerable<JsonElement> AsArray(this JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Array => e.EnumerateArray(),
        JsonValueKind.Null or JsonValueKind.Undefined => Enumerable.Empty<JsonElement>(),
        _ => new[] { e },
    };

    public static string? Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
            : null;

    public static long? Num(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.Number => v.GetInt64(),
                JsonValueKind.String when long.TryParse(v.GetString(), out var n) => n,
                _ => null,
            }
            : null;

    public static bool? Bool(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;
}

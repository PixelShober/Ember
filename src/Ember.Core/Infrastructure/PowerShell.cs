using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Ember.Core;

/// <summary>
/// Runs Windows PowerShell 5.1 scripts. Scripts are passed as -EncodedCommand (UTF-16, so umlauts
/// and quotes inside the script survive), and the output is forced to UTF-8.
/// </summary>
public static class PowerShell
{
    // Setting the console encoding fails in Constrained Language Mode; JSON results therefore go
    // through a UTF-8 file (Set-Content works in every language mode), see RunJsonAsync.
    const string Prelude =
        "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" +
        "try{[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding $false}catch{};\n";

    public static string Exe => ProcessRunner.SystemExe(@"WindowsPowerShell\v1.0\powershell.exe");

    /// <summary>Single-quoted PowerShell string literal.</summary>
    public static string Quote(string? value) => "'" + (value ?? "").Replace("'", "''") + "'";

    internal static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(Prelude + script));

    public static Task<ProcResult> RunAsync(string script, CancellationToken ct, int timeoutSec = 60, bool logOutput = true)
    {
        Log.Debug("PowerShell:\n" + script.Trim());
        return ProcessRunner.RunAsync(Exe, new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Encode(script) },
                                      ct, timeoutSec, OutputKind.Utf8, displayArgs: "-EncodedCommand …", logOutput: logOutput);
    }

    /// <summary>Runs a script whose output is one JSON string (ConvertTo-Json) and returns the parsed result.</summary>
    public static async Task<JsonElement> RunJsonAsync(string script, CancellationToken ct, int timeoutSec = 60)
    {
        var resultFile = Path.Combine(Path.GetTempPath(), $"ember-ps-{Guid.NewGuid():N}.json");
        var wrapped =
            "$__out = & {\n" + script + "\n}\n" +
            $"Set-Content -LiteralPath {Quote(resultFile)} -Value ($__out -join \"`n\") -Encoding UTF8\n";
        try
        {
            var result = await RunAsync(wrapped, ct, timeoutSec, logOutput: false);
            if (!result.Ok)
                throw new EmberException("powershell", ("detail", FirstErrorLine(result)));
            var text = File.Exists(resultFile) ? await File.ReadAllTextAsync(resultFile, ct) : result.StdOut;
            LogResult(text);
            return ParseJson(text);
        }
        finally
        {
            try { File.Delete(resultFile); } catch { }
        }
    }

    /// <summary>
    /// Runs a script elevated (UAC prompt). The script fills <c>$result</c>; Ember writes it to a
    /// temporary JSON file, because an elevated process cannot redirect its output to us.
    /// </summary>
    public static async Task<JsonElement> RunElevatedJsonAsync(string script, CancellationToken ct, int timeoutSec = 300)
    {
        var resultFile = Path.Combine(Path.GetTempPath(), $"ember-elevated-{Guid.NewGuid():N}.json");
        var wrapped =
            "$result = $null\n" +
            "try {\n" + script + "\n} catch { $result = @{ error = $_.Exception.Message } }\n" +
            $"Set-Content -LiteralPath {Quote(resultFile)} -Value ($result | ConvertTo-Json -Depth 6 -Compress) -Encoding UTF8\n";

        Log.Debug("PowerShell (elevated):\n" + script.Trim());
        var psi = new ProcessStartInfo(Exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + Encode(wrapped),
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
        }

        try
        {
            if (!File.Exists(resultFile)) throw new EmberException("elevatedNoResult");
            var text = await File.ReadAllTextAsync(resultFile, ct);
            LogResult(text);
            var json = ParseJson(text);
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var err))
                throw new EmberException("powershell", ("detail", err.GetString()));
            return json;
        }
        finally
        {
            try { File.Delete(resultFile); } catch { }
        }
    }

    /// <summary>What Windows answered, so a log from a test machine shows the actual data (debug level).</summary>
    static void LogResult(string text)
    {
        text = text.Trim().TrimStart('\uFEFF');
        Log.Debug("PowerShell result: " + (text.Length > 6000 ? text[..6000] + " …" : text));
    }

    internal static JsonElement ParseJson(string text)
    {
        text = text.Trim().TrimStart('﻿');
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

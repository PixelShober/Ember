using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ember.Core;

/// <param name="Ready">Build and model are there: the start script and Pi can start the server.</param>
/// <param name="Reason">Why not (noBuild, noCudaRuntime, noModel); null when ready.</param>
/// <param name="PiExtension">Pi starts llama-server by itself (ember-llama.ts is in Pi's extensions folder).</param>
public sealed record LaunchInfo(bool Ready, string? Reason, string? Model, string Profile, int Port,
                                string ConfigFile, string ScriptFile, string ExtensionFile, bool PiExtension);

/// <summary>
/// llama-server without Ember, like px: Ember only sets it up. It keeps three files current:
/// llama.json (build, model and arguments, exactly as Ember would start it), a start script for a
/// console (also in the Start menu) and, once Pi is set up, a Pi extension that starts the server
/// when Pi uses the local model. Script and extension take everything from llama.json, so a model or
/// profile chosen in Ember applies at the next start without Ember running.
/// </summary>
public static class LlamaLaunch
{
    public const string ShortcutName = "llama-server (Ember)";
    public static string ConfigFile => Path.Combine(Paths.LlamaDir, "llama.json");
    public static string ScriptFile => Path.Combine(Paths.LlamaDir, "llama-server starten.cmd");
    public static string LogFile => Path.Combine(Paths.LogDir, "llama-server.log");
    public static string ExtensionFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pi", "agent", "extensions", "ember-llama.ts");

    static string? _written;

    /// <summary>The model the server starts with: the one chosen last, else the first one that is there.</summary>
    public static ModelState? Model(UserSettings s, TeamConfig team)
    {
        var ready = Models.List(s, team).Where(m => m.Downloaded).ToList();
        return ready.FirstOrDefault(m => m.Id.Equals(s.LlamaModel, StringComparison.OrdinalIgnoreCase)) ?? ready.FirstOrDefault();
    }

    /// <summary>Writes llama.json and the start script when something changed; adds the Start menu entry once.</summary>
    public static LaunchInfo Sync(UserSettings s, TeamConfig team)
    {
        var profile = LlamaCpp.Profile(s.LlamaProfile);
        var build = LlamaCpp.TryCurrent(s);
        var model = Model(s, team);
        string? reason = build == null ? "noBuild" : !build.Complete ? "noCudaRuntime" : model == null ? "noModel" : null;

        var config = new JsonObject
        {
            ["format"] = 1,
            ["writtenBy"] = "Ember " + AppInfo.Version,
            ["ready"] = reason == null,
            ["reason"] = reason == null ? null : L.T($"ai.launch.reasons.{reason}"),
            ["exe"] = build == null ? null : Path.Combine(build.Path, "llama-server.exe"),
            ["cwd"] = build?.Path,
            ["args"] = reason == null ? new JsonArray(LlamaCpp.ServerArgs(model!, profile, s.LlamaPort, s.LlamaArgs).Select(a => (JsonNode?)a).ToArray()) : null,
            ["port"] = s.LlamaPort,
            ["model"] = model?.Name,
            ["alias"] = model?.Alias,
            ["profile"] = profile.Id,
            ["context"] = profile.Context,
            ["log"] = LogFile,
        };
        var json = config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var script = reason == null
            ? Script(Path.Combine(build!.Path, "llama-server.exe"), build.Path, LlamaCpp.ServerArgs(model!, profile, s.LlamaPort, s.LlamaArgs), s.LlamaPort, model!.Name)
            : NotReadyScript(L.T($"ai.launch.reasons.{reason}"));
        try
        {
            if (_written != json + script || !File.Exists(ConfigFile) || !File.Exists(ScriptFile))
            {
                Paths.Ensure(Paths.LlamaDir);
                WriteIfChanged(ConfigFile, json);
                // cmd.exe reads the script in the console's code page; chcp 65001 in its first line switches to UTF-8.
                WriteIfChanged(ScriptFile, script);
                _written = json + script;
                if (reason == null && !ShortcutExists()) Shortcut.CreateInStartMenu(ShortcutName, ScriptFile, Paths.LlamaDir, L.T("ai.launch.shortcut"));
            }
            // Pi set up before: keep its extension at the current version.
            if (File.Exists(ExtensionFile)) WriteIfChanged(ExtensionFile, Extension());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(L.T("ai.launch.writeFailed", ("detail", ex.Message)), "llama");
        }
        return new LaunchInfo(reason == null, reason, model?.Name, profile.Id, s.LlamaPort, ConfigFile, ScriptFile, ExtensionFile, File.Exists(ExtensionFile));
    }

    /// <summary>Puts the Pi extension in place (from "Mit llama-server verbinden" in the Pi card).</summary>
    public static void InstallPiExtension()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ExtensionFile)!);
        WriteIfChanged(ExtensionFile, Extension());
        Log.Info(L.T("ai.launch.piExtension", ("path", ExtensionFile)), "pi");
    }

    static void WriteIfChanged(string file, string text)
    {
        if (File.Exists(file) && File.ReadAllText(file) == text) return;
        File.WriteAllText(file, text, new UTF8Encoding(false));
    }

    static bool ShortcutExists() =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName + ".lnk"));

    /// <summary>The extension with the path of llama.json filled in (template embedded in Ember.Core).</summary>
    internal static string Extension()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Ember.ember-llama.ts")
                           ?? throw new InvalidOperationException("ember-llama.ts missing");
        var template = new StreamReader(stream).ReadToEnd();
        var texts = new JsonObject();
        foreach (var key in new[] { "noConfig", "notReady", "loading", "ready", "readyLong", "waiting", "exited", "timeout", "stopped", "notRunning", "command", "stateReady", "stateLoading", "stateOff" })
            texts[key] = L.T("ai.launch.pi." + key);
        return template.Replace("%CONFIG%", ConfigFile.Replace("`", ""))
                       .Replace("%PROVIDER%", PiAgent.Provider)
                       .Replace("%MESSAGES%", texts.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    /// <summary>
    /// The start script: a console with llama-server in front, Ctrl+C stops it. If the server already
    /// answers (started by Pi), it says so instead of failing on the busy port.
    /// </summary>
    internal static string Script(string exe, string dir, IEnumerable<string> args, int port, string model)
    {
        var sb = new StringBuilder();
        sb.Append("@echo off\r\n");
        sb.Append("chcp 65001 >nul\r\n");
        sb.Append("rem Written by Ember. Ember rewrites this file when the model, profile or llama.cpp build changes.\r\n");
        sb.Append("title llama-server\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"curl.exe -s -o nul --noproxy * --max-time 2 http://127.0.0.1:{port}/health 2>nul && (\r\n");
        sb.Append("  echo ").Append(Echo(L.T("ai.launch.alreadyRunning", ("port", port)))).Append("\r\n");
        sb.Append("  pause\r\n  exit /b 0\r\n)\r\n");
        sb.Append("echo ").Append(Echo(L.T("ai.launch.starting", ("model", model), ("port", port)))).Append("\r\n");
        sb.Append("cd /d ").Append(Quote(dir)).Append("\r\n");
        sb.Append(Quote(exe));
        foreach (var a in args) sb.Append(' ').Append(Quote(a));
        sb.Append("\r\n");
        // Ended by an error: keep the window open so the message can be read.
        sb.Append("if errorlevel 1 pause\r\n");
        return sb.ToString();
    }

    static string NotReadyScript(string reason) =>
        "@echo off\r\nchcp 65001 >nul\r\ntitle llama-server\r\necho " + Echo(reason) + "\r\npause\r\n";

    /// <summary>Text for "echo": cmd's special characters escaped.</summary>
    internal static string Echo(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (c == '%') sb.Append("%%");
            else if ("^&|<>()".Contains(c)) sb.Append('^').Append(c);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// One argument for a cmd line: quoted when needed, following the rules llama-server's C runtime
    /// uses to split it again (backslashes before a quote doubled, quotes escaped); % doubled for cmd.
    /// </summary>
    internal static string Quote(string arg)
    {
        string result;
        if (arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c is '"' or '&' or '|' or '<' or '>' or '^' or '(' or ')' or ','  or ';' or '='))
        {
            result = arg;
        }
        else
        {
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
                sb.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            sb.Append('\\', slashes * 2).Append('"');
            result = sb.ToString();
        }
        return result.Replace("%", "%%");
    }

    // ------------------------------------------------------------------ a server started without Ember

    /// <summary>A llama-server Ember did not start itself (Pi or the start script), or null.</summary>
    public static Process? ExternalServer(Process? own)
    {
        try
        {
            var all = Process.GetProcessesByName("llama-server");
            var found = all.FirstOrDefault(p => own == null || p.Id != own.Id);
            foreach (var p in all) if (p != found) p.Dispose();
            return found;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>What llama.json says about the server it starts (model, alias, profile, context).</summary>
    public static (string? Model, string? Alias, string? Profile, int? Context) Started()
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(ConfigFile));
            return ((string?)node?["model"], (string?)node?["alias"], (string?)node?["profile"], (int?)node?["context"]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, null, null, null);
        }
    }

    /// <summary>The end of the server log the Pi extension writes (for layers on the GPU, speed, errors).</summary>
    public static IEnumerable<string> LogTail(int bytes = 256 * 1024)
    {
        try
        {
            using var fs = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > bytes) fs.Seek(-bytes, SeekOrigin.End);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            return reader.ReadToEnd().Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}

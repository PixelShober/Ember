using System.Diagnostics;

namespace Ember.Core;

public sealed record PxStatus(bool Installed, bool Running, bool PortOpen, bool Debug, string Url, bool ConfigOutdated);

/// <summary>Starting, stopping and debugging px.</summary>
public static class PxControl
{
    static Process? _debugProcess;

    public static bool DebugRunning => _debugProcess is { HasExited: false };

    public static string? FindExe(string installDir, string name)
    {
        try
        {
            return Directory.Exists(installDir)
                ? Directory.EnumerateFiles(installDir, name, SearchOption.AllDirectories).FirstOrDefault()
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static Process[] RunningProcesses() =>
        Process.GetProcessesByName("px").Concat(Process.GetProcessesByName("pxw")).ToArray();

    public static async Task<PxStatus> StatusAsync(EffectiveSettings s)
    {
        var processes = RunningProcesses();
        bool running = processes.Length > 0;
        foreach (var p in processes) p.Dispose();
        bool installed = FindExe(s.InstallDir, "px.exe") != null;
        bool portOpen = await Net.PortOpenAsync("127.0.0.1", s.Port, 500);
        return new PxStatus(installed, running, portOpen, DebugRunning, s.PxUrl, installed && ConfigOutdated(s));
    }

    /// <summary>True when px.ini no longer matches the effective settings (e.g. new proxy in team.json).</summary>
    internal static bool ConfigOutdated(EffectiveSettings s)
    {
        try
        {
            if (!File.Exists(s.ConfigIni)) return true;
            var port = IniFile.Get(s.ConfigIni, "proxy", "port");
            if (port != null && port != s.Port.ToString()) return true;
            if (s.UsePac)
            {
                var pac = IniFile.Get(s.ConfigIni, "proxy", "pac");
                var wanted = string.IsNullOrWhiteSpace(s.PacUrl) ? Net.ReadPacFromRegistry() : s.PacUrl.Trim();
                return pac != null && wanted != null && !pac.Equals(wanted, StringComparison.OrdinalIgnoreCase);
            }
            var server = IniFile.Get(s.ConfigIni, "proxy", "server");
            return server != null && !server.Equals(s.UpstreamHostPort, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Stops px cleanly (px --quit) and kills what is left. Returns the number of processes.</summary>
    public static async Task<int> StopAsync(EffectiveSettings s, CancellationToken ct)
    {
        StopDebug();

        var before = RunningProcesses();
        int count = before.Length;
        foreach (var p in before) p.Dispose();
        if (count == 0) return 0;

        var px = FindExe(s.InstallDir, "px.exe");
        if (px != null)
        {
            try
            {
                await ProcessRunner.RunAsync(px, new[] { "--config=" + s.ConfigIni, "--quit" }, ct, 20);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warn("px --quit: " + ex.Message);
            }
            await Task.Delay(500, ct);
        }

        foreach (var p in RunningProcesses())
        {
            using (p)
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                    Log.Info(L.T("px.processStopped", ("name", p.ProcessName), ("pid", p.Id)));
                }
                catch (Exception ex)
                {
                    Log.Warn(L.T("px.processStopFailed", ("name", p.ProcessName), ("pid", p.Id), ("detail", ex.Message)));
                }
            }
        }
        return count;
    }

    /// <summary>Starts px in the background (pxw.exe, no console window).</summary>
    public static void Start(EffectiveSettings s)
    {
        var exe = FindExe(s.InstallDir, "pxw.exe") ?? FindExe(s.InstallDir, "px.exe")
                  ?? throw new EmberException("pxNotInstalled", ("path", s.InstallDir));
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        };
        psi.ArgumentList.Add("--config=" + s.ConfigIni);
        Process.Start(psi)?.Dispose();
        Log.Info(L.T("px.started", ("path", exe)));
    }

    public static async Task WaitForPortAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await Net.PortOpenAsync("127.0.0.1", port, 500))
        {
            if (DateTime.UtcNow > deadline)
                throw new EmberException("pxPortTimeout", ("seconds", (int)timeout.TotalSeconds), ("port", port));
            await Task.Delay(500, ct);
        }
    }

    /// <summary>Starts px in the foreground with verbose logging; its output appears in the log (source "px").</summary>
    public static async Task StartDebugAsync(EffectiveSettings s, Action onExit)
    {
        await StopAsync(s, CancellationToken.None);
        var exe = FindExe(s.InstallDir, "px.exe")
                  ?? throw new EmberException("pxNotInstalled", ("path", s.InstallDir));

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        };
        psi.ArgumentList.Add("--config=" + s.ConfigIni);
        psi.ArgumentList.Add("--log=4");
        psi.ArgumentList.Add("--foreground=1");
        psi.Environment["PYTHONUNBUFFERED"] = "1";   // px is Python: otherwise output only arrives in blocks
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
        psi.StandardErrorEncoding = System.Text.Encoding.UTF8;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) Log.Info(e.Data, "px"); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Log.Info(e.Data, "px"); };
        process.Exited += (_, _) =>
        {
            int code = -1;
            try { code = process.ExitCode; } catch { }
            Log.Info(L.T("px.debugEnded", ("code", code)));
            onExit();
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _debugProcess = process;
        Log.Info(L.T("px.debugStarted"));
    }

    public static void StopDebug()
    {
        var p = _debugProcess;
        _debugProcess = null;
        if (p == null) return;
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
    }
}

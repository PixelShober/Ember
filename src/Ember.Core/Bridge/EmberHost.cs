using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Ember.Core;

/// <summary>
/// The whole host side in one object: settings, team configuration, jobs, updates and the bridge
/// API the UI talks to. The WPF shell only creates it and forwards messages.
/// </summary>
public sealed class EmberHost : IDisposable
{
    readonly List<LogEntry> _pendingLog = new();
    readonly object _logSync = new();
    readonly CancellationTokenSource _cts = new();
    Timer? _logTimer, _pxTimer, _updateTimer;
    PxStatus? _lastPx;

    public EmberHost(IHostShell shell)
    {
        Shell = shell;
        Settings = UserSettings.Load();
        L.Configure(Settings.Language);
        Updater = new Updater(() => Effective, () => Team.Config);
        Bridge = new Bridge();
        RegisterApi();

        Jobs.Changed += job => Bridge.Emit("job", job);
        Updater.Changed += status => Bridge.Emit("update", status);
        Log.Written += OnLog;
    }

    public IHostShell Shell { get; }
    public Bridge Bridge { get; }
    public UserSettings Settings { get; private set; }
    public TeamConfigService Team { get; } = new();
    public JobManager Jobs { get; } = new();
    public Updater Updater { get; }

    public EffectiveSettings Effective => EffectiveSettings.Build(Settings, Team.Config);

    /// <summary>Admin in the team configuration: may use the advanced modules (KI, Energie).</summary>
    public bool IsAdmin => AdminCheck.IsAdmin(Team.Config);

    /// <summary>The advanced tabs are visible (admin, and not hidden with Ctrl+E). Also switches the DevTools.</summary>
    public bool ShowAdvanced => IsAdmin && !Settings.HideAdvanced;

    /// <summary>Loads the team configuration and starts the background timers.</summary>
    public async Task InitializeAsync()
    {
        Log.Cleanup();
        Log.Info(L.T("app.started", ("version", AppInfo.Version), ("mode", AppInfo.Mode), ("language", L.Tag)));
        await Team.LoadAsync(Settings, _cts.Token);
        Shell.SetDevTools(ShowAdvanced);

        _logTimer = new Timer(_ => FlushLog(), null, 150, 150);
        _pxTimer = new Timer(async _ => await PollPxAsync(), null, 500, 4000);
        if (Settings.AutoUpdate && !AppInfo.IsPrerelease)
            _updateTimer = new Timer(async _ => await Updater.CheckAsync(download: true, _cts.Token), null,
                                     TimeSpan.FromSeconds(15), TimeSpan.FromHours(6));
    }

    void OnLog(LogEntry entry)
    {
        lock (_logSync) _pendingLog.Add(entry);
    }

    void FlushLog()
    {
        List<LogEntry> batch;
        lock (_logSync)
        {
            if (_pendingLog.Count == 0) return;
            batch = new List<LogEntry>(_pendingLog);
            _pendingLog.Clear();
        }
        Bridge.Emit("log", batch);
    }

    async Task PollPxAsync()
    {
        try
        {
            var status = await PxControl.StatusAsync(Effective);
            if (status == _lastPx) return;
            _lastPx = status;
            Bridge.Emit("px.status", status);
        }
        catch (Exception ex)
        {
            Log.Debug("px status: " + ex.Message);
        }
    }

    void RequireAdmin()
    {
        if (!IsAdmin) throw new EmberException("moduleNotAllowed");
    }

    void RequireNoPxJob()
    {
        if (Jobs.IsRunning("px")) throw new EmberException("jobRunning", ("title", L.T("px.title")));
    }

    object Info() => new
    {
        version = AppInfo.Version,
        uiVersion = AppInfo.UiVersion,
        bridge = AppInfo.BridgeVersion,
        mode = AppInfo.Mode,
        language = L.Tag,
        languageSetting = Settings.Language,
        user = Elevation.AccountName,
        computer = Environment.MachineName,
        elevated = Elevation.IsElevated,
        admin = IsAdmin,
        showAdvanced = ShowAdvanced,
        root = Paths.Root,
        logDir = Paths.LogDir,
        team = Team.Status,
        teamName = Team.Config.Name,
        serviceCenter = Team.Config.ServiceCenter?.Email,
        repo = "https://github.com/" + AppInfo.GitHubRepo,
        docs = "https://pixelshober.github.io/Ember/",
    };

    void RegisterApi()
    {
        var b = Bridge;

        // ------------------------------------------------------------------ app
        b.Register("app.info", Info);
        // Ctrl+E: admins show or hide the advanced tabs (KI, Energie).
        b.Register("app.toggleAdvanced", () =>
        {
            RequireAdmin();
            Settings.HideAdvanced = !Settings.HideAdvanced;
            Settings.Save();
            Shell.SetDevTools(ShowAdvanced);
            Bridge.Emit("app.info", Info());
            return ShowAdvanced;
        });
        b.Register("app.ready", (args, _) =>
        {
            Log.Debug("UI ready: " + args.OptStr("version"));
            AppReady?.Invoke(args.OptStr("version") ?? "?");
            return Task.FromResult<object?>(null);
        });
        b.Register("app.openExternal", (args, _) =>
        {
            Shell.OpenUrl(args.Req("url"));
            return Task.FromResult<object?>(null);
        });
        b.Register("app.openFolder", (args, _) =>
        {
            var dir = args.Req("key") switch
            {
                "logs" => Paths.LogDir,
                "settings" => Paths.ConfigDir,
                "px" => Effective.InstallDir,
                "pxConfig" => Path.GetDirectoryName(Paths.PxConfigIni)!,
                "reports" => Paths.ReportDir,
                "models" => Models.Dir(Settings),
                "llama" => Paths.LlamaDir,
                var other => throw new EmberException("unknownFolder", ("key", other)),
            };
            if (!Directory.Exists(dir)) throw new EmberException("folderMissing", ("path", dir));
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true })?.Dispose();
            return Task.FromResult<object?>(null);
        });
        b.Register("app.pickFolder", async (args, _) => await Shell.PickFolderAsync(args.Req("title", ""), args.OptStr("initial")));
        b.Register("app.pickFile", async (args, _) => await Shell.PickFileAsync(args.Req("title", ""), args.Req("filter", "*.*|*.*")));

        // ------------------------------------------------------------------ settings & team
        b.Register("settings.get", () => SettingsView());
        b.Register("settings.save", async (args, ct) =>
        {
            var next = args.Get<UserSettings>("settings");
            var error = EffectiveSettings.Build(next, Team.Config).Validate();
            if (error != null) throw new EmberException("settingsInvalid", ("detail", error));
            bool languageChanged = next.Language != Settings.Language;
            bool teamChanged = next.TeamConfigPath != Settings.TeamConfigPath;
            next.HideAdvanced = Settings.HideAdvanced;   // only Ctrl+E changes this (app.toggleAdvanced)
            next.Save();
            Settings = next;
            if (languageChanged) L.Configure(next.Language);
            if (teamChanged) await Team.LoadAsync(Settings, ct);
            if (teamChanged) Shell.SetDevTools(ShowAdvanced);
            Log.Info(L.T("settings.saved", ("path", Paths.SettingsFile)));
            if (languageChanged || teamChanged) Bridge.Emit("app.info", Info());
            return SettingsView();
        });
        b.Register("settings.defaults", () => new UserSettings());
        b.Register("team.status", () => new { status = Team.Status, name = Team.Config.Name });
        b.Register("team.reload", async ct =>
        {
            await Team.LoadAsync(Settings, ct);
            Shell.SetDevTools(ShowAdvanced);
            Bridge.Emit("app.info", Info());
            return SettingsView();
        });

        // ------------------------------------------------------------------ proxy & px
        b.Register("proxy.detect", async ct => await Net.DetectProxyAsync(ct));
        b.Register("proxy.diagnose", async ct => await ProxyTools.DiagnoseAsync(Effective, ct));
        b.Register("proxy.set", async (args, ct) => await ProxyTools.SetAsync(args.Req("tool"), Effective, ct));
        b.Register("proxy.remove", async (args, ct) => await ProxyTools.RemoveAsync(args.Req("tool"), Effective, ct, onlyIfOurs: !args.Flag("force")));
        b.Register("px.status", async _ => await PxControl.StatusAsync(Effective));
        b.Register("px.install", () =>
        {
            var error = Effective.Validate();
            if (error != null) throw new EmberException("settingsInvalid", ("detail", error));
            var ctx = new PxContext(Effective);
            return Jobs.Start("px", "px", L.T("px.jobs.install"), PxPipelines.Install(ctx), job =>
            {
                ctx.Dispose();
                return PollPxAsync();
            }).Snapshot();
        });
        b.Register("px.uninstall", () =>
        {
            var ctx = new PxContext(Effective);
            return Jobs.Start("px", "px", L.T("px.jobs.uninstall"), PxPipelines.Uninstall(ctx), job =>
            {
                ctx.Dispose();
                return PollPxAsync();
            }).Snapshot();
        });
        b.Register("px.start", async ct =>
        {
            RequireNoPxJob();
            PxControl.Start(Effective);
            await PxControl.WaitForPortAsync(Effective.Port, TimeSpan.FromSeconds(20), ct);
            await PollPxAsync();
            return null;
        });
        b.Register("px.stop", async ct =>
        {
            RequireNoPxJob();
            var n = await PxControl.StopAsync(Effective, ct);
            await PollPxAsync();
            return n;
        });
        b.Register("px.restart", async ct =>
        {
            RequireNoPxJob();
            await PxControl.StopAsync(Effective, ct);
            PxControl.Start(Effective);
            await PxControl.WaitForPortAsync(Effective.Port, TimeSpan.FromSeconds(20), ct);
            await PollPxAsync();
            return null;
        });
        b.Register("px.debug", async ct =>
        {
            RequireNoPxJob();
            if (PxControl.DebugRunning)
            {
                PxControl.StopDebug();
                await Task.Delay(500, ct);
                PxControl.Start(Effective);
            }
            else
            {
                await PxControl.StartDebugAsync(Effective, () => _ = PollPxAsync());
            }
            await PollPxAsync();
            return PxControl.DebugRunning;
        });

        // ------------------------------------------------------------------ jobs
        b.Register("jobs.list", () => Jobs.List());
        b.Register("jobs.cancel", (args, _) => { Jobs.Cancel(args.Req("id")); return Task.FromResult<object?>(null); });
        b.Register("jobs.retry", (args, _) => { Jobs.Retry(args.Req("id")); return Task.FromResult<object?>(null); });
        b.Register("jobs.dismiss", (args, _) => { Jobs.Remove(args.Req("id")); return Task.FromResult<object?>(null); });

        // ------------------------------------------------------------------ git
        b.Register("git.get", async ct => await GitIdentity.GetAsync(ct));
        b.Register("git.set", async (args, ct) =>
        {
            await GitIdentity.SetAsync(args.Req("name"), args.Req("email"), ct);
            return await GitIdentity.GetAsync(ct);
        });

        // ------------------------------------------------------------------ software & mail
        b.Register("software.list", async ct => await SoftwareCenter.ListAsync(Team.Config, ct));
        b.Register("software.open", (args, _) =>
        {
            SoftwareCenter.Open(args.OptStr("link"));
            return Task.FromResult<object?>(null);
        });
        b.Register("software.install", (args, _) =>
        {
            var key = args.Req("key");
            var packageId = args.Req("packageId");
            var programId = args.Req("programId");
            var name = args.Req("name", key);
            var displayPattern = Team.Config.Software?.FirstOrDefault(s => s.Key == key) is { } entry
                ? string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Name + "*" : entry.DisplayName!
                : name + "*";
            return Task.FromResult<object?>(Jobs.Start("software:" + key, "software", L.T("software.jobs.install", ("name", name)),
                SoftwareSteps(packageId, programId, displayPattern)).Snapshot());
        });
        b.Register("mail.compose", async (args, ct) =>
        {
            var template = args.Req("template");
            var vars = args.Dict("vars");
            Func<CancellationToken, Task<string>>? report = args.Flag("attachReport")
                ? c => Diagnostics.BuildAsync(Effective, Team.Status, c)
                : null;
            return await Mail.ComposeAsync(Team.Config, template, vars, report, ct);
        });
        b.Register("mail.preview", (args, _) =>
        {
            var (subject, body) = Mail.Render(Team.Config, args.Req("template"), args.Dict("vars"));
            return Task.FromResult<object?>(new { to = Team.Config.ServiceCenter?.Email, subject, body });
        });
        b.Register("diagnostics.report", async ct =>
        {
            var text = await Diagnostics.BuildAsync(Effective, Team.Status, ct);
            return new { text, path = Diagnostics.Save(text) };
        });

        // ------------------------------------------------------------------ power
        // ------------------------------------------------------------------ power (advanced)
        b.Register("power.state", async ct => { RequireAdmin(); return await PowerProfiles.GetStateAsync(Team.Config, ct); });
        b.Register("power.apply", async (args, ct) => { RequireAdmin(); return await PowerProfiles.ChangeAsync(Team.Config, args.Req("profile"), PowerProfiles.Mode.Apply, ct); });
        b.Register("power.defaults", async (args, ct) => { RequireAdmin(); return await PowerProfiles.ChangeAsync(Team.Config, args.Req("profile"), PowerProfiles.Mode.Defaults, ct); });
        b.Register("power.restore", async (args, ct) => { RequireAdmin(); return await PowerProfiles.ChangeAsync(Team.Config, args.Req("profile"), PowerProfiles.Mode.Backup, ct); });

        // ------------------------------------------------------------------ wake-on-LAN for this machine (KI tab, advanced)
        b.Register("wol.local", async ct => { RequireAdmin(); return await WolTarget.GetStateAsync(ct); });
        b.Register("wol.apply", async ct => { RequireAdmin(); var errors = await WolTarget.ApplyAsync(ct); return new { errors, state = await WolTarget.GetStateAsync(ct) }; });
        b.Register("wol.reset", async ct => { RequireAdmin(); var errors = await WolTarget.ResetAsync(ct); return new { errors, state = await WolTarget.GetStateAsync(ct) }; });

        // ------------------------------------------------------------------ system
        b.Register("system.checks", async ct => await SystemChecks.RunAsync(ct));
        b.Register("system.list", () => SystemChecks.Pending());
        b.Register("system.check", async (args, ct) => await SystemChecks.RunOneAsync(args.Req("id"), ct));
        b.Register("system.action", async (args, ct) => await SystemChecks.RunActionAsync(args.Req("check"), args.Req("action"), ct));
        b.Register("system.adminCommands", (args, _) =>
            Task.FromResult<object?>(SystemChecks.AdminCommands(args.Req("check"), args.Req("action"))));

        // ------------------------------------------------------------------ AI (advanced)
        b.Register("ai.status", async ct =>
        {
            RequireAdmin();
            return new
            {
                gpus = await Gpu.NvidiaAsync(ct),
                llama = await LlamaCpp.StatusAsync(Settings, ct),
                models = Models.List(Settings, Team.Config),
                modelsDir = Models.Dir(Settings),
            };
        });
        b.Register("ai.llama.install", () =>
        {
            RequireAdmin();
            return Jobs.Start("llama", "llama", L.T("ai.llama.jobs.install"), LlamaCpp.InstallSteps(Settings, Effective, current =>
            {
                Settings.LlamaCurrent = current;
                Settings.Save();
            })).Snapshot();
        });
        b.Register("ai.llama.select", (args, _) =>
        {
            RequireAdmin();
            Settings.LlamaCurrent = args.Req("name");
            Settings.Save();
            return Task.FromResult<object?>(null);
        });
        b.Register("ai.server.start", (args, _) =>
        {
            RequireAdmin();
            var model = Models.List(Settings, Team.Config).FirstOrDefault(m => m.Id == args.Req("model")) ?? throw new EmberException("modelUnknown");
            LlamaCpp.StartServer(Settings, model.Path);
            return Task.FromResult<object?>(null);
        });
        b.Register("ai.server.stop", () => { LlamaCpp.StopServer(); return null; });
        b.Register("ai.models.download", (args, _) =>
        {
            RequireAdmin();
            var id = args.Req("id");
            var model = Models.All(Settings, Team.Config).FirstOrDefault(m => m.Id == id) ?? throw new EmberException("modelUnknown");
            return Task.FromResult<object?>(Jobs.Start("model:" + id, "model", L.T("ai.models.jobs.download", ("name", model.Name.Length > 0 ? model.Name : id)),
                Models.DownloadSteps(model, Paths.Ensure(Models.Dir(Settings)), Effective)).Snapshot());
        });
        b.Register("ai.models.delete", (args, _) =>
        {
            RequireAdmin();
            var model = Models.List(Settings, Team.Config).FirstOrDefault(m => m.Id == args.Req("id")) ?? throw new EmberException("modelUnknown");
            if (File.Exists(model.Path)) File.Delete(model.Path);
            return Task.FromResult<object?>(null);
        });

        // ------------------------------------------------------------------ updates
        b.Register("update.status", () => Updater.Status);
        b.Register("update.check", async ct => { await Updater.CheckAsync(download: true, ct); return Updater.Status; });
        b.Register("update.applyUi", () => { Shell.ApplyUiUpdate(); return null; });
        b.Register("update.applyExe", () =>
        {
            if (Jobs.AnyRunning) throw new EmberException("jobRunning", ("title", Jobs.List().First(j => j.State == JobState.Running).Title));
            Shell.ApplyExeUpdate();
            return null;
        });

        // ------------------------------------------------------------------ log
        b.Register("log.recent", () => Log.Recent());
        b.Register("log.clear", () => { Log.Clear(); return null; });
        b.Register("log.save", (args, _) =>
        {
            var text = string.Join(Environment.NewLine, Log.Recent().Select(e => e.ToString()));
            var path = Diagnostics.Save(text);
            Mail.ShowInExplorer(path);
            return Task.FromResult<object?>(path);
        });
    }

    /// <summary>Raised when the UI reports that it started (used for the UI rollback check).</summary>
    public event Action<string>? AppReady;

    object SettingsView()
    {
        var t = Team.Config.Proxy;
        return new
        {
            user = Settings,
            team = new
            {
                usePac = t?.UsePac,
                pacUrl = t?.PacUrl,
                upstreamProxy = t?.Upstream,
                internalDomains = t?.InternalDomains is { } d ? string.Join(", ", d) : null,
                port = t?.Port,
            },
            effective = new
            {
                Effective.UsePac,
                Effective.PacUrl,
                Effective.UpstreamProxy,
                internalDomains = string.Join(", ", Effective.InternalDomainList),
                Effective.Port,
                Effective.PxUrl,
                detectedPac = Net.ReadPacFromRegistry(),
            },
            teamStatus = Team.Status,
            paths = new { settings = Paths.SettingsFile, logs = Paths.LogDir, pxConfig = Paths.PxConfigIni, models = Models.Dir(Settings) },
        };
    }

    List<SetupStep> SoftwareSteps(string packageId, string programId, string displayPattern) => new()
    {
        new(L.T("software.steps.trigger"), async (step, ct) =>
        {
            await SoftwareCenter.ExecuteProgramAsync(packageId, programId, ct);
            step.Report(L.T("software.triggered"));
        }),
        new(L.T("software.steps.wait"), async (step, ct) =>
        {
            step.SetProgress(null);
            var deadline = DateTime.UtcNow.AddMinutes(45);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                var (state, lastRun) = await SoftwareCenter.ProgramStateAsync(packageId, programId, ct);
                var key = SoftwareCenter.StateKey(state);
                step.Report(L.T($"software.states.{key}") + (lastRun != null ? $" · {lastRun}" : ""), log: false);
                bool installed = InstalledSoftware.Read().Any(a => InstalledSoftware.Matches(a.Name, displayPattern));
                if (installed || key == "installed")
                {
                    step.Report(L.T("software.states.installed"));
                    return;
                }
                if (key == "error") throw new EmberException("sccmInstallFailed", ("status", lastRun ?? state?.ToString(CultureInfo.InvariantCulture)));
                if (key == "reboot")
                {
                    step.Report(L.T("software.states.reboot"));
                    return;
                }
            }
            throw new EmberException("sccmInstallTimeout");
        }),
    };

    public void Dispose()
    {
        _cts.Cancel();
        _logTimer?.Dispose();
        _pxTimer?.Dispose();
        _updateTimer?.Dispose();
        Log.Written -= OnLog;
        LlamaCpp.StopServer();
        if (PxControl.DebugRunning)
        {
            // Replace the debug px with the normal one, otherwise the tools lose their proxy.
            PxControl.StopDebug();
            try { PxControl.Start(Effective); } catch { }
        }
    }
}

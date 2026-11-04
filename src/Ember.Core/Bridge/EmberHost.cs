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

    public EffectiveSettings Effective => EffectiveSettings.Build(Settings, Team.Config, _detected);

    /// <summary>Company proxy as Windows reports it (detected once at start, see <see cref="DetectProxyAtStartAsync"/>).</summary>
    ProxyDetectionResult? _detected;

    /// <summary>Which modules this account sees: roles in team.json, or a starter key while there is none.</summary>
    public Grant Grant => Permissions.Compute(Team.Config, Team.Status, Settings.UnlockKey, Elevation.AccountName, Access.MyGroups, Access.DevBuild);

    /// <summary>The advanced tabs are visible (the account has one, and Ctrl+E did not hide them).</summary>
    public bool ShowAdvanced => ModuleIds.Advanced.Any(Grant.Has) && !Settings.HideAdvanced;

    /// <summary>DevTools (F12) only for whoever manages the permissions, and not while Ctrl+E hides the advanced tabs.</summary>
    public bool DevToolsAllowed => Grant.Has("access") && !Settings.HideAdvanced;

    /// <summary>
    /// What works without any module: the lock and setup screens (key, team.json path, request mail).
    /// Everything else needs a module (see <see cref="Permissions.ModulesFor"/>).
    /// </summary>
    static readonly HashSet<string> OpenMethods = new(StringComparer.Ordinal)
    {
        "app.info", "app.ready", "app.setAppearance", "app.openExternal", "app.unlock", "app.pickFile",
        "team.reload", "team.setPath", "mail.compose", "mail.preview",
    };

    /// <summary>Loads the team configuration and starts the background timers.</summary>
    public async Task InitializeAsync()
    {
        Log.Cleanup();
        Log.Info(L.T("app.started", ("version", AppInfo.Version), ("mode", AppInfo.Mode), ("language", L.Tag)));
        FixColorScheme();
        await Team.LoadAsync(Settings, _cts.Token);
        Shell.SetDevTools(DevToolsAllowed);
        _ = DetectProxyAtStartAsync();

        _logTimer = new Timer(_ => FlushLog(), null, 150, 150);
        _pxTimer = new Timer(async _ => await PollPxAsync(), null, 500, 4000);
        if (Settings.AutoUpdate && !AppInfo.IsPrerelease)
            _updateTimer = new Timer(async _ => await Updater.CheckAsync(download: true, _cts.Token), null,
                                     TimeSpan.FromSeconds(15), TimeSpan.FromHours(6));
    }

    /// <summary>
    /// Reads PAC address and system proxy and the proxies in the PAC file. They fill the proxy
    /// settings wherever neither the user nor team.json has a value.
    /// </summary>
    async Task DetectProxyAtStartAsync()
    {
        try
        {
            var r = await Net.DetectProxyAsync(_cts.Token);
            _detected = r;
            var d = EffectiveSettings.Detected.From(r);
            if (d.PacUrl == null && d.Upstream == null) Log.Info(L.T("proxy.detect.nothing"));
            else Log.Info(L.T("proxy.detect.atStart", ("pac", d.PacUrl ?? "–"), ("proxy", d.Upstream ?? "–")));
            Bridge.Emit("settings.detected", null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn(L.T("proxy.detect.failed", ("detail", Net.Describe(ex))));
        }
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

    /// <summary>Whether the window frame should be dark for a colour scheme (no choice yet: like Windows).</summary>
    public static bool DarkFrame(string? scheme) => scheme switch
    {
        "dark" => true,
        "light" => false,
        _ => WindowsUsesDarkTheme(),
    };

    /// <summary>Only light or dark: the first start takes what Windows uses and keeps it.</summary>
    void FixColorScheme()
    {
        if (Settings.ColorScheme is "light" or "dark") return;
        Settings.ColorScheme = WindowsUsesDarkTheme() ? "dark" : "light";
        Settings.Save();
    }

    public static bool WindowsUsesDarkTheme()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>After team.json or the key changed: DevTools and the UI follow the new modules.</summary>
    void GrantChanged()
    {
        Shell.SetDevTools(DevToolsAllowed);
        Bridge.Emit("app.info", Info());
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
        windowsLanguage = L.Resolve("auto", System.Globalization.CultureInfo.CurrentUICulture).Language,
        user = Elevation.AccountName,
        computer = Environment.MachineName,
        elevated = Elevation.IsElevated,
        modules = ModuleIds.Known.Where(Grant.Has).ToList(),
        grant = new { Grant.Source, Grant.Roles, Grant.KeyLevel },
        locked = Grant.Locked,
        admin = Grant.Has("access"),
        advanced = ModuleIds.Advanced.Any(Grant.Has),
        showAdvanced = ShowAdvanced,
        access = Grant.Any,
        accessContact = Team.Config.Access?.Contact ?? Team.Config.ServiceCenter?.Email,
        appearance = new { scheme = Settings.ColorScheme, design = Settings.Design },
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
        b.Guard = method => OpenMethods.Contains(method) || Permissions.MayCall(Grant, method);

        // ------------------------------------------------------------------ app
        b.Register("app.info", Info);
        // Colour scheme and design (switch in the sidebar). The window frame follows.
        b.Register("app.setAppearance", (args, _) =>
        {
            var scheme = args.OptStr("scheme") is "light" or "dark" ? args.OptStr("scheme")! : Settings.ColorScheme!;
            var design = args.OptStr("design") == "glut" ? "glut" : "efeu";
            if (scheme != Settings.ColorScheme || design != Settings.Design)
            {
                Settings.ColorScheme = scheme;
                Settings.Design = design;
                Settings.Save();
            }
            Shell.SetDarkFrame(DarkFrame(scheme));
            return Task.FromResult<object?>(null);
        });
        // Ctrl+E: whoever has one of the advanced tabs (KI, Energie, Berechtigungen) shows or hides them.
        b.Register("app.toggleAdvanced", () =>
        {
            if (!ModuleIds.Advanced.Any(Grant.Has)) throw new EmberException("moduleNotAllowed");
            Settings.HideAdvanced = !Settings.HideAdvanced;
            Settings.Save();
            Shell.SetDevTools(DevToolsAllowed);
            Bridge.Emit("app.info", Info());
            return ShowAdvanced;
        });
        // Starter key (only counts while there is no team.json).
        b.Register("app.unlock", (args, _) =>
        {
            var key = args.Req("key");
            var level = StarterKeys.Level(key) ?? throw new EmberException("keyInvalid");
            Settings.UnlockKey = key.Trim();
            Settings.Save();
            Log.Info(L.T("access.unlocked", ("level", L.T($"access.keyLevels.{level}"))));
            GrantChanged();
            return Task.FromResult<object?>(Info());
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
                "playwright" => Playwright.BrowsersDir,
                "playwrightProject" => Settings.PlaywrightProject ?? throw new EmberException("folderMissing", ("path", "–")),
                "root" => Paths.Root,
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
            if (teamChanged) Shell.SetDevTools(DevToolsAllowed);
            Log.Info(L.T("settings.saved", ("path", Paths.SettingsFile)));
            if (languageChanged || teamChanged) Bridge.Emit("app.info", Info());
            return SettingsView();
        });
        b.Register("settings.defaults", () => new UserSettings());
        b.Register("team.status", () => new { status = Team.Status, name = Team.Config.Name });
        b.Register("team.reload", async ct =>
        {
            await Team.LoadAsync(Settings, ct);
            GrantChanged();
            return Grant.Has("settings") ? SettingsView() : null;
        });
        // Setup screen and start page: point Ember at a team.json. Without one anybody may (that is
        // how a new machine gets started); once one is loaded only the settings module may change it.
        b.Register("team.setPath", async (args, ct) =>
        {
            if (Team.Status.LoadedAt != null && !Grant.Has("settings")) throw new EmberException("accessDenied");
            var path = args.Req("path").Trim().Trim('"');
            await TeamConfigService.TryReadAsync(path, ct);   // throws with the reason when the file is unreadable
            Settings.TeamConfigPath = path;
            Settings.Save();
            await Team.LoadAsync(Settings, ct);
            GrantChanged();
            return Info();
        });

        // ------------------------------------------------------------------ access (admins)
        b.Register("access.get", async ct => await Access.ViewAsync(Team.Config, Team.Status, ct));
        b.Register("access.lookup", (args, _) => Task.FromResult<object?>(Access.Lookup(args.Req("name"))));
        b.Register("access.save", async (args, ct) =>
        {
            var change = args.Get<AccessChange>("change") ?? throw new EmberException("missingParam", ("name", "change"));
            var path = Team.Status.Path ?? throw new EmberException("accessNoTeamFile");
            if (Team.Status.FromCache) throw new EmberException("teamOffline", ("path", path));
            await Task.Run(() => Access.Save(path, change, Elevation.AccountName, Access.MyGroups), ct).WaitAsync(TimeSpan.FromSeconds(20), ct);
            await Team.LoadAsync(Settings, ct);
            GrantChanged();
            return await Access.ViewAsync(Team.Config, Team.Status, ct);
        });

        // ------------------------------------------------------------------ proxy & px
        b.Register("proxy.detect", async ct => await Net.DetectProxyAsync(ct));
        b.Register("proxy.diagnose", async ct => await ProxyTools.DiagnoseAsync(Effective, ct));
        b.Register("git.proxy", async ct => await ProxyTools.GitAsync(Effective, ct));
        b.Register("proxy.set", async (args, ct) =>
        {
            var tool = args.Req("tool");
            // Pointing a tool at px while px is missing or stopped would cut it off the network.
            if (tool != "nodeCerts" && await PxControl.StatusAsync(Effective) is not { Installed: true, PortOpen: true })
                throw new EmberException("pxNotRunning");
            return await ProxyTools.SetAsync(tool, Effective, ct);
        });
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
        // Developer tools: Software Center package if IT deploys one, else straight from the vendor.
        b.Register("devtools.latest", async (args, ct) => (await DevTools.LatestAsync(args.Req("id"), Effective, Team.Config, ct)).Version);
        b.Register("devtools.install", (args, _) =>
        {
            var id = args.Req("id");
            var steps = DevToolSteps(id, args.OptStr("packageId"), args.OptStr("programId"), null);
            return Task.FromResult<object?>(Jobs.Start("devtool:" + id, "devtool", L.T("devtools.jobs.install", ("name", DevTools.NameOf(id))), steps).Snapshot());
        });
        b.Register("devtools.installMany", (args, _) =>
        {
            var tools = args.Get<List<DevToolRequest>>("tools") ?? new List<DevToolRequest>();
            if (tools.Count == 0) throw new EmberException("devToolNothingToDo");
            var steps = tools.SelectMany(t => DevToolSteps(t.Id, t.PackageId, t.ProgramId, DevTools.NameOf(t.Id))).ToList();
            return Task.FromResult<object?>(Jobs.Start("devtools", "devtool", L.T("devtools.jobs.installMany", ("count", tools.Count)), steps).Snapshot());
        });
        // Playwright browsers for one project (versions follow the project's Playwright package).
        b.Register("playwright.state", () => Playwright.State(Team.Config, Settings.PlaywrightProject));
        b.Register("playwright.inspect", (args, _) =>
        {
            var dir = args.Req("dir");
            if (!Directory.Exists(dir)) throw new EmberException("folderMissing", ("path", dir));
            Settings.PlaywrightProject = dir;
            Settings.Save();
            return Task.FromResult<object?>(Playwright.State(Team.Config, dir));
        });
        b.Register("playwright.install", (args, _) =>
        {
            var dir = args.Req("dir");
            if (!Directory.Exists(dir)) throw new EmberException("folderMissing", ("path", dir));
            return Task.FromResult<object?>(Jobs.Start("playwright", "playwright", L.T("playwright.jobs.install", ("path", dir)),
                Playwright.InstallSteps(dir, args.OptStr("browsers") ?? "chromium", args.Flag("add"), Effective, Team.Config)).Snapshot());
        });
        // A new project from the Ember template; name and folder are checked before the job starts.
        b.Register("playwright.create", (args, _) =>
        {
            var parent = args.Req("parent");
            var name = args.Req("name").Trim();
            if (!PlaywrightTemplate.ValidName(name)) throw new EmberException("playwrightNameInvalid", ("name", name));
            if (!Directory.Exists(parent)) throw new EmberException("folderMissing", ("path", parent));
            var dir = Path.Combine(parent, name);
            if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any()) throw new EmberException("playwrightFolderNotEmpty", ("path", dir));
            Settings.PlaywrightProject = dir;
            Settings.Save();
            return Task.FromResult<object?>(Jobs.Start("playwright", "playwright", L.T("playwright.jobs.create", ("name", name)),
                Playwright.CreateSteps(parent, name, args.OptStr("browsers") ?? "chromium", Effective, Team.Config)).Snapshot());
        });
        // When the browser download is blocked: run the tests in the Edge Windows already has.
        b.Register("playwright.useEdge", (args, _) =>
        {
            if (Playwright.EdgePath == null) throw new EmberException("playwrightNoEdge");
            var setup = PlaywrightTemplate.UseEdge(args.Req("dir"));
            Log.Info(L.T(setup.Created ? "playwright.edgeCreated" : "playwright.edgeExisting", ("file", setup.File), ("command", setup.Command)), "playwright");
            return Task.FromResult<object?>(setup);
        });
        b.Register("playwright.openInCode", (args, _) =>
        {
            var dir = args.Req("dir");
            var code = Tools.VsCodeExe ?? throw new EmberException("toolMissing", ("tool", "VS Code"));
            if (!Directory.Exists(dir)) throw new EmberException("folderMissing", ("path", dir));
            var psi = new ProcessStartInfo(code) { UseShellExecute = false, WorkingDirectory = dir };
            psi.ArgumentList.Add(dir);
            Process.Start(psi)?.Dispose();
            return Task.FromResult<object?>(null);
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
            var template = args.Req("template");
            var (subject, body) = Mail.Render(Team.Config, template, args.Dict("vars"));
            return Task.FromResult<object?>(new { to = Mail.Recipient(Team.Config, template), subject, body });
        });
        b.Register("diagnostics.report", async ct =>
        {
            var text = await Diagnostics.BuildAsync(Effective, Team.Status, ct);
            return new { text, path = Diagnostics.Save(text) };
        });

        // ------------------------------------------------------------------ power
        b.Register("power.state", async ct => await PowerProfiles.GetStateAsync(Team.Config, ct));
        b.Register("power.apply", async (args, ct) => await PowerProfiles.ChangeAsync(Team.Config, args.Req("profile"), PowerProfiles.Mode.Apply, ct));
        b.Register("power.defaults", async (args, ct) => await PowerProfiles.ChangeAsync(Team.Config, args.Req("profile"), PowerProfiles.Mode.Defaults, ct));
        b.Register("power.restore", async (args, ct) => await PowerProfiles.ChangeAsync(Team.Config, args.Req("profile"), PowerProfiles.Mode.Backup, ct));

        // ------------------------------------------------------------------ wake-on-LAN for this machine (KI tab)
        b.Register("wol.local", async ct => await WolTarget.GetStateAsync(ct));
        b.Register("wol.apply", async ct => { var errors = await WolTarget.ApplyAsync(ct); return new { errors, state = await WolTarget.GetStateAsync(ct) }; });
        b.Register("wol.reset", async ct => { var errors = await WolTarget.ResetAsync(ct); return new { errors, state = await WolTarget.GetStateAsync(ct) }; });

        // ------------------------------------------------------------------ system
        b.Register("system.checks", async ct => await SystemChecks.RunAsync(ct));
        b.Register("system.list", () => SystemChecks.Pending());
        b.Register("system.check", async (args, ct) => await SystemChecks.RunOneAsync(args.Req("id"), ct));
        b.Register("system.action", async (args, ct) => await SystemChecks.RunActionAsync(args.Req("check"), args.Req("action"), ct));
        b.Register("system.adminCommands", (args, _) =>
            Task.FromResult<object?>(SystemChecks.AdminCommands(args.Req("check"), args.Req("action"))));

        // ------------------------------------------------------------------ AI
        b.Register("ai.status", async ct =>
        {
            var models = Models.List(Settings, Team.Config);
            var profile = LlamaCpp.Profile(Settings.LlamaProfile);
            var llama = await LlamaCpp.StatusAsync(Settings, ct);
            // Pi follows the running server, otherwise the model that would start.
            var alias = llama.ServerAlias ?? models.FirstOrDefault(m => m.Downloaded)?.Alias ?? Models.Qwen.Alias!;
            return new
            {
                gpus = await Gpu.NvidiaAsync(ct),
                llama,
                models,
                modelsDir = Models.Dir(Settings),
                profiles = LlamaCpp.Profiles,
                profile = profile.Id,
                pi = PiAgent.State(alias, llama.ServerContext ?? profile.Context, Settings.LlamaPort),
            };
        });
        b.Register("ai.llama.install", () =>
        {
            return Jobs.Start("llama", "llama", L.T("ai.llama.jobs.install"), LlamaCpp.InstallSteps(Settings, Effective, current =>
            {
                Settings.LlamaCurrent = current;
                Settings.Save();
            })).Snapshot();
        });
        b.Register("ai.llama.select", (args, _) =>
        {
            Settings.LlamaCurrent = args.Req("name");
            Settings.Save();
            return Task.FromResult<object?>(null);
        });
        b.Register("ai.server.start", (args, _) =>
        {
            var model = Models.List(Settings, Team.Config).FirstOrDefault(m => m.Id == args.Req("model")) ?? throw new EmberException("modelUnknown");
            if (args.OptStr("profile") is { } profile && LlamaCpp.Profiles.Any(p => p.Id == profile) && profile != Settings.LlamaProfile)
            {
                Settings.LlamaProfile = profile;
                Settings.Save();
            }
            LlamaCpp.StartServer(Settings, model);
            return Task.FromResult<object?>(null);
        });
        b.Register("ai.server.stop", () => { LlamaCpp.StopServer(); return null; });
        b.Register("ai.models.download", (args, _) =>
        {
            var id = args.Req("id");
            var model = Models.All(Settings, Team.Config).Select(x => x.Entry).FirstOrDefault(m => m.Id == id) ?? throw new EmberException("modelUnknown");
            if (!string.IsNullOrWhiteSpace(model.Path)) throw new EmberException("modelLocal", ("path", model.Path));
            return Task.FromResult<object?>(Jobs.Start("model:" + id, "model", L.T("ai.models.jobs.download", ("name", model.Name.Length > 0 ? model.Name : id)),
                Models.DownloadSteps(model, Paths.Ensure(Models.Dir(Settings)), Effective)).Snapshot());
        });
        // A GGUF that is already on this PC (or a share): chosen in a file dialog, nothing is downloaded.
        b.Register("ai.models.addLocal", async (args, _) =>
        {
            var file = args.OptStr("path") ?? await Shell.PickFileAsync(L.T("ai.models.pickLocal"), "GGUF (*.gguf)|*.gguf");
            if (file == null) return null;
            var entry = Models.AddLocal(Settings, file);
            Settings.Save();
            return entry.Id;
        });
        // Downloaded models: the file goes. Local ones (picked on this PC): only the entry, never the file.
        b.Register("ai.models.delete", (args, _) =>
        {
            var model = Models.List(Settings, Team.Config).FirstOrDefault(m => m.Id == args.Req("id")) ?? throw new EmberException("modelUnknown");
            if (model.Local)
            {
                if (!model.Mine) throw new EmberException("modelFromTeam");
                Settings.Models.RemoveAll(m => m.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase));
                Settings.Save();
            }
            else if (File.Exists(model.Path))
            {
                File.Delete(model.Path);
            }
            return Task.FromResult<object?>(null);
        });
        b.Register("ai.pi.install", () => Jobs.Start("pi", "pi", L.T("ai.pi.jobs.install"), PiAgent.InstallSteps(Effective, Team.Config)).Snapshot());
        b.Register("ai.pi.configure", async ct =>
        {
            var llama = await LlamaCpp.StatusAsync(Settings, ct);
            var models = Models.List(Settings, Team.Config);
            var alias = llama.ServerAlias ?? models.FirstOrDefault(m => m.Downloaded)?.Alias ?? Models.Qwen.Alias!;
            var context = llama.ServerContext ?? LlamaCpp.Profile(Settings.LlamaProfile).Context;
            return PiAgent.Configure(alias, context, Settings.LlamaPort);
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
                usePac = (bool?)null,
                pacUrl = (string?)null,
                upstreamProxy = (string?)null,
                internalDomains = t?.InternalDomains is { } d ? string.Join(", ", d) : null,
                port = t?.Port,
            },
            detected = new
            {
                usePac = EffectiveSettings.Detected.From(_detected).UsePac,
                pacUrl = EffectiveSettings.Detected.From(_detected).PacUrl,
                upstreamProxy = EffectiveSettings.Detected.From(_detected).Upstream,
                internalDomains = EffectiveSettings.Detected.From(_detected).Domains is { } found ? string.Join(", ", found) : null,
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

    List<SetupStep> SoftwareSteps(string packageId, string programId, string displayPattern, string? titlePrefix = null) => new()
    {
        new((titlePrefix != null ? titlePrefix + ": " : "") + L.T("software.steps.trigger"), async (step, ct) =>
        {
            await SoftwareCenter.ExecuteProgramAsync(packageId, programId, ct);
            step.Report(L.T("software.triggered"));
        }),
        new((titlePrefix != null ? titlePrefix + ": " : "") + L.T("software.steps.wait"), async (step, ct) =>
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

    sealed record DevToolRequest(string Id, string? PackageId, string? ProgramId);

    List<SetupStep> DevToolSteps(string id, string? packageId, string? programId, string? titlePrefix) =>
        packageId is { Length: > 0 } && programId is { Length: > 0 }
            ? SoftwareSteps(packageId, programId, DevTools.DisplayPattern(id), titlePrefix)
            : DevTools.DirectSteps(id, Effective, Team.Config, titlePrefix);

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

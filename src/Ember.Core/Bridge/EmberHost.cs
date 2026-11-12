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
    Timer? _logTimer, _pxTimer, _updateTimer, _teamTimer;
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

    /// <summary>Which modules this account sees: its roles in the (signed) team file.</summary>
    public Grant RealGrant => Permissions.Compute(Team.Config, Team.Status, Elevation.AccountName, Access.MyGroups, Access.DevBuild);

    /// <summary>
    /// What the window shows and the bridge allows: the account's own modules, or, while an admin
    /// previews another role (see app.preview), that role's. Not saved: a restart ends the preview.
    /// </summary>
    public Grant Grant => _preview != null && RealGrant.Has("access") ? Permissions.Preview(Team.Config, _preview) ?? RealGrant : RealGrant;

    /// <summary>"role:&lt;name&gt;", "key:&lt;level&gt;" or "none" while an admin previews; null otherwise.</summary>
    string? _preview;

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
        "app.info", "app.ready", "app.setAppearance", "app.openExternal", "app.pickFile", "app.preview",
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
        _ = RefreshDisplayNameAsync();

        _logTimer = new Timer(_ => FlushLog(), null, 150, 150);
        _pxTimer = new Timer(async _ => await PollPxAsync(), null, 500, 4000);
        // A rotated key or a changed role should not wait for the next start.
        _teamTimer = new Timer(async _ => await ReloadTeamIfChangedAsync(), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
        if (Settings.AutoUpdate && !AppInfo.IsPrerelease)
            _updateTimer = new Timer(async _ => await Updater.CheckAsync(download: true, _cts.Token), null,
                                     TimeSpan.FromSeconds(15), TimeSpan.FromHours(6));
    }

    /// <summary>Reads team.json again when it changed on the share; the window follows when the access changed.</summary>
    async Task ReloadTeamIfChangedAsync()
    {
        try
        {
            if (!await Team.ChangedOnDiskAsync(_cts.Token)) return;
            var before = Grant;
            await Team.LoadAsync(Settings, _cts.Token);
            var after = Grant;
            if (!before.Modules.SetEquals(after.Modules) || before.Locked != after.Locked || !before.Roles.SequenceEqual(after.Roles))
                GrantChanged();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug("team reload: " + ex.Message);
        }
    }

    /// <summary>
    /// "Vorname Nachname" for the greeting: the copy from the last start right away, then the
    /// directory once more; the window only hears about it if the name changed.
    /// </summary>
    async Task RefreshDisplayNameAsync()
    {
        try
        {
            var name = await GitIdentity.SenderAsync(_cts.Token);
            if (string.IsNullOrWhiteSpace(name) || name == Environment.UserName || name == Settings.DisplayName) return;
            Settings.DisplayName = name;
            Settings.Save();
            Bridge.Emit("app.info", Info());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug("display name: " + ex.Message);
        }
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
        displayName = Settings.DisplayName ?? GitIdentity.NameFromAccount(Environment.UserName),
        computer = Environment.MachineName,
        elevated = Elevation.IsElevated,
        modules = ModuleIds.Known.Where(Grant.Has).ToList(),
        grant = new { Grant.Source, Grant.Roles },
        // An admin can look at Ember through any role or nobody's (and back), see app.preview.
        preview = RealGrant.Has("access") ? new
        {
            current = _preview,
            roles = Permissions.Roles(Team.Config).Select(r => r.Name).Where(n => n.Length > 0).Distinct().ToList(),
        } : null,
        locked = Grant.Locked,
        admin = Grant.Has("access"),
        advanced = ModuleIds.Advanced.Any(Grant.Has),
        showAdvanced = ShowAdvanced,
        access = Grant.Any,
        appearance = new { scheme = Settings.ColorScheme, design = Settings.Design },
        root = Paths.Root,
        logDir = Paths.LogDir,
        team = Team.Status,
        teamName = Team.Config.Name,
        serviceCenter = string.IsNullOrWhiteSpace(Team.Config.ServiceCenter?.Email) ? null : Team.Config.ServiceCenter!.Email,
        supervisor = string.IsNullOrWhiteSpace(Team.Config.Supervisor?.Email) ? null : new { Team.Config.Supervisor!.Name, Team.Config.Supervisor.Email },
        repo = "https://github.com/" + AppInfo.GitHubRepo,
        docs = "https://pixelshober.github.io/Ember/",
    };

    /// <summary>The placeholders from the dialog plus the sender's full name for the closing line.</summary>
    static async Task<Dictionary<string, string?>> MailVars(JsonElement args, CancellationToken ct)
    {
        var vars = new Dictionary<string, string?>(args.Dict("vars"), StringComparer.OrdinalIgnoreCase)
        {
            ["sender"] = await GitIdentity.SenderAsync(ct),
        };
        return vars;
    }

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
        // Admins only: see Ember as a role, a key level or nobody would; null ends it. The real
        // account is checked, so the way back stays open while the preview hides Berechtigungen.
        b.Register("app.preview", (args, _) =>
        {
            if (!RealGrant.Has("access")) throw new EmberException("accessDenied");
            var target = args.OptStr("target");
            if (target != null && Permissions.Preview(Team.Config, target) == null) throw new EmberException("previewUnknown", ("target", target));
            _preview = target;
            Log.Info(target == null ? L.T("app.previewEnd") : L.T("app.previewLog", ("target", target)));
            Shell.SetDevTools(DevToolsAllowed);
            var info = Info();
            Bridge.Emit("app.info", info);
            return Task.FromResult<object?>(info);
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
                "playwrightTemplate" => PlaywrightTemplates.Location(Team.Config, Settings).Path ?? throw new EmberException("folderMissing", ("path", "–")),
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
            next.LlamaProfileChosen |= Settings.LlamaProfileChosen;
            next.DisplayName ??= Settings.DisplayName;
            // Only Ember itself writes these (team file versions, signing key path).
            next.TeamVersions = Settings.TeamVersions;
            next.TeamKeyFile ??= Settings.TeamKeyFile;
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
        b.Register("access.lookup", (args, _) => Task.FromResult<object?>(Access.Lookup(args.Req("name"))));
        b.Register("access.search", async (args, ct) =>
        {
            try
            {
                return await Access.SearchAsync(args.Req("query"), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Debug("directory search: " + ex.Message);   // no domain, no VPN: no suggestions, typing still works
                return Array.Empty<DirectoryHit>();
            }
        });
        // The team file: only here can it change, and only signed with the admins' key.
        b.Register("team.admin", async ct => await TeamAdmin.ViewAsync(Team, Settings, ct));
        b.Register("team.save", async (args, ct) =>
        {
            var doc = args.Get<TeamDoc>("doc") ?? throw new EmberException("missingParam", ("name", "doc"));
            var version = args.TryGetProperty("version", out var v) && v.TryGetInt32(out var n) ? n : throw new EmberException("missingParam", ("name", "version"));
            var keyFile = args.Req("keyFile");
            var password = args.Req("password");
            await Task.Run(() => TeamAdmin.Save(Team, Settings, doc, args.OptStr("extra") ?? "", version, keyFile, password, Elevation.AccountName, Access.MyGroups), ct)
                      .WaitAsync(TimeSpan.FromSeconds(60), ct);
            await Team.LoadAsync(Settings, ct);
            GrantChanged();
            return await TeamAdmin.ViewAsync(Team, Settings, ct);
        });
        b.Register("team.newKey", (args, _) =>
            Task.FromResult<object?>(TeamAdmin.NewKey(args.Req("folder"), args.Req("password"), Settings)));

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
        b.Register("software.list", async ct =>
        {
            var list = await SoftwareCenter.ListAsync(Team.Config, ct);
            // Tools the account's roles do not offer (e.g. Bruno for developers) are not shown at all.
            return list with { DevTools = list.DevTools.Where(d => !Grant.Hides(d.Id)).ToList() };
        });
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
            if (Grant.Hides(id)) throw new EmberException("accessDenied");
            var steps = DevToolSteps(id, args.OptStr("packageId"), args.OptStr("programId"), null);
            return Task.FromResult<object?>(Jobs.Start("devtool:" + id, "devtool", L.T("devtools.jobs.install", ("name", DevTools.NameOf(id))), steps).Snapshot());
        });
        b.Register("devtools.installMany", (args, _) =>
        {
            var tools = args.Get<List<DevToolRequest>>("tools") ?? new List<DevToolRequest>();
            tools = tools.Where(t => !Grant.Hides(t.Id)).ToList();
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
            // "builtin": Ember's own template even when a team template exists.
            var template = args.OptStr("template") == "builtin" ? null : PlaywrightTemplates.Location(Team.Config, Settings).Path;
            return Task.FromResult<object?>(Jobs.Start("playwright", "playwright", L.T("playwright.jobs.create", ("name", name)),
                Playwright.CreateSteps(parent, name, args.OptStr("browsers") ?? "chromium", template, Effective, Team.Config)).Snapshot());
        });
        // ---- project templates (Visual Studio style): which one, preview of a new one, replace it
        b.Register("playwright.template", async ct => await PlaywrightTemplates.InfoAsync(Team.Config, Settings, ct));
        b.Register("playwright.template.use", async (args, ct) =>
        {
            // Only while team.json names no template; null goes back to Ember's built-in one.
            if (!string.IsNullOrWhiteSpace(Team.Config.Playwright?.Template)) throw new EmberException("playwrightTemplateFromTeam");
            Settings.PlaywrightTemplate = args.OptStr("path")?.Trim() ?? "";
            Settings.Save();
            return await PlaywrightTemplates.InfoAsync(Team.Config, Settings, ct);
        });
        b.Register("playwright.template.preview", async (args, ct) =>
        {
            var dir = args.Req("dir");
            return await Task.Run(() => PlaywrightTemplates.Preview(dir), ct).WaitAsync(TimeSpan.FromSeconds(30), ct);
        });
        b.Register("playwright.template.save", async (args, ct) =>
        {
            var dir = args.Req("dir");
            var keep = args.Get<List<string>>("keep") ?? new List<string>();
            var (target, _) = PlaywrightTemplates.Location(Team.Config, Settings);
            target = args.OptStr("target") is { Length: > 0 } chosen && string.IsNullOrWhiteSpace(Team.Config.Playwright?.Template) ? chosen : target;
            if (target == null) throw new EmberException("playwrightTemplateNoTarget");
            if (Path.GetFullPath(target).TrimEnd('\\').Equals(Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new EmberException("playwrightTemplateSameFolder");
            var count = await Task.Run(() => PlaywrightTemplates.Save(dir, target, keep, args.OptStr("name"), args.OptStr("description"), Elevation.AccountName), ct)
                                  .WaitAsync(TimeSpan.FromMinutes(2), ct);
            if (string.IsNullOrWhiteSpace(Team.Config.Playwright?.Template) && Settings.PlaywrightTemplate != target)
            {
                Settings.PlaywrightTemplate = target;
                Settings.Save();
            }
            return new { count, template = await PlaywrightTemplates.InfoAsync(Team.Config, Settings, ct) };
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
        // The log and the diagnosis report never go along (see Mail).
        b.Register("mail.compose", async (args, ct) =>
            await Mail.ComposeAsync(Team.Config, args.Req("template"), await MailVars(args, ct), ct));
        b.Register("mail.preview", async (args, ct) =>
        {
            var template = args.Req("template");
            var (subject, body) = Mail.Render(Team.Config, template, await MailVars(args, ct));
            return new { to = Mail.Recipient(Team.Config, template), subject, body };
        });
        b.Register("diagnostics.report", async ct =>
        {
            var text = await Diagnostics.BuildAsync(Effective, Team.Status, ct);
            var path = Diagnostics.Save(text);
            Mail.ShowInExplorer(path);
            return new { text, path };
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
            var model = models.FirstOrDefault(m => m.Alias == llama.ServerAlias) ?? models.FirstOrDefault(m => m.Downloaded);
            var alias = llama.ServerAlias ?? model?.Alias ?? Models.Qwen.Alias!;
            return new
            {
                gpus = await Gpu.NvidiaAsync(ct),
                llama,
                models,
                modelsDir = Models.Dir(Settings),
                profiles = LlamaCpp.Profiles,
                profile = profile.Id,
                pi = PiAgent.State(alias, llama.ServerContext ?? profile.Context, Settings.LlamaPort, model?.MmprojPresent ?? false),
            };
        });
        b.Register("ai.llama.latest", async ct =>
        {
            try
            {
                return new { tag = await LlamaCpp.LatestAsync(Settings, Effective, ct) };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Debug("llama latest: " + Net.Describe(ex));
                return new { tag = (string?)null };   // offline: the button stays neutral
            }
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
            var name = args.Req("name");
            Settings.LlamaCurrent = name.StartsWith("custom", StringComparison.Ordinal) ? "custom" : name;
            Settings.Save();
            return Task.FromResult<object?>(null);
        });
        b.Register("ai.server.start", async (args, ct) =>
        {
            var model = Models.List(Settings, Team.Config).FirstOrDefault(m => m.Id == args.Req("model")) ?? throw new EmberException("modelUnknown");
            if (args.OptStr("profile") is { } profile && LlamaCpp.Profiles.Any(p => p.Id == profile)
                && (profile != Settings.LlamaProfile || !Settings.LlamaProfileChosen))
            {
                Settings.LlamaProfile = profile;
                Settings.LlamaProfileChosen = true;
                Settings.Save();
            }
            await LlamaCpp.CheckBeforeStartAsync(Settings, ct);
            LlamaCpp.StartServer(Settings, model);
            return null;
        });
        // A llama.cpp folder that already works (e.g. C:\llama): Ember starts that build instead of its own.
        b.Register("ai.llama.useFolder", async (args, _) =>
        {
            var dir = args.OptStr("path") ?? await Shell.PickFolderAsync(L.T("ai.llama.pickFolder"), null);
            if (dir == null) return null;
            if (!File.Exists(Path.Combine(dir, "llama-server.exe"))) throw new EmberException("llamaNoServerIn", ("path", dir));
            Settings.LlamaCustomDir = dir;
            Settings.LlamaCurrent = "custom";
            Settings.Save();
            return dir;
        });
        b.Register("ai.server.stop", () => { LlamaCpp.StopServer(); return null; });
        b.Register("ai.models.download", (args, _) =>
        {
            var id = args.Req("id");
            var model = Models.All(Settings, Team.Config).Select(x => x.Entry).FirstOrDefault(m => m.Id == id) ?? throw new EmberException("modelUnknown");
            if (!string.IsNullOrWhiteSpace(model.Path)) throw new EmberException("modelLocal", ("path", model.Path));
            var state = Models.List(Settings, Team.Config).FirstOrDefault(m => m.Id == id);
            return Task.FromResult<object?>(Jobs.Start("model:" + id, "model", L.T("ai.models.jobs.download", ("name", model.Name.Length > 0 ? model.Name : id)),
                Models.DownloadSteps(model, Paths.Ensure(Models.Dir(Settings)), Effective, state)).Snapshot());
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
            var model = models.FirstOrDefault(m => m.Alias == llama.ServerAlias) ?? models.FirstOrDefault(m => m.Downloaded);
            var alias = llama.ServerAlias ?? model?.Alias ?? Models.Qwen.Alias!;
            var context = llama.ServerContext ?? LlamaCpp.Profile(Settings.LlamaProfile).Context;
            var file = PiAgent.Configure(alias, context, Settings.LlamaPort, model?.MmprojPresent ?? false);
            if (PiAgent.InstalledVersion(PiAgent.WebSearchPackage) != null) PiAgent.SetWebSearchProxy(Effective.PxUrl);
            return file;
        });
        // Pi itself and its extensions: installed version, newest on npm, update available.
        b.Register("ai.pi.packages", async ct =>
        {
            var (pi, packages) = await PiAgent.PackagesAsync(Effective, Team.Config, ct);
            return new { pi, packages };
        });
        b.Register("ai.pi.package", (args, _) =>
        {
            var id = args.Req("id");
            var package = PiAgent.Packages(Team.Config).FirstOrDefault(p => p.Id == id) ?? throw new EmberException("unknownAction", ("action", id));
            return Task.FromResult<object?>(Jobs.Start("pi:" + id, "pi", L.T("ai.pi.jobs.package", ("name", package.Npm)),
                PiAgent.PackageSteps(package, args.Flag("update"), Effective, Team.Config)).Snapshot());
        });
        b.Register("ai.pi.update", () => Jobs.Start("pi", "pi", L.T("ai.pi.jobs.update"), PiAgent.UpdateSteps(Effective, Team.Config)).Snapshot());

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
        _teamTimer?.Dispose();
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

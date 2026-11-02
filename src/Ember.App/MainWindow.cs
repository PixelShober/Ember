using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Ember.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace Ember.App;

/// <summary>
/// The only window: a WebView2 that shows the web UI. Everything the page may do on the system goes
/// through the bridge, and only from Ember's own origin.
/// </summary>
public sealed class MainWindow : Window, IHostShell
{
    const string DevServer = "http://localhost:5173";

    readonly WebView2 _web = new();
    readonly UiHost _ui = new();
    readonly bool _uiDev;
    readonly DispatcherTimer _readyTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    bool _closing;

    public MainWindow(bool uiDev)
    {
        _uiDev = uiDev;
        Host = new EmberHost(this);

        Title = "Ember";
        Width = 1180;
        Height = 780;
        MinWidth = 880;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _dark = EmberHost.DarkFrame(Host.Settings.ColorScheme);
        ApplyFrameColors();
        Content = _web;
        WindowPlacement.Restore(this);

        SourceInitialized += (_, _) =>
        {
            SetTitleBar(_dark);
            InitSeeThrough();
        };
        Activated += (_, _) => CheckSeeThrough();
        Deactivated += (_, _) => CheckSeeThrough();
        _wmTimer.Tick += (_, _) =>
        {
            UpdateSeeThrough();
            if (++_wmTicks >= 12) _wmTimer.Stop();
        };
        Loaded += async (_, _) => await InitializeWebViewAsync();

        Host.Bridge.Outgoing += json => Dispatcher.InvokeAsync(() =>
        {
            if (!_closing) _web.CoreWebView2?.PostWebMessageAsJson(json);
        });
        Host.AppReady += version => Dispatcher.InvokeAsync(() => OnUiReady(version));
        _readyTimer.Tick += (_, _) => OnUiTimeout();
    }

    public EmberHost Host { get; }

    bool IsTrusted(string? url) =>
        url != null && (url.StartsWith(UiHost.Origin + "/", StringComparison.OrdinalIgnoreCase) ||
                        (_uiDev && url.StartsWith(DevServer + "/", StringComparison.OrdinalIgnoreCase)));

    async Task InitializeWebViewAsync()
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions { Language = L.Culture.Name };
            var env = await CoreWebView2Environment.CreateAsync(null, Paths.Ensure(Paths.WebViewDataDir), options);
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            Log.Error("WebView2: " + Net.Describe(ex));
            MessageBox.Show(L.T("webview2.failed", ("detail", Net.Describe(ex))), "Ember", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        var settings = core.Settings;
#if DEBUG
        bool debug = true;
#else
        bool debug = false;
#endif
        settings.AreDevToolsEnabled = debug || Host.DevToolsAllowed;
        settings.AreBrowserAcceleratorKeysEnabled = debug;   // no F5 / Ctrl+P / Ctrl+F in an app
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;

        core.WebMessageReceived += (_, e) =>
        {
            if (!IsTrusted(e.Source)) return;
            var json = e.WebMessageAsJson;
            _ = Task.Run(() => Host.Bridge.HandleAsync(json));
        };
        core.NavigationStarting += (_, e) =>
        {
            if (IsTrusted(e.Uri)) return;
            e.Cancel = true;
            OpenUrl(e.Uri);
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenUrl(e.Uri);
        };
        core.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess) Log.Warn($"UI navigation: {e.WebErrorStatus}");
        };
        core.ContextMenuRequested += (_, e) =>
        {
            // Only clipboard actions (and "Inspect" for developers) – no Back, Reload, Print, Save as.
            var keep = new HashSet<string> { "cut", "copy", "paste", "selectAll", "emoji", "spellCheck", "inspectElement" };
            for (int i = e.MenuItems.Count - 1; i >= 0; i--)
                if (!keep.Contains(e.MenuItems[i].Name) && e.MenuItems[i].Kind != CoreWebView2ContextMenuItemKind.Separator)
                    e.MenuItems.RemoveAt(i);
        };
        core.DocumentTitleChanged += (_, _) => Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "Ember" : core.DocumentTitle;

        if (_uiDev)
        {
            Log.Info("UI dev server: " + DevServer);
            core.Navigate(DevServer + "/");
            return;
        }

        var start = _ui.Prepare();
        if (start == null)
        {
            core.NavigateToString(UiHost.PlaceholderHtml);
            return;
        }
        ShowUi(start.Value.Dir, start.Value.Version);
    }

    void ShowUi(string dir, string version)
    {
        var core = _web.CoreWebView2;
        try { core.ClearVirtualHostNameToFolderMapping(UiHost.HostName); } catch { }
        core.SetVirtualHostNameToFolderMapping(UiHost.HostName, dir, CoreWebView2HostResourceAccessKind.Allow);
        // The query string keeps the browser cache from serving an older index.html.
        core.Navigate($"{UiHost.Origin}/index.html?v={Uri.EscapeDataString(version)}");
        Log.Info(L.T("app.uiShown", ("version", version), ("kind", _ui.CurrentIsEmbedded ? "embedded" : "update")));
        _readyTimer.Stop();
        _readyTimer.Start();
    }

    void OnUiReady(string version)
    {
        _readyTimer.Stop();
        if (!_ui.CurrentIsEmbedded && _ui.CurrentDir != null)
        {
            Host.Updater.UiApplied(_ui.CurrentVersion);
            UiPackages.Cleanup(_ui.CurrentDir);
        }
    }

    /// <summary>A downloaded UI that does not report "ready" in time is marked bad; Ember goes back to the embedded one.</summary>
    void OnUiTimeout()
    {
        _readyTimer.Stop();
        if (_ui.CurrentIsEmbedded) return;
        Log.Warn(L.T("app.uiRollback", ("version", _ui.CurrentVersion)));
        UiPackages.MarkBad(_ui.CurrentVersion);
        if (_ui.FallbackToEmbedded() is { } fallback) ShowUi(fallback.Dir, fallback.Version);
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (Host.Jobs.AnyRunning &&
            MessageBox.Show(L.T("app.confirmClose"), "Ember", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        _closing = true;
        WindowPlacement.Save(this);
        Host.Dispose();
        base.OnClosing(e);
    }

    // ------------------------------------------------------------------ IHostShell

    public Task<string?> PickFolderAsync(string title, string? initial) =>
        Dispatcher.InvokeAsync(() =>
        {
            var dialog = new OpenFolderDialog { Title = title };
            if (initial != null && Directory.Exists(initial)) dialog.InitialDirectory = initial;
            return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
        }).Task;

    public Task<string?> PickFileAsync(string title, string filter) =>
        Dispatcher.InvokeAsync(() =>
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }).Task;

    public void ApplyUiUpdate() => Dispatcher.InvokeAsync(() =>
    {
        if (UiPackages.BestDownloaded(_ui.CurrentVersion) is { } next)
        {
            _ui.Select(next.Dir, next.Manifest.Version);
            ShowUi(next.Dir, next.Manifest.Version);
        }
    });

    public void ApplyExeUpdate() => Dispatcher.InvokeAsync(() =>
    {
        try
        {
            ExeUpdate.Apply();
            Close();
        }
        catch (Exception ex)
        {
            Log.Error(Net.Describe(ex));
        }
    });

    public void SetDevTools(bool enabled) => Dispatcher.InvokeAsync(() =>
    {
        if (_web.CoreWebView2 != null) _web.CoreWebView2.Settings.AreDevToolsEnabled = enabled;
    });

    /// <summary>Opens links outside of Ember – only web, mail and Software Center links.</summary>
    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http" or "mailto" or "softwarecenter"))
        {
            Log.Warn(L.T("app.linkBlocked", ("url", url)));
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn(ex.Message);
        }
    }

    // ------------------------------------------------------------------ theme

    bool _dark;

    /// <summary>Title bar and the background behind the page follow the colour scheme chosen in the UI.</summary>
    public void SetDarkFrame(bool dark) => Dispatcher.InvokeAsync(() =>
    {
        _dark = dark;
        ApplyFrameColors();
        SetTitleBar(dark);
    });

    /// <summary>
    /// Page background of the current design, so resizing never flashes a different colour. While Ember
    /// is see-through, both are transparent so the desktop shows behind the page.
    /// </summary>
    void ApplyFrameColors()
    {
        if (_seeThrough != null)
        {
            Background = Brushes.Transparent;
            _web.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            return;
        }
        var glut = Host.Settings.Design == "glut";
        var c = (_dark, glut) switch
        {
            (true, true) => Color.FromRgb(0x1c, 0x10, 0x11),
            (true, false) => Color.FromRgb(0x12, 0x19, 0x13),
            (false, true) => Color.FromRgb(0xfb, 0xf0, 0xec),
            _ => Color.FromRgb(0xf1, 0xf4, 0xee),
        };
        Background = new SolidColorBrush(c);
        _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
    }

    void SetTitleBar(bool dark)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int on = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
    }

    // ------------------------------------------------------------------ see-through in the background

    /// <summary>Opacity of the page while Ember is see-through; null = opaque.</summary>
    double? _seeThrough;
    readonly DispatcherTimer _wmTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    int _wmTicks;

    /// <summary>
    /// Lets the window's alpha channel through to the desktop: DWM "blur behind" with an empty region
    /// (since Windows 8 it no longer blurs, it only honours the alpha). WPF and WebView2 stay opaque
    /// until Ember is inactive and see-through.
    /// </summary>
    void InitSeeThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        if (source == null) return;
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        source.AddHook(WndProc);
        var region = CreateRectRgn(0, 0, -1, -1);
        var blur = new DwmBlurBehind { Flags = 0x1 | 0x2 /* ENABLE | BLURREGION */, Enable = true, BlurRegion = region };
        DwmEnableBlurBehindWindow(handle, ref blur);
        DeleteObject(region);
    }

    /// <summary>GlazeWM sets the window's alpha after the focus moved: look again a few times.</summary>
    void CheckSeeThrough()
    {
        UpdateSeeThrough();
        _wmTicks = 0;
        _wmTimer.Stop();
        _wmTimer.Start();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_STYLECHANGED = 0x007D, GWL_EXSTYLE = -20;
        if (msg == WM_STYLECHANGED && wParam.ToInt32() == GWL_EXSTYLE) Dispatcher.InvokeAsync(CheckSeeThrough);
        return IntPtr.Zero;
    }

    /// <summary>
    /// Inactive, Ember lets the desktop shine through like the other windows. GlazeWM (as WinMarchy
    /// sets it up) makes inactive windows transparent through the window's layered alpha, which the
    /// WebView2 content ignores; Ember reads that alpha and draws the page with the same opacity.
    /// </summary>
    void UpdateSeeThrough()
    {
        if (_closing) return;
        double? target = IsActive ? null : Host.Settings.BackgroundTransparency switch
        {
            "off" => null,
            "always" => WindowsTransparencyOn() ? 0.9 : null,
            _ => WindowManagerOpacity(),
        };
        if (target == _seeThrough) return;
        _seeThrough = target;
        ApplyFrameColors();
        Host.Bridge.Emit("window.seeThrough", new { opacity = target });
    }

    /// <summary>The alpha a window manager put on this window (WS_EX_LAYERED + LWA_ALPHA), or null.</summary>
    double? WindowManagerOpacity()
    {
        const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000, LWA_ALPHA = 0x2;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || (GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64() & WS_EX_LAYERED) == 0) return null;
        if (!GetLayeredWindowAttributes(handle, out _, out var alpha, out var flags) || (flags & LWA_ALPHA) == 0 || alpha >= 250) return null;
        return Math.Round(Math.Max(alpha, (byte)64) / 255.0, 2);
    }

    /// <summary>Windows → Personalisation → Colours → "Transparency effects".</summary>
    static bool WindowsTransparencyOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int v || v != 0;
        }
        catch
        {
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DwmBlurBehind
    {
        public int Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Enable;
        public IntPtr BlurRegion;
        [MarshalAs(UnmanagedType.Bool)] public bool TransitionOnMaximized;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind blurBehind);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out uint flags);
}

/// <summary>Remembers window size and position in C:\Ember\Config\window.json.</summary>
static class WindowPlacement
{
    sealed record Placement(double Left, double Top, double Width, double Height, bool Maximized);

    static string File => Paths.WindowFile;

    public static void Restore(Window w)
    {
        try
        {
            if (!System.IO.File.Exists(File)) return;
            var p = Json.Deserialize<Placement>(System.IO.File.ReadAllText(File));
            if (p == null || p.Width < 400 || p.Height < 300) return;
            // Only restore positions that are still on a screen (monitors change at the dock).
            var visible = p.Left >= SystemParameters.VirtualScreenLeft - 50 && p.Top >= SystemParameters.VirtualScreenTop - 50 &&
                          p.Left + 100 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                          p.Top + 100 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
            w.Width = p.Width;
            w.Height = p.Height;
            if (visible)
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = p.Left;
                w.Top = p.Top;
            }
            if (p.Maximized) w.WindowState = WindowState.Maximized;
        }
        catch
        {
            // start with defaults
        }
    }

    public static void Save(Window w)
    {
        try
        {
            var r = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.Width, w.Height) : w.RestoreBounds;
            Directory.CreateDirectory(Paths.ConfigDir);
            System.IO.File.WriteAllText(File, Json.Serialize(new Placement(r.Left, r.Top, r.Width, r.Height, w.WindowState == WindowState.Maximized)));
        }
        catch
        {
            // not important
        }
    }
}

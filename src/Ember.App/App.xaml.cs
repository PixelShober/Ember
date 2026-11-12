using System.Windows;
using System.Windows.Threading;
using Ember.Core;

namespace Ember.App;

public partial class App : Application
{
    SingleInstance? _instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved: " + Net.Describe(args.Exception));
            args.SetObserved();
        };

        Paths.MigrateOldLocations();
        L.Configure(UserSettings.Load().Language);
        var args = e.Args;
        bool afterUpdate = args.Contains(ExeUpdate.AfterUpdateSwitch, StringComparer.OrdinalIgnoreCase);

        // Always run from C:\Ember\App: copy there if needed and restart from there.
        if (SelfInstaller.EnsureInstalled(args) == SelfInstaller.Result.Relaunched)
        {
            Shutdown();
            return;
        }

        // Only one Ember at a time; a second start brings the running window to the front.
        // After an update the old instance may still be closing, so wait a moment.
        _instance = SingleInstance.Acquire(waitSeconds: afterUpdate ? 15 : 0);
        if (_instance == null)
        {
            Shutdown();
            return;
        }
        if (afterUpdate) ExeUpdate.CleanupOld();

        var window = new MainWindow(args.Contains("--ui-dev", StringComparer.OrdinalIgnoreCase));
        _instance.Activated += () => Dispatcher.InvokeAsync(window.BringToFront);

        // WebView2 must exist before the web UI can start; the bootstrap window downloads it if needed.
        await window.Host.InitializeAsync();
        if (!await WebView2Bootstrap.EnsureAsync(window.Host))
        {
            window.Host.Dispose();
            Shutdown(1);
            return;
        }

        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(L.T("app.unexpectedError") + ": " + Net.Describe(e.Exception));
        Log.Debug(e.Exception.ToString());
        MessageBox.Show(Net.Describe(e.Exception), L.T("app.unexpectedError"), MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

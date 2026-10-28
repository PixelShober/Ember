using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ember.Core;
using Microsoft.Web.WebView2.Core;

namespace Ember.App;

/// <summary>
/// Makes sure the WebView2 runtime exists before the web UI starts. If it is missing, Ember
/// downloads the standalone installer with .NET (px → company proxy with Windows login →
/// system proxy) – not with PowerShell, which does not get through without px – checks the
/// Microsoft signature and installs it silently (per user without admin rights).
/// </summary>
public static class WebView2Bootstrap
{
    /// <summary>Evergreen Standalone Installer x64. The bootstrapper would download again by itself.</summary>
    public const string DefaultInstallerUrl = "https://go.microsoft.com/fwlink/?linkid=2124701";

    public static string? InstalledVersion()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return null;
        }
    }

    public static async Task<bool> EnsureAsync(EmberHost host)
    {
        var version = InstalledVersion();
        if (version != null)
        {
            Log.Debug("WebView2 " + version);
            return true;
        }

        Log.Warn(L.T("webview2.missing"));
        var window = new BootstrapWindow();
        window.Show();
        try
        {
            var url = host.Team.Config.WebView2InstallerUrl is { Length: > 0 } custom ? custom : DefaultInstallerUrl;
            var target = Path.Combine(Paths.Ensure(Paths.DownloadDir), "MicrosoftEdgeWebView2RuntimeInstallerX64.exe");

            var (http, route) = await Net.SmartClientAsync(host.Effective, TimeSpan.FromMinutes(30));
            window.SetStatus(L.T("webview2.downloading", ("route", route)));
            using (http)
            {
                await Net.DownloadAsync(http, url, target, (done, total) =>
                    window.Dispatcher.InvokeAsync(() => window.SetProgress(done, total)), CancellationToken.None, resume: true);
            }

            if (!Authenticode.IsSignedBy(target, "Microsoft Corporation")) throw new EmberException("webView2Signature");

            window.SetStatus(L.T("webview2.installing"));
            window.SetProgress(0, null);
            var r = await ProcessRunner.RunAsync(target, new[] { "/silent", "/install" }, CancellationToken.None, 900);
            Log.Info($"WebView2 installer: exit code {r.ExitCode}");
            try { File.Delete(target); } catch { }

            version = InstalledVersion();
            if (version == null) throw new EmberException("webView2InstallFailed", ("code", r.ExitCode));
            Log.Info(L.T("webview2.installed", ("version", version)));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(Net.Describe(ex));
            window.Close();
            var answer = MessageBox.Show(L.T("webview2.failed", ("detail", Net.Describe(ex))), "Ember",
                                         MessageBoxButton.YesNo, MessageBoxImage.Error);
            if (answer == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true })?.Dispose();
            return false;
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }

    /// <summary>A small native window – the web UI cannot show anything without WebView2.</summary>
    sealed class BootstrapWindow : Window
    {
        readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        readonly ProgressBar _bar = new() { Height = 6, IsIndeterminate = true };
        readonly TextBlock _detail = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0) };

        public BootstrapWindow()
        {
            Title = "Ember";
            Width = 440;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            FontSize = 14;
            _status.Text = L.T("webview2.preparing");
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Children =
                {
                    new TextBlock { Text = L.T("webview2.title"), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                    _status, _bar, _detail,
                },
            };
        }

        public void SetStatus(string text) => _status.Text = text;

        public void SetProgress(long done, long? total)
        {
            _bar.IsIndeterminate = total is null or 0;
            if (total > 0) _bar.Value = 100.0 * done / total.Value;
            _detail.Text = total > 0 ? $"{Net.Mb(done)} / {Net.Mb(total.Value)}" : done > 0 ? Net.Mb(done) : "";
        }
    }
}

using System.Diagnostics;
using System.Text;

namespace Ember.Core;

public sealed record MailResult(string Method, string To, string Subject, string Body, string? ReportPath, string? ReportText);

/// <summary>
/// Prepares a mail to the service center. Ember never sends anything itself: the user reviews
/// the mail and sends it. Classic Outlook gets a draft with the report attached; everything
/// else gets a mailto: link (no attachment possible) plus the report as file and in the UI.
/// </summary>
public static class Mail
{
    public static readonly string[] Templates = { "softwareRequest", "adminAction", "problem" };

    public static (string Subject, string Body) Render(TeamConfig team, string template, IReadOnlyDictionary<string, string?> vars)
    {
        MailTemplate? t = null;
        team.MailTemplates?.TryGetValue(template, out t);
        var subject = t?.Subject?.Get() ?? L.T($"mail.templates.{template}.subject");
        var body = t?.Body?.Get() ?? L.T($"mail.templates.{template}.body");

        var all = new Dictionary<string, string?>(vars, StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = vars.TryGetValue("user", out var u) && u != null ? u : Elevation.AccountName,
            ["computer"] = Environment.MachineName,
            ["date"] = DateTime.Now.ToString("d", L.Culture),
            ["ember"] = AppInfo.Version,
        };
        foreach (var (k, v) in all)
        {
            subject = subject.Replace("{" + k + "}", v ?? "");
            body = body.Replace("{" + k + "}", v ?? "");
        }
        var prefix = team.ServiceCenter?.SubjectPrefix;
        if (!string.IsNullOrWhiteSpace(prefix)) subject = prefix.Trim() + " " + subject;
        return (subject, body);
    }

    public static async Task<MailResult> ComposeAsync(TeamConfig team, string template, IReadOnlyDictionary<string, string?> vars,
                                                      Func<CancellationToken, Task<string>>? report, CancellationToken ct)
    {
        var to = team.ServiceCenter?.Email;
        if (string.IsNullOrWhiteSpace(to)) throw new EmberException("mailNoRecipient");
        var (subject, body) = Render(team, template, vars);

        string? reportPath = null, reportText = null;
        if (report != null)
        {
            reportText = await report(ct);
            reportPath = Diagnostics.Save(reportText);
        }

        if (TryOutlook(to, subject, body, reportPath))
        {
            Log.Info(L.T("mail.outlookDraft", ("to", to)));
            return new MailResult("outlook", to, subject, body, reportPath, reportText);
        }

        var mailBody = reportPath == null ? body : body + "\n\n" + L.T("mail.attachHint", ("path", reportPath));
        var uri = MailtoUri(to, subject, mailBody);
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        if (reportPath != null) ShowInExplorer(reportPath);
        Log.Info(L.T("mail.mailtoOpened", ("to", to)));
        return new MailResult("mailto", to, subject, body, reportPath, reportText);
    }

    /// <summary>
    /// mailto: with UTF-8 percent encoding (RFC 6068), so ä ö ü ß arrive intact.
    /// Long bodies are cut: many mail programs stop at about 2000 characters.
    /// </summary>
    public static string MailtoUri(string to, string subject, string body, int maxBody = 1500)
    {
        if (body.Length > maxBody) body = body[..maxBody] + " …";
        body = body.Replace("\r\n", "\n").Replace("\n", "\r\n");
        return $"mailto:{Uri.EscapeDataString(to).Replace("%40", "@")}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
    }

    /// <summary>Classic Outlook via late-bound COM (no interop assembly needed).</summary>
    static bool TryOutlook(string to, string subject, string body, string? attachment)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var type = Type.GetTypeFromProgID("Outlook.Application");
        if (type == null) return false;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                dynamic app = Activator.CreateInstance(type)!;
                dynamic mail = app.CreateItem(0);   // olMailItem
                mail.To = to;
                mail.Subject = subject;
                mail.Body = body;
                if (attachment != null) mail.Attachments.Add(attachment);
                mail.Display(false);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            Log.Warn("Outlook: timeout");
            return false;
        }
        if (error != null) Log.Warn("Outlook: " + error.Message);
        return error == null;
    }

    public static void ShowInExplorer(string file)
    {
        try
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(explorer) { ArgumentList = { "/select," + file } })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug("explorer: " + ex.Message);
        }
    }
}

/// <summary>Plain-text diagnosis report for the service center (no passwords, there are none).</summary>
public static class Diagnostics
{
    public static string Save(string text)
    {
        var path = Path.Combine(Paths.Ensure(Paths.ReportDir), $"ember-diagnose-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        // UTF-8 with BOM: Notepad, Outlook previews and older editors then show umlauts correctly.
        File.WriteAllText(path, text, new UTF8Encoding(true));
        return path;
    }

    public static async Task<string> BuildAsync(EffectiveSettings s, TeamStatus team, CancellationToken ct)
    {
        var sb = new StringBuilder();
        void Line(string label, object? value) => sb.Append(label.PadRight(22)).Append(": ").AppendLine(value?.ToString() ?? "–");

        sb.AppendLine(L.T("diagnostics.title"));
        sb.AppendLine(new string('=', 60));
        Line(L.T("diagnostics.created"), DateTime.Now.ToString("g", L.Culture));
        Line("Ember", $"{AppInfo.Version} (UI {AppInfo.UiVersion}, {AppInfo.Mode})");
        Line(L.T("diagnostics.computer"), Environment.MachineName);
        Line(L.T("diagnostics.user"), Elevation.AccountName);
        Line("Windows", WindowsVersion());
        Line(L.T("diagnostics.language"), $"{System.Globalization.CultureInfo.CurrentUICulture.Name} / Ember {L.Tag}");
        Line(L.T("diagnostics.admin"), Elevation.IsElevated ? L.T("common.yes") : L.T("common.no"));
        Line(L.T("diagnostics.team"), team.Path == null ? "–" : $"{team.Path} ({(team.FromCache ? L.T("diagnostics.cache") : "OK")}{(team.Error != null ? ", " + team.Error : "")})");
        sb.AppendLine();

        var px = await PxControl.StatusAsync(s);
        sb.AppendLine("px");
        sb.AppendLine(new string('-', 60));
        Line(L.T("diagnostics.installed"), px.Installed ? s.InstallDir : L.T("common.no"));
        Line(L.T("diagnostics.running"), px.Running ? L.T("common.yes") : L.T("common.no"));
        Line(L.T("diagnostics.port"), $"{s.PxUrl} {(px.PortOpen ? "OK" : L.T("diagnostics.closed"))}");
        Line("PAC", string.IsNullOrWhiteSpace(s.PacUrl) ? Net.ReadPacFromRegistry() : s.PacUrl);
        Line(L.T("diagnostics.upstream"), s.UpstreamHostPort.Length > 0 ? s.UpstreamHostPort : "–");
        sb.AppendLine();

        sb.AppendLine(L.T("diagnostics.proxyTable"));
        sb.AppendLine(new string('-', 60));
        foreach (var row in await ProxyTools.DiagnoseAsync(s, ct))
            sb.Append(row.Label.PadRight(22)).Append(": [").Append(L.T($"proxy.states.{row.State.ToString().ToLowerInvariant()}")).Append("] ").AppendLine(row.Value);
        sb.AppendLine();

        sb.AppendLine(L.T("diagnostics.tools"));
        sb.AppendLine(new string('-', 60));
        foreach (var (name, exe, args) in new[]
                 {
                     ("git", Tools.Git, "--version"), ("node", Tools.Node, "--version"), ("dotnet", Tools.Dotnet, "--version"),
                 })
        {
            string version = "–";
            if (exe != null)
            {
                try { version = (await ProcessRunner.RunAsync(exe, new[] { args }, ct, 20, logOutput: false)).LastLine; }
                catch (Exception ex) when (ex is not OperationCanceledException) { version = ex.Message; }
            }
            Line(name, version);
        }
        sb.AppendLine();

        sb.AppendLine(L.T("diagnostics.log"));
        sb.AppendLine(new string('-', 60));
        foreach (var entry in Log.Recent(150, LogLevel.Info)) sb.AppendLine(entry.ToString());
        return sb.ToString();
    }

    static string WindowsVersion()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return Environment.OSVersion.ToString();
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = key?.GetValue("ProductName") as string ?? "Windows";
            var display = key?.GetValue("DisplayVersion") as string;
            var build = key?.GetValue("CurrentBuild") as string;
            var ubr = key?.GetValue("UBR");
            // Windows 11 still reports "Windows 10" in ProductName; the build number tells.
            if (int.TryParse(build, out var b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
            return $"{product} {display} (Build {build}.{ubr})";
        }
        catch
        {
            return Environment.OSVersion.ToString();
        }
    }
}

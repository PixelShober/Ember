using System.Runtime.InteropServices;
using System.Text;

namespace Ember.Core;

public sealed record GitIdentityInfo(bool GitFound, string? Name, string? Email, string? SuggestedName, string? SuggestedEmail, string? Source);

/// <summary>
/// Reads the git identity and suggests one from the Windows account
/// (AD attribute "mail", otherwise the UPN; display name from AD or Windows).
/// </summary>
public static class GitIdentity
{
    public static async Task<GitIdentityInfo> GetAsync(CancellationToken ct)
    {
        string? name = null, email = null;
        var git = Tools.Git;
        if (git != null)
        {
            name = NullIfEmpty((await ProcessRunner.RunAsync(git, new[] { "config", "--global", "--get", "user.name" }, ct, logOutput: false)).LastLine);
            email = NullIfEmpty((await ProcessRunner.RunAsync(git, new[] { "config", "--global", "--get", "user.email" }, ct, logOutput: false)).LastLine);
        }

        var (suggestedName, suggestedEmail, source) = await SuggestAsync(ct);
        return new GitIdentityInfo(git != null, name, email, suggestedName, suggestedEmail, source);
    }

    public static async Task SetAsync(string name, string email, CancellationToken ct)
    {
        name = name.Trim();
        email = email.Trim();
        if (name.Length == 0 || !email.Contains('@')) throw new EmberException("gitIdentityInvalid");
        var git = Tools.Git ?? throw new EmberException("toolMissing", ("tool", "git"));
        // ArgumentList, never cmd.exe: names like "Jürgen Mößner" must reach git unchanged (UTF-8).
        foreach (var (key, value) in new[] { ("user.name", name), ("user.email", email) })
        {
            var r = await ProcessRunner.RunAsync(git, new[] { "config", "--global", key, value }, ct);
            if (!r.Ok) throw new EmberException("commandFailed", ("command", "git config " + key), ("detail", r.Output));
        }
        Log.Info(L.T("git.identitySet", ("name", name), ("email", email)));
    }

    static async Task<(string? Name, string? Email, string? Source)> SuggestAsync(CancellationToken ct)
    {
        string? adMail = null, adDisplay = null, adGiven = null, adSurname = null;
        try
        {
            var json = await PowerShell.RunJsonAsync("""
                $s = New-Object System.DirectoryServices.DirectorySearcher
                $s.Filter = "(&(objectCategory=person)(sAMAccountName=$env:USERNAME))"
                foreach ($p in 'mail','displayName','givenName','sn') { [void]$s.PropertiesToLoad.Add($p) }
                $r = $s.FindOne()
                if ($r) {
                  @{ mail = "$($r.Properties['mail'])"; displayName = "$($r.Properties['displayname'])";
                     givenName = "$($r.Properties['givenname'])"; sn = "$($r.Properties['sn'])" } | ConvertTo-Json -Compress
                } else { 'null' }
                """, ct, 20);
            adMail = NullIfEmpty(json.Str("mail"));
            adDisplay = NullIfEmpty(json.Str("displayName"));
            adGiven = NullIfEmpty(json.Str("givenName"));
            adSurname = NullIfEmpty(json.Str("sn"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug("AD lookup: " + ex.Message);   // no domain, no VPN, or Constrained Language Mode
        }

        var upn = NullIfEmpty(GetUserName(ExtendedNameFormat.UserPrincipal));
        var display = NullIfEmpty(GetUserName(ExtendedNameFormat.Display));

        var name = BuildName(adGiven, adSurname, adDisplay ?? display) ?? Environment.UserName;
        var email = adMail ?? (upn != null && upn.Contains('@') ? upn : null);
        var source = adMail != null ? "ad" : email != null ? "upn" : null;
        return (name, email, source);
    }

    /// <summary>
    /// German ADs often use "Surname, Given name" as display name; git wants "Given name Surname".
    /// </summary>
    internal static string? BuildName(string? given, string? surname, string? display)
    {
        if (!string.IsNullOrWhiteSpace(given) && !string.IsNullOrWhiteSpace(surname)) return $"{given.Trim()} {surname.Trim()}";
        if (string.IsNullOrWhiteSpace(display)) return null;
        var d = display.Trim();
        var comma = d.IndexOf(',');
        if (comma > 0 && comma < d.Length - 1)
        {
            var last = d[..comma].Trim();
            var first = d[(comma + 1)..].Trim();
            // "Mustermann, Max (IT-Entwicklung)" → drop the department in brackets
            var bracket = first.IndexOf('(');
            if (bracket > 0) first = first[..bracket].Trim();
            return $"{first} {last}";
        }
        return d;
    }

    static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) || s == "null" ? null : s.Trim();

    enum ExtendedNameFormat { Display = 3, UserPrincipal = 8 }

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetUserNameEx(ExtendedNameFormat nameFormat, StringBuilder? userName, ref uint size);

    static string? GetUserName(ExtendedNameFormat format)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            uint size = 512;
            var sb = new StringBuilder((int)size);
            return GetUserNameEx(format, sb, ref size) ? sb.ToString() : null;
        }
        catch
        {
            return null;
        }
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace Ember.Core;

/// <summary>
/// Windows PowerShell 5.1 sends Invoke-WebRequest, Invoke-RestMethod (irm) and WebClient through the
/// Windows proxy from the PAC file without signing in there, so installers like
/// "irm https://… | iex" fail at the company proxy. A block in the profile of both PowerShells
/// (CurrentUserAllHosts, so also VS Code and "powershell -c") points them at px instead; internal
/// domains and localhost go directly.
/// </summary>
public static class PowerShellProfile
{
    const string Begin = "# >>> Ember: px >>>";
    const string End = "# <<< Ember: px <<<";

    static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>Windows PowerShell 5.1 and PowerShell 7, profile for all hosts of the current user.</summary>
    public static IReadOnlyList<string> Files => new[]
    {
        Path.Combine(Documents, "WindowsPowerShell", "profile.ps1"),
        Path.Combine(Documents, "PowerShell", "profile.ps1"),
    };

    /// <summary>The block, ASCII only, so the file's encoding does not matter.</summary>
    internal static string Block(string pxUrl, IEnumerable<string> internalDomains)
    {
        var bypass = NoProxy.ForTools(internalDomains)
                            .Where(d => !d.Contains('/') && !Regex.IsMatch(d, @"^[\d.]+$"))
                            .Select(d => "'(://|\\.)" + Regex.Escape(d).Replace("'", "''") + "(:\\d+)?$'");
        var nl = "\r\n";
        return Begin + nl +
               "# Set up by Ember: Invoke-WebRequest, Invoke-RestMethod (irm) and WebClient go through px," + nl +
               "# which signs in to the company proxy. Internal domains and localhost go directly." + nl +
               "try {" + nl +
               $"    $emberProxy = New-Object System.Net.WebProxy('{pxUrl.Replace("'", "''")}', $true, [string[]]@({string.Join(", ", bypass)}))" + nl +
               "    [System.Net.WebRequest]::DefaultWebProxy = $emberProxy" + nl +
               "    if ($PSVersionTable.PSVersion.Major -ge 7) { [System.Net.Http.HttpClient]::DefaultProxy = $emberProxy }" + nl +
               "} catch { }" + nl +
               End;
    }

    /// <summary>The px address in the block, or null without block.</summary>
    internal static string? UrlIn(string text)
    {
        var block = Regex.Match(text, Regex.Escape(Begin) + "(.*?)" + Regex.Escape(End), RegexOptions.Singleline);
        if (!block.Success) return null;
        var url = Regex.Match(block.Groups[1].Value, @"WebProxy\('([^']*)'");
        return url.Success ? url.Groups[1].Value : "";
    }

    /// <summary>Replaces Ember's block, or appends it; everything else in the profile stays.</summary>
    internal static string Apply(string? text, string block)
    {
        text ??= "";
        var pattern = new Regex(Regex.Escape(Begin) + ".*?" + Regex.Escape(End), RegexOptions.Singleline);
        if (pattern.IsMatch(text)) return pattern.Replace(text, block.Replace("$", "$$"), 1);
        var nl = text.Contains("\r\n") || text.Length == 0 ? "\r\n" : "\n";
        var sep = text.Length == 0 ? "" : text.EndsWith('\n') ? nl : nl + nl;
        return text + sep + block.Replace("\r\n", nl) + nl;
    }

    internal static string Remove(string text) =>
        Regex.Replace(text, @"(\r?\n)?" + Regex.Escape(Begin) + ".*?" + Regex.Escape(End) + @"\r?\n?", "", RegexOptions.Singleline);

    /// <summary>The px address both profiles point to, "" for different or missing blocks, null for none at all.</summary>
    public static string? Current()
    {
        var urls = Files.Select(f => File.Exists(f) ? UrlIn(Read(f).Text) : null).ToList();
        if (urls.All(u => u == null)) return null;
        return urls.All(u => u != null && u == urls[0]) ? urls[0] : "";
    }

    public static IReadOnlyList<string> Set(EffectiveSettings s)
    {
        var block = Block(s.PxUrl, s.InternalDomainList);
        foreach (var file in Files)
        {
            var (text, encoding) = File.Exists(file) ? Read(file) : ("", new UTF8Encoding(true));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Apply(text, block), encoding);
        }
        return Files;
    }

    public static int Unset()
    {
        int count = 0;
        foreach (var file in Files.Where(File.Exists))
        {
            var (text, encoding) = Read(file);
            if (UrlIn(text) == null) continue;
            File.WriteAllText(file, Remove(text), encoding);
            count++;
        }
        return count;
    }

    /// <summary>Keeps the file's encoding: UTF-8 with or without BOM, otherwise the Windows code page (older profiles).</summary>
    static (string Text, Encoding Encoding) Read(string file)
    {
        var bytes = File.ReadAllBytes(file);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));
        try
        {
            return (new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes), new UTF8Encoding(false));
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var ansi = Encoding.GetEncoding(1252);
            return (ansi.GetString(bytes), ansi);
        }
    }

    /// <summary>
    /// Windows PowerShell only runs profiles with RemoteSigned, Unrestricted or Bypass. Returns the
    /// policy that blocks them (Restricted, the Windows default, or AllSigned), else null.
    /// </summary>
    public static string? BlockingPolicy()
    {
        if (!OperatingSystem.IsWindows()) return null;
        string? Value(Microsoft.Win32.RegistryKey hive, string path)
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                return key?.GetValue("ExecutionPolicy") as string;
            }
            catch
            {
                return null;
            }
        }
        const string policy = @"SOFTWARE\Policies\Microsoft\Windows\PowerShell";
        const string shell = @"SOFTWARE\Microsoft\PowerShell\1\ShellIds\Microsoft.PowerShell";
        var effective = Value(Microsoft.Win32.Registry.LocalMachine, policy) ?? Value(Microsoft.Win32.Registry.CurrentUser, policy)
                        ?? Value(Microsoft.Win32.Registry.CurrentUser, shell) ?? Value(Microsoft.Win32.Registry.LocalMachine, shell)
                        ?? "Restricted";
        return effective.Equals("Restricted", StringComparison.OrdinalIgnoreCase) || effective.Equals("AllSigned", StringComparison.OrdinalIgnoreCase)
            ? effective : null;
    }
}

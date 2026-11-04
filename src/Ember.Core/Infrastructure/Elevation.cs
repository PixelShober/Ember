using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Ember.Core;

public static class Elevation
{
    /// <summary>True when Ember itself runs elevated (then UAC prompts are not needed).</summary>
    public static bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>"DOMAIN\user" of the signed-in Windows account.</summary>
    public static string AccountName => $"{Environment.UserDomainName}\\{Environment.UserName}";

    /// <summary>Group names ("DOMAIN\Group") of the current Windows token.</summary>
    public static IReadOnlyList<string> GroupNames()
    {
        var names = new List<string>();
        if (!OperatingSystem.IsWindows()) return names;
        using var identity = WindowsIdentity.GetCurrent();
        foreach (var sid in identity.Groups ?? new IdentityReferenceCollection())
        {
            try
            {
                names.Add(sid.Translate(typeof(NTAccount)).Value);
            }
            catch
            {
                // SIDs without a name (e.g. logon session) are irrelevant here.
            }
        }
        return names;
    }

    /// <summary>
    /// Starts a program with admin rights (UAC prompt) and waits for it. Returns the exit code.
    /// If Ember already runs elevated, there is no prompt.
    /// </summary>
    public static async Task<int> RunAsync(string file, string arguments, CancellationToken ct, int timeoutSec)
    {
        Log.Debug($"> (admin) {Path.GetFileName(file)} {arguments}");
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(file) ?? "",
        };
        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new EmberException("elevationCancelled");
        }
        if (process == null) throw new EmberException("processStart", ("file", file));
        using (process)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                throw new EmberException("processTimeout", ("file", Path.GetFileName(file)), ("seconds", timeoutSec));
            }
            var code = process.ExitCode;
            Log.Debug($"  exit code {code}");
            return code;
        }
    }
}

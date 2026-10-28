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
}

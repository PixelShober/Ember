using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Ember.Core;

/// <summary>
/// Node.js (and so npm, npx, Playwright, Claude Code …) brings its own list of root certificates and
/// ignores the Windows certificate store. Behind a proxy that inspects TLS, Node then fails with
/// "unable to get local issuer certificate". Ember exports the trusted roots (and intermediate CAs)
/// from Windows into one PEM file and points NODE_EXTRA_CA_CERTS at it.
/// </summary>
public static class NodeCertificates
{
    public const string Variable = "NODE_EXTRA_CA_CERTS";

    public static string BundleFile => Path.Combine(Paths.CertsDir, "windows-ca.pem");

    /// <summary>The variable points at an existing file.</summary>
    public static bool Configured => UserEnvironment.Get(Variable) is { Length: > 0 } path && File.Exists(path);

    /// <summary>Writes the bundle and sets the variable. Returns the number of certificates.</summary>
    public static int Setup()
    {
        Paths.Ensure(Paths.CertsDir);
        var count = Export(BundleFile);
        UserEnvironment.Set(Variable, BundleFile);
        return count;
    }

    /// <summary>Removes the variable, but only if it points at Ember's bundle.</summary>
    public static bool Remove()
    {
        var current = UserEnvironment.Get(Variable);
        if (current == null || !current.Equals(BundleFile, StringComparison.OrdinalIgnoreCase)) return false;
        UserEnvironment.Set(Variable, null);
        return true;
    }

    static int Export(string file)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pem = new StringBuilder();
        pem.Append("# Exported by Ember from the Windows certificate store (Root and intermediate CAs).\n");
        pem.Append("# Created ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append(". Ember renews it on \"Set up\".\n\n");
        foreach (var (name, location) in new[]
                 {
                     (StoreName.Root, StoreLocation.LocalMachine),
                     (StoreName.Root, StoreLocation.CurrentUser),
                     (StoreName.CertificateAuthority, StoreLocation.LocalMachine),
                 })
        {
            try
            {
                using var store = new X509Store(name, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                foreach (var cert in store.Certificates)
                {
                    using (cert)
                    {
                        if (cert.NotAfter < DateTime.Now || !seen.Add(cert.Thumbprint)) continue;
                        pem.Append("# ").Append(cert.Subject).Append('\n');
                        pem.Append(cert.ExportCertificatePem()).Append("\n\n");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Certificate store {location}\\{name}: {ex.Message}");
            }
        }
        if (seen.Count == 0) throw new EmberException("certsNone");
        File.WriteAllText(file, pem.ToString(), new UTF8Encoding(false));
        return seen.Count;
    }
}

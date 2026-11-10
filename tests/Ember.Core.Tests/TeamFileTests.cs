using System.Security.Cryptography;
using Ember.Core;
using Xunit;

public class TeamFileTests
{
    const string Location = @"\\srv\team\team.ember";

    static (ECDsa Key, IReadOnlyList<ECDsa> Trusted) Keys()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ECDsa.Create();
        pub.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);
        return (key, new[] { pub });
    }

    static string Payload(int version = 3, string location = Location) =>
        $$"""{ "version": {{version}}, "location": "{{location.Replace("\\", "\\\\")}}", "signedBy": "FIRMA\\lead", "access": { "roles": [] } }""";

    [Fact]
    public void A_signed_file_opens_and_says_who_signed_it()
    {
        var (key, trusted) = Keys();
        var sealedText = TeamFile.Seal(Payload(), key);
        Assert.True(TeamFile.IsEnvelope(sealedText));
        var content = TeamFile.Open(sealedText, trusted);
        Assert.True(content.Signed && content.Trusted);
        Assert.Equal(TeamFile.KeyId(key), content.KeyId);
        var config = TeamConfig.Parse(content.Payload);
        Assert.Equal(3, config.Version);
        Assert.Null(TeamFile.Problem(content, config, Location, 2, signatureRequired: true));
    }

    [Fact]
    public void A_changed_or_foreign_file_is_refused()
    {
        var (key, trusted) = Keys();
        var sealedText = TeamFile.Seal(Payload(), key);

        // Someone puts himself into a role: the payload no longer matches the signature.
        var forged = sealedText.Replace(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Payload())),
                                        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Payload().Replace("[]", "[{\"name\":\"x\",\"modules\":[\"*\"]}]"))));
        var content = TeamFile.Open(forged, trusted);
        Assert.False(content.Trusted);
        Assert.Equal("teamSignatureInvalid", TeamFile.Problem(content, TeamConfig.Parse(content.Payload), Location, 0, true));

        // Signed with a key of one's own.
        var (own, _) = Keys();
        var foreign = TeamFile.Open(TeamFile.Seal(Payload(), own), trusted);
        Assert.Equal("teamSignatureInvalid", TeamFile.Problem(foreign, TeamConfig.Parse(foreign.Payload), Location, 0, true));

        // Plain JSON.
        var plain = TeamFile.Open(Payload(), trusted);
        Assert.Equal("teamUnsigned", TeamFile.Problem(plain, TeamConfig.Parse(plain.Payload), Location, 0, true));
        // … which a build without public key still takes.
        Assert.Null(TeamFile.Problem(plain, TeamConfig.Parse(plain.Payload), Location, 0, false));
    }

    [Fact]
    public void A_copy_elsewhere_or_an_older_version_is_refused()
    {
        var (key, trusted) = Keys();
        var content = TeamFile.Open(TeamFile.Seal(Payload(3), key), trusted);
        var config = TeamConfig.Parse(content.Payload);
        Assert.Equal("teamWrongPlace", TeamFile.Problem(content, config, @"C:\Users\x\Desktop\team.ember", 0, true));
        Assert.Equal("teamOlder", TeamFile.Problem(content, config, Location, 4, true));
        Assert.Null(TeamFile.Problem(content, config, @"\\SRV\Team\team.ember", 3, true));   // case and same version are fine
    }

    [Fact]
    public void The_admin_key_is_password_protected()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ember-key-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal("teamKeyPasswordShort", Assert.Throws<EmberException>(() => TeamFile.NewKey("kurz")).Code);
            var (priv, pub, id) = TeamFile.NewKey("ein langes Passwort");
            Assert.Contains("ENCRYPTED PRIVATE KEY", priv);
            var file = Path.Combine(dir, "k.pem");
            File.WriteAllText(file, priv);
            using var key = TeamFile.OpenKey(file, "ein langes Passwort");
            Assert.Equal(id, TeamFile.KeyId(key));
            Assert.Equal(id, TeamFile.KeyId(TeamFile.ParsePublicKeys("# Team\n" + pub + "\n")[0]));
            Assert.Equal("teamKeyPassword", Assert.Throws<EmberException>(() => TeamFile.OpenKey(file, "falsch falsch falsch")).Code);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_form_and_the_extra_json_make_one_payload()
    {
        var doc = new TeamDoc(
            new TeamDocServiceCenter("sc@firma.example", "", "[Ember]", ""),
            new TeamDocSupervisor("Lindner, Petra", "", "petra@firma.example"),
            new TeamDocAccess(new List<AccessRole> { new("Admins", new() { "*" }, new() { new(@"FIRMA\lead", "user") }, new() { "bruno" }) },
                              new List<AccessEntry> { new(@"FIRMA\weg", "user") }),
            new TeamDocUpdate(""),
            new TeamDocPlaywright(@"\\srv\vorlagen\pw", ""),
            new Dictionary<string, TeamDocMail> { ["problem"] = new("", "Hallo,\r\n{details}") });
        var extra = """{ "name": "Team", "proxy": { "internalDomains": ["firma.local"] }, "access": { "roles": [] } }""";
        var payload = TeamAdmin.Payload(doc, extra, 7, Location, @"FIRMA\lead", new DateTime(2026, 11, 10, 9, 0, 0));
        var config = TeamConfig.Parse(payload);

        Assert.Equal(7, config.Version);
        Assert.Equal(Location, config.Location);
        Assert.Equal("Team", config.Name);
        Assert.Equal("firma.local", config.Proxy!.InternalDomains![0]);
        Assert.Single(config.Access!.Roles!);   // the form wins over "access" in the extra JSON
        Assert.Equal(new[] { @"FIRMA\weg" }, config.Access.Blocked!.Users);
        Assert.Equal("Petra", config.Supervisor!.GreetingName());
        Assert.Null(config.Update);
        Assert.Equal("Hallo,\n{details}", config.MailTemplates!["problem"].Body!.Get());

        // What the forms own does not show up again as extra.
        var back = TeamAdmin.Extra(payload);
        Assert.Contains("internalDomains", back);
        Assert.DoesNotContain("access", back);
        Assert.DoesNotContain("version", back);

        Assert.Equal("teamExtraInvalid", Assert.Throws<EmberException>(() => TeamAdmin.Payload(doc, "[1, 2]", 1, Location, "x", DateTime.Now)).Code);
    }
}

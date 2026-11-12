using System.Text;
using System.Text.Json.Nodes;

namespace Ember.Core;

public sealed record TeamDocServiceCenter(string Email, string Name, string SubjectPrefix, string ProblemIntro);
public sealed record TeamDocSupervisor(string Name, string FirstName, string Email);
public sealed record TeamDocAccess(List<AccessRole> Roles, List<AccessEntry> Blocked);
public sealed record TeamDocUpdate(string Mirror);
public sealed record TeamDocPlaywright(string Template, string DownloadHost);
public sealed record TeamDocMail(string Subject, string Body);

/// <summary>The required parts of the team file, as the Berechtigungen page edits them.</summary>
public sealed record TeamDoc(TeamDocServiceCenter ServiceCenter, TeamDocSupervisor Supervisor, TeamDocAccess Access,
                             TeamDocUpdate Update, TeamDocPlaywright Playwright, Dictionary<string, TeamDocMail> MailTemplates);

/// <param name="Extra">The optional sections (proxy, software, power, devTools …) as JSON text.</param>
/// <param name="KeyFile">Where the admin's signing key was last taken from; never the password.</param>
public sealed record TeamFileView(TeamDoc Doc, string Extra, string? Path, int Version, string? SignedBy, DateTime? SignedAt, string? KeyId,
                                  bool SignatureRequired, bool Trusted, bool Writable, string? Problem, string Account,
                                  IReadOnlyList<string> MyGroups, IReadOnlyList<string> Modules, string? KeyFile);

public sealed record TeamKeyCreated(string Path, string PublicKey, string KeyId);

/// <summary>
/// Editing the team file in the Berechtigungen tab. The required parts (service center, supervisor,
/// access, update, Playwright, mail texts) are forms, everything else is JSON. Saving counts the
/// version up, binds the file to its path and signs it with the admins' key (see <see cref="TeamFile"/>).
/// A plain team.json is turned into team.ember next to it on the first save.
/// </summary>
public static class TeamAdmin
{
    static readonly string[] Templates = { "softwareRequest", "problem", "adminAction" };

    /// <summary>Keys in the payload that the forms or the signature own; everything else is "extra".</summary>
    static readonly HashSet<string> Owned = new(StringComparer.OrdinalIgnoreCase)
    {
        "$schema", "version", "location", "signedBy", "signedAt",
        "serviceCenter", "supervisor", "access", "admins", "update", "playwright", "mailTemplates",
    };

    public static async Task<TeamFileView> ViewAsync(TeamConfigService team, UserSettings s, CancellationToken ct)
    {
        var status = team.Status;
        string? problem = status.Path == null ? "noTeamFile" : status.FromCache ? "teamOffline" : null;
        bool writable = false;
        if (problem == null)
        {
            try
            {
                var target = Target(status.Path!);
                writable = await Task.Run(() => CanWrite(target), ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
                if (!writable) problem = "teamReadOnly";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                problem = "teamOffline";
            }
        }

        var domain = Environment.UserDomainName + "\\";
        var mine = Access.MyGroups.Where(g => g.StartsWith(domain, StringComparison.OrdinalIgnoreCase) &&
                                              !g.StartsWith(Environment.MachineName + "\\", StringComparison.OrdinalIgnoreCase))
                                  .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                                  .ToList();
        return new TeamFileView(Doc(team.Config), Extra(team.Payload), status.Path, team.Config.Version, status.SignedBy, status.SignedAt,
                                status.KeyId, TeamFile.SignatureRequired, status.Trusted, writable && problem == null, problem,
                                Elevation.AccountName, mine, ModuleIds.Known, string.IsNullOrWhiteSpace(s.TeamKeyFile) ? null : s.TeamKeyFile);
    }

    /// <summary>The file the next save writes: the .ember file itself, or team.ember next to a plain team.json.</summary>
    public static string Target(string path) =>
        path.EndsWith(TeamFile.Extension, StringComparison.OrdinalIgnoreCase) ? path : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", "team" + TeamFile.Extension);

    static bool CanWrite(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                using var _ = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                return true;
            }
            var probe = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file)!, ".ember-write-" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    internal static TeamDoc Doc(TeamConfig c)
    {
        var mails = Templates.ToDictionary(id => id, id =>
        {
            MailTemplate? m = null;
            c.MailTemplates?.TryGetValue(id, out m);
            return new TeamDocMail(m?.Subject?.Get() ?? "", m?.Body?.Get() ?? "");
        });
        return new TeamDoc(
            new TeamDocServiceCenter(c.ServiceCenter?.Email ?? "", c.ServiceCenter?.Name ?? "", c.ServiceCenter?.SubjectPrefix ?? "", c.ServiceCenter?.ProblemIntro?.Get() ?? ""),
            new TeamDocSupervisor(c.Supervisor?.Name ?? "", c.Supervisor?.FirstName ?? "", c.Supervisor?.Email ?? ""),
            new TeamDocAccess(
                Permissions.Roles(c).Select(r => new AccessRole(r.Name, (r.Modules ?? new()).ToList(), Access.Entries(r.Users, r.Groups), r.HideTools ?? new())).ToList(),
                Access.Entries(c.Access?.Blocked?.Users, c.Access?.Blocked?.Groups)),
            new TeamDocUpdate(c.Update?.Mirror ?? ""),
            new TeamDocPlaywright(c.Playwright?.Template ?? "", c.Playwright?.DownloadHost ?? ""),
            mails);
    }

    /// <summary>Everything the forms do not cover, pretty-printed; "" when there is nothing.</summary>
    internal static string Extra(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return "";
        try
        {
            if (JsonNode.Parse(payload, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true }) is not JsonObject root) return "";
            var extra = new JsonObject();
            foreach (var (k, v) in root.ToList())
                if (!Owned.Contains(k)) extra[k] = v?.DeepClone();
            return extra.Count == 0 ? "" : Json.Format(extra) + "\n";
        }
        catch (System.Text.Json.JsonException)
        {
            return "";
        }
    }

    /// <summary>The payload: signature data first, then the forms, then the extra sections.</summary>
    internal static string Payload(TeamDoc doc, string extra, int version, string location, string signedBy, DateTime signedAt)
    {
        JsonObject rest;
        try
        {
            rest = string.IsNullOrWhiteSpace(extra) ? new JsonObject()
                : JsonNode.Parse(extra, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                  ?? throw new EmberException("teamExtraInvalid", ("detail", "{ … }"));
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new EmberException("teamExtraInvalid", ("detail", ex.Message));
        }

        static JsonNode? Str(string? v) => string.IsNullOrWhiteSpace(v) ? null : JsonValue.Create(v.Trim());
        static JsonObject Clean(JsonObject o)
        {
            foreach (var k in o.Where(p => p.Value == null).Select(p => p.Key).ToList()) o.Remove(k);
            return o;
        }
        static JsonArray Arr(IEnumerable<string> items) => new(items.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());

        var root = new JsonObject
        {
            ["version"] = version,
            ["location"] = location,
            ["signedBy"] = signedBy,
            ["signedAt"] = signedAt.ToString("s", System.Globalization.CultureInfo.InvariantCulture),
        };
        if (rest["name"] is { } name) root["name"] = name.DeepClone();

        var sc = Clean(new JsonObject { ["email"] = Str(doc.ServiceCenter.Email), ["name"] = Str(doc.ServiceCenter.Name), ["subjectPrefix"] = Str(doc.ServiceCenter.SubjectPrefix), ["problemIntro"] = Str(doc.ServiceCenter.ProblemIntro) });
        if (sc.Count > 0) root["serviceCenter"] = sc;
        var sv = Clean(new JsonObject { ["name"] = Str(doc.Supervisor.Name), ["firstName"] = Str(doc.Supervisor.FirstName), ["email"] = Str(doc.Supervisor.Email) });
        if (sv.Count > 0) root["supervisor"] = sv;

        var roles = new JsonArray();
        foreach (var r in doc.Access.Roles)
        {
            var o = new JsonObject
            {
                ["name"] = r.Name.Trim(),
                ["modules"] = Arr(r.Modules.Select(m => m.Trim()).Distinct(StringComparer.Ordinal)),
                ["users"] = Arr(Access.Names(r.Members, "user")),
                ["groups"] = Arr(Access.Names(r.Members, "group")),
            };
            if (r.HideTools is { Count: > 0 } hide) o["hideTools"] = Arr(hide.Distinct(StringComparer.OrdinalIgnoreCase));
            roles.Add(o);
        }
        var access = new JsonObject { ["roles"] = roles };
        if (doc.Access.Blocked.Count > 0)
            access["blocked"] = new JsonObject { ["users"] = Arr(Access.Names(doc.Access.Blocked, "user")), ["groups"] = Arr(Access.Names(doc.Access.Blocked, "group")) };
        root["access"] = access;

        var up = Clean(new JsonObject { ["mirror"] = Str(doc.Update.Mirror) });
        if (up.Count > 0) root["update"] = up;
        var pw = Clean(new JsonObject { ["template"] = Str(doc.Playwright.Template), ["downloadHost"] = Str(doc.Playwright.DownloadHost) });
        if (pw.Count > 0) root["playwright"] = pw;

        var mails = new JsonObject();
        foreach (var (id, m) in doc.MailTemplates)
        {
            if (!Templates.Contains(id)) continue;
            var o = Clean(new JsonObject { ["subject"] = Str(m.Subject), ["body"] = string.IsNullOrWhiteSpace(m.Body) ? null : JsonValue.Create(m.Body.Replace("\r\n", "\n").TrimEnd()) });
            if (o.Count > 0) mails[id] = o;
        }
        if (mails.Count > 0) root["mailTemplates"] = mails;

        foreach (var (k, v) in rest.ToList())
            if (!Owned.Contains(k) && k != "name") root[k] = v?.DeepClone();

        var text = Json.Format(root) + "\n";
        try
        {
            TeamConfig.Parse(text);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new EmberException("teamExtraInvalid", ("detail", ex.Message));
        }
        return text;
    }

    /// <summary>
    /// Signs and writes the team file. Refuses when the file changed in between, when the key is not
    /// one this Ember build trusts, or when the admin would lock themselves out.
    /// </summary>
    public static string Save(TeamConfigService team, UserSettings s, TeamDoc doc, string extra, int version, string keyFile, string password,
                              string account, IReadOnlyList<string> groups)
    {
        var path = team.Status.Path ?? throw new EmberException("accessNoTeamFile");
        if (team.Status.FromCache) throw new EmberException("teamOffline", ("path", path));
        Access.Validate(doc.Access.Roles);
        if (!Access.DevBuild) Access.EnsureStillAdmin(doc.Access.Roles, doc.Access.Blocked, account, groups);

        using var key = TeamFile.OpenKey(keyFile, password);
        if (TeamFile.SignatureRequired && !TeamFile.TrustedKeys.Any(t => TeamFile.KeyId(t) == TeamFile.KeyId(key)))
            throw new EmberException("teamKeyUnknown", ("key", TeamFile.KeyId(key)));

        // Someone else saved in between: reload, check, save again.
        var bytes = File.ReadAllBytes(path);
        var current = TeamConfig.Parse(TeamFile.Open(new UTF8Encoding(false).GetString(bytes).TrimStart('﻿')).Payload);
        if (current.Version != version) throw new EmberException("teamChanged", ("path", path));

        var target = Target(path);
        var location = TeamFile.Normalize(target);
        var payload = Payload(doc, extra, version + 1, location, account, DateTime.Now);
        Access.Backup(path, bytes);
        var tmp = target + ".tmp";
        File.WriteAllText(tmp, TeamFile.Seal(payload, key), new UTF8Encoding(false));
        File.Move(tmp, target, overwrite: true);

        s.TeamKeyFile = keyFile;
        s.TeamVersions[location] = version + 1;
        if (!TeamFile.SamePath(target, path)) s.TeamConfigPath = target;
        s.Save();
        Log.Info(L.T("team.signed", ("path", target), ("version", version + 1), ("key", TeamFile.KeyId(key))));
        return target;
    }

    /// <summary>A new signing key in the given folder (never overwrites); the public key goes into config/team-signing.pub.</summary>
    public static TeamKeyCreated NewKey(string folder, string password, UserSettings s)
    {
        if (!Directory.Exists(folder)) throw new EmberException("folderMissing", ("path", folder));
        var (priv, pub, id) = TeamFile.NewKey(password);
        var file = System.IO.Path.Combine(folder, "ember-team-signing.pem");
        for (int i = 2; File.Exists(file); i++) file = System.IO.Path.Combine(folder, $"ember-team-signing-{i}.pem");
        File.WriteAllText(file, priv);
        s.TeamKeyFile = file;
        s.Save();
        Log.Info(L.T("team.keyCreated", ("path", file), ("key", id)));
        return new TeamKeyCreated(file, pub, id);
    }
}

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ember.Core;

/// <param name="Source">"team" (team.json playwright.template), "user" (chosen in Ember) or "builtin" (Ember's own).</param>
/// <param name="Path">The template folder; null for the built-in one.</param>
/// <param name="Problem">Why the folder cannot be used right now (missing, unreachable); the built-in one steps in.</param>
public sealed record PlaywrightTemplateInfo(string Source, string? Path, string? Name, string? Description, DateTime? Created,
                                            string? CreatedBy, int Files, IReadOnlyList<string> Sample, string? Problem, bool Writable);

/// <param name="Action">"keep" (copied), "drop" (left out, its folder stays as structure) or "skip" (never part of a template).</param>
/// <param name="Reason">Why Ember suggests leaving it out: test, data, secret, generated, report, dependencies, git.</param>
public sealed record TemplateEntry(string Path, bool Dir, string Action, string? Reason, long Size);

/// <summary>
/// Project templates for Playwright, the way Visual Studio uses them: a folder with the structure of a
/// clean project. A new project is a copy in which $projectname$ and $year$ are replaced; afterwards
/// Ember installs the Playwright package and the browsers. The folder comes from team.json
/// (playwright.template), else from the user's settings, else Ember's built-in template applies.
/// A template is made from an existing project: configuration and code stay, tests, data, secrets and
/// everything generated go, their folders stay empty (.gitkeep), so the structure is kept.
/// </summary>
public static class PlaywrightTemplates
{
    public const string ManifestName = "ember-template.json";

    /// <summary>Never part of a template and never copied from one.</summary>
    static readonly string[] SkipDirs = { "node_modules", ".git", "test-results", "playwright-report", "blob-report", ".cache", ".playwright", "coverage", ".nyc_output" };

    static readonly string[] TextExtensions =
        { ".ts", ".mts", ".cts", ".tsx", ".js", ".mjs", ".cjs", ".jsx", ".json", ".jsonc", ".md", ".txt", ".yml", ".yaml", ".html", ".css",
          ".xml", ".editorconfig", ".gitignore", ".gitattributes", ".npmrc", ".nvmrc", ".prettierrc", ".eslintrc", ".env.example", ".ps1", ".cmd", ".sh" };

    public static (string? Path, string Source) Location(TeamConfig team, UserSettings s) =>
        !string.IsNullOrWhiteSpace(team.Playwright?.Template) ? (team.Playwright!.Template!.Trim(), "team")
        : !string.IsNullOrWhiteSpace(s.PlaywrightTemplate) ? (s.PlaywrightTemplate.Trim(), "user")
        : (null, "builtin");

    public static async Task<PlaywrightTemplateInfo> InfoAsync(TeamConfig team, UserSettings s, CancellationToken ct)
    {
        var (path, source) = Location(team, s);
        if (path == null) return new("builtin", null, null, null, null, null, 0, BuiltinSample, null, false);
        try
        {
            // Network shares can hang without VPN.
            return await Task.Run(() => Read(path, source), ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(source, path, null, null, null, null, 0, Array.Empty<string>(), ex is TimeoutException ? L.T("errors.timeoutSuffix") : Net.Describe(ex), false);
        }
    }

    static readonly string[] BuiltinSample = { "package.json", "playwright.config.ts", "tests/example.spec.ts", ".gitignore", ".vscode/extensions.json", "README.md" };

    static PlaywrightTemplateInfo Read(string path, string source)
    {
        if (!Directory.Exists(path))
            return new(source, path, null, null, null, null, 0, Array.Empty<string>(), L.T("playwright.template.missing"), CanCreate(path));
        var files = Files(path).Where(f => f != ManifestName).ToList();
        string? name = null, description = null, by = null;
        DateTime? created = null;
        var manifest = System.IO.Path.Combine(path, ManifestName);
        if (File.Exists(manifest))
        {
            try
            {
                var m = JsonNode.Parse(File.ReadAllText(manifest)) as JsonObject;
                name = m?["name"]?.GetValue<string>();
                description = m?["description"]?.GetValue<string>();
                by = m?["createdBy"]?.GetValue<string>();
                if (DateTime.TryParse(m?["created"]?.GetValue<string>(), out var d)) created = d;
            }
            catch (Exception ex)
            {
                Log.Debug("template manifest: " + ex.Message);
            }
        }
        return new(source, path, name, description, created, by, files.Count, files.Take(12).ToList(), null, Writable(path));
    }

    static bool CanCreate(string path)
    {
        var parent = System.IO.Path.GetDirectoryName(path.TrimEnd('\\', '/'));
        return parent != null && Directory.Exists(parent) && Writable(parent);
    }

    static bool Writable(string dir)
    {
        try
        {
            var probe = System.IO.Path.Combine(dir, ".ember-write-test-" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Relative paths ("tests/login.spec.ts") of all files, without the folders that never belong in a template.</summary>
    static IEnumerable<string> Files(string root) =>
        Walk(root, root).Where(e => !e.Dir && e.Action != "skip").Select(e => e.Path);

    // ------------------------------------------------------------------ new project from the template

    /// <summary>Copies the template into parent\name and fills in the placeholders. Returns the files written.</summary>
    public static List<string> Instantiate(string template, string parent, string name)
    {
        if (!PlaywrightTemplate.ValidName(name)) throw new EmberException("playwrightNameInvalid", ("name", name));
        if (!Directory.Exists(template)) throw new EmberException("playwrightTemplateMissing", ("path", template));
        if (!Directory.Exists(parent)) throw new EmberException("folderMissing", ("path", parent));
        var dir = System.IO.Path.Combine(parent, name);
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
            throw new EmberException("playwrightFolderNotEmpty", ("path", dir));

        var values = new Dictionary<string, string>
        {
            ["$projectname$"] = name,
            ["$safeprojectname$"] = name,
            ["$year$"] = DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var written = new List<string>();
        foreach (var e in Walk(template, template).Where(e => e.Action != "skip"))
        {
            if (e.Path == ManifestName) continue;
            var relative = Fill(e.Path, values);
            var target = System.IO.Path.Combine(new[] { dir }.Concat(relative.Split('/')).ToArray());
            if (e.Dir)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            var source = System.IO.Path.Combine(new[] { template }.Concat(e.Path.Split('/')).ToArray());
            if (IsText(e.Path)) File.WriteAllText(target, Fill(File.ReadAllText(source), values));
            else File.Copy(source, target);
            written.Add(relative);
        }
        // An old template may still lack the package: npm adds it in the next step.
        return written;
    }

    internal static string Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (k, v) in values) text = text.Replace(k, v, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    static bool IsText(string path)
    {
        var file = System.IO.Path.GetFileName(path).ToLowerInvariant();
        return TextExtensions.Any(x => file.EndsWith(x, StringComparison.Ordinal)) || !file.Contains('.') || file.StartsWith('.');
    }

    // ------------------------------------------------------------------ template from a project

    /// <summary>What a template made from this project would contain, with Ember's suggestion per file.</summary>
    public static List<TemplateEntry> Preview(string project)
    {
        if (!Directory.Exists(project)) throw new EmberException("folderMissing", ("path", project));
        if (!File.Exists(System.IO.Path.Combine(project, "package.json"))) throw new EmberException("playwrightTemplateNoProject", ("path", project));
        var list = Walk(project, project).ToList();
        if (list.Count > 5000) throw new EmberException("playwrightTemplateTooBig", ("path", project), ("count", list.Count));
        return list;
    }

    static IEnumerable<TemplateEntry> Walk(string root, string dir)
    {
        IEnumerable<string> dirs, files;
        try
        {
            dirs = Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
            files = Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            yield break;
        }
        foreach (var d in dirs)
        {
            var rel = Relative(root, d);
            var (action, reason) = SuggestDir(rel);
            yield return new TemplateEntry(rel, true, action, reason, 0);
            if (action == "skip") continue;   // node_modules and friends are not even listed inside
            foreach (var e in Walk(root, d))
                yield return action == "drop" && e.Action == "keep" ? e with { Action = "drop", Reason = reason } : e;
        }
        foreach (var f in files)
        {
            var rel = Relative(root, f);
            var (action, reason) = SuggestFile(rel);
            long size = 0;
            try { size = new FileInfo(f).Length; } catch { /* unreadable: size unknown */ }
            yield return new TemplateEntry(rel, false, action, reason, size);
        }
    }

    static string Relative(string root, string path) => System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');

    internal static (string Action, string? Reason) SuggestDir(string rel)
    {
        var name = rel.Split('/')[^1].ToLowerInvariant();
        if (name == ".git") return ("skip", "git");
        if (name == "node_modules") return ("skip", "dependencies");
        if (SkipDirs.Contains(name)) return ("skip", "report");
        if (name == ".auth" || (name == "auth" && rel.Contains("playwright", StringComparison.OrdinalIgnoreCase))) return ("drop", "secret");
        if (name.EndsWith("-snapshots", StringComparison.Ordinal) || name is "screenshots" or "downloads" or "uploads") return ("drop", "data");
        if (name is "data" or "testdata" or "test-data" or "test_data") return ("drop", "data");
        return ("keep", null);
    }

    internal static (string Action, string? Reason) SuggestFile(string rel)
    {
        var name = rel.Split('/')[^1].ToLowerInvariant();
        if (rel == ManifestName) return ("skip", "generated");
        if (name is "package-lock.json" or "yarn.lock" or "pnpm-lock.yaml" or "npm-shrinkwrap.json") return ("skip", "generated");
        if (name.EndsWith(".log", StringComparison.Ordinal) || name is ".ds_store" or "thumbs.db") return ("skip", "generated");
        if (name.StartsWith(".env", StringComparison.Ordinal) && name != ".env.example" && name != ".env.sample") return ("drop", "secret");
        if (name.Contains("storagestate", StringComparison.Ordinal) || name.EndsWith(".auth.json", StringComparison.Ordinal) || name.EndsWith(".pem", StringComparison.Ordinal) || name.EndsWith(".pfx", StringComparison.Ordinal) || name.EndsWith(".key", StringComparison.Ordinal))
            return ("drop", "secret");
        if (Regex.IsMatch(name, @"\.(spec|test)\.[cm]?[jt]sx?$")) return ("drop", "test");
        if (Regex.IsMatch(name, @"\.(csv|xlsx?|png|jpe?g|gif|webp|pdf|zip|har|sqlite|db)$")) return ("drop", "data");
        return ("keep", null);
    }

    /// <summary>
    /// Writes the template: the old one goes to C:\Ember\Config\template-backups first, then the kept
    /// files are copied, package.json gets $projectname$, a baseURL in the config becomes an
    /// environment variable, and every folder whose files were left out stays with a .gitkeep.
    /// </summary>
    public static int Save(string project, string target, IReadOnlyCollection<string> keep, string? name, string? description, string account)
    {
        var entries = Preview(project);
        var keepSet = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
        var projectName = ReadName(project);

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            Backup(target);
            foreach (var f in Directory.EnumerateFiles(target)) File.Delete(f);
            foreach (var d in Directory.EnumerateDirectories(target)) Directory.Delete(d, recursive: true);
        }
        Directory.CreateDirectory(target);

        int count = 0;
        foreach (var e in entries.Where(e => e.Action != "skip"))
        {
            var dest = System.IO.Path.Combine(new[] { target }.Concat(e.Path.Split('/')).ToArray());
            if (e.Dir)
            {
                Directory.CreateDirectory(dest);
                continue;
            }
            if (!keepSet.Contains(e.Path)) continue;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            var source = System.IO.Path.Combine(new[] { project }.Concat(e.Path.Split('/')).ToArray());
            if (IsText(e.Path)) File.WriteAllText(dest, Scrub(e.Path, File.ReadAllText(source), projectName));
            else File.Copy(source, dest);
            count++;
        }
        // Empty folders stay part of the structure (git would drop them otherwise).
        foreach (var d in Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories))
            if (!Directory.EnumerateFileSystemEntries(d).Any()) File.WriteAllText(System.IO.Path.Combine(d, ".gitkeep"), "");

        var manifest = new JsonObject
        {
            ["name"] = string.IsNullOrWhiteSpace(name) ? projectName ?? "Playwright" : name.Trim(),
            ["description"] = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            ["createdFrom"] = System.IO.Path.GetFileName(project.TrimEnd('\\', '/')),
            ["createdBy"] = account,
            ["created"] = DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture),
            ["ember"] = AppInfo.Version,
        };
        File.WriteAllText(System.IO.Path.Combine(target, ManifestName), Json.Format(manifest) + "\n");
        Log.Info(L.T("playwright.template.saved", ("path", target), ("count", count)));
        return count;
    }

    static string? ReadName(string project)
    {
        try
        {
            return (JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(project, "package.json"))) as JsonObject)?["name"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The project's own name becomes $projectname$; a fixed baseURL becomes process.env.BASE_URL.</summary>
    internal static string Scrub(string path, string text, string? projectName)
    {
        var file = path.Split('/')[^1].ToLowerInvariant();
        if (file == "package.json")
        {
            try
            {
                if (JsonNode.Parse(text) is JsonObject pkg)
                {
                    pkg["name"] = "$projectname$";
                    pkg["version"] = "1.0.0";
                    pkg.Remove("description");
                    pkg.Remove("author");
                    pkg.Remove("repository");
                    pkg.Remove("bugs");
                    pkg.Remove("homepage");
                    return Json.WriteLike(pkg, text);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // not JSON after all: copy as is
            }
            return text;
        }
        if (file.StartsWith("playwright.", StringComparison.Ordinal) && file.Contains(".config.", StringComparison.Ordinal))
            text = Regex.Replace(text, @"baseURL\s*:\s*(['""`])https?://[^'""`]*\1", "baseURL: process.env.BASE_URL");
        if (!string.IsNullOrWhiteSpace(projectName) && projectName.Length >= 3 && file is "readme.md")
            text = text.Replace(projectName, "$projectname$", StringComparison.Ordinal);
        return text;
    }

    static void Backup(string target)
    {
        var dir = System.IO.Path.Combine(Paths.ConfigDir, "template-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        foreach (var f in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            var dest = System.IO.Path.Combine(dir, System.IO.Path.GetRelativePath(target, f));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            File.Copy(f, dest);
        }
        foreach (var old in Directory.EnumerateDirectories(System.IO.Path.GetDirectoryName(dir)!).OrderByDescending(d => d, StringComparer.Ordinal).Skip(10))
            Directory.Delete(old, recursive: true);
        Log.Info(L.T("playwright.template.backup", ("path", dir)));
    }
}

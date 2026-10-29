using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ember.Core;

/// <param name="File">The config file Playwright uses for Edge.</param>
/// <param name="Command">How to run the tests with it ("npm test" or "npm run test:edge").</param>
public sealed record PlaywrightEdgeSetup(string File, string Command, bool Created);

/// <summary>
/// The Ember template for Playwright projects: the layout "npm init playwright" creates (config,
/// example test, .gitignore, VS Code recommendation), with reports that do not pop up a browser and,
/// on request, the Microsoft Edge that is already installed instead of downloaded browsers.
/// </summary>
public static class PlaywrightTemplate
{
    /// <summary>"chromium", "all" (Chromium, Firefox, WebKit) or "msedge" (installed Edge, no download).</summary>
    public static string Mode(string? value) => value is "all" or "msedge" ? value : "chromium";

    public static readonly string[] ConfigNames =
        { "playwright.config.ts", "playwright.config.mts", "playwright.config.cts", "playwright.config.js", "playwright.config.mjs", "playwright.config.cjs" };

    static readonly string[] Reserved = { "node_modules", "favicon.ico", "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9" };

    /// <summary>npm package rules (lower case, URL-safe), and the name must work as a Windows folder.</summary>
    public static bool ValidName(string? name) =>
        name is { Length: > 0 and <= 214 } &&
        Regex.IsMatch(name, @"^[a-z0-9][a-z0-9._-]*$") &&
        !name.EndsWith('.') &&
        !Reserved.Contains(name.Split('.')[0]);

    /// <summary>Files of a new project, by relative path. npm adds @playwright/test afterwards (latest version).</summary>
    public static Dictionary<string, string> NewProject(string name, string mode)
    {
        var package = new JsonObject
        {
            ["name"] = name,
            ["version"] = "1.0.0",
            ["private"] = true,
            ["scripts"] = Scripts(mode),
        };
        return new Dictionary<string, string>
        {
            ["package.json"] = Json.Format(package) + "\n",
            ["playwright.config.ts"] = Config(mode),
            ["tests/example.spec.ts"] = ExampleTest,
            [".gitignore"] = MergeIgnore(null)!,
            [".vscode/extensions.json"] = "{\n  \"recommendations\": [\"ms-playwright.playwright\"]\n}\n",
            ["README.md"] = Readme(name, mode),
        };
    }

    /// <summary>Creates the project folder and writes the template. The folder must not exist yet or be empty.</summary>
    public static List<string> Write(string parent, string name, string mode)
    {
        if (!ValidName(name)) throw new EmberException("playwrightNameInvalid", ("name", name));
        if (!Directory.Exists(parent)) throw new EmberException("folderMissing", ("path", parent));
        var dir = Path.Combine(parent, name);
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
            throw new EmberException("playwrightFolderNotEmpty", ("path", dir));

        var files = NewProject(name, mode);
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(new[] { dir }.Concat(relative.Split('/')).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        return files.Keys.ToList();
    }

    /// <summary>
    /// After "npm install -D @playwright/test" in an existing project: config and example test only if
    /// there are none yet, the usual .gitignore entries and the npm scripts that are still missing.
    /// </summary>
    public static List<string> Scaffold(string dir, string mode)
    {
        var written = new List<string>();
        if (!ConfigNames.Any(n => File.Exists(Path.Combine(dir, n))))
        {
            File.WriteAllText(Path.Combine(dir, "playwright.config.ts"), Config(mode));
            written.Add("playwright.config.ts");
            if (!Directory.Exists(Path.Combine(dir, "tests")))
            {
                Directory.CreateDirectory(Path.Combine(dir, "tests"));
                File.WriteAllText(Path.Combine(dir, "tests", "example.spec.ts"), ExampleTest);
                written.Add("tests/example.spec.ts");
            }
        }
        var ignore = Path.Combine(dir, ".gitignore");
        if (MergeIgnore(File.Exists(ignore) ? File.ReadAllText(ignore) : null) is { } merged)
        {
            File.WriteAllText(ignore, merged);
            written.Add(".gitignore");
        }
        if (AddScripts(dir, Scripts(mode))) written.Add("package.json");
        return written;
    }

    /// <summary>
    /// Points the project at the installed Microsoft Edge (channel "msedge"): without a config, a new
    /// playwright.config.ts for Edge; with one, playwright.msedge.config.* next to it, which keeps
    /// everything from the existing config and only swaps the browsers, plus "npm run test:edge".
    /// </summary>
    public static PlaywrightEdgeSetup UseEdge(string dir)
    {
        var project = Playwright.Inspect(dir);
        if (project.Package == null) throw new EmberException("playwrightNotInProject", ("path", dir));
        if (project.Package != "@playwright/test") throw new EmberException("playwrightEdgeLibrary", ("package", project.Package));

        var edgeFile = ConfigNames.Select(EdgeConfigName).FirstOrDefault(n => File.Exists(Path.Combine(dir, n)));
        if (edgeFile != null) return new PlaywrightEdgeSetup(edgeFile, "npm run test:edge", false);
        if (project.Config == null)
        {
            File.WriteAllText(Path.Combine(dir, "playwright.config.ts"), Config("msedge"));
            AddScripts(dir, Scripts("msedge"));
            return new PlaywrightEdgeSetup("playwright.config.ts", "npm test", true);
        }
        if (project.UsesEdge) return new PlaywrightEdgeSetup(project.Config, "npm test", false);

        var (file, content) = EdgeConfig(project.Config);
        File.WriteAllText(Path.Combine(dir, file), content);
        AddScripts(dir, new JsonObject { ["test:edge"] = "playwright test -c " + file });
        return new PlaywrightEdgeSetup(file, "npm run test:edge", true);
    }

    static string EdgeConfigName(string config) => config.Replace("playwright.config.", "playwright.msedge.config.");

    /// <summary>playwright.msedge.config.* that imports the existing config and switches its Chromium projects to Edge.</summary>
    internal static (string File, string Content) EdgeConfig(string existing)
    {
        var typescript = existing.EndsWith("ts", StringComparison.Ordinal);
        var file = typescript ? "playwright.msedge.config.ts" : "playwright.msedge.config.mjs";
        // TypeScript resolves the import without an extension; Node's ES modules need the exact file name.
        var import = typescript ? "./" + Path.GetFileNameWithoutExtension(existing) : "./" + existing;
        var content = $$"""
            import { defineConfig, devices } from '@playwright/test';
            import base from '{{import}}';

            // Same as {{existing}}, but the Chromium projects run in the Microsoft Edge installed on
            // Windows, so Playwright does not need to download any browsers. Firefox and WebKit would
            // need a download and are left out. Start with: npm run test:edge
            const chromium = (base.projects ?? []).filter((p) => !p.use?.browserName || p.use.browserName === 'chromium');

            export default defineConfig({
              ...base,
              projects: chromium.length
                ? chromium.map((p) => ({ ...p, use: { ...p.use, channel: 'msedge' } }))
                : [{ name: 'msedge', use: { ...devices['Desktop Edge'], channel: 'msedge' } }],
            });

            """;
        return (file, content);
    }

    public static string Config(string mode)
    {
        var projects = Mode(mode) switch
        {
            "msedge" => new[] { "{ name: 'msedge', use: { ...devices['Desktop Edge'], channel: 'msedge' } }," },
            "all" => new[]
            {
                "{ name: 'chromium', use: { ...devices['Desktop Chrome'] } },",
                "{ name: 'firefox', use: { ...devices['Desktop Firefox'] } },",
                "{ name: 'webkit', use: { ...devices['Desktop Safari'] } },",
            },
            _ => new[] { "{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }," },
        };
        var edgeNote = Mode(mode) == "msedge"
            ? "\n// Tests run in the Microsoft Edge installed on Windows: no browser download needed.\n"
            : "\n";
        return $$"""
            import { defineConfig, devices } from '@playwright/test';

            // Ember template, see https://playwright.dev/docs/test-configuration{{edgeNote}}export default defineConfig({
              testDir: './tests',
              fullyParallel: true,
              forbidOnly: !!process.env.CI,
              retries: process.env.CI ? 2 : 0,
              workers: process.env.CI ? 1 : undefined,
              reporter: [['list'], ['html', { open: 'never' }]],
              use: {
                // baseURL: 'http://localhost:3000',
                trace: 'on-first-retry',
                screenshot: 'only-on-failure',
              },
              projects: [
            {{string.Join("\n", projects.Select(p => "    " + p))}}
              ],
            });

            """;
    }

    internal const string ExampleTest = """
        import { test, expect } from '@playwright/test';

        test('has title', async ({ page }) => {
          await page.goto('https://playwright.dev/');
          await expect(page).toHaveTitle(/Playwright/);
        });

        test('get started link', async ({ page }) => {
          await page.goto('https://playwright.dev/');
          await page.getByRole('link', { name: 'Get started' }).click();
          await expect(page.getByRole('heading', { name: 'Installation' })).toBeVisible();
        });

        """;

    static JsonObject Scripts(string mode) => new()
    {
        ["test"] = "playwright test",
        ["test:ui"] = "playwright test --ui",
        ["test:headed"] = "playwright test --headed",
        ["report"] = "playwright show-report",
        ["codegen"] = Mode(mode) == "msedge" ? "playwright codegen --channel msedge" : "playwright codegen",
    };

    static string Readme(string name, string mode)
    {
        var de = L.Language == "de";
        var browsers = Mode(mode) switch
        {
            "msedge" => de ? "Die Tests laufen im installierten Microsoft Edge, ein Browser-Download ist nicht nötig." : "Tests run in the installed Microsoft Edge, no browser download needed.",
            "all" => de ? "Die Tests laufen in Chromium, Firefox und WebKit." : "Tests run in Chromium, Firefox and WebKit.",
            _ => de ? "Die Tests laufen in Chromium." : "Tests run in Chromium.",
        };
        var text = de
            ? $"""
              # {name}

              Playwright-Tests, angelegt mit Ember. {browsers}

              | Befehl | Was passiert |
              |---|---|
              | `npm test` | alle Tests ausführen |
              | `npm run test:ui` | Tests im UI-Modus ansehen und einzeln starten |
              | `npm run test:headed` | Tests mit sichtbarem Browserfenster |
              | `npm run report` | den letzten HTML-Bericht öffnen |
              | `npm run codegen` | Klicks im Browser aufzeichnen und als Test ausgeben |

              Tests liegen in `tests/`, die Einstellungen in `playwright.config.ts`. Mehr unter https://playwright.dev/docs/intro.

              """
            : $"""
              # {name}

              Playwright tests, created with Ember. {browsers}

              | Command | What it does |
              |---|---|
              | `npm test` | run all tests |
              | `npm run test:ui` | browse and run tests in UI mode |
              | `npm run test:headed` | run tests with a visible browser window |
              | `npm run report` | open the last HTML report |
              | `npm run codegen` | record clicks in the browser as a test |

              Tests live in `tests/`, settings in `playwright.config.ts`. More at https://playwright.dev/docs/intro.

              """;
        return L.Swiss ? L.ToSwiss(text) : text;
    }

    static readonly string[] IgnoreEntries =
        { "node_modules/", "/test-results/", "/playwright-report/", "/blob-report/", "/playwright/.cache/", "/playwright/.auth/" };

    /// <summary>The Playwright entries missing from a .gitignore; null when nothing is missing.</summary>
    internal static string? MergeIgnore(string? existing)
    {
        static string Key(string line) => line.Trim().Trim('/');
        var present = (existing ?? "").Split('\n').Select(Key).ToHashSet(StringComparer.Ordinal);
        var missing = IgnoreEntries.Where(e => !present.Contains(Key(e))).ToList();
        if (missing.Count == 0) return null;
        if (string.IsNullOrEmpty(existing)) return string.Join("\n", missing) + "\n";
        var newline = existing.Contains("\r\n") ? "\r\n" : "\n";
        var start = existing.EndsWith('\n') ? "" : newline;
        return existing + start + newline + "# Playwright" + newline + string.Join(newline, missing) + newline;
    }

    /// <summary>Adds the npm scripts that are still missing (npm's placeholder "test" counts as missing).</summary>
    static bool AddScripts(string dir, JsonObject scripts)
    {
        var path = Path.Combine(dir, "package.json");
        var before = File.ReadAllText(path);
        var after = WithScripts(before, scripts);
        if (after == before) return false;
        File.WriteAllText(path, after);
        return true;
    }

    internal static string WithScripts(string json, JsonObject scripts)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) as JsonObject
                   ?? throw new EmberException("playwrightPackageJson", ("detail", "{ … }"));
        }
        catch (JsonException ex)
        {
            throw new EmberException("playwrightPackageJson", ("detail", ex.Message));
        }
        if (root["scripts"] is not JsonObject existing)
        {
            if (root["scripts"] != null) return json;
            root["scripts"] = existing = new JsonObject();
        }
        var changed = false;
        foreach (var (name, command) in scripts)
        {
            var current = existing[name]?.GetValue<string>();
            if (current != null && !(name == "test" && current.Contains("no test specified", StringComparison.Ordinal))) continue;
            existing[name] = command?.GetValue<string>();
            changed = true;
        }
        // Keep the file's own indentation and line endings, like npm does.
        return changed ? Json.WriteLike(root, json) : json;
    }
}

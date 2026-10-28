using System.Text.Json;

namespace Ember.Core;

public sealed class ExeAsset
{
    public string Name { get; set; } = "Ember.exe";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    /// <summary>Last release in which the exe's code changed. Older exes update; newer ones only get the UI.</summary>
    public string CodeVersion { get; set; } = "0.0.0";
}

public sealed class UiAsset
{
    public string Name { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Version { get; set; } = "0.0.0";
    public int RequiresBridge { get; set; } = 1;
}

/// <summary>latest.json, attached to every GitHub release (and optionally copied to a mirror share).</summary>
public sealed class UpdateManifest
{
    public string Version { get; set; } = "0.0.0";
    public int Bridge { get; set; } = 1;
    public DateTime? Published { get; set; }
    public ExeAsset Exe { get; set; } = new();
    public UiAsset Ui { get; set; } = new();
    public string? Notes { get; set; }
}

public sealed class UiManifest
{
    public string Version { get; set; } = "0.0.0-dev";
    public int RequiresBridge { get; set; } = 1;
}

public sealed record UpdateStatus(
    string Current, string UiCurrent, string? Latest, bool UiAvailable, bool ExeAvailable,
    string? UiReady, bool ExeReady, bool Checking, DateTime? LastCheck, string? Error, string? Notes, string Source);

/// <summary>Downloaded UI packages under C:\Ember\Cache\ui\&lt;version&gt;.</summary>
public static class UiPackages
{
    static string BadFile => Path.Combine(Paths.UiDir, "bad.json");

    public static UiManifest? ReadManifest(string dir)
    {
        try
        {
            var file = Path.Combine(dir, "ui-manifest.json");
            return File.Exists(file) ? Json.Deserialize<UiManifest>(File.ReadAllText(file)) : null;
        }
        catch
        {
            return null;
        }
    }

    public static HashSet<string> BadVersions()
    {
        try
        {
            return File.Exists(BadFile) ? Json.Deserialize<HashSet<string>>(File.ReadAllText(BadFile)) ?? new() : new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>A UI that did not start is never used again.</summary>
    public static void MarkBad(string version)
    {
        var bad = BadVersions();
        bad.Add(version);
        Directory.CreateDirectory(Paths.UiDir);
        File.WriteAllText(BadFile, Json.Serialize(bad));
    }

    /// <summary>Newest downloaded UI that this exe can run and that is newer than <paramref name="than"/>.</summary>
    public static (string Dir, UiManifest Manifest)? BestDownloaded(string than)
    {
        if (!Directory.Exists(Paths.UiDir)) return null;
        var bad = BadVersions();
        return Directory.GetDirectories(Paths.UiDir)
            .Where(d => !Path.GetFileName(d).StartsWith("embedded", StringComparison.OrdinalIgnoreCase) && !d.EndsWith(".tmp"))
            .Select(d => (Dir: d, Manifest: ReadManifest(d)))
            .Where(x => x.Manifest != null && x.Manifest.RequiresBridge <= AppInfo.BridgeVersion && !bad.Contains(x.Manifest.Version) &&
                        File.Exists(Path.Combine(x.Dir, "index.html")) && AppInfo.CompareVersions(x.Manifest.Version, than) > 0)
            .OrderByDescending(x => x.Manifest!.Version, Comparer<string>.Create(AppInfo.CompareVersions))
            .Select(x => ((string, UiManifest)?)(x.Dir, x.Manifest!))
            .FirstOrDefault();
    }

    /// <summary>Removes downloaded packages except the one in use.</summary>
    public static void Cleanup(string keepDir)
    {
        try
        {
            if (!Directory.Exists(Paths.UiDir)) return;
            foreach (var d in Directory.GetDirectories(Paths.UiDir))
            {
                if (Path.GetFullPath(d).Equals(Path.GetFullPath(keepDir), StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(d).StartsWith("embedded", StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(d, recursive: true); } catch { }
            }
        }
        catch
        {
            // not important
        }
    }
}

/// <summary>
/// Checks GitHub releases (or the mirror share from team.json) and downloads updates in the
/// background. UI updates are applied live by the host (reload, no restart); an exe update
/// needs a short restart.
/// </summary>
public sealed class Updater
{
    readonly Func<EffectiveSettings> _settings;
    readonly Func<TeamConfig> _team;
    UpdateManifest? _manifest;
    int _checking;

    public Updater(Func<EffectiveSettings> settings, Func<TeamConfig> team)
    {
        _settings = settings;
        _team = team;
        Status = new UpdateStatus(AppInfo.Version, AppInfo.UiVersion, null, false, false, null, false, false, null, null, null, "github");
    }

    public UpdateStatus Status { get; private set; }

    public event Action<UpdateStatus>? Changed;

    public static string ExeNewPath => Paths.InstalledExe + ".new";

    string? Mirror => _team().Update?.Mirror is { Length: > 0 } m ? m : null;

    void Publish(UpdateStatus s)
    {
        Status = s;
        Changed?.Invoke(s);
    }

    public async Task CheckAsync(bool download, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        var source = Mirror != null ? "mirror" : "github";
        Publish(Status with { Checking = true, Error = null, Source = source, UiCurrent = AppInfo.UiVersion });
        try
        {
            var manifest = await FetchManifestAsync(ct);
            _manifest = manifest;
            bool uiNewer = AppInfo.CompareVersions(manifest.Ui.Version, AppInfo.UiVersion) > 0;
            bool uiCompatible = manifest.Ui.RequiresBridge <= AppInfo.BridgeVersion;
            bool uiAvailable = uiNewer && uiCompatible && !UiPackages.BadVersions().Contains(manifest.Ui.Version);
            bool exeAvailable = AppInfo.CompareVersions(manifest.Exe.CodeVersion, AppInfo.Version) > 0 || (uiNewer && !uiCompatible);

            Publish(Status with
            {
                Latest = manifest.Version, UiAvailable = uiAvailable, ExeAvailable = exeAvailable,
                LastCheck = DateTime.Now, Notes = manifest.Notes, Checking = download && (uiAvailable || exeAvailable),
            });
            Log.Info(L.T("update.checked", ("latest", manifest.Version), ("current", AppInfo.Version), ("ui", AppInfo.UiVersion)));

            download &= !AppInfo.IsPrerelease;   // test builds only report, they never replace themselves
            if (download && uiAvailable) await DownloadUiAsync(manifest, ct);
            if (download && exeAvailable && AppInfo.Mode == "installed") await DownloadExeAsync(manifest, ct);
            Publish(Status with { Checking = false });
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Warn(L.T("update.failed", ("detail", Net.Describe(ex))));
            Publish(Status with { Checking = false, Error = Net.Describe(ex), LastCheck = DateTime.Now });
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    async Task<UpdateManifest> FetchManifestAsync(CancellationToken ct)
    {
        string text;
        if (Mirror is { } mirror)
        {
            text = await Task.Run(() => File.ReadAllText(Path.Combine(mirror, "latest.json")), ct).WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
        else
        {
            var (http, _) = await Net.SmartClientAsync(_settings(), TimeSpan.FromSeconds(30));
            using (http)
            {
                using var response = await http.GetAsync($"https://github.com/{AppInfo.GitHubRepo}/releases/latest/download/latest.json", ct);
                Net.EnsureOk(response);
                text = await response.Content.ReadAsStringAsync(ct);
            }
        }
        return Json.Deserialize<UpdateManifest>(text) ?? throw new JsonException("latest.json is empty");
    }

    async Task<string> FetchAssetAsync(UpdateManifest m, string name, string target, CancellationToken ct)
    {
        if (Mirror is { } mirror)
        {
            await Task.Run(() => File.Copy(Path.Combine(mirror, name), target, overwrite: true), ct);
        }
        else
        {
            var (http, _) = await Net.SmartClientAsync(_settings(), TimeSpan.FromMinutes(15));
            using (http)
                await Net.DownloadAsync(http, $"https://github.com/{AppInfo.GitHubRepo}/releases/download/v{m.Version}/{name}", target, null, ct);
        }
        return await Net.Sha256Async(target, ct);
    }

    async Task DownloadUiAsync(UpdateManifest m, CancellationToken ct)
    {
        var dir = Path.Combine(Paths.UiDir, m.Ui.Version);
        if (UiPackages.ReadManifest(dir)?.Version == m.Ui.Version)
        {
            Publish(Status with { UiReady = m.Ui.Version });
            return;
        }
        var zip = Path.Combine(Paths.Ensure(Paths.DownloadDir), m.Ui.Name);
        var hash = await FetchAssetAsync(m, m.Ui.Name, zip, ct);
        if (!hash.Equals(m.Ui.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new EmberException("shaMismatch", ("expected", m.Ui.Sha256), ("actual", hash));

        var tmp = dir + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Zip.ExtractSafe(zip, tmp, ct);
        if (UiPackages.ReadManifest(tmp)?.Version != m.Ui.Version) throw new EmberException("uiPackageInvalid");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.Move(tmp, dir);
        File.Delete(zip);
        Log.Info(L.T("update.uiReady", ("version", m.Ui.Version)));
        Publish(Status with { UiReady = m.Ui.Version });
    }

    async Task DownloadExeAsync(UpdateManifest m, CancellationToken ct)
    {
        var target = ExeNewPath;
        var hash = await FetchAssetAsync(m, m.Exe.Name, target, ct);
        if (!hash.Equals(m.Exe.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            throw new EmberException("shaMismatch", ("expected", m.Exe.Sha256), ("actual", hash));
        }
        Log.Info(L.T("update.exeReady", ("version", m.Version)));
        Publish(Status with { ExeReady = true });
    }

    /// <summary>Called by the host after it switched to a new UI.</summary>
    public void UiApplied(string version) =>
        Publish(Status with { UiCurrent = version, UiReady = null, UiAvailable = false });
}

namespace Ember.Core;

public enum ProxyState
{
    /// <summary>Points to px.</summary>
    Px,
    /// <summary>Points somewhere else (left untouched unless the user asks).</summary>
    Other,
    Unset,
    /// <summary>The tool is not installed.</summary>
    Missing,
    /// <summary>Shown for information only (Windows proxy / PAC).</summary>
    Info,
}

public sealed record ProxyRow(string Tool, string Label, string Value, ProxyState State, bool CanSet, bool CanRemove, string? Hint);

/// <summary>
/// Per tool: read the current proxy, point it to px, or remove it again.
/// Ember never stores passwords – px signs in with Kerberos, the tools only talk to 127.0.0.1.
/// </summary>
public static class ProxyTools
{
    public static readonly string[] Ids = { "windows", "env", "git", "npm", "nuget", "pip", "vscode" };

    public static bool IsOurs(string? value, EffectiveSettings s)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim().TrimEnd('/');
        return v.Equals(s.PxUrl, StringComparison.OrdinalIgnoreCase) ||
               v.Equals($"http://localhost:{s.Port}", StringComparison.OrdinalIgnoreCase) ||
               v.Equals($"127.0.0.1:{s.Port}", StringComparison.OrdinalIgnoreCase);
    }

    static ProxyState StateOf(EffectiveSettings s, params string?[] values)
    {
        var set = values.Where(v => !string.IsNullOrWhiteSpace(v) && v != "null" && v != "undefined").ToList();
        if (set.Count == 0) return ProxyState.Unset;
        return set.All(v => IsOurs(v, s)) ? ProxyState.Px : ProxyState.Other;
    }

    public static async Task<List<ProxyRow>> DiagnoseAsync(EffectiveSettings s, CancellationToken ct)
    {
        var rows = new List<ProxyRow>();
        foreach (var id in Ids)
        {
            try
            {
                rows.Add(await DiagnoseAsync(id, s, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(new ProxyRow(id, L.T($"proxy.tools.{id}"), Net.Describe(ex), ProxyState.Other, false, false, null));
            }
        }
        return rows;
    }

    static async Task<ProxyRow> DiagnoseAsync(string id, EffectiveSettings s, CancellationToken ct)
    {
        var label = L.T($"proxy.tools.{id}");
        switch (id)
        {
            case "windows":
            {
                var pac = Net.ReadPacFromRegistry();
                var sys = Net.ReadSystemProxyFromRegistry();
                var value = pac != null ? "PAC " + pac : sys ?? L.T("proxy.none");
                return new ProxyRow(id, label, value, ProxyState.Info, false, false, L.T("proxy.hints.windows"));
            }
            case "env":
            {
                var http = Environment.GetEnvironmentVariable("HTTP_PROXY", EnvironmentVariableTarget.User);
                var https = Environment.GetEnvironmentVariable("HTTPS_PROXY", EnvironmentVariableTarget.User);
                var no = Environment.GetEnvironmentVariable("NO_PROXY", EnvironmentVariableTarget.User);
                var value = $"HTTP_PROXY={http ?? "–"} · HTTPS_PROXY={https ?? "–"} · NO_PROXY={no ?? "–"}";
                var state = StateOf(s, http, https);
                return new ProxyRow(id, label, value, state, state != ProxyState.Px, state == ProxyState.Px, null);
            }
            case "git":
            {
                if (Tools.Git == null) return Missing(id, label);
                var r = await ProcessRunner.RunAsync(Tools.Git, new[] { "config", "--global", "--get", "http.proxy" }, ct, logOutput: false);
                var value = r.LastLine;
                var state = StateOf(s, value);
                return new ProxyRow(id, label, Shown(value), state, state != ProxyState.Px, state == ProxyState.Px, null);
            }
            case "npm":
            {
                if (Tools.Npm == null) return Missing(id, label);
                var p = (await ProcessRunner.RunCmdAsync($"\"{Tools.Npm}\" config get proxy", ct, 60, logOutput: false)).LastLine;
                var hp = (await ProcessRunner.RunCmdAsync($"\"{Tools.Npm}\" config get https-proxy", ct, 60, logOutput: false)).LastLine;
                var state = StateOf(s, p, hp);
                return new ProxyRow(id, label, $"proxy={Shown(p)} · https-proxy={Shown(hp)}", state,
                                    state != ProxyState.Px, state == ProxyState.Px, null);
            }
            case "nuget":
            {
                var value = NuGetConfigEditor.Get(Tools.NuGetConfig, "http_proxy");
                var state = StateOf(s, value);
                return new ProxyRow(id, label, Shown(value), state, state != ProxyState.Px, state == ProxyState.Px, L.T("proxy.hints.nuget"));
            }
            case "pip":
            {
                var value = IniFile.Get(Tools.PipIni, "global", "proxy");
                var state = StateOf(s, value);
                return new ProxyRow(id, label, Shown(value), state, state != ProxyState.Px, state == ProxyState.Px, null);
            }
            case "vscode":
            {
                if (!Tools.VsCodeInstalled) return Missing(id, label);
                var text = File.Exists(Tools.VsCodeSettings) ? await File.ReadAllTextAsync(Tools.VsCodeSettings, ct) : "";
                var value = JsoncEditor.GetString(text, "http.proxy");
                var state = StateOf(s, value);
                return new ProxyRow(id, label, Shown(value), state, state != ProxyState.Px, state == ProxyState.Px, L.T("proxy.hints.vscode"));
            }
        }
        throw new EmberException("unknownTool", ("tool", id));
    }

    static ProxyRow Missing(string id, string label) =>
        new(id, label, L.T("proxy.toolMissing"), ProxyState.Missing, false, false, null);

    static string Shown(string? value) => string.IsNullOrWhiteSpace(value) || value == "null" ? "–" : value;

    /// <summary>Points a tool at px. Returns a short report for the UI.</summary>
    public static async Task<string> SetAsync(string id, EffectiveSettings s, CancellationToken ct)
    {
        switch (id)
        {
            case "env":
                // SetEnvironmentVariable(User) notifies all windows and can block for a moment.
                await Task.Run(() =>
                {
                    Environment.SetEnvironmentVariable("HTTP_PROXY", s.PxUrl, EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable("HTTPS_PROXY", s.PxUrl, EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable("NO_PROXY", s.NoProxyList, EnvironmentVariableTarget.User);
                }, ct);
                return $"HTTP(S)_PROXY={s.PxUrl} · NO_PROXY={s.NoProxyList}";

            case "git":
            {
                var git = Tools.Git ?? throw new EmberException("toolMissing", ("tool", "git"));
                var r = await ProcessRunner.RunAsync(git, new[] { "config", "--global", "http.proxy", s.PxUrl }, ct);
                if (!r.Ok) throw new EmberException("commandFailed", ("command", "git config"), ("detail", r.Output));
                return "git config --global http.proxy " + s.PxUrl;
            }

            case "npm":
            {
                var npm = Tools.Npm ?? throw new EmberException("toolMissing", ("tool", "npm"));
                foreach (var (key, value) in new[] { ("proxy", s.PxUrl), ("https-proxy", s.PxUrl), ("noproxy", s.NoProxyList) })
                {
                    var r = await ProcessRunner.RunCmdAsync($"\"{npm}\" config set {key} {value}", ct);
                    if (!r.Ok) throw new EmberException("commandFailed", ("command", "npm config set " + key), ("detail", r.Output));
                }
                return "npm: proxy, https-proxy, noproxy";
            }

            case "nuget":
                NuGetConfigEditor.Set(Tools.NuGetConfig, "http_proxy", s.PxUrl);
                return $"http_proxy = {s.PxUrl} → {Tools.NuGetConfig}";

            case "pip":
                IniFile.Set(Tools.PipIni, "global", "proxy", s.PxUrl);
                return $"proxy = {s.PxUrl} → {Tools.PipIni}";

            case "vscode":
            {
                var path = Tools.VsCodeSettings;
                var text = File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : "";
                if (text.Length > 0) File.Copy(path, path + ".ember.bak", overwrite: true);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, JsoncEditor.SetString(text, "http.proxy", s.PxUrl), ct);
                return $"\"http.proxy\": \"{s.PxUrl}\" → {path}";
            }
        }
        throw new EmberException("unknownTool", ("tool", id));
    }

    /// <summary>Removes the proxy of a tool – by default only if it points to px (foreign settings stay).</summary>
    public static async Task<string> RemoveAsync(string id, EffectiveSettings s, CancellationToken ct, bool onlyIfOurs = true)
    {
        switch (id)
        {
            case "env":
            {
                var removed = new List<string>();
                var kept = new List<string>();
                await Task.Run(() =>
                {
                    foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY" })
                    {
                        var value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
                        if (value == null) continue;
                        if (!onlyIfOurs || IsOurs(value, s))
                        {
                            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
                            removed.Add(name);
                        }
                        else kept.Add($"{name}={value}");
                    }
                    var noProxy = Environment.GetEnvironmentVariable("NO_PROXY", EnvironmentVariableTarget.User);
                    if (noProxy != null && (!onlyIfOurs || noProxy.StartsWith("localhost,127.0.0.1,::1", StringComparison.OrdinalIgnoreCase)))
                    {
                        Environment.SetEnvironmentVariable("NO_PROXY", null, EnvironmentVariableTarget.User);
                        removed.Add("NO_PROXY");
                    }
                }, ct);
                foreach (var k in kept) Log.Warn(L.T("proxy.keptForeign", ("value", k)));
                return removed.Count > 0 ? L.T("proxy.removed", ("items", string.Join(", ", removed))) : L.T("proxy.nothingToRemove");
            }

            case "git":
            {
                var git = Tools.Git ?? throw new EmberException("toolMissing", ("tool", "git"));
                var current = await ProcessRunner.RunAsync(git, new[] { "config", "--global", "--get", "http.proxy" }, ct);
                if (current.LastLine.Length == 0) return L.T("proxy.nothingToRemove");
                if (onlyIfOurs && !IsOurs(current.LastLine, s)) return L.T("proxy.unchanged", ("value", current.LastLine));
                var r = await ProcessRunner.RunAsync(git, new[] { "config", "--global", "--unset", "http.proxy" }, ct);
                if (!r.Ok) throw new EmberException("commandFailed", ("command", "git config --unset"), ("detail", r.Output));
                return L.T("proxy.removed", ("items", "http.proxy"));
            }

            case "npm":
            {
                var npm = Tools.Npm ?? throw new EmberException("toolMissing", ("tool", "npm"));
                var current = (await ProcessRunner.RunCmdAsync($"\"{npm}\" config get proxy", ct)).LastLine;
                if (onlyIfOurs && !IsOurs(current, s)) return L.T("proxy.unchanged", ("value", current));
                foreach (var key in new[] { "proxy", "https-proxy", "noproxy" })
                {
                    var r = await ProcessRunner.RunCmdAsync($"\"{npm}\" config delete {key}", ct);
                    if (!r.Ok) Log.Warn($"npm config delete {key}: {r.Output}");
                }
                return L.T("proxy.removed", ("items", "proxy, https-proxy, noproxy"));
            }

            case "nuget":
            {
                var current = NuGetConfigEditor.Get(Tools.NuGetConfig, "http_proxy");
                if (current == null) return L.T("proxy.nothingToRemove");
                if (onlyIfOurs && !IsOurs(current, s)) return L.T("proxy.unchanged", ("value", current));
                NuGetConfigEditor.Set(Tools.NuGetConfig, "http_proxy", null);
                return L.T("proxy.removed", ("items", "http_proxy"));
            }

            case "pip":
            {
                var current = IniFile.Get(Tools.PipIni, "global", "proxy");
                if (current == null) return L.T("proxy.nothingToRemove");
                if (onlyIfOurs && !IsOurs(current, s)) return L.T("proxy.unchanged", ("value", current));
                IniFile.Set(Tools.PipIni, "global", "proxy", null);
                return L.T("proxy.removed", ("items", "proxy"));
            }

            case "vscode":
            {
                var path = Tools.VsCodeSettings;
                if (!File.Exists(path)) return L.T("proxy.nothingToRemove");
                var text = await File.ReadAllTextAsync(path, ct);
                var current = JsoncEditor.GetString(text, "http.proxy");
                if (current == null) return L.T("proxy.nothingToRemove");
                if (onlyIfOurs && !IsOurs(current, s)) return L.T("proxy.unchanged", ("value", current));
                await File.WriteAllTextAsync(path, JsoncEditor.SetString(text, "http.proxy", null), ct);
                return L.T("proxy.removed", ("items", "http.proxy"));
            }
        }
        throw new EmberException("unknownTool", ("tool", id));
    }
}

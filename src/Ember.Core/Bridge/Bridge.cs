using System.Text.Json;

namespace Ember.Core;

/// <summary>
/// JSON messages between the web UI and the host.
/// Request  (UI → host): { "id": 7, "method": "px.status", "params": { … } }
/// Response (host → UI): { "id": 7, "result": … }  or  { "id": 7, "error": { "code", "message", "params" } }
/// Event    (host → UI): { "event": "log", "data": … }
/// </summary>
public sealed class Bridge
{
    public delegate Task<object?> Handler(JsonElement args, CancellationToken ct);

    readonly Dictionary<string, Handler> _methods = new(StringComparer.Ordinal);

    /// <summary>Serialized messages for the UI. The host posts them to WebView2.</summary>
    public event Action<string>? Outgoing;

    public IReadOnlyCollection<string> Methods => _methods.Keys;

    /// <summary>Checked before every call; false answers with the error "accessDenied".</summary>
    public Func<string, bool>? Guard { get; set; }

    public void Register(string method, Handler handler) => _methods[method] = handler;

    public void Register(string method, Func<CancellationToken, Task<object?>> handler) =>
        _methods[method] = (_, ct) => handler(ct);

    public void Register(string method, Func<object?> handler) =>
        _methods[method] = (_, _) => Task.FromResult(handler());

    public void Emit(string name, object? data) =>
        Outgoing?.Invoke(JsonSerializer.Serialize(new { @event = name, data }, Json.Options));

    /// <summary>Handles one request. Never throws: errors go back to the UI as error responses.</summary>
    public async Task HandleAsync(string json, CancellationToken ct = default)
    {
        object? id = null;
        string method = "?";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var i)) id = i.Clone();
            method = root.TryGetProperty("method", out var m) ? m.GetString() ?? "?" : "?";
            var args = root.TryGetProperty("params", out var p) ? p.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
            if (!_methods.TryGetValue(method, out var handler)) throw new EmberException("unknownMethod", ("method", method));
            if (Guard != null && !Guard(method)) throw new EmberException("accessDenied");
            var result = await handler(args, ct);
            Send(new { id, result });
        }
        catch (EmberException ex)
        {
            Log.Debug($"bridge {method}: {ex.Code} {ex.Message}");
            Send(new { id, error = new { code = ex.Code, message = ex.Message, @params = ex.Params } });
        }
        catch (Exception ex)
        {
            Log.Warn($"{method}: {Net.Describe(ex)}");
            Log.Debug(ex.ToString());
            var code = ex is OperationCanceledException ? "cancelled" : "internal";
            Send(new { id, error = new { code, message = Net.Describe(ex), @params = new Dictionary<string, object?>() } });
        }
    }

    void Send(object message) => Outgoing?.Invoke(JsonSerializer.Serialize(message, Json.Options));
}

/// <summary>Helpers to read request parameters.</summary>
public static class Args
{
    public static string Req(this JsonElement args, string name, string? fallback = null)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString()!;
        return fallback ?? throw new EmberException("missingParam", ("name", name));
    }

    public static string? OptStr(this JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static bool Flag(this JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static T Get<T>(this JsonElement args, string name) where T : class =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
            ? Json.Deserialize<T>(v) ?? throw new EmberException("missingParam", ("name", name))
            : throw new EmberException("missingParam", ("name", name));

    public static Dictionary<string, string?> Dict(this JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? v.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString())
            : new Dictionary<string, string?>();
}

using System.Text.Json;

namespace Ember.Core.Tests;

[Collection("language")]
public class BridgeTests
{
    static async Task<JsonElement> Call(Bridge bridge, string json)
    {
        string? answer = null;
        bridge.Outgoing += m => answer = m;
        await bridge.HandleAsync(json);
        return JsonDocument.Parse(answer!).RootElement.Clone();
    }

    [Fact]
    public async Task Returns_results_with_the_request_id()
    {
        var bridge = new Bridge();
        bridge.Register("echo", (args, _) => Task.FromResult<object?>(new { name = args.Req("name") }));
        var r = await Call(bridge, """{ "id": 7, "method": "echo", "params": { "name": "Jürgen Mößner" } }""");
        Assert.Equal(7, r.GetProperty("id").GetInt32());
        Assert.Equal("Jürgen Mößner", r.GetProperty("result").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Translated_errors_carry_code_and_params()
    {
        L.Configure("de");
        var bridge = new Bridge();
        bridge.Register("fail", () => throw new EmberException("toolMissing", ("tool", "git")));
        var r = await Call(bridge, """{ "id": 1, "method": "fail" }""");
        var error = r.GetProperty("error");
        Assert.Equal("toolMissing", error.GetProperty("code").GetString());
        Assert.Equal("git wurde nicht gefunden.", error.GetProperty("message").GetString());
        Assert.Equal("git", error.GetProperty("params").GetProperty("tool").GetString());
    }

    [Fact]
    public async Task Unknown_methods_and_broken_json_do_not_throw()
    {
        var bridge = new Bridge();
        var r = await Call(bridge, """{ "id": 2, "method": "nope" }""");
        Assert.Equal("unknownMethod", r.GetProperty("error").GetProperty("code").GetString());

        var broken = await Call(bridge, "{ not json");
        Assert.Equal("internal", broken.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Events_are_serialized_camel_case_with_umlauts()
    {
        var bridge = new Bridge();
        string? message = null;
        bridge.Outgoing += m => message = m;
        bridge.Emit("px.status", new PxStatus(true, true, true, false, "http://127.0.0.1:3128", false));
        Assert.Contains("\"event\":\"px.status\"", message);
        Assert.Contains("\"portOpen\":true", message);

        bridge.Emit("log", new[] { new LogEntry(DateTime.Now, LogLevel.Warning, "ember", "Größe prüfen") });
        Assert.Contains("Größe prüfen", message);
        Assert.Contains("\"level\":\"warning\"", message);
    }
}

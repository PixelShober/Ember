using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core;

public static class Json
{
    /// <summary>
    /// camelCase, enums as strings, umlauts written as-is (no ü escapes) – both for the
    /// bridge and for files people open in an editor.
    /// </summary>
    public static readonly JsonSerializerOptions Options = Create(indented: false);

    public static readonly JsonSerializerOptions Indented = Create(indented: true);

    static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }

    public static string Serialize<T>(T value, bool indented = false) =>
        JsonSerializer.Serialize(value, indented ? Indented : Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static T? Deserialize<T>(JsonElement element) => element.Deserialize<T>(Options);
}

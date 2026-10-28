using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ember.Core;

/// <summary>
/// Minimal, comment-preserving edits of VS Code's settings.json (JSON with comments).
/// Only a single top-level string value is touched; everything else stays byte for byte.
/// </summary>
public static class JsoncEditor
{
    static Regex KeyValue(string key) =>
        new("\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);

    public static string? GetString(string text, string key)
    {
        var m = KeyValue(key).Match(text);
        return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
    }

    /// <summary>Sets (or with value == null removes) "key": "value".</summary>
    public static string SetString(string text, string key, string? value)
    {
        var regex = KeyValue(key);
        var m = regex.Match(text);
        if (value == null)
        {
            if (!m.Success) return text;
            // "key": "v",  → remove the whole member including indentation and trailing comma
            var withComma = new Regex("[ \\t]*" + regex + "[ \\t]*,[ \\t]*(\\r?\\n)?");
            if (withComma.IsMatch(text)) return withComma.Replace(text, "", 1);
            // last member: remove the comma before it instead
            var before = new Regex(",\\s*" + regex);
            return before.IsMatch(text) ? before.Replace(text, "", 1) : regex.Replace(text, "", 1);
        }

        var literal = "\"" + key + "\": \"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        if (m.Success) return text[..m.Index] + literal + text[(m.Index + m.Length)..];

        if (text.Trim().Length == 0) return "{\n    " + literal + "\n}\n";
        int brace = text.IndexOf('{');
        if (brace < 0) throw new FormatException("settings.json has no object");
        bool empty = Regex.IsMatch(text[(brace + 1)..], @"^\s*}");
        if (empty) return text[..(brace + 1)] + "\n    " + literal + "\n" + text[(text.IndexOf('}', brace))..];
        return text[..(brace + 1)] + "\n    " + literal + "," + text[(brace + 1)..];
    }
}

/// <summary>Reads and writes &lt;config&gt;&lt;add key="http_proxy" value="…"/&gt; in NuGet.Config.</summary>
public static class NuGetConfigEditor
{
    public static string? Get(string path, string key)
    {
        if (!File.Exists(path)) return null;
        var doc = XDocument.Load(path);
        return doc.Root?.Element("config")?.Elements("add")
                  .FirstOrDefault(e => string.Equals((string?)e.Attribute("key"), key, StringComparison.OrdinalIgnoreCase))
                  ?.Attribute("value")?.Value;
    }

    public static void Set(string path, string key, string? value)
    {
        var doc = File.Exists(path)
            ? XDocument.Load(path, LoadOptions.PreserveWhitespace)
            : new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("configuration"));
        var root = doc.Root ?? throw new FormatException("NuGet.Config has no root");
        var config = root.Element("config");
        if (config == null)
        {
            if (value == null) return;
            config = new XElement("config");
            root.Add(config);
        }
        var entry = config.Elements("add")
                          .FirstOrDefault(e => string.Equals((string?)e.Attribute("key"), key, StringComparison.OrdinalIgnoreCase));
        if (value == null) entry?.Remove();
        else if (entry != null) entry.SetAttributeValue("value", value);
        else config.Add(new XElement("add", new XAttribute("key", key), new XAttribute("value", value)));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        doc.Save(path);
    }
}

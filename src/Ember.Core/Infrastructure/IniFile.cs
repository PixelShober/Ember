using System.Text.RegularExpressions;

namespace Ember.Core;

/// <summary>Minimal INI editor (pip.ini). Keeps comments and unrelated keys untouched.</summary>
public static class IniFile
{
    public static string? Get(string path, string section, string key)
    {
        if (!File.Exists(path)) return null;
        var lines = File.ReadAllLines(path);
        var (start, end) = FindSection(lines, section);
        if (start < 0) return null;
        for (int i = start + 1; i < end; i++)
        {
            var m = Regex.Match(lines[i], $@"^\s*{Regex.Escape(key)}\s*[=:]\s*(.*)$", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();
        }
        return null;
    }

    /// <summary>Sets a value; value == null removes the key.</summary>
    public static void Set(string path, string section, string key, string? value)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var (start, end) = FindSection(lines, section);

        if (start < 0)
        {
            if (value == null) return;
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add($"[{section}]");
            lines.Add($"{key} = {value}");
        }
        else
        {
            int keyIndex = -1;
            for (int i = start + 1; i < end; i++)
                if (Regex.IsMatch(lines[i], $@"^\s*{Regex.Escape(key)}\s*[=:]", RegexOptions.IgnoreCase)) { keyIndex = i; break; }

            if (keyIndex >= 0)
            {
                if (value == null) lines.RemoveAt(keyIndex);
                else lines[keyIndex] = $"{key} = {value}";
            }
            else if (value != null)
            {
                int insertAt = end;
                while (insertAt > start + 1 && lines[insertAt - 1].Trim().Length == 0) insertAt--;
                lines.Insert(insertAt, $"{key} = {value}");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllLines(path, lines);
    }

    static (int start, int end) FindSection(IReadOnlyList<string> lines, string section)
    {
        int start = -1;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase)) { start = i; break; }
        if (start < 0) return (-1, -1);
        int end = lines.Count;
        for (int i = start + 1; i < lines.Count; i++)
            if (lines[i].TrimStart().StartsWith('[')) { end = i; break; }
        return (start, end);
    }
}

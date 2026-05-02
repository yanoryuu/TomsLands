using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class InfoBrokerDialogueLoader
{
    private const string CsvResourceName = "InfoBrokerDialogues";

    private static Dictionary<string, string> _cache;

    public static string Get(string id)
    {
        if (_cache == null) LoadAll();
        if (_cache != null && _cache.TryGetValue(id, out var msg)) return msg;
        return string.Empty;
    }

    public static void ClearCache() => _cache = null;

    private static void LoadAll()
    {
        _cache = new Dictionary<string, string>();

        var textAsset = Resources.Load<TextAsset>(CsvResourceName);
        if (textAsset == null)
        {
            Debug.LogError($"[InfoBrokerDialogueLoader] Resources/{CsvResourceName}.csv が見つかりません。");
            return;
        }

        var lines = textAsset.text.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var columns = SplitCsvLine(line);
            if (columns.Count < 2) continue;

            _cache[columns[0].Trim()] = columns[1].Trim();
        }
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new StringBuilder();

        foreach (char c in line)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(c);
        }

        result.Add(current.ToString());
        return result;
    }
}


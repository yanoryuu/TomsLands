using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class DebtDataLoader
{
    private const string CsvResourceName = "DebtData";
    private static List<DebtData> _cache;

    public static List<DebtData> LoadAll()
    {
        if (_cache != null) return _cache;

        var textAsset = Resources.Load<TextAsset>(CsvResourceName);
        if (textAsset == null)
        {
            Debug.LogError($"[DebtDataLoader] Resources/{CsvResourceName}.csv が見つかりません。");
            return new List<DebtData>();
        }

        _cache = ParseCsv(textAsset.text);
        Debug.Log($"[DebtDataLoader] {_cache.Count}件の借金データを読み込みました。");
        return _cache;
    }

    /// <summary>
    /// 指定サイクルの返済額を返す。定義外のサイクルは最終行の金額を使用。
    /// </summary>
    public static int GetAmount(int cycle)
    {
        var data = LoadAll();
        if (data.Count == 0) return 0;

        foreach (var d in data)
        {
            if (d.Cycle == cycle) return d.Amount;
        }

        return data[data.Count - 1].Amount;
    }

    public static void ClearCache() => _cache = null;

    private static List<DebtData> ParseCsv(string csvText)
    {
        var result = new List<DebtData>();
        var lines = csvText.Split('\n');

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var columns = SplitCsvLine(line);
            if (columns.Count < 2) continue;

            if (int.TryParse(columns[0].Trim(), out int cycle) &&
                int.TryParse(columns[1].Trim(), out int amount))
            {
                result.Add(new DebtData(cycle, amount));
            }
        }

        return result;
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

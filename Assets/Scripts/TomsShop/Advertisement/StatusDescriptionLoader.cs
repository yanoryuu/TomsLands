using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Resources/StatusDescriptions.csv からステータス説明文を読み込むローダー。
/// CSV形式: statusType,title,message
/// </summary>
public static class StatusDescriptionLoader
{
    private const string CsvResourceName = "StatusDescriptions";

    private static Dictionary<StatusType, (string title, string message)> _cache;

    /// <summary>
    /// 指定ステータスの (タイトル, 本文) を返す。未ロードなら自動で読み込む。
    /// CSVに該当行がなければ ("", "") を返す。
    /// </summary>
    public static (string title, string message) Get(StatusType type)
    {
        if (_cache == null) LoadAll();
        if (_cache != null && _cache.TryGetValue(type, out var entry)) return entry;
        return (string.Empty, string.Empty);
    }

    /// <summary>
    /// キャッシュをクリアする（主にエディタ再読み込み用）
    /// </summary>
    public static void ClearCache()
    {
        _cache = null;
    }

    private static void LoadAll()
    {
        _cache = new Dictionary<StatusType, (string, string)>();

        var textAsset = AddressableLoader.Load<TextAsset>(CsvResourceName);
        if (textAsset == null)
        {
            Debug.LogError($"[StatusDescriptionLoader] Resources/{CsvResourceName}.csv が見つかりません。");
            return;
        }

        var lines = textAsset.text.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var columns = SplitCsvLine(line);
            if (columns.Count < 3) continue;

            var typeStr = columns[0].Trim();
            if (!Enum.TryParse<StatusType>(typeStr, out var type))
            {
                Debug.LogWarning($"[StatusDescriptionLoader] 未知の StatusType: '{typeStr}' (行 {i + 1})");
                continue;
            }

            var msg = columns[2].Trim().Replace("\\n", "\n");
            _cache[type] = (columns[1].Trim(), msg);
        }

        Debug.Log($"[StatusDescriptionLoader] Loaded {_cache.Count} status descriptions.");
    }

    /// <summary>
    /// CSV行をカンマで分割する。ダブルクォートで囲まれたカンマは区切りとして扱わない。
    /// </summary>
    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result;
    }
}

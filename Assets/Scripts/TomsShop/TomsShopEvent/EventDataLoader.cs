using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resources/EventDatas.csv からイベントデータを読み込むローダー。
/// CSV形式: id,title,description,command1,param1Key1,param1Value1,command2,param2Key1,param2Value1
/// </summary>
public static class EventDataLoader
{
    private static List<TomsEvent> _cachedEvents;

    /// <summary>
    /// CSVからイベント一覧を読み込む（キャッシュあり）
    /// </summary>
    public static List<TomsEvent> LoadAll()
    {
        if (_cachedEvents != null) return _cachedEvents;

        var textAsset = AddressableLoader.Load<TextAsset>("EventDatas");
        if (textAsset == null)
        {
            Debug.LogError("[EventDataLoader] Resources/EventDatas.csv が見つかりません。");
            return new List<TomsEvent>();
        }

        _cachedEvents = ParseCsv(textAsset.text);
        Debug.Log($"[EventDataLoader] Loaded {_cachedEvents.Count} events from CSV.");
        return _cachedEvents;
    }

    /// <summary>
    /// IDでイベントを検索する
    /// </summary>
    public static TomsEvent FindById(string eventId)
    {
        var events = LoadAll();
        foreach (var e in events)
        {
            if (e.id == eventId) return e;
        }

        Debug.LogWarning($"[EventDataLoader] Event not found: {eventId}");
        return null;
    }

    /// <summary>
    /// キャッシュをクリアする
    /// </summary>
    public static void ClearCache()
    {
        _cachedEvents = null;
    }

    /// <summary>
    /// CSVテキストをパースしてTomsEventリストに変換
    /// </summary>
    private static List<TomsEvent> ParseCsv(string csvText)
    {
        var result = new List<TomsEvent>();
        var lines = csvText.Split('\n');

        // 1行目はヘッダーなのでスキップ
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var columns = SplitCsvLine(line);
            if (columns.Count < 3) continue;

            var e = new TomsEvent
            {
                id = columns[0].Trim(),
                title = columns[1].Trim(),
                description = columns[2].Trim(),
                commands = new List<TomsEventCommand>()
            };

            // command1,param1Key1,param1Value1, command2,param2Key1,param2Value1, ...
            // CSVカラムインデックス: 3,4,5 → command1, 6,7,8 → command2, ...
            int colIndex = 3;
            for (int cmdNum = 1; cmdNum <= 10; cmdNum++)
            {
                if (colIndex >= columns.Count) break;

                string commandName = colIndex < columns.Count ? columns[colIndex].Trim() : "";
                colIndex++;

                if (string.IsNullOrEmpty(commandName)) break;

                var cmd = new TomsEventCommand
                {
                    command = commandName,
                    parameters = new Dictionary<string, string>()
                };

                // paramKey, paramValue のペア
                if (colIndex < columns.Count)
                {
                    string paramKey = columns[colIndex].Trim();
                    colIndex++;

                    if (colIndex < columns.Count)
                    {
                        string paramValue = columns[colIndex].Trim();
                        colIndex++;

                        if (!string.IsNullOrEmpty(paramKey))
                        {
                            cmd.parameters[paramKey] = paramValue;
                        }
                    }
                }

                e.commands.Add(cmd);
            }

            result.Add(e);
        }

        return result;
    }

    /// <summary>
    /// CSV行をカンマで分割する（簡易版。ダブルクォートで囲まれたカンマは無視）
    /// </summary>
    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

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


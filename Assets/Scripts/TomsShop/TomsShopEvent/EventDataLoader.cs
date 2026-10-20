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

        var baked = ParseCsv(textAsset.text);

        // サーバー配信（balance.json の events 区画）があれば全置換、無ければCSVのベイク値
        _cachedEvents = RemoteBalance.ApplyEvents(baked);
        Debug.Log($"[EventDataLoader] Loaded {_cachedEvents.Count} events (CSV={baked.Count}, remote={( _cachedEvents != baked ? "適用" : "なし")}).");
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
            // ゼロ幅スペース（U+200B）は取り除く。ふりがな付きの文から貼り付けた名残で、検索・置換・文字数の計測を狂わせる
            var line = lines[i].Replace("\u200B", "").Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var columns = SplitCsvLine(line);
            if (columns.Count < 3) continue;

            // 空行（idだけでタイトルが無い行）は抽選プールに入れない
            if (string.IsNullOrWhiteSpace(columns[0]) || string.IsNullOrWhiteSpace(columns[1])) continue;

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

            WarnNonNumericAmount(e);
            result.Add(e);
        }

        return result;
    }

    /// <summary>
    /// amount が数値でない行をエラーで知らせる。実行時は 0 扱いで「（効果なし）」になり、気づきにくい
    /// （2026-10 まで同梱の 007〜013・019〜021 が "Trust" や空欄のまま空振りしていた）。
    /// </summary>
    private static void WarnNonNumericAmount(TomsEvent e)
    {
        foreach (var cmd in e.commands)
        {
            if (cmd.parameters.TryGetValue("amount", out var raw) && !int.TryParse(raw, out _))
                Debug.LogError($"[EventDataLoader] イベント {e.id}「{e.title}」の {cmd.command}.amount が数値ではありません（'{raw}'）。EventDatas.csv を直してください。");
        }
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


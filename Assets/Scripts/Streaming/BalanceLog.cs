using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// バランス調整の計測ログ（2026-10-30。審議 panel_1030b の stream_result、panel_1030e の影のログ）。
/// 1 行 = 1 件の JSON。エディタと開発ビルドだけで書く（製品ビルドでは何もしない）。
/// - Recordings/balance_log_&lt;RunTag または yyyyMMdd&gt;.jsonl（persona_log_*.jsonl と同じフォルダ。マラソンのラン ID で分かれる）
/// - echo = true の行は Editor.log / Player.log にも「[BalanceLog] {json}」で出す（ログを読む分析スクリプト用）
/// 種類（kind）: stream_result（配信 1 本の決着） / boss_situation（ボス・中ボスの状況。影） / heat_turn（毎ターンの熱。影・ファイルだけ）。
/// 配信の間は BeginStream で渡したダンジョン・Lv・日などが各行に付く。
/// </summary>
public static class BalanceLog
{
    /// <summary>テスト用: 設定するとファイル・コンソールに書かずにここへ渡す。</summary>
    public static Action<JObject> TestSink { get; set; }

    private static JObject _stream;
    private static int _streamSeq;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        _stream = null;
        _streamSeq = 0;
    }

    /// <summary>配信の始まり。以後の行にこの配信の情報を付ける。</summary>
    public static void BeginStream(string dungeon, int level, int day, int flowIndex, float clearPct, SupportOddsTier tier)
    {
        _streamSeq++;
        _stream = new JObject
        {
            ["stream"] = $"{DateTime.Now:HHmmss}_{_streamSeq}",
            ["dungeon"] = dungeon,
            ["level"] = level,
            ["day"] = day,
            ["flowIndex"] = flowIndex,
            ["clearPct"] = clearPct >= 0f ? Math.Round(clearPct, 1) : (double?)null,
            ["estimateTier"] = SupportOdds.Key(tier),
        };
    }

    public static void EndStream() => _stream = null;

    public static void Write(string kind, JObject payload, bool echo = true)
    {
        var line = new JObject
        {
            ["kind"] = kind,
            ["time"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        if (_stream != null)
            foreach (var p in _stream.Properties()) line[p.Name] = p.Value.DeepClone();
        if (payload != null)
            foreach (var p in payload.Properties()) line[p.Name] = p.Value.DeepClone();

        if (TestSink != null)
        {
            TestSink(line);
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        string tag = SafeTag(PersonaLog.RunTag);
        if (tag != null) line["run"] = tag;
        string json = line.ToString(Formatting.None);
        if (echo) Debug.Log("[BalanceLog] " + json);
        try
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"balance_log_{tag ?? DateTime.Now.ToString("yyyyMMdd")}.jsonl");
            File.AppendAllText(path, json + "\n", new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[BalanceLog] 書けませんでした: {e.Message}");
        }
#endif
    }

    private static string SafeTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;
        var sb = new StringBuilder();
        foreach (char ch in tag) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
        return sb.Length > 0 ? sb.ToString() : null;
    }
}

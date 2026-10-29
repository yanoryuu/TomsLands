#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>判断1件の記録（persona_log_*.jsonl の1行）。</summary>
public sealed class PersonaLogEntry
{
    [JsonProperty("time")] public string Time;
    [JsonProperty("day")] public int Day;
    [JsonProperty("persona")] public string Persona;
    [JsonProperty("model")] public string Model;
    [JsonProperty("question")] public string Question;
    [JsonProperty("choice")] public string Choice;
    [JsonProperty("probabilities", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, float> Probabilities;
    [JsonProperty("confidence", NullValueHandling = NullValueHandling.Ignore)] public float? Confidence;
    [JsonProperty("line")] public string Line;
    /// <summary>一言をミームにしたときのミームの id（tools/persona_play/memes/*.json）。</summary>
    [JsonProperty("meme", NullValueHandling = NullValueHandling.Ignore)] public string Meme;
    /// <summary>めたんがミームに返したとき、きっかけのずんだもんの行（zundamon_line）。</summary>
    [JsonProperty("memeCue", NullValueHandling = NullValueHandling.Ignore)] public string MemeCue;
    [JsonProperty("fallback")] public bool Fallback;
    [JsonProperty("latencyMs", NullValueHandling = NullValueHandling.Ignore)] public long? LatencyMs;

    // ---- 学習（PersonaLearning）。rules があれば想起・学習の材料に使える行 ----
    [JsonProperty("rules", NullValueHandling = NullValueHandling.Ignore)] public int? Rules;
    /// <summary>マラソンのラン ID（PersonaLog.RunTag）。</summary>
    [JsonProperty("run", NullValueHandling = NullValueHandling.Ignore)] public string RunTag;
    [JsonProperty("decisionId", NullValueHandling = NullValueHandling.Ignore)] public string DecisionId;
    /// <summary>"day" / "stream"（特徴ベクトルの種類）。</summary>
    [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)] public string Kind;
    /// <summary>結果の付け方（nextDay / afterStream / stream）。</summary>
    [JsonProperty("horizon", NullValueHandling = NullValueHandling.Ignore)] public string Horizon;
    [JsonProperty("features", NullValueHandling = NullValueHandling.Ignore)] public float[] Features;
    /// <summary>学習器の調整（選択肢 → log 倍率）。probabilities は kev の確率のまま。</summary>
    [JsonProperty("adjust", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, float> Adjust;
    [JsonProperty("finalProbabilities", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, float> FinalProbabilities;
    /// <summary>state に載せた似た場面（英語の短文）。</summary>
    [JsonProperty("recalled", NullValueHandling = NullValueHandling.Ignore)] public List<string> Recalled;
    /// <summary>question = "resolve" の行: どの判断（decisionId）の結果か、と損益（G）。</summary>
    [JsonProperty("ref", NullValueHandling = NullValueHandling.Ignore)] public string Ref;
    [JsonProperty("reward", NullValueHandling = NullValueHandling.Ignore)] public float? Reward;

    // ---- 選び方（PersonaChooser）と相談プレイ（PersonaConsult） ----
    /// <summary>選び方の理由（sample / margin / close / explore / floor / always / hard_route / score / argmax …）。</summary>
    [JsonProperty("why", NullValueHandling = NullValueHandling.Ignore)] public string Why;
    /// <summary>場面の倍率（めたんの慎重な選び方。選択肢 → 倍率）。</summary>
    [JsonProperty("situation", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, float> Situation;
    /// <summary>相談プレイ: ずんだもんの案・めたんの助言・関係（agree / condition / disagree / unvoiced）・聞いたか。choice が最終。</summary>
    [JsonProperty("proposal", NullValueHandling = NullValueHandling.Ignore)] public string Proposal;
    [JsonProperty("advice", NullValueHandling = NullValueHandling.Ignore)] public string Advice;
    [JsonProperty("relation", NullValueHandling = NullValueHandling.Ignore)] public string Relation;
    [JsonProperty("followed", NullValueHandling = NullValueHandling.Ignore)] public bool? Followed;

    // ---- 人格の形成（Docs/Persona_Formation_Design.md。rules 4 以降） ----
    /// <summary>判断した時点の気分（conf / sad / smug / irk / label）。</summary>
    [JsonProperty("mood", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, object> Mood;
    /// <summary>結果の行: 結果で動く前と後の気分。</summary>
    [JsonProperty("moodBefore", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, object> MoodBefore;
    [JsonProperty("moodAfter", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, object> MoodAfter;
    /// <summary>一言の表情（jikkyo.py の表情: ama / hero / hiso / namida / tsun / normal / surprise）と山場の印（praise / collapse / mount / cry …）。</summary>
    [JsonProperty("e", NullValueHandling = NullValueHandling.Ignore)] public string E;
    [JsonProperty("beat", NullValueHandling = NullValueHandling.Ignore)] public string Beat;
    /// <summary>選んだ手の大胆さ b・下振れ d・難しい道 hard（0〜1）。</summary>
    [JsonProperty("b", NullValueHandling = NullValueHandling.Ignore)] public float? B;
    [JsonProperty("d", NullValueHandling = NullValueHandling.Ignore)] public float? D;
    [JsonProperty("hard", NullValueHandling = NullValueHandling.Ignore)] public float? Hard;
    /// <summary>流儀 S(a)（選択肢 → log 倍率）と人格の事前分布 p_kev·exp(S)（技能なし）。</summary>
    [JsonProperty("style", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, float> Style;
    [JsonProperty("priorProbabilities", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, float> PriorProbabilities;
    /// <summary>KL の上限で縮めた後の技能の重み（1 = 縮めていない）と、そのときの KL（nat）。</summary>
    [JsonProperty("skillWeight", NullValueHandling = NullValueHandling.Ignore)] public float? SkillWeight;
    [JsonProperty("kl", NullValueHandling = NullValueHandling.Ignore)] public float? Kl;
    /// <summary>エントロピーの下限で上げた温度（1 = 上げていない）。</summary>
    [JsonProperty("entropyTemp", NullValueHandling = NullValueHandling.Ignore)] public float? EntropyTemp;
    /// <summary>手持ちの苦しさ（0〜1。返済が近くて重い・現金が少ない）。</summary>
    [JsonProperty("cashStress", NullValueHandling = NullValueHandling.Ignore)] public float? CashStress;
    /// <summary>ゲームの速度（Time.timeScale）。</summary>
    [JsonProperty("timeScale", NullValueHandling = NullValueHandling.Ignore)] public float? TimeScale;
}

/// <summary>
/// 判断ごとの一言を Recordings/persona_log_&lt;yyyyMMdd_HHmmss&gt;.jsonl に書く（録画と同じフォルダ。.gitignore 済み）。
/// 1 回のオートプレイ（戦略の Reset ごと）に 1 ファイル。マラソン中は RunTag をファイル名と各行に付ける。
/// </summary>
public sealed class PersonaLog
{
    private readonly string _path;
    private static readonly JsonSerializerSettings Settings = new() { NullValueHandling = NullValueHandling.Ignore };

    public string FilePath => _path;

    /// <summary>
    /// マラソン（Editor の PersonaMarathon）のラン ID。Play に入った直後に設定される（Play ごとに null に戻る）。
    /// 設定されていればファイル名を persona_log_&lt;日時&gt;_&lt;RunTag&gt;.jsonl にし、各行の run に入れる。
    /// </summary>
    public static string RunTag { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRunTag() => RunTag = null;

    public PersonaLog()
    {
        string dir = Path.Combine(PersonaPlayConfig.ProjectRoot, "Recordings");
        string tag = SafeTag(RunTag);
        _path = Path.Combine(dir, $"persona_log_{DateTime.Now:yyyyMMdd_HHmmss}{(tag != null ? "_" + tag : "")}.jsonl");
    }

    private static string SafeTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;
        var sb = new StringBuilder();
        foreach (char ch in tag) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
        return sb.Length > 0 ? sb.ToString() : null;
    }

    public void Write(PersonaLogEntry entry)
    {
        if (entry == null) return;
        entry.Time ??= DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        entry.RunTag ??= RunTag;
        entry.TimeScale ??= (float)System.Math.Round(Time.timeScale, 2);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.AppendAllText(_path, JsonConvert.SerializeObject(entry, Settings) + "\n", new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] ログを書けませんでした: {_path}: {e.Message}");
        }
    }
}
#endif

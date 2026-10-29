#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// =====================================================================
// 人格プレイの学習（2026-10-27 ユーザー決定の ② 似た場面の想起 と ③ kev の確率に掛ける小さな学習器）。
//
// - 判断ごとに盤面の特徴（数値 10 個。ダンジョン名は使わない: #135 で弱点がランごとにシャッフルされるため）を作り、
//   結果（損益）が出たら「判断 + 特徴 + 損益」を 1 件として人格ごとに貯める（UserSettings/PersonaMemory/<persona>_recall.json）。
// - 想起: 今の盤面に近い過去の判断を K 件だけ英語の短文にして state.similarPastSituations に載せる（件数は履歴の長さによらず一定）。
// - 学習器: 質問 × 選択肢ごとのリッジ回帰（特徴 → 損益）。予測の差（その質問の kev の確率で重み付けした平均との差）を
//   advantage として p ∝ p_kev × exp(λ × advantage)。λ は人格ごと（めたん大・ずんだもん小）。
//   件数が少ないうちはリッジ（0 への縮み）で予測がほぼ 0 になるので、運の良し悪し 1 回では動かない。
//   UserSettings/PersonaMemory/<persona>_model.json（質問 × 選択肢の数で大きさが決まる。件数では増えない）。
// 純粋なロジックだけ（ゲームのモデルには触らない）。
// =====================================================================

/// <summary>盤面の特徴ベクトル（0〜1 に正規化した 11 個）と、その英語の説明。</summary>
public static class PersonaFeatures
{
    /// <summary>
    /// ルールの版。弱点シャッフル（#135）と見せ場の配信熱（#134）以降を 2、人格ごとのセーブを続けて村を育てる（村の総合Lv を特徴に足して 11 個）以降を 3 とする。
    /// 人格の形成（特性・気分・流儀 S(a)・人格ごとの効用・KL の上限・エントロピーの下限。Docs/Persona_Formation_Design.md）以降を 4 とする。
    /// 版が違う履歴は想起・学習に使わない。
    /// </summary>
    public const int Rules = 4;

    public const string Day = "day";
    public const string Stream = "stream";
    public const string Village = "village";
    public const int Dim = 11;

    // 結果の付け方
    public const string HorizonNextDay = "nextDay";         // 次の営業日の朝までの資産（現金＋在庫の時価）の増減
    public const string HorizonAfterStream = "afterStream"; // 次の配信が終わった後の朝までの資産の増減（情報・魔王軍支援・配信向け仕入れ）
    public const string HorizonStream = "stream";           // その配信の損益（売上＋防衛報酬−スパチャの純支出）
    public const string HorizonNextRun = "nextRun";
    public const string HorizonRunEnd = "runEnd";           // 店への投資（改装・開発・設備・取引所）: そのランの終わりまでの資産（現金＋在庫＋取引所の評価額）の増減         // 村での判断: 次のラン（1 周）の最初の営業日から終わりまでの所持金の増減

    // day:     0 day, 1 cash, 2 debtPressure, 3 debtDays, 4 streamDays, 5 weaknessKnown, 6 dungeonLevel, 7 heroLevel, 8 stockShare, 9 shopLevel, 10 villageShare
    // stream:  0 day, 1 cash, 2 heroHp, 3 monsters, 4 bossAlive, 5 dungeonLevel, 6 heroLevel, 7 weaknessKnown, 8 budgetShare, 9 supportPaid, 10 villageShare
    // village: 0 runs, 1 villageFunds, 2 investableShare, 3 hallLevel, 4 facilityLevel, 5 costShare, 6 isHall, 7 lastRunProfit, 8 rank, 9 builtShare, 10 villageShare
    //          （予算の質問では 4〜6 は 0。施設ごとの質問は質問キー invest_<施設ID> で分ける）
    // villageShare = 村の総合Lv / 全施設を最大まで建てたときの総合Lv
    private static readonly float[] DayWeights = { 1.5f, 1.5f, 1f, 0.7f, 1f, 1f, 0.7f, 0.5f, 0.7f, 0.3f, 0.5f };
    private static readonly float[] StreamWeights = { 1f, 1.2f, 1.2f, 0.7f, 0.7f, 1f, 0.7f, 0.8f, 0.5f, 1f, 0.5f };
    private static readonly float[] VillageWeights = { 0.7f, 1.5f, 0.7f, 0.8f, 1f, 1.2f, 0.5f, 1f, 0.5f, 0.5f, 1f };

    public static float[] ForDay(int day, int cash, int debtAmount, int debtDaysLeft, int streamDays, bool weaknessKnown,
        int dungeonLevel, int heroLevel, float stockShare, int shopLevel, float villageShare) =>
        new[]
        {
            Clamp01(day / 40f),
            CashScale(cash),
            Clamp01(cash > 0 ? debtAmount / (float)cash / 3f : debtAmount > 0 ? 1f : 0f),
            debtDaysLeft < 0 ? 1f : Clamp01(debtDaysLeft / 10f),
            streamDays < 0 ? 1f : Clamp01(streamDays / 5f),
            weaknessKnown ? 1f : 0f,
            Clamp01(dungeonLevel / 10f),
            Clamp01(heroLevel / 30f),
            Clamp01(stockShare),
            Clamp01(shopLevel / 10f),
            Clamp01(villageShare),
        };

    public static float[] ForStream(int day, int cash, int heroHpPct, int monsters, bool bossAlive, int dungeonLevel, int heroLevel,
        bool weaknessKnown, float budgetShare, bool supportPaid, float villageShare) =>
        new[]
        {
            Clamp01(day / 40f),
            CashScale(cash),
            heroHpPct < 0 ? 1f : Clamp01(heroHpPct / 100f),
            Clamp01(monsters / 10f),
            bossAlive ? 1f : 0f,
            Clamp01(dungeonLevel / 10f),
            Clamp01(heroLevel / 30f),
            weaknessKnown ? 1f : 0f,
            Clamp01(budgetShare),
            supportPaid ? 1f : 0f,
            Clamp01(villageShare),
        };

    /// <summary>村での判断。facilityLevel / facilityMax / cost は施設ごとの質問のときだけ（予算の質問では -1）。</summary>
    public static float[] ForVillage(int runs, int villageFunds, int investable, int facilityCount, int hallLevel, int facilityLevel, int facilityMax,
        int cost, bool isHall, int lastRunProfit, float rankShare, int built, float villageShare) =>
        new[]
        {
            Clamp01(runs / 20f),
            CashScale(villageFunds),
            facilityCount > 0 ? Clamp01(investable / (float)facilityCount) : 0f,
            Clamp01(hallLevel / 5f),
            facilityLevel < 0 || facilityMax <= 0 ? 0f : Clamp01(facilityLevel / (float)facilityMax),
            cost <= 0 ? 0f : villageFunds > 0 ? Clamp01(cost / (float)villageFunds) : 1f,
            isHall ? 1f : 0f,
            Clamp01(0.5f + lastRunProfit / 2_000_000f),
            Clamp01(rankShare),
            facilityCount > 0 ? Clamp01(built / (float)facilityCount) : 0f,
            Clamp01(villageShare),
        };

    /// <summary>log10(所持金) / 6（100 万G で 1）。</summary>
    private static float CashScale(int cash) => Clamp01(Mathf.Log10(Mathf.Max(1, cash)) / 6f);

    private static float Clamp01(float v) => float.IsNaN(v) ? 0f : Mathf.Clamp01(v);

    /// <summary>保存用に小数 3 桁へ丸める。</summary>
    public static float[] Compact(float[] f) => f?.Select(v => (float)Math.Round(v, 3)).ToArray();

    /// <summary>重み付きユークリッド距離（種類が違えば無限大）。</summary>
    public static float Distance(string kind, float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != Dim || b.Length != Dim) return float.PositiveInfinity;
        var w = kind == Stream ? StreamWeights : kind == Village ? VillageWeights : DayWeights;
        float sum = 0f;
        for (int i = 0; i < Dim; i++)
        {
            float d = a[i] - b[i];
            sum += w[i] * d * d;
        }
        return Mathf.Sqrt(sum);
    }

    // ---------------- 英語の説明（特徴から作り直すので、散文をパースしない） ----------------

    public static string DescribeSituation(string kind, float[] f)
    {
        if (f == null || f.Length != Dim) return "an earlier situation";
        int day = Mathf.RoundToInt(f[0] * 40f);
        string cash = Gold(Mathf.Pow(10f, f[1] * 6f));
        if (kind == Village)
        {
            var v = new List<string>
            {
                $"village after {Mathf.RoundToInt(f[0] * 20f)} runs",
                $"village funds ~{cash}",
                $"village {Mathf.RoundToInt(f[10] * 100f)}% built",
                $"hall Lv{Mathf.RoundToInt(f[3] * 5f)}",
            };
            if (f[5] > 0f) v.Add($"the next level cost {Mathf.RoundToInt(f[5] * 100f)}% of the funds");
            float profit = (f[7] - 0.5f) * 2_000_000f;
            if (Mathf.Abs(profit) >= 1000f) v.Add($"last run {(profit >= 0 ? "+" : "-")}{Gold(Mathf.Abs(profit))}");
            return string.Join(", ", v);
        }
        if (kind == Stream)
        {
            var parts = new List<string> { $"stream on day {day}", $"cash ~{cash}" };
            if (f[2] < 0.999f) parts.Add($"hero HP {Mathf.RoundToInt(f[2] * 100f)}%");
            parts.Add($"{Mathf.RoundToInt(f[3] * 10f)} monsters" + (f[4] > 0.5f ? " with the boss" : ""));
            parts.Add($"dungeon Lv{Mathf.RoundToInt(f[5] * 10f)}");
            if (f[9] > 0.5f) parts.Add("I had paid the demon army");
            return string.Join(", ", parts);
        }
        var p = new List<string> { $"day {day}", $"cash ~{cash}" };
        int debtDays = Mathf.RoundToInt(f[3] * 10f);
        if (f[3] < 0.999f && debtDays <= 3) p.Add(f[2] > 0.33f ? $"a heavy debt payment due in {debtDays} days" : $"debt due in {debtDays} days");
        int streamDays = Mathf.RoundToInt(f[4] * 5f);
        if (f[4] < 0.999f && streamDays <= 2)
        {
            p.Add(streamDays == 0 ? "stream tomorrow" : $"stream in {streamDays} days");
            p.Add(f[5] > 0.5f ? "weakness known" : "weakness unknown");
            p.Add($"dungeon Lv{Mathf.RoundToInt(f[6] * 10f)}");
        }
        if (f[8] > 0.5f) p.Add("most money in stock");
        return string.Join(", ", p);
    }

    private static readonly string[] IntensityWords = { "never", "rarely", "sometimes", "often", "at every chance" };

    public static string DescribeChoice(string question, string choice)
    {
        int.TryParse(choice, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level);
        switch (question)
        {
            case "budget": return $"spend about {Mathf.RoundToInt(level * 20f)}% of spendable cash";
            case "buy_style": return $"buy by {choice?.Replace('_', ' ')} order";
            case "newspaper": return choice == "true" ? "read the newspaper" : "skip the newspaper";
            case "intel": return choice == "true" ? "buy the dungeon information" : "skip the dungeon information";
            case "support":
                return level <= 0 ? "not pay the demon army" : level >= 3 ? "raise the dungeon as far as cash allowed" : $"raise the dungeon by {level} level(s)";
            case "focus_share": return $"put {level * 25}% of the budget into goods for the stream";
            case "stream_side": return choice == "hero" ? "back the hero" : choice == "demon" ? "back the dungeon" : "back nobody";
            case "intervene": return choice == "none" || string.IsNullOrEmpty(choice) ? "pay for no super chat" : $"pay mostly for the {choice} super chat";
            case "intensity": return $"pay for super chats {IntensityWords[Mathf.Clamp(level, 0, IntensityWords.Length - 1)]}";
            case "village_budget": return $"spend about {level * 25}% of the village funds";
            case "shop_upgrade": return choice == "true" ? "remodel the shop" : "skip the shop remodel";
            case "smith_upgrade": return choice == "true" ? "develop the blacksmith" : "skip developing the blacksmith";
            case "broker_upgrade": return choice == "true" ? "develop the information broker" : "skip developing the information broker";
            case "machine": return choice == "none" ? "place no machine" : $"place a {choice} machine";
            case "finance":
                return choice switch
                {
                    "none" => "not trade at the exchange",
                    "sell" => "sell a fund",
                    "bond_short" => "buy a short bond",
                    "bond_long" => "buy a long bond",
                    "fund_market" => "buy the market fund",
                    "fund_attribute" => "buy an attribute fund",
                    _ => $"trade {choice}",
                };
            case "finance_share": return $"put about {new[] { 10, 25, 50, 75 }[Mathf.Clamp(level, 0, 3)]}% of spendable cash into it";
            case "hero_weapon": return choice == "change" ? "change the hero's weapon" : "keep the hero's weapon";
            case "hero_armor": return choice == "change" ? "change the hero's armor" : "keep the hero's armor";
            case "hero_tactics": return $"use {choice} tactics";
            case "starter_relic": return $"start the run with the {choice?.Replace("relic_", "").Replace('_', ' ')} relic";
            default:
                if (question != null && question.StartsWith("invest_", StringComparison.Ordinal))
                {
                    string id = question.Substring("invest_".Length);
                    return choice == "true" ? $"invest in the {id}" : $"not invest in the {id}";
                }
                return $"{question}={choice}";
        }
    }

    public static string DescribeResult(string horizon, float reward)
    {
        string g = (reward >= 0 ? "+" : "-") + Gold(Mathf.Abs(reward));
        return horizon switch
        {
            HorizonStream => $"stream net {g}",
            HorizonAfterStream => $"net worth {g} by the day after the stream",
            HorizonNextRun => $"next run {g} from its first shop day",
            HorizonRunEnd => $"net worth {g} by the end of the run",
            _ => $"net worth {g} by the next day",
        };
    }

    /// <summary>"similar past: &lt;situation&gt; → chose X → result Y"</summary>
    public static string DescribeRecord(PersonaDecisionRecord r) =>
        $"similar past: {DescribeSituation(r.Kind, r.Features)} → chose to {DescribeChoice(r.Question, r.Choice)} → result {DescribeResult(r.Horizon, r.Reward ?? 0f)}";

    private static string Gold(float g)
    {
        if (g >= 999_500f) return (g / 1_000_000f).ToString("0.0", CultureInfo.InvariantCulture) + "M G";
        if (g >= 1000f) return (g / 1000f).ToString(g >= 10_000f ? "0" : "0.0", CultureInfo.InvariantCulture) + "k G";
        return Mathf.RoundToInt(g).ToString(CultureInfo.InvariantCulture) + " G";
    }
}

/// <summary>判断 1 件と、その結果（想起・学習の単位）。</summary>
public sealed class PersonaDecisionRecord
{
    [JsonProperty("id")] public string Id;
    [JsonProperty("t")] public string Time;
    [JsonProperty("run", NullValueHandling = NullValueHandling.Ignore)] public string RunTag;
    [JsonProperty("q")] public string Question;
    [JsonProperty("c")] public string Choice;
    [JsonProperty("k")] public string Kind;
    [JsonProperty("h")] public string Horizon;
    [JsonProperty("f")] public float[] Features;
    /// <summary>損益（G）。結果が出るまで null。</summary>
    [JsonProperty("r", NullValueHandling = NullValueHandling.Ignore)] public float? Reward;
    /// <summary>判断した時点の資産（現金＋在庫の時価。nextDay / afterStream 用。保存しない）。</summary>
    [JsonIgnore] public int StartWorth;
}

/// <summary>
/// 結果の出た判断の履歴（人格ごと、最大 MaxRecords 件）。ファイルが無ければ Recordings/persona_log_*.jsonl から作り直す。
/// </summary>
public sealed class PersonaRecallStore
{
    public const int MaxRecords = 4000;

    [JsonProperty("persona")] public string Persona;
    [JsonProperty("rules")] public int Rules = PersonaFeatures.Rules;
    [JsonProperty("records")] public List<PersonaDecisionRecord> Records = new();

    [JsonIgnore] private string _path;
    [JsonIgnore] public bool Dirty;

    public static string PathFor(string personaId) =>
        Path.Combine(PersonaPlayConfig.ProjectRoot, "UserSettings", "PersonaMemory", personaId + "_recall.json");

    public static PersonaRecallStore Load(string personaId)
    {
        string path = PathFor(personaId);
        PersonaRecallStore store = null;
        try
        {
            if (File.Exists(path)) store = JsonConvert.DeserializeObject<PersonaRecallStore>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] 想起の履歴を読めないので判断ログから作り直します: {path}: {e.Message}");
        }
        if (store == null || store.Rules != PersonaFeatures.Rules)
        {
            store = new PersonaRecallStore { Persona = personaId };
            store.RebuildFromLogs(personaId);
        }
        store.Persona = personaId;
        store.Records ??= new List<PersonaDecisionRecord>();
        store.Records.RemoveAll(r => r == null || r.Features == null || r.Features.Length != PersonaFeatures.Dim || !r.Reward.HasValue);
        store._path = path;
        return store;
    }

    /// <summary>判断ログ（新しいファイルから）の decisionId つきの判断と resolve の行を突き合わせて履歴を作る。</summary>
    private void RebuildFromLogs(string personaId)
    {
        try
        {
            string dir = Path.Combine(PersonaPlayConfig.ProjectRoot, "Recordings");
            if (!Directory.Exists(dir)) return;
            var decisions = new Dictionary<string, PersonaDecisionRecord>();
            var rewards = new Dictionary<string, float>();
            foreach (var file in Directory.GetFiles(dir, "persona_log_*.jsonl").OrderByDescending(f => f, StringComparer.Ordinal))
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.IndexOf("\"rules\"", StringComparison.Ordinal) < 0) continue;
                    JObject o;
                    try { o = JObject.Parse(line); }
                    catch (Exception) { continue; }
                    if ((string)o["persona"] != personaId || (int?)o["rules"] != PersonaFeatures.Rules) continue;
                    if ((string)o["question"] == "resolve")
                    {
                        string rf = (string)o["ref"];
                        float? rw = (float?)o["reward"];
                        if (rf != null && rw.HasValue) rewards[rf] = rw.Value;
                        continue;
                    }
                    string id = (string)o["decisionId"];
                    var feats = o["features"]?.ToObject<float[]>();
                    if (id == null || feats == null || feats.Length != PersonaFeatures.Dim) continue;
                    decisions[id] = new PersonaDecisionRecord
                    {
                        Id = id, Time = (string)o["time"], RunTag = (string)o["run"], Question = (string)o["question"],
                        Choice = (string)o["choice"], Kind = (string)o["kind"], Horizon = (string)o["horizon"], Features = feats,
                    };
                }
                if (decisions.Count >= MaxRecords * 2) break;
            }
            foreach (var kv in decisions)
            {
                if (!rewards.TryGetValue(kv.Key, out var r)) continue;
                kv.Value.Reward = r;
                Records.Add(kv.Value);
            }
            Records.Sort((a, b) => string.CompareOrdinal(a.Time ?? "", b.Time ?? ""));
            while (Records.Count > MaxRecords) Records.RemoveAt(0);
            Dirty = Records.Count > 0;
            if (Records.Count > 0) Debug.Log($"[PersonaPlay] {personaId}: 判断ログから想起の履歴を {Records.Count} 件作り直しました");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] 判断ログから想起の履歴を作れませんでした: {e.Message}");
        }
    }

    public void Add(PersonaDecisionRecord record)
    {
        if (record?.Features == null || !record.Reward.HasValue) return;
        Records.Add(record);
        while (Records.Count > MaxRecords) Records.RemoveAt(0);
        Dirty = true;
    }

    public void Save()
    {
        if (!Dirty || _path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, JsonConvert.SerializeObject(this, Formatting.None));
            Dirty = false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] 想起の履歴を書けませんでした: {_path}: {e.Message}");
        }
    }

    /// <summary>
    /// 今の盤面に近い過去の判断を k 件（同じ種類・聞く質問だけ・同じ質問は maxPerQuestion 件まで）。近い順。
    /// 同じ距離なら新しい方。
    /// </summary>
    public List<PersonaDecisionRecord> FindSimilar(string kind, float[] features, ICollection<string> questions, int k, int maxPerQuestion = 2)
    {
        var result = new List<PersonaDecisionRecord>();
        if (k <= 0 || features == null || Records.Count == 0) return result;
        var ranked = new List<(float d, int i)>();
        for (int i = 0; i < Records.Count; i++)
        {
            var r = Records[i];
            if (r.Kind != kind || (questions != null && !questions.Contains(r.Question))) continue;
            float d = PersonaFeatures.Distance(kind, features, r.Features);
            if (float.IsInfinity(d)) continue;
            ranked.Add((d, i));
        }
        ranked.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : b.i.CompareTo(a.i));
        var perQuestion = new Dictionary<string, int>();
        foreach (var (_, i) in ranked)
        {
            var r = Records[i];
            perQuestion.TryGetValue(r.Question, out int n);
            if (n >= maxPerQuestion) continue;
            perQuestion[r.Question] = n + 1;
            result.Add(r);
            if (result.Count >= k) break;
        }
        return result;
    }
}

/// <summary>
/// 質問 × 選択肢ごとのリッジ回帰（特徴＋切片 → 損益/1 万G）。古い結果は forget で少しずつ薄める。
/// </summary>
public sealed class PersonaReweighter
{
    public const int MaxPairs = 96;
    private const int D = PersonaFeatures.Dim + 1;
    /// <summary>損益の単位（1 = 1 万G）と、運の外れ値を抑える上限。</summary>
    public const float RewardScale = 10000f;
    public const float RewardClip = 5f;
    /// <summary>1 選択肢あたりの調整（log の倍率）の上限。kev の判断を消さない。</summary>
    public const float MaxLogAdjust = 2f;

    public sealed class Pair
    {
        [JsonProperty("n")] public double N;          // 実効件数（forget 込み）
        [JsonProperty("count")] public int Count;     // 生の件数
        [JsonProperty("sumR")] public double SumR;
        [JsonProperty("xtx")] public double[] XtX = new double[D * D];
        [JsonProperty("xty")] public double[] XtY = new double[D];
        /// <summary>生の損益（1 万G 単位、forget 込み）の合計。効用の「振れの罰」の平均に使う。</summary>
        [JsonProperty("sumRaw")] public double SumRaw;
    }

    [JsonProperty("persona")] public string Persona;
    [JsonProperty("rules")] public int Rules = PersonaFeatures.Rules;
    [JsonProperty("ridge")] public float Ridge = 5f;
    [JsonProperty("forget")] public float Forget = 0.998f;
    /// <summary>1 選択肢あたりの log 倍率の上限（人格ごと。Load で PersonaProfile から入れる）。</summary>
    [JsonIgnore] public float Cap = MaxLogAdjust;
    /// <summary>
    /// 予測を「その質問の平均の結果」へ縮める件数（0 = 縮めない＝2026-10-28 朝までの式）。
    /// 未経験の選択肢の予測が 0 のままだと、結果がいつもプラスの質問（配信の損益など）で一度選んだ選択肢だけが勝ち続けるため、
    /// 未経験・件数の少ない選択肢は平均扱いにする（反実仮想の公平さ）。
    /// </summary>
    [JsonIgnore] public float Shrink;
    /// <summary>件数の少ない選択肢への楽観の上乗せ（log 倍率。Optimism / √(1 + 実効件数)）。</summary>
    [JsonIgnore] public float Optimism;
    [JsonProperty("updates")] public int Updates;
    /// <summary>
    /// 技能の重みの倍率（固まりの安全弁。自由な質問の最多の手が 1 ランで 85% を超えたら次のランは半分、超えなければ 1.5 倍で 1 へ戻す）。
    /// </summary>
    [JsonProperty("skillScale")] public float SkillScale = 1f;

    // ---- 人格ごとの効用（5.2。学習器に入れる前に損益を変換する。同じ経験から 2 人が違う「良い手」を学ぶ） ----
    /// <summary>損失回避 λ（負けを λ 倍。めたん 2.25）。</summary>
    [JsonIgnore] public float LossAversion = 1f;
    /// <summary>勝ちの指数（大勝ちを重く見る。ずんだもん 1.15）。</summary>
    [JsonIgnore] public float GainPower = 1f;
    /// <summary>負けの重み（ずんだもん 0.7 = 損から学ばない）。</summary>
    [JsonIgnore] public float LossWeight = 1f;
    /// <summary>振れの罰（|x − 平均| に掛ける。めたん 0.1 = 金欠で振れが嫌い）。</summary>
    [JsonIgnore] public float VariancePenalty;
    [JsonProperty("pairs")] public Dictionary<string, Pair> Pairs = new();

    [JsonIgnore] private string _path;
    [JsonIgnore] private readonly Dictionary<string, double[]> _weights = new();

    public static string PathFor(string personaId) =>
        Path.Combine(PersonaPlayConfig.ProjectRoot, "UserSettings", "PersonaMemory", personaId + "_model.json");

    public static PersonaReweighter Load(PersonaProfile p)
    {
        var model = Load(p.Id, p.RidgePrior, p.Forget);
        model.Cap = Mathf.Clamp(p.MaxLogAdjust, 0f, MaxLogAdjust);
        model.Shrink = Mathf.Max(0f, p.ShrinkCount);
        model.Optimism = Mathf.Max(0f, p.Optimism);
        var t = p.Traits;
        if (t != null && t.LossAversion > 1.2f)
        {
            model.LossAversion = t.LossAversion;
            model.VariancePenalty = 0.1f;
        }
        else if (t != null)
        {
            model.GainPower = 1.15f;
            model.LossWeight = 0.7f;
        }
        model.SkillScale = Mathf.Clamp(model.SkillScale <= 0f ? 1f : model.SkillScale, 0.125f, 1f);
        return model;
    }

    public static PersonaReweighter Load(string personaId, float ridge, float forget)
    {
        string path = PathFor(personaId);
        PersonaReweighter model = null;
        try
        {
            if (File.Exists(path)) model = JsonConvert.DeserializeObject<PersonaReweighter>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] 学習器を読めないので空から始めます: {path}: {e.Message}");
        }
        if (model == null || model.Rules != PersonaFeatures.Rules) model = new PersonaReweighter();
        model.Persona = personaId;
        model.Ridge = Mathf.Max(0.1f, ridge);
        model.Forget = Mathf.Clamp(forget, 0.9f, 1f);
        model.Pairs ??= new Dictionary<string, Pair>();
        foreach (var key in model.Pairs.Where(kv => kv.Value?.XtX == null || kv.Value.XtX.Length != D * D || kv.Value.XtY == null || kv.Value.XtY.Length != D)
                     .Select(kv => kv.Key).ToList())
            model.Pairs.Remove(key);
        model._path = path;
        return model;
    }

    public void Save()
    {
        if (_path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, JsonConvert.SerializeObject(this, Formatting.None));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] 学習器を書けませんでした: {_path}: {e.Message}");
        }
    }

    private static string Key(string question, string choice) => question + "|" + choice;

    private static double[] WithBias(float[] x)
    {
        var v = new double[D];
        v[0] = 1.0;
        for (int i = 0; i < PersonaFeatures.Dim && i < (x?.Length ?? 0); i++) v[i + 1] = x[i];
        return v;
    }

    /// <summary>
    /// 損益（1 万G 単位）を人格の効用にする: めたん u = x（x ≥ 0）/ λ·x（x &lt; 0）− 0.1·|x − 平均|、
    /// ずんだもん u = sign(x)·|x|^1.15、負けは 0.7 倍。
    /// </summary>
    public double Utility(double x, double mean)
    {
        double u = x >= 0 ? Math.Pow(x, GainPower) : -LossWeight * LossAversion * Math.Pow(-x, GainPower);
        if (VariancePenalty > 0f) u -= VariancePenalty * Math.Abs(x - mean);
        return u;
    }

    /// <summary>結果 1 件で学習する（reward は G）。</summary>
    public void Update(string question, string choice, float[] features, float rewardGold)
    {
        if (string.IsNullOrEmpty(question) || string.IsNullOrEmpty(choice) || features == null) return;
        string key = Key(question, choice);
        if (!Pairs.TryGetValue(key, out var p))
        {
            if (Pairs.Count >= MaxPairs) return;   // 大きさの上限（選択肢は固定なのでふつうは届かない）
            Pairs[key] = p = new Pair();
        }
        double raw = Mathf.Clamp(rewardGold / RewardScale, -RewardClip, RewardClip);
        double r = Math.Max(-RewardClip, Math.Min(RewardClip, Utility(raw, p.N > 0 ? p.SumRaw / p.N : raw)));
        var x = WithBias(features);
        double g = Forget;
        p.SumRaw = p.SumRaw * g + raw;
        for (int i = 0; i < D * D; i++) p.XtX[i] *= g;
        for (int i = 0; i < D; i++) p.XtY[i] *= g;
        p.N = p.N * g + 1.0;
        p.SumR = p.SumR * g + r;
        p.Count++;
        for (int i = 0; i < D; i++)
        {
            p.XtY[i] += x[i] * r;
            for (int j = 0; j < D; j++) p.XtX[i * D + j] += x[i] * x[j];
        }
        Updates++;
        _weights.Remove(key);
    }

    /// <summary>予測した損益（1 万G 単位）。学習していなければ 0。</summary>
    public float Predict(string question, string choice, float[] features)
    {
        string key = Key(question, choice);
        if (!Pairs.TryGetValue(key, out var p) || p.Count == 0) return 0f;
        if (!_weights.TryGetValue(key, out var w))
        {
            w = Solve(p);
            _weights[key] = w;
        }
        if (w == null) return 0f;
        var x = WithBias(features);
        double y = 0;
        for (int i = 0; i < D; i++) y += w[i] * x[i];
        return (float)Math.Max(-RewardClip, Math.Min(RewardClip, y));
    }

    /// <summary>(XtX + ridge I) w = XtY をガウスの消去法で解く。</summary>
    private double[] Solve(Pair p)
    {
        var a = new double[D, D + 1];
        for (int i = 0; i < D; i++)
        {
            for (int j = 0; j < D; j++) a[i, j] = p.XtX[i * D + j] + (i == j ? Ridge : 0.0);
            a[i, D] = p.XtY[i];
        }
        for (int col = 0; col < D; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < D; r++) if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col])) pivot = r;
            if (Math.Abs(a[pivot, col]) < 1e-12) return null;
            if (pivot != col)
                for (int c = 0; c <= D; c++) (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
            for (int r = 0; r < D; r++)
            {
                if (r == col) continue;
                double f = a[r, col] / a[col, col];
                if (f == 0) continue;
                for (int c = col; c <= D; c++) a[r, c] -= f * a[col, c];
            }
        }
        var w = new double[D];
        for (int i = 0; i < D; i++) w[i] = a[i, D] / a[i, i];
        return w;
    }

    /// <summary>
    /// kev の確率を学習器で寄せる。adjust には選択肢ごとの log 倍率（λ × advantage、±MaxLogAdjust で切る）を返す。
    /// λ = 0・学習前・確率が無いときは kev の確率をそのまま返す（adjust は 0）。
    /// </summary>
    public Dictionary<string, float> Adjust(string question, IReadOnlyDictionary<string, float> kevProbs, float[] features, float lambda,
        out Dictionary<string, float> adjust)
    {
        adjust = new Dictionary<string, float>();
        var final = new Dictionary<string, float>();
        if (kevProbs == null || kevProbs.Count == 0) return final;
        var preds = new Dictionary<string, float>();
        var counts = new Dictionary<string, double>();
        double mean = Shrink > 0f ? QuestionMean(question) : 0.0;
        double total = 0, baseline = 0;
        foreach (var kv in kevProbs)
        {
            double n = Pairs.TryGetValue(Key(question, kv.Key), out var pair) ? pair.N : 0.0;
            float pred = lambda > 0f ? Predict(question, kv.Key, features) : 0f;
            // 件数の少ない（未経験の）選択肢は、その質問の平均の結果へ縮める
            if (Shrink > 0f) pred = (float)((n * pred + Shrink * mean) / (n + Shrink));
            preds[kv.Key] = pred;
            counts[kv.Key] = n;
            double pk = Math.Max(0f, kv.Value);
            total += pk;
            baseline += pk * pred;
        }
        baseline = total > 0 ? baseline / total : 0;
        double sum = 0;
        foreach (var kv in kevProbs)
        {
            float a = lambda * (float)(preds[kv.Key] - baseline);
            if (Optimism > 0f) a += Optimism / (float)Math.Sqrt(1.0 + counts[kv.Key]);
            a = Mathf.Clamp(a, -Cap, Cap);
            adjust[kv.Key] = (float)Math.Round(a, 4);
            double v = Math.Max(0f, kv.Value) * Math.Exp(a);
            final[kv.Key] = (float)v;
            sum += v;
        }
        if (sum <= 0) return kevProbs.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var key in final.Keys.ToList()) final[key] = (float)Math.Round(final[key] / sum, 5);
        return final;
    }

    /// <summary>その質問の結果の平均（1 万G 単位、forget 込みの全選択肢の合計から）。</summary>
    private double QuestionMean(string question)
    {
        double n = 0, s = 0;
        string prefix = question + "|";
        foreach (var kv in Pairs)
        {
            if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal) || kv.Value == null) continue;
            n += kv.Value.N;
            s += kv.Value.SumR;
        }
        return n > 0 ? s / n : 0.0;
    }
}
#endif

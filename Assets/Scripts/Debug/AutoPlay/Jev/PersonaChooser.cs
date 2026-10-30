#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 判断の材料のうち、特徴（PersonaFeatures）に入らない画面の数字（情報料・魔王軍支援の額・村の投資の見込みなど）。
/// 分からないものは -1 / null（場面の倍率は中立）。
/// </summary>
public sealed class PersonaSituation
{
    public string Kind;                 // PersonaFeatures.Day / Stream / Village
    public float[] Features;
    public int Cash = -1;               // 所持金（村では村資金）
    public float IntelCostShare = -1f;  // 情報料 / 所持金
    public float SupportGainRatio = -1f;// （支援後の防衛報酬 − 今の防衛報酬）/ 支援 1 レベルの額
    public string Side;                 // 配信で張った側（勢いの質問用）
    public int PlausibleCount = -1;     // 村: 元が取れそうな施設の数
    public bool? Plausible;             // 村: この施設は元が取れそうか（店の改装・開発でも使う）
    public string FacilityId;
    /// <summary>選択肢ごとの場面の倍率（店への投資・勇者の装備。戦略が画面の数字から先に作る。めたんの見方）。</summary>
    public Dictionary<string, float> OptionMult;
}

/// <summary>
/// めたん（PersonaProfile.Careful）の選び方。tools/persona_play/replay_selection.py に同じ手順の写しがある（数字を変えたら両方直す）。
///
/// 1. 学習器で寄せた確率に「場面の倍率」（画面の数字から。Situation）を掛けて正規化する。
/// 2. 選択肢: 1 位と 2 位の差が SelectMargin 以上なら 1 位。近ければ 1 位から SelectMargin 以内の候補を温度 SelectTemperature で抽選。
///    段階（score）: 倍率を掛けた分布の期待値を使い、小数部が 0.5 ± LevelBand の外なら丸めた段階、間なら近さで抽選。
/// 3. 確率 ExploreRate で、kev が 5% 以上つけた別の手（段階は隣だけ）を試す（未経験の手がいつまでも試されない、を防ぐ）。
/// 真偽（noul）は閾値 t を引いた確率 σ(logit(v) − logit(t)) を true / false の 2 択にして同じ手順を通す。
/// </summary>
public static class PersonaChooser
{
    public struct Result
    {
        public string Key;
        /// <summary>margin（はっきり 1 位）/ close（近いので場面と抽選）/ explore（試しに別の手）/ only。</summary>
        public string Why;
        /// <summary>倍率を掛けた後の、選んだ手の確率（自分の自信）。</summary>
        public float Prob;
        /// <summary>倍率を掛けた後の分布。</summary>
        public Dictionary<string, float> Shaped;
    }

    public const float ExploreMinKev = 0.05f;
    /// <summary>
    /// エントロピーの下限（5.3.2）の選び方の側: 慎重な選び方は 1 位を決め打ちするので、確率の下限だけでは選択が散らない。
    /// 自由な質問で kev が 10% 以上つけた手が 2 つ以上あるときは、別の手を試す確率をこれまで上げる（2 択で 0.85 : 0.15 ≈ 0.6 bit）。
    /// </summary>
    public const float EntropyFloorExplore = 0.15f;
    public static readonly HashSet<string> PreferredFacilities = new() { "bank", "tavern", "hall" };

    public static Result Careful(System.Random rng, PersonaProfile p, IReadOnlyDictionary<string, float> final,
        IReadOnlyDictionary<string, float> kev, IReadOnlyDictionary<string, float> mult, bool levels, float exploreRate = -1f)
    {
        float exploreP = exploreRate >= 0f ? exploreRate : p.ExploreRate;
        var keys = levels
            ? final.Keys.OrderBy(k => int.TryParse(k, out var i) ? i : 0).ToList()
            : final.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var q = new Dictionary<string, float>();
        float sum = 0f;
        foreach (var k in keys)
        {
            float v = Mathf.Max(0f, final[k]) * (mult != null && mult.TryGetValue(k, out var m) ? m : 1f);
            q[k] = v;
            sum += v;
        }
        foreach (var k in keys) q[k] = sum > 0f ? q[k] / sum : 1f / keys.Count;
        if (keys.Count == 1) return new Result { Key = keys[0], Why = "only", Prob = 1f, Shaped = q };
        var src = kev ?? final;
        double r = rng.NextDouble();

        if (levels)
        {
            float e = keys.Sum(k => Level(k) * q[k]);
            int min = Level(keys[0]), max = Level(keys[keys.Count - 1]);
            int lo = Mathf.Clamp(Mathf.FloorToInt(e), min, max);
            int hi = Mathf.Min(max, lo + 1);
            float frac = e - lo;
            string top = frac <= 0.5f - p.LevelBand ? lo.ToString() : frac >= 0.5f + p.LevelBand ? hi.ToString() : null;
            if (r < exploreP)
            {
                int center = top != null ? Level(top) : Mathf.RoundToInt(e);
                var pool = keys.Where(k => Mathf.Abs(Level(k) - center) == 1 && Get(src, k) >= ExploreMinKev).ToList();
                if (pool.Count > 0) return Make(Weighted(rng, pool, pool.Select(k => q[k] + 1e-6f).ToList()), "explore", q);
            }
            if (top != null) return Make(top, "margin", q);
            float pHi = 1f / (1f + Mathf.Exp(-(frac - 0.5f) / (0.3f * Mathf.Max(0.05f, p.SelectTemperature))));
            return Make(rng.NextDouble() < pHi ? hi.ToString() : lo.ToString(), "close", q);
        }

        var ranked = keys.OrderByDescending(k => q[k]).ToList();
        string first = ranked[0];
        var explore = keys.Where(k => k != first && Get(src, k) >= ExploreMinKev).ToList();
        if (explore.Count > 0 && r < exploreP)
            return Make(Weighted(rng, explore, explore.Select(k => q[k] + 1e-6f).ToList()), "explore", q);
        if (q[first] - q[ranked[1]] >= p.SelectMargin) return Make(first, "margin", q);
        var cands = ranked.Where(k => q[k] >= q[first] - p.SelectMargin).ToList();
        float inv = 1f / Mathf.Max(0.05f, p.SelectTemperature);
        return Make(Weighted(rng, cands, cands.Select(k => Mathf.Pow(q[k], inv)).ToList()), "close", q);
    }

    /// <summary>真偽: 閾値を引いた確率で Careful を通す。</summary>
    public static Result CarefulNoul(System.Random rng, PersonaProfile p, float v, float threshold, IReadOnlyDictionary<string, float> mult,
        float exploreRate = -1f)
    {
        float x = Logit(v) - Logit(threshold);
        float pt = 1f / (1f + Mathf.Exp(-x));
        var probs = new Dictionary<string, float> { ["true"] = pt, ["false"] = 1f - pt };
        return Careful(rng, p, probs, probs, mult, false, exploreRate);
    }

    private static Result Make(string key, string why, Dictionary<string, float> q) =>
        new() { Key = key, Why = why, Prob = q.TryGetValue(key, out var v) ? v : 0f, Shaped = q };

    private static int Level(string k) => int.TryParse(k, out var i) ? i : 0;

    private static float Get(IReadOnlyDictionary<string, float> d, string k) => d != null && d.TryGetValue(k, out var v) ? v : 0f;

    private static float Logit(float p)
    {
        p = Mathf.Clamp(p, 1e-6f, 1f - 1e-6f);
        return Mathf.Log(p / (1f - p));
    }

    private static string Weighted(System.Random rng, IList<string> keys, IList<float> weights)
    {
        double total = 0;
        foreach (var w in weights) total += w;
        double x = rng.NextDouble() * total;
        for (int i = 0; i < keys.Count; i++)
        {
            x -= weights[i];
            if (x <= 0) return keys[i];
        }
        return keys[keys.Count - 1];
    }

    // =====================================================
    // 場面の倍率（画面の数字だけ。ログの features から作り直せるように、ほとんど features から読む）
    // =====================================================

    /// <summary>選択肢 → 倍率（載っていなければ 1）。真偽の質問は "true" の倍率（true と false の比に掛かる）。</summary>
    public static Dictionary<string, float> Situation(string question, PersonaSituation s)
    {
        var m = new Dictionary<string, float>();
        void Mul(string k, float v) => m[k] = (m.TryGetValue(k, out var old) ? old : 1f) * v;
        var f = s?.Features;
        if (s?.OptionMult != null)
            foreach (var kv in s.OptionMult) Mul(kv.Key, kv.Value);
        if (f == null || f.Length < PersonaFeatures.Dim) return m;

        switch (question)
        {
            case "shop_upgrade":
            case "smith_upgrade":
            case "broker_upgrade":
            case "machine_upgrade":
            case "machine_repair":
                // 元が取れそうか（使ってよい額の 3 割以内・返済が近くて重くない）。めたんは数えてから払う
                if (s.Plausible == true) Mul("true", 2f);
                else if (s.Plausible == false) Mul("true", 0.4f);
                break;
            case "budget":
            case "buy_style":
            case "intel":
            case "support":
            case "focus_share":
            {
                float cash = f[1] > 0f ? Mathf.Pow(10f, 6f * f[1]) : 0f;
                float debtRatio = 3f * f[2];
                float debtDays = 10f * f[3];
                float streamDays = 5f * f[4];
                bool weak = f[5] >= 0.5f;
                bool tight = debtRatio >= 0.6f && debtDays <= 3f;          // 返済が近くて重い
                bool comfy = debtRatio <= 0.2f && cash >= 50000f;          // 返済が軽くて手持ちに余裕
                if (question == "budget")
                {
                    for (int lv = 0; lv < 6; lv++)
                    {
                        string k = lv.ToString();
                        if (tight) Mul(k, lv >= 3 ? 0.5f : lv <= 1 ? 1.4f : 1f);
                        if (comfy) Mul(k, lv is 2 or 3 ? 1.3f : lv == 0 ? 0.6f : 1f);
                        if (streamDays <= 1f && weak && lv == 3) Mul(k, 1.2f);
                    }
                }
                else if (question == "buy_style")
                {
                    if (weak && streamDays <= 1f) Mul("dungeon_focus", 1.4f);
                    if (tight)
                    {
                        Mul("recommend", 1.2f);
                        Mul("dividend", 0.8f);
                    }
                }
                else if (question == "intel")
                {
                    float share = s.IntelCostShare;
                    if (share >= 0f && share <= 0.05f) Mul("true", 1.5f);
                    else if (share >= 0.25f) Mul("true", 0.4f);
                    if (tight) Mul("true", 0.5f);
                    if (streamDays >= 3f) Mul("true", 0.7f);
                }
                else if (question == "support")
                {
                    float gain = s.SupportGainRatio;
                    for (int lv = 0; lv < 4; lv++)
                    {
                        string k = lv.ToString();
                        if (comfy && debtDays > 2f) Mul(k, lv == 1 ? 1.4f : lv >= 2 ? 0.6f : 1f);
                        if (tight && lv == 0) Mul(k, 2f);
                        if (gain >= 1.5f && lv == 1) Mul(k, 1.5f);
                        if (gain >= 0f && gain < 0.8f && lv >= 1) Mul(k, 0.4f);
                    }
                }
                else
                {
                    for (int lv = 0; lv < 5; lv++)
                    {
                        string k = lv.ToString();
                        if (tight) Mul(k, lv <= 1 ? 1.4f : lv >= 3 ? 0.6f : 1f);
                        if (comfy && lv == 2) Mul(k, 1.2f);
                    }
                }
                break;
            }
            case "stream_side":
            case "intensity":
            {
                bool weak = f[7] >= 0.5f;
                float budgetShare = f[8];
                bool supportPaid = f[9] >= 0.5f;
                float cash = f[1] > 0f ? Mathf.Pow(10f, 6f * f[1]) : 0f;
                if (question == "stream_side")
                {
                    if (weak) Mul("hero", 1.5f);       // 弱点の武器を揃えた勇者は勝ちやすい
                    else
                    {
                        Mul("none", 1.4f);             // 弱点を知らずに張らない
                        Mul("hero", 0.8f);
                    }
                    if (supportPaid)
                    {
                        Mul("demon", 1.8f);            // 魔王軍に出資したなら筋を通す
                        Mul("hero", 0.6f);
                    }
                    if (budgetShare < 0.05f) Mul("none", 1.3f);
                }
                else
                {
                    for (int lv = 0; lv < 5; lv++)
                    {
                        string k = lv.ToString();
                        if (s.Side == "none") Mul(k, lv <= 1 ? 1.6f : 0.5f);
                        if (budgetShare >= 0.3f && lv == 3) Mul(k, 1.2f);
                        if (cash < 50000f && lv >= 3) Mul(k, 0.6f);
                        if (s.Side == "hero" && weak && lv == 3) Mul(k, 1.2f);
                    }
                }
                break;
            }
            case "village_budget":
                for (int lv = 0; lv < 5; lv++)
                {
                    string k = lv.ToString();
                    if (s.PlausibleCount > 0) Mul(k, lv >= 2 ? 1.4f : lv == 0 ? 0.4f : 1f);
                    else if (s.PlausibleCount == 0) Mul(k, lv <= 1 ? 1.5f : 1f);
                }
                break;
            default:
                if (question.StartsWith("invest_", StringComparison.Ordinal))
                {
                    if (s.Plausible == true) Mul("true", 3f);
                    else if (s.Plausible == false) Mul("true", 0.3f);
                    if (PreferredFacilities.Contains(question.Substring("invest_".Length))) Mul("true", 1.5f);
                }
                break;
        }
        return m;
    }

    /// <summary>村の施設を何周で取り返せれば「元が取れる」と見るか（前の周に村へ入った額の何倍まで払うか）。</summary>
    public const int PaybackLaps = 4;

    /// <summary>
    /// 村の施設は元が取れそうか（めたん）: 費用が村資金から残す額を引いた範囲にあり、
    /// 村資金の 5% 以下（遊ばせておくだけのお金）か、前の周に村資金へ入った額の PaybackLaps 倍以下（数周で取り返せる）。
    /// 比べるのは村資金の単位どうし（換金率 village.conversionRate を変えても判断がずれない）。
    /// 前の周が無い（入った額が 0 以下）ときは 5% の決まりだけで見る。
    /// </summary>
    public static bool PaybackPlausible(int cost, int funds, int reserve, int lastRunVillageFunds)
    {
        if (cost <= 0 || cost > funds - reserve) return false;
        if (cost <= funds * 0.05f) return true;
        return lastRunVillageFunds > 0 && cost <= (long)PaybackLaps * lastRunVillageFunds;
    }
}
#endif

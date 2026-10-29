#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 流儀 S(a)（Docs/Persona_Formation_Design.md の 4.1・5.1）。特性と気分から決まり、学習しない。
/// S(a) = β_risk·(0.3·clamp(conf − 基準点, ±0.5) + 0.4·smug + 0.5·irk)·b(a) − β_loss·((λ−1)·cashStress + 2·(λ_eff − λ))·d(a) + β_break·sad·hard(a)
/// - b(a) 大胆さ・d(a) 下振れ・hard(a) 難しい道（0〜1、質問ごとの表は Of）。
/// - 平常の大胆さ（risk）は ScoreBias・HardRouteChance・FlashyBoost が持っているので、ここは気分のずれ（ハウスマネー・取り返し）と損失回避だけ。
/// - λ_eff: 負けた直後（8 判断）は λ × 1.2（めたん: 負けた次は予算を下げる）。
/// - めたんの崩れ（イラ、1 ランに 1 回）: 損失回避を 0.3 倍に弱め、大胆さに +0.5 を足す。
/// - 取り返し（sad·hard）は損失回避の弱い人格（ずんだもん）だけ。
/// tools/persona_play/replay_selection.py の style() / compose() に同じ写しがある（数字を変えたら両方直す）。
/// </summary>
public static class PersonaStyle
{
    public const float BetaRisk = 4f, BetaLoss = 1f, BetaBreak = 4f;   // β_break 1.5 → 4（rules 4.1。M4: 負けの後の取り返しが平常と見分けられなかった）
    /// <summary>取り返しの持ち越し: 負けの後の 8 判断（LossStreakDecisions）は sad が減衰しても、この分だけ取り返しに向かう（配信の判断まで届かせる）。</summary>
    public const float ChaseAfterLoss = 0.4f;
    public static readonly HashSet<string> FlashyFacilities = new() { "plaza", "guild", "artisan" };

    public struct Attr
    {
        public float B, D, Hard;
        public Attr(float b, float d, float hard)
        {
            B = b;
            D = d;
            Hard = hard;
        }
    }

    public static int MaxLevel(string q) => q switch
    {
        "budget" => 5,
        "support" => 3,
        "intensity" => 4,
        "focus_share" => 4,
        "village_budget" => 4,
        "finance_share" => 3,
        _ => 0,
    };

    /// <summary>選択肢の大胆さ・下振れ・難しい道。</summary>
    public static Attr Of(string q, string key)
    {
        if (string.IsNullOrEmpty(q) || key == null) return default;
        int max = MaxLevel(q);
        if (max > 0)
        {
            int.TryParse(key, out int lv);
            float b = Mathf.Clamp01(lv / (float)max);
            return new Attr(b, b, q == "support" && lv >= 2 ? 1f : 0f);
        }
        switch (q)
        {
            case "stream_side":
                return key == "demon" ? new Attr(1f, 1f, 1f) : key == "hero" ? new Attr(1f, 0.7f, 0f) : default;
            case "intel":
                return key == "false" ? new Attr(1f, 0.5f, 1f) : new Attr(0f, 0.2f, 0f);
            case "newspaper":
                return key == "false" ? new Attr(0.5f, 0f, 0f) : default;
            case "buy_style":
                return key switch
                {
                    "dungeon_focus" => new Attr(0.8f, 0.6f, 0.5f),
                    "bargain" => new Attr(0.5f, 0.3f, 0f),
                    "dividend" => new Attr(0.3f, 0.2f, 0f),
                    _ => new Attr(0.2f, 0.1f, 0f),
                };
            case "shop_upgrade":
            case "smith_upgrade":
            case "broker_upgrade":
                return key == "true" ? new Attr(0.6f, 0.4f, 0f) : default;
            case "machine":
                return key == "none" ? default
                    : PersonaRequests.FlashyMachines.Contains(key) ? new Attr(1f, 0.6f, 0.5f) : new Attr(0.5f, 0.4f, 0f);
            case "finance":
                if (key == "none") return default;
                if (key.StartsWith("sell_", StringComparison.Ordinal)) return new Attr(0.2f, 0.1f, 0f);
                return key.StartsWith("bond", StringComparison.Ordinal) ? new Attr(0.2f, 0.1f, 0f) : new Attr(0.9f, 0.8f, 0.5f);
            case "hero_tactics":
                return key == "Aggressive" ? new Attr(0.8f, 0.5f, 0f) : default;
            case "starter_relic":
                // 派手なもの（バズ・イベント・防衛報酬）は大胆・下振れあり、手堅いもの（毎朝のお金・仕入れ値・納付金）は 0、ほか（陳列・製造）はその間
                return PersonaRequests.FlashyRelics.Contains(key) ? new Attr(1f, 0.6f, 0.5f)
                    : PersonaRequests.SteadyRelics.Contains(key) ? default : new Attr(0.4f, 0.3f, 0f);
            case "intervene":
            case "intervene_now":
                return key is "special" or "bossbuff" ? new Attr(1f, 0.6f, 0.5f) : key == "none" ? default : new Attr(0.5f, 0.4f, 0f);
        }
        if (q.StartsWith("invest_", StringComparison.Ordinal))
        {
            bool flashy = FlashyFacilities.Contains(q.Substring("invest_".Length));
            return key == "true" ? new Attr(flashy ? 1f : 0.6f, 0.5f, flashy ? 1f : 0f) : default;
        }
        return default;
    }

    /// <summary>手持ちの苦しさ（0〜1）: 返済が近くて重い・現金が少ない（画面の数字＝特徴から）。</summary>
    public static float CashStress(PersonaSituation s)
    {
        var f = s?.Features;
        if (f == null || f.Length < PersonaFeatures.Dim) return 0f;
        float cash = f[1] > 0f ? Mathf.Pow(10f, 6f * f[1]) : 0f;
        float lowCash = cash < 50000f ? 0.4f : cash < 150000f ? 0.15f : 0f;
        if (s.Kind == PersonaFeatures.Day)
        {
            float debtRatio = 3f * f[2];
            float debtDays = 10f * f[3];
            float debt = debtRatio >= 0.3f ? Mathf.Clamp01(debtRatio) * (debtDays <= 3f ? 1f : debtDays <= 6f ? 0.5f : 0.2f) : 0f;
            return Mathf.Clamp01(debt + lowCash);
        }
        if (s.Kind == PersonaFeatures.Stream) return lowCash;
        return 0f;
    }

    /// <summary>選択肢ごとの S(a)（log 倍率）。特性・気分が無いときは空。</summary>
    public static Dictionary<string, float> S(PersonaProfile p, PersonaMood mood, string q, IEnumerable<string> keys, float cashStress, bool collapse)
    {
        var s = new Dictionary<string, float>();
        var t = p?.Traits;
        if (t == null || mood == null || keys == null) return s;
        float lambda = t.LossAversion;
        float lambdaEff = mood.LossStreakDecisions > 0 ? lambda * 1.2f : lambda;
        float irk = mood.Irk;
        if (collapse)
        {
            lambda = 1f + (lambda - 1f) * 0.3f;
            lambdaEff = lambda;
            irk += 0.5f;
        }
        // 自信のずれは ±0.5 で切る（ずんだもんは基準点 +0.5 なので上には 0.5 しか動かないが、下には 1.5 動く。負けで臆病になりすぎない）
        float boldMood = 0.3f * Mathf.Clamp(mood.Conf - mood.ConfBase, -0.5f, 0.5f) + 0.4f * mood.Smug + 0.5f * irk;
        float loss = (lambda - 1f) * cashStress + 2f * (lambdaEff - lambda);
        float chase = t.LossAversion <= 1.2f ? mood.Sad + (mood.LossStreakDecisions > 0 ? ChaseAfterLoss : 0f) : 0f;
        foreach (var k in keys)
        {
            var a = Of(q, k);
            float v = BetaRisk * boldMood * a.B - BetaLoss * loss * a.D + BetaBreak * chase * a.Hard;
            s[k] = (float)Math.Round(Mathf.Clamp(v, -3f, 3f), 4);
        }
        return s;
    }

    public static bool AnyNonZero(IReadOnlyDictionary<string, float> s) => s != null && s.Values.Any(v => Mathf.Abs(v) > 1e-4f);

    // =====================================================
    // 合成: log π(a) = log p_kev(a) + w·adjust(a) + S(a)（5.1）。KL の上限・エントロピーの下限（5.3）
    // =====================================================

    public sealed class Composed
    {
        public Dictionary<string, float> Final;
        public Dictionary<string, float> Prior;
        public float SkillWeight = 1f;
        public float Kl;
        public float EntropyTemp = 1f;
    }

    /// <summary>
    /// kev の確率・学習器の log 倍率・流儀から最終の確率を作る。klEps &gt; 0 なら KL(π‖π_prior) ≤ klEps になるまで技能の重みを二分探索で縮め、
    /// hMinBits &gt; 0 なら（kev が 10% 以上つけた手が 2 つ以上あるとき）エントロピーが下限を満たすまで温度を上げる（最大 8）。
    /// </summary>
    public static Composed Compose(IReadOnlyDictionary<string, float> kev, IReadOnlyDictionary<string, float> adjust,
        IReadOnlyDictionary<string, float> style, float skillScale, float klEps, float hMinBits)
    {
        var res = new Composed();
        var keys = kev.Keys.ToList();
        float A(string k) => adjust != null && adjust.TryGetValue(k, out var v) ? v : 0f;
        float St(string k) => style != null && style.TryGetValue(k, out var v) ? v : 0f;
        Dictionary<string, float> Mix(float w, float temp)
        {
            var d = new Dictionary<string, float>();
            double sum = 0;
            foreach (var k in keys)
            {
                double pk = Math.Max(0f, kev[k]);
                double v = pk <= 0 ? 0 : Math.Exp((Math.Log(pk) + w * A(k) + St(k)) / temp);
                d[k] = (float)v;
                sum += v;
            }
            foreach (var k in keys) d[k] = sum > 0 ? (float)(d[k] / sum) : 1f / keys.Count;
            return d;
        }
        var prior = Mix(0f, 1f);
        float wSkill = Mathf.Max(0f, skillScale);
        var final = Mix(wSkill, 1f);
        float kl = Kl(final, prior);
        if (klEps > 0f && kl > klEps && wSkill > 0f)
        {
            float lo = 0f, hi = wSkill;
            for (int i = 0; i < 20; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (Kl(Mix(mid, 1f), prior) > klEps) hi = mid;
                else lo = mid;
            }
            wSkill = lo;
            final = Mix(wSkill, 1f);
            kl = Kl(final, prior);
        }
        res.SkillWeight = wSkill;
        res.Kl = kl;
        if (hMinBits > 0f && keys.Count(k => kev[k] >= 0.10f) >= 2 && EntropyBits(final) < hMinBits)
        {
            float lo = 1f, hi = 8f;
            if (EntropyBits(Mix(wSkill, hi)) >= hMinBits)
            {
                for (int i = 0; i < 20; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (EntropyBits(Mix(wSkill, mid)) >= hMinBits) hi = mid;
                    else lo = mid;
                }
            }
            res.EntropyTemp = hi;
            final = Mix(wSkill, hi);
        }
        res.Final = final.ToDictionary(kv => kv.Key, kv => (float)Math.Round(kv.Value, 5));
        res.Prior = prior.ToDictionary(kv => kv.Key, kv => (float)Math.Round(kv.Value, 5));
        return res;
    }

    public static float Kl(IReadOnlyDictionary<string, float> p, IReadOnlyDictionary<string, float> q)
    {
        double kl = 0;
        foreach (var kv in p)
        {
            if (kv.Value <= 0f) continue;
            double qv = q.TryGetValue(kv.Key, out var v) ? Math.Max(1e-9, v) : 1e-9;
            kl += kv.Value * Math.Log(kv.Value / qv);
        }
        return (float)Math.Max(0, kl);
    }

    public static float EntropyBits(IReadOnlyDictionary<string, float> p)
    {
        double h = 0;
        foreach (var v in p.Values)
            if (v > 0f) h -= v * Math.Log(v, 2);
        return (float)h;
    }
}
#endif

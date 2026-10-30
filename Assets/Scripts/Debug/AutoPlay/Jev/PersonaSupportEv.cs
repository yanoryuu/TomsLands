#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 魔王軍支援を期待値で決める（2026-10-30 の分析 P7「支援先の選び方」。PersonaPlayConfig.SupportByEv が true のときだけ使う）。
///
/// 支援は次の配信先のダンジョンを k 段上げる。勇者が負けたときだけ防衛報酬が入り、出資額は勝っても負けても戻らない。
///   期待値(k) = 負け率(ダンジョン, 今のLv + k) × 防衛報酬(今のLv + k) − 出資額(k)
/// を k = 0〜上限で比べ、いちばん大きい k にする（k = 0 は出資 0・今のLvのまま）。
/// 負け率はダンジョン×レベルごとに数えた値（tools/persona_play/metrics/support_rates.py が persona_log の stream_result から書く表）を、
/// 件数が少ないうちはダンジョン全体の率へ、それも少なければ分析（2026-10-30、41 周）の既定値へ縮める。
/// レベルが上がって勇者が勝ちやすくなることは無いので、負け率はレベルについて下がらないようにそろえる。
/// 配信中の売上・張った側への影響は入れていない（防衛報酬と出資額だけ）。
/// </summary>
public sealed class PersonaSupportRates
{
    public sealed class Cell
    {
        [JsonProperty("n")] public int N;
        [JsonProperty("losses")] public int Losses;
    }

    /// <summary>ダンジョン（DungeonName の名前）→ レベル（文字列）→ 件数と勇者の負けの数。</summary>
    [JsonProperty("rates")] public Dictionary<string, Dictionary<string, Cell>> Rates = new();
    [JsonProperty("updated")] public string Updated;

    /// <summary>分析（2026-10-30 03:48〜、41 周・全配信）のダンジョン別の勇者の負け率。表が無い・少ないときの既定値。</summary>
    public static readonly Dictionary<string, float> PriorLossRate = new()
    {
        ["DeepGreenBeastForest"] = 0.03f,
        ["DemonKingCastle"] = 0.83f,
        ["MausoleumOblivion"] = 0.09f,
        ["ScorchingVolcanoPrison"] = 0.09f,
        ["IceMistCave"] = 0.43f,
        ["AncientMechanicalCastle"] = 0.89f,
    };

    /// <summary>既定値・ダンジョン全体の率へ縮める強さ（仮の件数）。</summary>
    public const float PriorWeight = 4f;
    public const float UnknownDungeonLossRate = 0.3f;

    public static PersonaSupportRates Load(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return JsonConvert.DeserializeObject<PersonaSupportRates>(File.ReadAllText(path)) ?? new PersonaSupportRates();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] 支援の負け率の表を読めないので既定値を使います: {e.Message}");
        }
        return new PersonaSupportRates();
    }

    /// <summary>ダンジョン全体の勇者の負け率（数えた分を既定値へ縮める）。</summary>
    public float DungeonLossRate(string dungeon)
    {
        float prior = dungeon != null && PriorLossRate.TryGetValue(dungeon, out var p) ? p : UnknownDungeonLossRate;
        int n = 0, losses = 0;
        if (dungeon != null && Rates != null && Rates.TryGetValue(dungeon, out var byLevel) && byLevel != null)
            foreach (var c in byLevel.Values)
            {
                if (c == null) continue;
                n += Mathf.Max(0, c.N);
                losses += Mathf.Clamp(c.Losses, 0, Mathf.Max(0, c.N));
            }
        return (losses + PriorWeight * prior) / (n + PriorWeight);
    }

    /// <summary>
    /// そのレベルでの勇者の負け率。数えたレベルはその数をダンジョン全体の率へ縮め、数えていないレベルは下の数えたレベルの率を引き継ぐ
    /// （下に数えたレベルが無ければダンジョン全体の率）。数えたレベルどうしでは、上のレベルが下のレベルを下回らないようにそろえる。
    /// </summary>
    public float LossRate(string dungeon, int level)
    {
        float baseRate = DungeonLossRate(dungeon);
        Dictionary<string, Cell> byLevel = null;
        if (dungeon != null && Rates != null) Rates.TryGetValue(dungeon, out byLevel);
        float? last = null;
        for (int lv = 0; lv <= Mathf.Max(0, level); lv++)
        {
            if (byLevel == null || !byLevel.TryGetValue(lv.ToString(), out var c) || c == null || c.N <= 0) continue;
            float r = (Mathf.Clamp(c.Losses, 0, c.N) + PriorWeight * baseRate) / (c.N + PriorWeight);
            last = last.HasValue ? Mathf.Max(last.Value, r) : r;
        }
        return Mathf.Clamp01(last ?? baseRate);
    }
}

public static class PersonaSupportEv
{
    public readonly struct Choice
    {
        public readonly int Levels;
        public readonly float Ev;
        public readonly float EvNone;
        public readonly int Cost;
        public readonly string Detail;

        public Choice(int levels, float ev, float evNone, int cost, string detail)
        {
            Levels = levels; Ev = ev; EvNone = evNone; Cost = cost; Detail = detail;
        }
    }

    /// <summary>
    /// 防衛報酬（rewardAt）に、そのレベルで配信したときの見込み（clearPctAt）の段のオッズ（streamTuning.supportOdds*）を掛ける。
    /// 掛け方は実際の支払いと同じ SupportOdds（Expected → FromClearPct・Multiplier・Apply → 出資の合計 stakeAt(lv) を下限）。
    /// オッズが全部 1 で元本保証も off なら rewardAt そのまま（見込みのドライランもしない）。
    /// </summary>
    public static Func<int, int> RewardWithOdds(Func<int, int> rewardAt, Func<int, float> clearPctAt, Func<int, int> stakeAt,
        StreamTuningData tuning = null)
    {
        if (rewardAt == null) return null;
        bool odds = clearPctAt != null && SupportOdds.IsActive(tuning);
        bool floor = stakeAt != null && SupportOdds.FloorsToStake(tuning);
        if (!odds && !floor) return rewardAt;
        return lv => SupportOdds.Expected(rewardAt(lv), clearPctAt != null ? () => clearPctAt(lv) : null,
            stakeAt != null ? stakeAt(lv) : 0, tuning);
    }

    /// <summary>
    /// 期待値のいちばん大きい段数。level = 今のLv、maxLevel = 上限、costAt(lv) = lv から lv+1 へ上げる額、rewardAt(lv) = lv での防衛報酬、
    /// budget = 出資に使ってよい額（これを超える段数は見ない）。同じ期待値なら少ない段数。
    /// </summary>
    public static Choice Choose(PersonaSupportRates rates, string dungeon, int level, int maxLevel, Func<int, int> costAt, Func<int, int> rewardAt, int budget)
    {
        rates ??= new PersonaSupportRates();
        float evNone = rates.LossRate(dungeon, level) * Mathf.Max(0, rewardAt(level));
        int best = 0, bestCost = 0;
        float bestEv = evNone;
        int cost = 0;
        var parts = new List<string> { $"Lv{level} p={rates.LossRate(dungeon, level):0.00} ev={evNone:0}" };
        for (int k = 1; level + k <= maxLevel; k++)
        {
            int step = costAt(level + k - 1);
            if (step <= 0) break;
            cost += step;
            if (cost > budget) break;
            int lv = level + k;
            float p = rates.LossRate(dungeon, lv);
            float ev = p * Mathf.Max(0, rewardAt(lv)) - cost;
            parts.Add($"Lv{lv} p={p:0.00} ev={ev:0}");
            if (ev > bestEv + 0.5f)
            {
                bestEv = ev;
                best = k;
                bestCost = cost;
            }
        }
        return new Choice(best, bestEv, evNone, bestCost, $"{dungeon}: " + string.Join(", ", parts) + $" → +{best}");
    }
}
#endif

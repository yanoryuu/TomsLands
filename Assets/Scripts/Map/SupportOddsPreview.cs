using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 魔王軍支援の画面に出す「配当のオッズの見込み」（2026-10-30 ユーザー決定: 支援する前にオッズを見せる）。
/// いまのLvの見込みと、もう 1 回支援したLvの見込みを並べ、「支援するほど勇者が不利になってオッズは下がる」を見せる。
/// 額の計算は SupportOdds.Payout（実際の支払いと同じ 1 か所）を通すだけで、ここでは式を持たない。
/// オッズが全部 1（SupportOdds.IsActive が false。今の配信の設定）なら何も出さない（見込みの計算もしない）。
/// 実際の倍率は配信開始時の見込み（GameFlowManager.PrepareBattleInput → PreStreamClearPct）で決まるので、その注記も出す。
/// </summary>
public static class SupportOddsPreview
{
    public const string FixedNote = "倍率は配信開始時の見込みで決まる";

    /// <summary>1 つのLvの見込み（見込みの％と、その％で配信したときの配当の内訳）。</summary>
    public readonly struct Line
    {
        public readonly int Level;
        public readonly float ClearPct;
        public readonly SupportPayout Payout;

        public Line(int level, float clearPct, SupportPayout payout)
        {
            Level = level;
            ClearPct = clearPct;
            Payout = payout;
        }

        public SupportOddsTier Tier => Payout.OddsTier;
        public float Multiplier => Payout.OddsMultiplier;
        public int Total => Payout.Total;
    }

    public sealed class Result
    {
        /// <summary>出すか（オッズが全部 1 なら false）。</summary>
        public bool Visible;
        /// <summary>敵情報が未調査で、段・額を伏せたか。</summary>
        public bool Hidden;
        public Line Current;
        public bool HasNext;
        public Line Next;
        /// <summary>画面に出す文（TMP リッチテキスト）。</summary>
        public string Text = string.Empty;

        public static Result None => new();
    }

    /// <summary>
    /// 見込みを作る。clearPctAt(Lv) はそのLvで配信したときのクリア見込み（表示％。ClearProbabilityCalculator.Calculate と同じ）、
    /// rewardAt(Lv) は防衛報酬の基本額（GetDefenseReward）、stakeAt(Lv) は出資の合計（GetInvestedTotal）、relicMul はレリックの配当倍率。
    /// nextLevel が null なら（最大Lv）いまの行だけ。infoKnown が false（敵情報が未調査）なら段・額は「？」にして見込みも計算しない。
    /// </summary>
    public static Result Build(int currentLevel, int? nextLevel, Func<int, float> clearPctAt, Func<int, int> rewardAt,
        Func<int, int> stakeAt, float relicMul, bool infoKnown, StreamTuningData d = null)
    {
        d ??= StreamTuning.Data;
        if (!SupportOdds.IsActive(d)) return Result.None;

        var r = new Result { Visible = true, Hidden = !infoKnown, HasNext = nextLevel.HasValue };
        var sb = new StringBuilder();
        if (!infoKnown)
        {
            sb.Append("いまの見込み: ？　<size=80%>（敵情報が未調査）</size>");
            if (nextLevel.HasValue) sb.Append('\n').Append(NextLabel(nextLevel.Value)).Append(": ？");
        }
        else
        {
            r.Current = At(currentLevel, clearPctAt, rewardAt, stakeAt, relicMul, d);
            sb.Append(FormatLine("いまの見込み", r.Current));
            if (nextLevel.HasValue)
            {
                r.Next = At(nextLevel.Value, clearPctAt, rewardAt, stakeAt, relicMul, d);
                sb.Append('\n').Append(FormatLine(NextLabel(nextLevel.Value), r.Next));
            }
        }
        sb.Append("\n<size=75%>※").Append(FixedNote).Append("</size>");
        r.Text = sb.ToString();
        return r;
    }

    /// <summary>そのLvで配信して勇者が負けたときの配当（SupportOdds.Payout そのもの）。</summary>
    public static Line At(int level, Func<int, float> clearPctAt, Func<int, int> rewardAt, Func<int, int> stakeAt, float relicMul,
        StreamTuningData d = null)
    {
        float pct = clearPctAt != null ? clearPctAt(level) : -1f;
        int reward = rewardAt != null ? rewardAt(level) : 0;
        int stake = stakeAt != null ? stakeAt(level) : 0;
        return new Line(level, pct, SupportOdds.Payout(reward, relicMul, pct, stake, d));
    }

    public static string NextLabel(int nextLevel) => $"Lv{nextLevel} にすると";

    /// <summary>倍率の表示（配当通知書と同じ書き方: 1.5 / 0.45 / 2.0）。</summary>
    public static string FormatMultiplier(float m) => $"×{m:0.0#}";

    /// <summary>段の名前（色付き。色はクリア見込みの表示と同じ）。</summary>
    public static string FormatTier(SupportOddsTier tier)
    {
        if (tier == SupportOddsTier.Unknown) return SupportOdds.Label(tier);
        return $"<color={ClearProbabilityCalculator.OutlookColorHex((ClearOutlook)(int)tier)}>{SupportOdds.Label(tier)}</color>";
    }

    /// <summary>「いまの見込み: 有利 ×1.5　配当 67,500G」。未出資（防衛報酬 0）なら配当は「なし」。</summary>
    public static string FormatLine(string head, Line line)
    {
        string payout = line.Payout.RelicReward > 0 ? $"配当 {line.Total:N0}G" : "配当なし（未支援）";
        if (line.Payout.FloorApplied) payout += "<size=80%>（元本保証）</size>";
        return $"{head}: {FormatTier(line.Tier)} {FormatMultiplier(line.Multiplier)}　{payout}";
    }

    /// <summary>レリックの配当倍率（配信開始時に RelicBattleEffects.SetFrom が確定する値と同じ）。</summary>
    public static float RelicMul(RelicEffectResolver resolver) => RelicBattleEffects.DefeatRewardMulOf(resolver);
}

/// <summary>
/// クリア見込み（ClearProbabilityCalculator.Calculate のドライラン）の覚え書き。
/// 鍵は (ダンジョン, Lv, 戦うときの勇者の HP・攻撃・防御)。HP・攻撃・防御は装備・レリックの勇者弱体化込みの値
/// （GetEffectiveStats。ドライランへ入る勇者の値そのもの）なので、装備を替えれば別の鍵になる。
/// 魔王軍支援の画面で選んだダンジョンの分だけ、必要になったときに計算する（所持金が変わるたびに作り直しても計算し直さない）。
/// </summary>
public sealed class ClearPctCache
{
    private readonly Dictionary<(DungeonName, int, int, int, int), float> cache = new();
    private readonly Func<DungeonData, int, float> compute;

    /// <summary>計算した回数（テスト用）。</summary>
    public int ComputeCount { get; private set; }

    public ClearPctCache(Func<DungeonData, int, float> compute) => this.compute = compute;

    public int Count => cache.Count;

    public void Clear() => cache.Clear();

    public float Get(DungeonData dungeon, int level, ClearProbabilityCalculator.HeroStats stats)
    {
        if (dungeon == null) return -1f;
        var key = (dungeon.key, level, stats.Hp, stats.Attack, stats.Defense);
        if (cache.TryGetValue(key, out float pct)) return pct;
        try { pct = compute(dungeon, level); }
        catch (Exception e)
        {
            Debug.LogWarning($"[ClearPctCache] クリア見込みを計算できませんでした ({dungeon.key} Lv{level}): {e.Message}");
            pct = -1f;
        }
        ComputeCount++;
        cache[key] = pct;
        return pct;
    }
}

using System;
using UnityEngine;

/// <summary>
/// 配信まわりの調整キー（gameconst.json の data.streamTuning.*。シート data に「streamTuning.キー名」の行で書く）。
/// どのキーも既定値は 2026-10-30 時点の動きそのまま（配信に無ければ今と同じ）。値はリセット時にシートから入れる。
/// 決定の記録: _scratch_wt04\tuning\tuning_log.md（2026-10-30 11:50 / 12:xx）。
/// </summary>
[Serializable]
public class StreamTuningData
{
    // --- 魔王軍支援の配当のオッズ（配信開始時の見込みの段ごとの倍率。防衛報酬 rewardGold × レリック × この値） ---
    // 段の境目は表示の見込みと同じ clearOutlook.*（ClearOutlookSettingsData。ここに別の境目は持たない）。
    // 予定の値（2026-10-30 ユーザー決定）: 楽勝 2.0 / 有利 1.5 / 互角 1.0 / 不利 0.45 / 絶望 0.3
    //   （境目を clearOutlook で 75 / 55 / 45 / 15 にしたとき: 楽勝 75〜 / 有利 55〜75 / 互角 45〜55 / 不利 15〜45 / 絶望 〜15）
    public float supportOddsEasy = 1f;
    public float supportOddsFavorable = 1f;
    public float supportOddsEven = 1f;
    public float supportOddsUnfavorable = 1f;
    public float supportOddsHopeless = 1f;

    /// <summary>村の広場の上乗せを「返金騒ぎを引いた売上」に掛ける（false = 今の動き: 返金前の売上に掛ける）。</summary>
    public bool plazaBonusAfterRefund = false;

    /// <summary>納付日の朝の損のイベントは、所持金を納付額より下へは減らさない（false = 今の動き: 0 まで減らす）。</summary>
    public bool paymentMorningLossFloor = false;

    /// <summary>勇者が勝った配信の、勇者の装備（武器・防具）の相場への倍率（ボスを倒さなかったとき。今の値 1.8）。</summary>
    public float heroWinPriceMultiplier = 1.8f;
    /// <summary>勇者が勝ってボスを倒した配信の、勇者の装備（武器・防具だけ）の相場への倍率（今の値 2.2）。</summary>
    public float heroWinBossPriceMultiplier = 2.2f;

    /// <summary>配信中に補充して売れ残った分の払い戻しの率（補充した単価 × この値。今の値 1.0 = 全額）。</summary>
    public float restockRefundRate = 1f;

    public StreamTuningData Clone() => (StreamTuningData)MemberwiseClone();
}

/// <summary>
/// ボスの反応の調整キー（gameconst.json の data.bossReaction.*）。反応の表そのものは balance.json の bossReaction（シート bal_bossReaction）。
/// 既定値では何も動かさない（影のログだけ）。決定: tuning_log.md 2026-10-30 12:30 / 13:00、審議 panel_1030e。
/// </summary>
[Serializable]
public class BossReactionSettingsData
{
    /// <summary>状況の札などの表示を出すか（既定 false。表示の中身はまだ無く、StreamingSalesController.BossSituationDecided を流すだけ）。</summary>
    public bool uiEnabled = false;
    /// <summary>毎ターンの熱・メーター・HP の影のログ（heat_turn）を書くか。</summary>
    public bool shadowTurnLog = true;

    // --- 点数 → 5 つの状況の境目（点数 = メーター 0〜5 ＋ HP −1〜0 ＋ 弱点 +1 ＋ 流れ ±0.5） ---
    public float winningMinScore = 5f;   // 勝ちムード
    public float favoredMinScore = 3.5f; // 優勢
    public float closeMinScore = 2f;     // 拮抗
    public float behindMinScore = 1f;    // 劣勢（これ未満は絶望）

    /// <summary>流れ: 何ターン前の勝敗メーターと比べるか。</summary>
    public int flowTurns = 3;
    /// <summary>流れ: メーターがこれ以上上がっていれば +0.5、下がっていれば −0.5。</summary>
    public float flowMeterDelta = 0.1f;

    // --- 出現の後の反応（ボスの間に各 1 回。既定 0 = 何もしない。審議 L2-D3b） ---
    /// <summary>逆転（劣勢・絶望で出たボスに、メーターが勝ち見込み 0.55 以上へ戻った）の熱。</summary>
    public float comebackHeat = 0f;
    /// <summary>逆転のときの売れ行きの気分の加算（武器・防具。0 = 変えない）。</summary>
    public float comebackWeaponMood = 0f;
    public float comebackArmorMood = 0f;
    /// <summary>崩れ（勝ちムード・優勢で出たボスに、メーターが負け見込み 0.45 以下へ落ちた）の熱。</summary>
    public float collapseHeat = 0f;
    public float collapseWeaponMood = 0f;
    public float collapseArmorMood = 0f;
    /// <summary>逆転・崩れで変えた気分が続くターン数（0 以下 = 配信の終わりまで）。</summary>
    public int afterMoodTurns = 0;

    public BossReactionSettingsData Clone() => (BossReactionSettingsData)MemberwiseClone();
}

/// <summary>
/// 配信開始時の見込み（ClearProbabilityCalculator の表示％）の 5 段。境目は表示の見込み（ToOutlook・clearOutlook.*）と同じ。
/// 数字の並びは勇者から見た強さ（0 = 絶望 … 4 = 楽勝）。
/// </summary>
public enum SupportOddsTier
{
    Unknown = -1,
    Hopeless = 0,
    Unfavorable = 1,
    Even = 2,
    Favorable = 3,
    Easy = 4,
}

/// <summary>魔王軍支援の配当のオッズ（見込みの段 → 倍率）。</summary>
public static class SupportOdds
{
    /// <summary>表示％ → 段。境目は ClearProbabilityCalculator.ToOutlook（画面の見込みと同じ 1 か所）。</summary>
    public static SupportOddsTier FromClearPct(float pct)
    {
        if (float.IsNaN(pct) || pct < 0f) return SupportOddsTier.Unknown;
        switch (ClearProbabilityCalculator.ToOutlook(pct))
        {
            case ClearOutlook.Easy: return SupportOddsTier.Easy;
            case ClearOutlook.Favorable: return SupportOddsTier.Favorable;
            case ClearOutlook.Even: return SupportOddsTier.Even;
            case ClearOutlook.Unfavorable: return SupportOddsTier.Unfavorable;
            default: return SupportOddsTier.Hopeless;
        }
    }

    /// <summary>勝敗メーター（0〜1。BattleOutlookEstimator）→ 段。</summary>
    public static SupportOddsTier FromMeter(float meter) => FromClearPct(Mathf.Clamp01(meter) * 100f);

    public static string Key(SupportOddsTier tier) => tier switch
    {
        SupportOddsTier.Easy => "easy",
        SupportOddsTier.Favorable => "favorable",
        SupportOddsTier.Even => "even",
        SupportOddsTier.Unfavorable => "unfavorable",
        SupportOddsTier.Hopeless => "hopeless",
        _ => "unknown",
    };

    public static string Label(SupportOddsTier tier) => tier switch
    {
        SupportOddsTier.Easy => "楽勝",
        SupportOddsTier.Favorable => "有利",
        SupportOddsTier.Even => "互角",
        SupportOddsTier.Unfavorable => "不利",
        SupportOddsTier.Hopeless => "絶望",
        _ => "不明",
    };

    /// <summary>段の倍率。見込みが分からない（Unknown）ときは 1。負の値は 0 にする。</summary>
    public static float Multiplier(SupportOddsTier tier, StreamTuningData d = null)
    {
        d ??= StreamTuning.Data;
        float m = tier switch
        {
            SupportOddsTier.Easy => d.supportOddsEasy,
            SupportOddsTier.Favorable => d.supportOddsFavorable,
            SupportOddsTier.Even => d.supportOddsEven,
            SupportOddsTier.Unfavorable => d.supportOddsUnfavorable,
            SupportOddsTier.Hopeless => d.supportOddsHopeless,
            _ => 1f,
        };
        return float.IsNaN(m) ? 1f : Mathf.Max(0f, m);
    }

    /// <summary>配当に倍率を掛ける。倍率 1 なら元の額そのまま（丸めの差も出さない）。</summary>
    public static int Apply(int reward, float multiplier)
    {
        if (reward <= 0) return reward;
        if (Mathf.Approximately(multiplier, 1f)) return reward;
        return (int)Math.Round(reward * (double)multiplier, MidpointRounding.AwayFromZero);
    }

    /// <summary>どれかの段の倍率が 1 でないか（全部 1 なら配当はどこでも今と同じ）。</summary>
    public static bool IsActive(StreamTuningData d = null)
    {
        d ??= StreamTuning.Data;
        foreach (SupportOddsTier t in AllTiers)
            if (!Mathf.Approximately(Multiplier(t, d), 1f)) return true;
        return false;
    }

    private static readonly SupportOddsTier[] AllTiers =
    {
        SupportOddsTier.Hopeless, SupportOddsTier.Unfavorable, SupportOddsTier.Even,
        SupportOddsTier.Favorable, SupportOddsTier.Easy,
    };

    /// <summary>
    /// 防衛報酬（魔王軍支援の配当）の計算の唯一の式。基本額 × レリック倍率（四捨五入）→ 見込みの段のオッズ（Apply）。
    /// 実際の支払い（BattleSceneStarter）・配当通知書・実績（AchievementTracker）・AI の見込み（オートプレイ）はみなここを通す。
    /// clearPct は配信開始時の見込み（表示％。分からなければ負の値 → オッズ 1）。
    /// </summary>
    public static SupportPayout Payout(int baseReward, float relicMultiplier, float clearPct, StreamTuningData d = null)
    {
        int relicReward = Mathf.RoundToInt(baseReward * relicMultiplier);
        var tier = FromClearPct(clearPct);
        float odds = Multiplier(tier, d);
        return new SupportPayout(baseReward, relicMultiplier, relicReward, tier, odds, Apply(relicReward, odds));
    }

    /// <summary>
    /// AI などの見込み用: レリック込みの配当 reward に、その見込み（clearPct()）の段のオッズを掛ける。
    /// オッズが全部 1 なら clearPct を呼ばずにそのまま返す（重いドライランをしない・今と同じ）。
    /// </summary>
    public static int Expected(int reward, Func<float> clearPct, StreamTuningData d = null)
    {
        d ??= StreamTuning.Data;
        if (reward <= 0 || clearPct == null || !IsActive(d)) return reward;
        float pct;
        try { pct = clearPct(); }
        catch (Exception) { return reward; }
        return Apply(reward, Multiplier(FromClearPct(pct), d));
    }
}

/// <summary>防衛報酬の内訳（基本額 × レリック倍率 = RelicReward、× 見込みの段のオッズ = Total）。</summary>
public readonly struct SupportPayout
{
    public readonly int BaseReward;
    /// <summary>レリックの倍率（DefeatRewardMul）。</summary>
    public readonly float Multiplier;
    /// <summary>レリック補正まで掛けた額（オッズの前）。</summary>
    public readonly int RelicReward;
    public readonly SupportOddsTier OddsTier;
    public readonly float OddsMultiplier;
    /// <summary>実際に受け取る額。</summary>
    public readonly int Total;

    public SupportPayout(int baseReward, float multiplier, int relicReward, SupportOddsTier oddsTier, float oddsMultiplier, int total)
    {
        BaseReward = baseReward;
        Multiplier = multiplier;
        RelicReward = relicReward;
        OddsTier = oddsTier;
        OddsMultiplier = oddsMultiplier;
        Total = total;
    }

    public static readonly SupportPayout None = new(0, 1f, 0, SupportOddsTier.Unknown, 1f, 0);
}

/// <summary>StreamTuningData を読む口と、キーを効かせる計算（テストのため純粋な関数にしてある）。</summary>
public static class StreamTuning
{
    private static readonly StreamTuningData Defaults = new();
    private static readonly BossReactionSettingsData BossDefaults = new();

    public static StreamTuningData Data => GameConst.Data?.streamTuning ?? Defaults;
    public static BossReactionSettingsData BossReaction => GameConst.Data?.bossReaction ?? BossDefaults;

    // ---- 勇者が勝ったときの装備の相場（ItemModel.HeroWinMultiplier） ----

    public static float HeroWinMultiplier(int defeatedBossCount, StreamTuningData d = null)
    {
        d ??= Data;
        float m = defeatedBossCount > 0 ? d.heroWinBossPriceMultiplier : d.heroWinPriceMultiplier;
        if (float.IsNaN(m) || m <= 0f)
            m = defeatedBossCount > 0 ? ItemModel.HeroWinBossPriceMultiplier : ItemModel.HeroWinPriceMultiplier;
        return m;
    }

    // ---- 補充の売れ残りの払い戻し（BattleSceneStarter.CalculateUnsoldRestockRefund） ----

    /// <summary>売れ残り数 × 補充単価 × restockRefundRate。率 1 なら今と同じ整数の掛け算。</summary>
    public static int RestockRefund(int unsoldQuantity, int unitCost, StreamTuningData d = null)
    {
        if (unsoldQuantity <= 0 || unitCost <= 0) return 0;
        d ??= Data;
        float rate = float.IsNaN(d.restockRefundRate) ? 1f : Mathf.Max(0f, d.restockRefundRate);
        if (Mathf.Approximately(rate, 1f)) return unsoldQuantity * unitCost;
        return (int)Math.Round((double)unsoldQuantity * unitCost * rate, MidpointRounding.AwayFromZero);
    }

    // ---- 村の広場の上乗せの土台（BattleResultHandler） ----

    /// <summary>広場の上乗せを掛ける売上。既定は配信の売上そのまま、キーが on なら返金騒ぎを引いた売上。</summary>
    public static int PlazaBonusBase(int rawStreamSales, int heroDefeatRefund, StreamTuningData d = null)
    {
        d ??= Data;
        if (!d.plazaBonusAfterRefund) return rawStreamSales;
        return Mathf.Max(0, rawStreamSales - Mathf.Max(0, heroDefeatRefund));
    }

    // ---- 納付日の朝の損の下限（TomsEventExecutor.ApplyChangeMoney） ----

    /// <summary>
    /// 損のイベント（amount &lt; 0）を当てたあとの所持金。floor &gt; 0 なら所持金を floor より下へは減らさない
    /// （もともと floor 未満なら 1G も取らない）。floor = 0 なら今と同じ（0 で止める）。
    /// </summary>
    public static int MoneyAfterLoss(int before, int amount, int floor)
    {
        if (amount >= 0) return before + amount;
        int after = Mathf.Max(0, before + amount);
        if (floor <= 0) return after;
        return Mathf.Max(after, Mathf.Min(before, floor));
    }

    /// <summary>今日が納付日の朝なら（キーが on のとき）その納付額、そうでなければ 0。</summary>
    public static int PaymentMorningFloor(TomsModel player, RelicEffectResolver relicResolver, StreamTuningData d = null)
    {
        d ??= Data;
        if (!d.paymentMorningLossFloor || player == null) return 0;
        int day = player.CurrentTurn.Value;
        int paid = player.DebtCycle.Value;
        if (DebtCalculator.CountDueCycles(day) <= paid) return 0; // 今日は納付の期日ではない（または納付済み）
        return DebtCalculator.GetAmount(paid + 1, player, relicResolver);
    }

    // ---- ランクの基準（ResultModel.RankBaseProfit） ----

    /// <summary>
    /// ランク A の基準 = （上書きがあれば上書き、無ければ rankBaseProfits の値）+ 村の総合Lv × rankBasePerVillageLevel。
    /// 既定（上書き 0・増分 0）なら rankBaseProfits の値そのまま。
    /// </summary>
    public static int RankBase(int tableValue, int overrideValue, int perVillageLevel, int villageTotalLevel)
    {
        long v = overrideValue > 0 ? overrideValue : tableValue;
        if (perVillageLevel != 0 && villageTotalLevel > 0) v += (long)perVillageLevel * villageTotalLevel;
        return (int)Math.Max(1L, Math.Min(int.MaxValue, v));
    }
}

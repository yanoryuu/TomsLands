using UnityEngine;

/// <summary>
/// 配当通知書（DemonKingPayoutView）に渡す内訳。
/// お金の計算は BattleSceneStarter で済んでいるので、ここは表示用の値だけを持つ。
/// </summary>
public sealed class DemonKingPayoutData
{
    /// <summary>ダンジョン名（例: 魔王城）</summary>
    public string DungeonName { get; }

    /// <summary>防衛したときのダンジョンLv</summary>
    public int DungeonLevel { get; }

    /// <summary>魔王軍支援でこのダンジョンに払った累計額。0 なら支援額の行と倍率ハンコを出さない</summary>
    public int SupportAmount { get; }

    /// <summary>防衛報酬の基本額（rewardGold）</summary>
    public int BaseReward { get; }

    /// <summary>レリックによる防衛報酬の倍率（1.0 で補正なし）</summary>
    public float RelicMultiplier { get; }

    /// <summary>実際に受け取る額（基本額 × 倍率。totalEarnings に含まれている額と同じ）</summary>
    public int FinalReward { get; }

    /// <summary>このダンジョンでこれまでに受け取った防衛報酬の合計（今回分を含まない）</summary>
    public int PreviousTotalReward { get; }

    /// <summary>今回分を含めた防衛報酬の受取累計</summary>
    public int CumulativeReward => PreviousTotalReward + FinalReward;

    public DemonKingPayoutData(string dungeonName, int dungeonLevel, int supportAmount, int baseReward, float relicMultiplier, int finalReward,
        int previousTotalReward = 0)
    {
        PreviousTotalReward = Mathf.Max(0, previousTotalReward);
        DungeonName = dungeonName;
        DungeonLevel = dungeonLevel;
        SupportAmount = supportAmount;
        BaseReward = baseReward;
        RelicMultiplier = relicMultiplier;
        FinalReward = finalReward;
    }

    /// <summary>実際の支払いの内訳（SupportPayout.Calculate）から作る。</summary>
    public static DemonKingPayoutData From(string dungeonName, int dungeonLevel, int supportAmount, SupportPayout payout, int previousTotalReward = 0)
        => new(dungeonName, dungeonLevel, supportAmount, payout.BaseReward, payout.Multiplier, payout.Total, previousTotalReward);

    /// <summary>レリック補正の行を出すか（倍率が 1.0 より大きいときだけ）</summary>
    public bool HasRelicBonus => RelicMultiplier > 1.0001f && FinalReward > BaseReward;

    /// <summary>レリック補正で増えた額</summary>
    public int RelicBonus => FinalReward - BaseReward;

    /// <summary>
    /// 回収倍率（受取累計 ÷ 支援額）。支援額が 0 なら 0。
    /// 支援は1回払えばずっと効き、防衛報酬は負けるたびに入るので、1回分ではなく累計で見る
    /// </summary>
    public float ReturnRate => SupportAmount > 0 ? (float)CumulativeReward / SupportAmount : 0f;

    /// <summary>今回の受け取りで、はじめて支援額を回収しきった（累計が支援額に届いた）</summary>
    public bool JustRecoveredPrincipal => SupportAmount > 0 && PreviousTotalReward < SupportAmount && CumulativeReward >= SupportAmount;
}

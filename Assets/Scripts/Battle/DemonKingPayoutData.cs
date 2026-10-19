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

    public DemonKingPayoutData(string dungeonName, int dungeonLevel, int supportAmount, int baseReward, float relicMultiplier, int finalReward)
    {
        DungeonName = dungeonName;
        DungeonLevel = dungeonLevel;
        SupportAmount = supportAmount;
        BaseReward = baseReward;
        RelicMultiplier = relicMultiplier;
        FinalReward = finalReward;
    }

    /// <summary>レリック補正の行を出すか（倍率が 1.0 より大きいときだけ）</summary>
    public bool HasRelicBonus => RelicMultiplier > 1.0001f && FinalReward > BaseReward;

    /// <summary>レリック補正で増えた額</summary>
    public int RelicBonus => FinalReward - BaseReward;

    /// <summary>回収倍率（受取額 ÷ 支援額）。支援額が 0 なら 0</summary>
    public float ReturnRate => SupportAmount > 0 ? (float)FinalReward / SupportAmount : 0f;
}

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

    /// <summary>実際に受け取る額（基本額 × レリック倍率 × オッズ。totalEarnings に含まれている額と同じ）</summary>
    public int FinalReward { get; }

    /// <summary>レリック補正まで掛けた額（オッズを掛ける前）。</summary>
    public int RelicReward { get; }

    /// <summary>配信開始時の見込みの段（魔王軍支援の配当のオッズ）。</summary>
    public SupportOddsTier OddsTier { get; }

    /// <summary>見込みの段のオッズ（1.0 で補正なし）。</summary>
    public float OddsMultiplier { get; }

    /// <summary>このダンジョンでこれまでに受け取った防衛報酬の合計（今回分を含まない）</summary>
    public int PreviousTotalReward { get; }

    /// <summary>今回分を含めた防衛報酬の受取累計</summary>
    public int CumulativeReward => PreviousTotalReward + FinalReward;

    /// <summary>オッズなし（倍率 1）の内訳。finalReward = 基本額 × レリック倍率。</summary>
    public DemonKingPayoutData(string dungeonName, int dungeonLevel, int supportAmount, int baseReward, float relicMultiplier, int finalReward,
        int previousTotalReward = 0)
        : this(dungeonName, dungeonLevel, supportAmount, baseReward, relicMultiplier, finalReward,
            SupportOddsTier.Unknown, 1f, finalReward, previousTotalReward)
    {
    }

    /// <summary>
    /// オッズ込みの内訳。relicReward = 基本額 × レリック倍率、finalReward = relicReward × オッズ（元本保証なら出資の合計まで引き上げた額）。
    /// principalGuaranteed = 元本保証（SupportPayout.FloorApplied）で受取額が引き上がった。
    /// </summary>
    public DemonKingPayoutData(string dungeonName, int dungeonLevel, int supportAmount, int baseReward, float relicMultiplier, int relicReward,
        SupportOddsTier oddsTier, float oddsMultiplier, int finalReward, int previousTotalReward = 0, bool principalGuaranteed = false)
    {
        PrincipalGuaranteed = principalGuaranteed;
        PreviousTotalReward = Mathf.Max(0, previousTotalReward);
        DungeonName = dungeonName;
        DungeonLevel = dungeonLevel;
        SupportAmount = supportAmount;
        BaseReward = baseReward;
        RelicMultiplier = relicMultiplier;
        RelicReward = relicReward;
        OddsTier = oddsTier;
        OddsMultiplier = oddsMultiplier;
        FinalReward = finalReward;
    }

    /// <summary>実際の支払いの内訳（SupportOdds.Payout）から作る。</summary>
    public static DemonKingPayoutData From(string dungeonName, int dungeonLevel, int supportAmount, SupportPayout payout, int previousTotalReward = 0)
        => new(dungeonName, dungeonLevel, supportAmount, payout.BaseReward, payout.Multiplier, payout.RelicReward,
            payout.OddsTier, payout.OddsMultiplier, payout.Total, previousTotalReward, payout.FloorApplied);

    /// <summary>元本保証（出資の合計を下限にする。streamTuning.supportPayoutFloorToStake）で受取額が引き上がったか。</summary>
    public bool PrincipalGuaranteed { get; }

    /// <summary>レリック補正の行を出すか（倍率が 1.0 より大きいときだけ）</summary>
    public bool HasRelicBonus => RelicMultiplier > 1.0001f && RelicReward > BaseReward;

    /// <summary>レリック補正で増えた額（オッズの分は含めない）</summary>
    public int RelicBonus => RelicReward - BaseReward;

    /// <summary>
    /// オッズの行を出すか（倍率が 1 でないとき、または元本保証で受取額が引き上がったとき。どちらでもなければ今までどおり行を出さない）。
    /// 元本保証の分もこの行に入れるので、明細は 4 行のまま（行を足すと受取額になる）。
    /// </summary>
    public bool HasOddsLine => !Mathf.Approximately(OddsMultiplier, 1f) || PrincipalGuaranteed;

    /// <summary>オッズ（と元本保証）で増減した額（下がる段ではマイナス）。基本額 + レリック補正 + これ = 受取額。</summary>
    public int OddsBonus => FinalReward - RelicReward;

    /// <summary>
    /// オッズの行の見出し（例: 見込み 不利 ×0.45）。元本保証で引き上がったときは「見込み 絶望 ×0.3（元本保証）」
    /// （右の金額とぶつからないよう括弧の中は小さく・短く）、オッズが 1 のまま元本保証だけ効いたときは「元本保証」。
    /// </summary>
    public string OddsLabel
    {
        get
        {
            string odds = $"見込み {SupportOdds.Label(OddsTier)} <size=80%>×{OddsMultiplier:0.0#}</size>";
            if (!PrincipalGuaranteed) return odds;
            if (Mathf.Approximately(OddsMultiplier, 1f)) return PrincipalGuaranteeLabel;
            return odds + $"<size=65%>（{PrincipalGuaranteeLabel}）</size>";
        }
    }

    /// <summary>元本保証の見出し（オッズの行に添える）。</summary>
    public const string PrincipalGuaranteeLabel = "元本保証";

    /// <summary>オッズの行の金額の数字（+12,000 / -8,000。単位 G は表示側で付ける。通知書のフォントに U+2212 の − が無いので ASCII の - を使う）。</summary>
    public string OddsValueNumber => (OddsBonus >= 0 ? "+" : "-") + $"{Mathf.Abs(OddsBonus):N0}";

    /// <summary>
    /// 回収倍率（受取累計 ÷ 支援額）。支援額が 0 なら 0。
    /// 支援は1回払えばずっと効き、防衛報酬は負けるたびに入るので、1回分ではなく累計で見る
    /// </summary>
    public float ReturnRate => SupportAmount > 0 ? (float)CumulativeReward / SupportAmount : 0f;

    /// <summary>今回の受け取りで、はじめて支援額を回収しきった（累計が支援額に届いた）</summary>
    public bool JustRecoveredPrincipal => SupportAmount > 0 && PreviousTotalReward < SupportAmount && CumulativeReward >= SupportAmount;
}

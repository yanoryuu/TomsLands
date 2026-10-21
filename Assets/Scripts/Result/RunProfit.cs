using UnityEngine;

/// <summary>
/// そのランの「儲け」= 純資産 − 初期資金 − 持ち込み − 立て替え（Docs/Village_Meta_Design.md §4）。
/// 村資金への変換（MetaProgressModel.ConvertRunToVillageFunds）とランク（ResultModel.EvaluateRank）の基準。
/// 元手（持ち込み・立て替え）はラン開始時に RunHistory へ記録したものを使う。
/// </summary>
public readonly struct RunProfit
{
    public int NetWorth { get; }
    public int InitialMoney { get; }
    public int Carried { get; }
    public int Advance { get; }
    public int Profit => NetWorth - InitialMoney - Carried - Advance;

    public RunProfit(int netWorth, int initialMoney, int carried, int advance)
    {
        NetWorth = netWorth;
        InitialMoney = initialMoney;
        Carried = Mathf.Max(0, carried);
        Advance = Mathf.Max(0, advance);
    }

    /// <summary>純資産と、このランの記録（RunHistory）・メタ進行から儲けを求める。</summary>
    public static RunProfit Compute(int netWorth, RunHistoryData history, MetaProgressModel meta)
    {
        int carried = history != null && history.startRecorded ? history.carriedGold : 0;
        return new RunProfit(netWorth, GameConst.InitMoney, carried, AdvanceOf(history, meta));
    }

    /// <summary>
    /// このランで受け取ったチュートリアルの立て替え（返済不要の資金）。
    /// 立て替えはプロフィールで1回だけ。ラン開始時にまだ受け取っていなくて、今は受け取り済みなら、このランで受け取った。
    /// 開始時の記録が無い古いランは「最初のラン（総ラン数0）で受け取り済みなら、このラン」とみなす。
    /// </summary>
    private static int AdvanceOf(RunHistoryData history, MetaProgressModel meta)
    {
        if (meta == null || !meta.HasSeenTutorial(TutorialSteps.StepFundsAdvanceGranted)) return 0;
        bool grantedThisRun = history != null && history.startRecorded
            ? !history.advanceGrantedBefore
            : meta.TotalRuns == 0;
        return grantedThisRun ? TutorialSteps.FundsAdvanceAmount : 0;
    }

    /// <summary>破産したラン用: 所持金の最高値をそのランの純資産の目安にした儲け（在庫・金融資産は数えない）。</summary>
    public static RunProfit ComputePeak(RunHistoryData history, MetaProgressModel meta, int currentMoney)
    {
        int peak = currentMoney;
        if (history?.moneyValues != null)
            foreach (int v in history.moneyValues) peak = Mathf.Max(peak, v);
        return Compute(peak, history, meta);
    }
}

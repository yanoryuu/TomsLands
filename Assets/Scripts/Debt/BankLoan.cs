using UnityEngine;

/// <summary>
/// 村の銀行 Lv5「勝負の融資」（Docs/Village_Meta_Design.md §0-6）。
/// 1ランに1回、配信の前日の朝に、持ち込み上限と同じ額（bankCarryLimits の最後の値。今は 70万G）を借りられる。
/// 配信の次の日の朝に1割の利息をつけて返す。返せなければ通常の納付と同じ（売上の前倒し → 金融資産 → 在庫の投げ売り → 破産。DebtPresenter）。
/// 最後の配信の前に借りて返す日が来ないまま終わったら、決算の純資産から返す額を差し引く（ResultModel）。
/// 使ったかどうか・返す額・返す日はランのセーブ（tomsData.json）に置く。
/// </summary>
public static class BankLoan
{
    public const string OfferTitle = "勝負の融資";
    public const string BorrowButton = "借りる";
    public const string SkipButton = "今回は使わない";
    public const string RepayTitle = "融資の返済日！";

    /// <summary>
    /// 今日、融資を持ちかけられるか: 銀行が Lv5・このランでまだ使っていない・明日が配信（配信の前日）。
    /// </summary>
    public static bool CanOffer(TomsModel toms, GameFlowManager flow) =>
        toms != null && flow != null && !toms.BankLoanUsed
        && VillageEffects.Current.BankLoanUnlocked && VillageEffects.BankLoanPrincipal > 0
        && !flow.IsAwaitingStream.Value && flow.GetDaysUntilNextStream() == 1;

    /// <summary>借りる額。</summary>
    public static int Principal => VillageEffects.BankLoanPrincipal;

    /// <summary>返す額（元本 + 利息）。</summary>
    public static int Repayment => VillageEffects.BankLoanRepayment(Principal);

    /// <summary>返す日（配信の次の日）。今日から配信まで daysUntilStream 日。</summary>
    public static int DueTurnFor(int currentTurn, int daysUntilStream) => currentTurn + Mathf.Max(1, daysUntilStream) + 1;

    /// <summary>まだ返していない融資があるか。</summary>
    public static bool HasOutstanding(TomsModel toms) => toms != null && toms.BankLoanOwed > 0;

    /// <summary>今日が返す日（を過ぎている）か。</summary>
    public static bool IsDue(TomsModel toms, int currentTurn) => HasOutstanding(toms) && currentTurn >= toms.BankLoanDueTurn;

    /// <summary>融資を受ける（所持金に足して保存し、リザルトの振り返りにも記録する）。</summary>
    public static void Take(TomsModel toms, GameFlowManager flow)
    {
        if (!CanOffer(toms, flow)) return;
        int turn = flow.CurrentTurn.Value;
        int due = DueTurnFor(turn, flow.GetDaysUntilNextStream());
        int principal = Principal;
        int owed = Repayment;
        toms.TakeBankLoan(principal, owed, due);
        RunHistory.RecordBankLoan(turn, owed);
        Debug.Log($"[BankLoan] 勝負の融資: +{principal}G（{due}日目に {owed}G を返す）");
    }

    /// <summary>返し終えたことを記録する（お金の出し入れは TomsModel.RepayBankLoan）。</summary>
    public static void OnRepaid(int turn) => RunHistory.RecordBankLoanRepaid(turn);
}

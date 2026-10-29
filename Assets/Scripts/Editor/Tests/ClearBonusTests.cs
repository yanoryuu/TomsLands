using System.Runtime.Serialization;
using NUnit.Framework;

/// <summary>
/// 完走ボーナス = clearBaseFlat（30万G）+ clearDebtMultiplier（2）× そのランで期日が来た納付金の予定額の合計。
/// 期日は 7 日ごと（n 回目は 7n 日目）。実際に払った額（減免状・借入・立て替え）では変わらない。
/// GameConstSettings.asset の値（基準額 15,000G・倍率 1.5・30万G・×2）で確かめる。
/// </summary>
public class ClearBonusTests
{
    // 村の施設なし（領主館の上乗せ 0）
    private static readonly VillageEffects NoFacilities = VillageEffects.ForLevels(0, 0, 0, 0, 0, 0);

    /// <summary>店・配信の日数（最後に納付の判定が走る日）から、儲け 0・持ち込み 0 のクリアの村資金を出す。</summary>
    private static int ClearFundsForDays(int days) =>
        MetaProgressModel.CalculateVillageFunds(true, 0, 0, GameModeId.Short, DebtCalculator.CountDueCycles(days), NoFacilities);

    [Test]
    public void SixDays_NoPaymentDue_FlatOnly()
    {
        Assert.AreEqual(0, DebtCalculator.CountDueCycles(6));
        Assert.AreEqual(300000, ClearFundsForDays(6));
    }

    [Test]
    public void SevenDays_FirstPayment()
    {
        Assert.AreEqual(1, DebtCalculator.CountDueCycles(7));
        Assert.AreEqual(330000, ClearFundsForDays(7));
    }

    [Test]
    public void FourteenDays_TwoPayments()
    {
        Assert.AreEqual(375000, ClearFundsForDays(14));
    }

    [Test]
    public void ThirtyFiveDays_FivePayments()
    {
        Assert.AreEqual(197813L, DebtCalculator.GetScheduledTotal(5));
        Assert.AreEqual(300000 + 2 * 197813, ClearFundsForDays(35));
    }

    [Test]
    public void ClearedRun_CountsUpToLastShopDay()
    {
        // リザルトの日数は End の日。最後に納付の判定が走るのはその 2 日前（最後の店の日）
        Assert.AreEqual(0, DebtCalculator.CountDueCyclesOfClearedRun(8));
        Assert.AreEqual(1, DebtCalculator.CountDueCyclesOfClearedRun(9));
    }

    [Test]
    public void DebtDiscount_DoesNotChangeBonus()
    {
        // 初回減免状・借入・立て替えは実際の納付額を変えるが、完走ボーナスは予定額で数える
        var toms = (TomsModel)FormatterServices.GetUninitializedObject(typeof(TomsModel));
        toms.FirstDebtDiscountRate = 0.5f;
        int paid = DebtCalculator.GetAmount(1, toms);
        Assert.AreNotEqual(GameConst.GetDebtAmount(1), paid);

        Assert.AreEqual(330000, MetaProgressModel.ClearBonusBase(1));
        Assert.AreEqual(330000, ClearFundsForDays(7));
    }

    [Test]
    public void Bankruptcy_GetsNoClearBonus()
    {
        Assert.AreEqual(0, MetaProgressModel.CalculateVillageFunds(false, 0, 0, GameModeId.Short, 5, NoFacilities));
    }
}

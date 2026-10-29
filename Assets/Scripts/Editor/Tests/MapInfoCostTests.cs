using NUnit.Framework;
using UnityEngine;

/// <summary>地図（ダンジョンの情報）の値段は、納付の期日が来るたびに mapInfoDebtMultiplier 倍になる。</summary>
public class MapInfoCostTests
{
    [Test]
    public void MapInfoCost_RisesAtEachDebtDueDay()
    {
        int interval = GameConst.DebtPaymentInterval;
        int baseCost = GameConst.GetMapInfoCost(0);
        Assert.Greater(baseCost, 0);

        Assert.AreEqual(0, DebtCalculator.CountDueCycles(interval - 1), "最初の期日の前日はまだ上がらない");
        Assert.AreEqual(baseCost, GameConst.GetMapInfoCost(DebtCalculator.CountDueCycles(interval - 1)));

        int afterFirst = GameConst.GetMapInfoCost(DebtCalculator.CountDueCycles(interval));
        int afterSecond = GameConst.GetMapInfoCost(DebtCalculator.CountDueCycles(interval * 2));
        float mult = GameConst.Data.mapInfoDebtMultiplier;
        Assert.AreEqual(Mathf.RoundToInt(baseCost * mult), afterFirst, 1, "1 回目の期日から値上がりする");
        Assert.AreEqual(Mathf.RoundToInt(baseCost * mult * mult), afterSecond, 1, "2 回目の期日でさらに値上がりする");
    }
}

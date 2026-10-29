using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 勇者敗北の返金騒ぎ = この配信で売れた武器・防具（勇者側の品）の売上 × heroDefeatRefundRate。
/// 道具は数えない。勝ち・途中終了は 0。割合は 0〜1 に収める。
/// </summary>
public class HeroDefeatRefundTests
{
    private static readonly Dictionary<string, ItemTypeData.ItemType> Types = new()
    {
        { "sword", ItemTypeData.ItemType.Weapon },
        { "shield", ItemTypeData.ItemType.Armor },
        { "potion", ItemTypeData.ItemType.Tool },
    };

    private static ItemTypeData.ItemType? TypeOf(string id)
        => Types.TryGetValue(id, out var t) ? t : null;

    private static BattleOutputSoldItem Sold(string id, int revenue, int qty = 1, int price = 0)
        => new() { ItemId = id, Revenue = revenue, SoldQuantity = qty, SoldPrice = price };

    private static readonly List<BattleOutputSoldItem> Mixed = new()
    {
        Sold("sword", 10000),
        Sold("shield", 5000),
        Sold("potion", 8000),
    };

    [Test]
    public void Defeat_RefundsShareOfWeaponAndArmorSalesOnly()
    {
        Assert.AreEqual(15000, HeroDefeatRefund.HeroSideSales(Mixed, TypeOf));
        Assert.AreEqual(6000, HeroDefeatRefund.Calculate(BattleResult.Defeat, Mixed, TypeOf, 0.4f));
    }

    [Test]
    public void VictoryAndAbort_NoRefund()
    {
        Assert.AreEqual(0, HeroDefeatRefund.Calculate(BattleResult.Victory, Mixed, TypeOf, 0.4f));
        Assert.AreEqual(0, HeroDefeatRefund.Calculate(BattleResult.Abort, Mixed, TypeOf, 0.4f));
    }

    [Test]
    public void ToolsOnly_NoRefund()
    {
        var sold = new List<BattleOutputSoldItem> { Sold("potion", 20000) };
        Assert.AreEqual(0, HeroDefeatRefund.Calculate(BattleResult.Defeat, sold, TypeOf, 0.4f));
    }

    [Test]
    public void RevenueMissing_FallsBackToQuantityTimesPrice()
    {
        // 代理配信（AutoPlay）など Revenue が無い記録は 品出し時価格×個数
        var sold = new List<BattleOutputSoldItem> { Sold("sword", 0, qty: 3, price: 1000) };
        Assert.AreEqual(1200, HeroDefeatRefund.Calculate(BattleResult.Defeat, sold, TypeOf, 0.4f));
    }

    [Test]
    public void Rate_IsClampedBetweenZeroAndOne()
    {
        Assert.AreEqual(0, HeroDefeatRefund.Calculate(BattleResult.Defeat, Mixed, TypeOf, 0f));
        Assert.AreEqual(0, HeroDefeatRefund.Calculate(BattleResult.Defeat, Mixed, TypeOf, -1f));
        // 1 を超えても売上ぶんまで（配信の売上より多くは引かない）
        Assert.AreEqual(15000, HeroDefeatRefund.Calculate(BattleResult.Defeat, Mixed, TypeOf, 2f));
    }

    [Test]
    public void UnknownItemsAndNulls_AreIgnored()
    {
        var sold = new List<BattleOutputSoldItem> { null, Sold("unknown", 9999), Sold("shield", 1001) };
        Assert.AreEqual(400, HeroDefeatRefund.Calculate(BattleResult.Defeat, sold, TypeOf, 0.4f)); // 400.4 → 400
        Assert.AreEqual(0, HeroDefeatRefund.Calculate(BattleResult.Defeat, null, TypeOf, 0.4f));
    }

    [Test]
    public void DefaultSetting_IsFortyPercent()
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<BattlePriceSettings>();
        try
        {
            Assert.AreEqual(0.4f, settings.heroDefeatRefundRate, 1e-6f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(settings);
        }
    }
}

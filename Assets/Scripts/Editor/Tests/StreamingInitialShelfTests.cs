using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 配信開始時の並べ方。持ち込んだ品ははじめ全部左の在庫スロットに並び、売り場（右）は空で始まる。
/// </summary>
public class StreamingInitialShelfTests
{
    private const int SellSlots = 5;
    private const int InventorySlots = 10;

    private static RuntimeItemData NewItem(string id, int price) =>
        new(id, id, price, 10, 10, 1, null, null, ItemTypeData.ItemType.Weapon, ItemTypeData.ItemAttribute.Fire, 1, initialTrend: 0f);

    private static List<RuntimeItemData> Carried(int count)
    {
        var list = new List<RuntimeItemData>();
        for (int i = 0; i < count; i++) list.Add(NewItem("item" + i, 1000 * (i + 1)));
        return list;
    }

    [Test]
    public void 持ち込んだ品は全部在庫に並び売り場は空()
    {
        var carried = Carried(8); // 持ち込める上限（StreamingSettingModel.MaxSelection）
        StreamingSalesController.PlanInitialShelves(carried, SellSlots, InventorySlots, out var forSale, out var inventory);

        Assert.AreEqual(0, forSale.Count, "売り場に最初から並んでいる");
        CollectionAssert.AreEqual(carried, inventory, "在庫に持ち込んだ順で並んでいない");
    }

    [Test]
    public void 在庫の枠を超えた分だけ売り場へ回す()
    {
        var carried = Carried(12);
        StreamingSalesController.PlanInitialShelves(carried, SellSlots, InventorySlots, out var forSale, out var inventory);

        CollectionAssert.AreEqual(carried.GetRange(0, InventorySlots), inventory);
        CollectionAssert.AreEqual(carried.GetRange(InventorySlots, 2), forSale, "在庫に入りきらない品が消えた");
    }
}

using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

public class BlackSmithModel
{
    public List<RuntimeItemData> armorRuntimeItems { get; private set; }
    public List<RuntimeItemData> weaponRuntimeItems { get; private set; }

    public Dictionary<string, BlackSmithItemData> itemCount { get; private set; }

    public BlackSmithModel()
    {
        armorRuntimeItems  = new List<RuntimeItemData>();
        weaponRuntimeItems = new List<RuntimeItemData>();
        itemCount          = new Dictionary<string, BlackSmithItemData>(); // ★ 追加
    }

    public void SetRuntimeItems(List<RuntimeItemData> weaponRuntimeItems, List<RuntimeItemData> armorRuntimeItems)
    {
        Debug.Log($"武器の数{weaponRuntimeItems.Count}、防具の数{armorRuntimeItems.Count}");
        this.armorRuntimeItems  = armorRuntimeItems;
        this.weaponRuntimeItems = weaponRuntimeItems;
    }

    public void SetItemCount(string key, int count, int maxCount)
    {
        if (itemCount.TryAdd(key, new BlackSmithItemData(count, maxCount)))
        {
            Debug.Log($"アイテム{key}を追加しました");
        }
        else if (itemCount.TryGetValue(key, out var value))
        {
            value.maxCount.Value  = maxCount; // ★ 追加：max も更新
            value.count.Value     = Mathf.Clamp(count, 0, value.maxCount.Value); // ★ Mathf.Clamp
            Debug.Log($"アイテム{key}の数を{value.count.Value}に変更 (max {value.maxCount.Value})");
        }
    }

    // 計算済みmaxを受け取り、countを0に戻す
    public int PurchaseItem(string itemId, int maxStockAfter)
    {
        if (itemCount.TryGetValue(itemId, out var value))
        {
            var count = value.count.Value;
            SetItemCount(itemId, 0, maxStockAfter); // ★ 購入後の残りmaxで更新
            return count;
        }
        return 0;
    }
}

public class BlackSmithItemData
{
    public ReactiveProperty<int> count {get;private set;}
    public ReactiveProperty<int> maxCount {get;private set;}
    public BlackSmithItemData(int count, int maxCount)
    {
        this.count = new ReactiveProperty<int>(count);
        this.maxCount = new ReactiveProperty<int>(maxCount);
    }
}

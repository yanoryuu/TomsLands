using System.Collections.Generic;
using System.IO;
using System.Linq;
using R3;
using UnityEngine;

public class ItemModel
{
    private readonly List<ItemData> masterItems;

    public ReactiveProperty<int> PlayerMoney { get; } = new(1000);

    public List<RuntimeItemData> RuntimeItems { get; private set; } = new();
    public List<RuntimeItemData> ShopItemList { get; private set; } = new();

    public ItemModel(List<ItemData> masterItems)
    {
        this.masterItems = masterItems;
    }

    public ItemData GetMasterItem(string itemId)
    {
        return masterItems.FirstOrDefault(item => item.itemId == itemId);
    }

    public RuntimeItemData GetRuntimeItem(string itemId)
    {
        return RuntimeItems.FirstOrDefault(item => item.ItemId == itemId);
    }

    public void PurchaseItem(string itemId, int quantity)
    {
        var runtime = GetRuntimeItem(itemId);
        if (runtime != null)
        {
            int totalPrice = runtime.CurrentPrice.Value * quantity;
            if (PlayerMoney.Value >= totalPrice)
            {
                PlayerMoney.Value -= totalPrice;
                runtime.Stock.Value += quantity;
                runtime.PurchasedThisTurn = true;

                float demandIncrease = 0.05f * quantity;
                runtime.Demand.Value = Mathf.Clamp01(runtime.Demand.Value + demandIncrease);
                runtime.UpdatePopularity();
            }
        }
    }

    public void SetShopItemList(Dictionary<string, int> selectedItems)
    {
        ShopItemList.Clear();

        foreach (var kvp in selectedItems)
        {
            var runtime = GetRuntimeItem(kvp.Key);
            if (runtime != null)
            {
                int assignStock = Mathf.Min(runtime.Stock.Value, kvp.Value);
                runtime.Stock.Value -= assignStock;

                var shopItem = new RuntimeItemData(
                    runtime.ItemId,
                    runtime.CurrentPrice.Value,
                    assignStock,
                    runtime.ItemIcon,
                    runtime.Demand.Value
                );

                ShopItemList.Add(shopItem);
            }
        }
    }

    public void SellItem(string itemId, int quantity)
    {
        var runtime = ShopItemList.FirstOrDefault(item => item.ItemId == itemId);
        if (runtime != null && runtime.Stock.Value >= quantity)
        {
            runtime.Stock.Value -= quantity;
            PlayerMoney.Value += runtime.CurrentPrice.Value * quantity;
        }
        else
        {
            Debug.Log("店舗に並べた商品しか売却できません！");
        }
    }

    public void UpdateItemPrices(GamePhase phase)
    {
        foreach (var runtime in RuntimeItems)
        {
            var master = GetMasterItem(runtime.ItemId);
            if (master != null)
            {
                float baseMultiplier = phase switch
                {
                    GamePhase.Battle => Random.Range(0.8f, 1.5f),
                    _ => Random.Range(0.95f, 1.05f)
                };

                float demandBonus = 1.0f + (runtime.Demand.Value * 0.2f);
                runtime.CurrentPrice.Value = Mathf.RoundToInt(master.basePrice * baseMultiplier * demandBonus);

                runtime.UpdatePopularity();
            }
        }
    }

    public void ApplyBattleResult(BattleResult result, List<string> usedItemIds)
    {
        foreach (var runtime in RuntimeItems)
        {
            if (usedItemIds.Contains(runtime.ItemId))
            {
                if (result == BattleResult.Victory)
                {
                    runtime.Demand.Value = Mathf.Clamp01(runtime.Demand.Value + 0.2f);
                }
                else
                {
                    runtime.Demand.Value = Mathf.Clamp01(runtime.Demand.Value - 0.2f);
                }
                runtime.UpdatePopularity();
            }
        }
    }

    public void ResetPurchasedFlags()
    {
        foreach (var runtime in RuntimeItems)
        {
            runtime.PurchasedThisTurn = false;
        }
    }

    public void SaveData()
    {
        var dataList = new RuntimeItemDataList(RuntimeItems.Select(r => r.ToPlainData()).ToList());
        string json = JsonUtility.ToJson(dataList, true);
        File.WriteAllText(Application.persistentDataPath + "/itemData.json", json);
    }

    public void LoadData()
    {
        string path = Application.persistentDataPath + "/itemData.json";
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            var dataList = JsonUtility.FromJson<RuntimeItemDataList>(json);
            RuntimeItems = dataList.items
                .Select(item => new RuntimeItemData(item))
                .ToList();
        }
        else
        {
            RuntimeItems = masterItems
                .Select(master => new RuntimeItemData(
                    master.itemId,
                    master.basePrice,
                    master.initialStock,
                    master.itemIcon,
                    Random.Range(0.3f, 0.7f)
                ))
                .ToList();
        }
    }
}

[System.Serializable]
public class RuntimeItemDataList
{
    public List<RuntimeItemDataPlain> items;

    public RuntimeItemDataList(List<RuntimeItemDataPlain> items)
    {
        this.items = items;
    }
}

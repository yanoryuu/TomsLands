using System.Collections.Generic;
using System.IO;
using System.Linq;
using R3;
using UnityEngine;

public class ItemModel
{
    public readonly List<ItemData> masterItems;

    public List<RuntimeItemData> RuntimeItems { get; private set; } = new();

    public List<RuntimeItemData> WeaponItem { get; private set;} = new();
    public List<RuntimeItemData> ArmorItems { get; private set; } = new();
    public List<RuntimeItemData> ToolItems { get; private set; } = new();
    
    public List<RuntimeItemData> DisplayItemList { get; private set; } = new();

    public ItemModel(List<ItemData> masterItems)
    {
        this.masterItems = masterItems;
        InitializeRuntimeItemsFromMaster();
        WeaponItem = CreateItemRuntimeList(RuntimeItems, ItemTypeData.ItemType.Weapon);
        ArmorItems = CreateItemRuntimeList(RuntimeItems, ItemTypeData.ItemType.Armor);
        ToolItems = CreateItemRuntimeList(RuntimeItems, ItemTypeData.ItemType.Tool);
    }

    public ItemData GetMasterItem(string itemId) =>
        masterItems.FirstOrDefault(item => item.itemId == itemId);

    public RuntimeItemData GetRuntimeItem(string itemId) =>
        RuntimeItems.FirstOrDefault(r => r.ItemId == itemId);

    public void PurchaseItem(string itemId, int quantity)
    {
        var item = GetRuntimeItem(itemId);
        if (item != null) item.Stock.Value += quantity;
    }

    public void SellItem(string itemId, int quantity)
    {
        var item = GetRuntimeItem(itemId);
        if (item != null && item.Stock.Value >= quantity)
            item.Stock.Value -= quantity;
    }

    public List<RuntimeItemData> CreateItemListForDisplay(Dictionary<string, int> selectedItems)
    {
        var list = new List<RuntimeItemData>();
        foreach (var kvp in selectedItems)
        {
            var runtime = GetRuntimeItem(kvp.Key);
            if (runtime != null)
            {
                int assignStock = Mathf.Min(runtime.Stock.Value, kvp.Value);
                runtime.Stock.Value -= assignStock;

                var newItem = new RuntimeItemData(
                    runtime.ItemId,
                    runtime.CurrentPrice.Value,
                    assignStock,
                    runtime.ItemIcon,
                    runtime.ItemType,
                    runtime.Demand.Value
                );
                list.Add(newItem);
            }
            else
            {
                Debug.LogWarning($"Item not found in RuntimeItems: {kvp.Key}");
            }
        }
        return list;
    }

    public void SetDisplayItemList(List<RuntimeItemData> runtimeItems)
    {
        DisplayItemList = runtimeItems;
    }

    public void UpdateItemPrices(GamePhase phase)
    {
        foreach (var runtime in RuntimeItems)
        {
            var master = GetMasterItem(runtime.ItemId);
            if (master == null) continue;

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

    public void ApplyBattleResult(BattleResult result, List<string> usedItemIds)
    {
        foreach (var runtime in RuntimeItems)
        {
            if (!usedItemIds.Contains(runtime.ItemId)) continue;

            runtime.Demand.Value = result == BattleResult.Victory
                ? Mathf.Clamp01(runtime.Demand.Value + 0.2f)
                : Mathf.Clamp01(runtime.Demand.Value - 0.2f);

            runtime.UpdatePopularity();
        }
    }

    public void ResetPurchasedFlags()
    {
        foreach (var runtime in RuntimeItems)
            runtime.PurchasedThisTurn = false;
    }

    public void SaveData()
    {
        var dataList = new RuntimeItemDataList(
            RuntimeItems.Select(r => r.ToPlainData()).ToList()
        );
        string json = JsonUtility.ToJson(dataList, true);
        File.WriteAllText(Application.persistentDataPath + "/itemData.json", json);
        Debug.Log("Item data saved.");
    }

    public void LoadData()
    {
        string path = Application.persistentDataPath + "/itemData.json";
        if (!File.Exists(path))
        {
            InitializeRuntimeItemsFromMaster();
            return;
        }

        string json = File.ReadAllText(path);
        var dataList = JsonUtility.FromJson<RuntimeItemDataList>(json);
        RuntimeItems = dataList.items
            .Select(item => new RuntimeItemData(item, SearchSpriteFromMaster(item.itemId)))
            .ToList();

        Debug.Log("Item data loaded.");
    }

    public void InitializeRuntimeItemsFromMaster()
    {
        RuntimeItems = masterItems
            .Select(master => new RuntimeItemData(
                master.itemId,
                master.basePrice,
                master.initialStock,
                master.itemIcon,
                master.itemType,
                Random.Range(0.3f, 0.7f)
            ))
            .ToList();

        Debug.Log("Runtime items initialized from master.");
    }

    private Sprite SearchSpriteFromMaster(string itemId)
    {
        var master = GetMasterItem(itemId);
        if (master != null) return master.itemIcon;

        Debug.LogWarning($"Master item not found for ID: {itemId}");
        return null;
    }

    public List<RuntimeItemData> CreateItemRuntimeList(List<RuntimeItemData> runtimeItems, ItemTypeData.ItemType itemtype)
    {
        List<RuntimeItemData> list = new List<RuntimeItemData>();
        foreach (var runtimeItem in runtimeItems)
        {
            if (runtimeItem.ItemType == itemtype)
            {
                list.Add(runtimeItem);
                Debug.Log($"Item: {runtimeItem.ItemId}, Type: {runtimeItem.ItemType}");
            }
        }
        
        return list;
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
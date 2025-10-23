using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class ItemModel
{
    public readonly List<ItemData> masterItems;

    public List<RuntimeItemData> RuntimeItems { get; private set; } = new();

    public ItemModel(List<ItemData> masterItems)
    {
        this.masterItems = masterItems;
        InitializeRuntimeItemsFromMaster();
        LoadData();
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
    
    public void Settlement(string itemId, int quantity)
    {
        var item = GetRuntimeItem(itemId);
        if (item != null && item.Stock.Value >= quantity)
        {
            item.Stock.Value -= quantity;
        }
        else
        {
            Debug.LogWarning($"Item {itemId} not found or insufficient stock for settlement.");
        }
    }

    public void UpdateItemPrices(GamePhase phase)
    {
        foreach (var runtime in RuntimeItems)
        {
            var master = GetMasterItem(runtime.ItemId);
            if (master == null) continue;

            float baseMultiplier = phase switch
            {
                GamePhase.Streaming => Random.Range(0.8f, 1.5f),
                _ => Random.Range(0.95f, 1.05f)
            };

            float demandBonus = 1.0f + (runtime.Demand.Value * 0.2f);
            
            runtime.UpdatePrice(Mathf.RoundToInt(master.basePrice * baseMultiplier * demandBonus));

            runtime.UpdatePopularity();
        }
    }
    
    public void BattleWinBonus(string itemId, int bonusAmountDivision = 1)
    {
        var runtime = GetRuntimeItem(itemId);
        if (runtime != null)
        {
            runtime.CurrentPrice.Value *= bonusAmountDivision;
            // 需要率は掛け算後に 0～1 の範囲へクランプ
            float newDemand = Mathf.Clamp01(runtime.Demand.Value * bonusAmountDivision);
            runtime.Demand.Value = newDemand;
            Debug.Log($"Battle win bonus applied to {itemId}: +{bonusAmountDivision} stock.");
        }
        else
        {
            Debug.LogWarning($"Item not found for battle win bonus: {itemId}");
        }
    }
    
    public void BattleDefeatPenalty(string itemId, int penaltyAmountMultiplier)
    {
        var runtime = GetRuntimeItem(itemId);
        if (runtime != null)
        {
            runtime.CurrentPrice.Value /= penaltyAmountMultiplier;
            runtime.Demand.Value /= penaltyAmountMultiplier;
            Debug.Log($"Battle defeat penalty applied to {itemId}: /{penaltyAmountMultiplier} stock.");
        }
        else
        {
            Debug.LogWarning($"Item not found for battle defeat penalty: {itemId}");
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

    //セーブ
    public void SaveData()
    {
        var dataList = new RuntimeItemDataList(
            RuntimeItems.Select(r => r.ToPlainData()).ToList()
        );
        string json = JsonUtility.ToJson(dataList, true);
        File.WriteAllText(Application.persistentDataPath + "/itemData.json", json);
        Debug.Log("Item data saved.");
    }

    //ここでロード
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

    //初期データ
    public void InitializeRuntimeItemsFromMaster()
    {
        RuntimeItems = masterItems
            .Select(master => new RuntimeItemData(
                master.itemId,
                master.basePrice,
                master.maxStock,
                master.initialStock,
                master.initialDisplayStock,
                master.itemIcon,
                master.itemType,
                master.itemAttribute,
                master.requiredLevel,
                Random.Range(0.3f, 0.7f),
                master.description
            ))
            .ToList();

        foreach (var runtimeItem in RuntimeItems)
        {
            Debug.Log(runtimeItem.ItemId);   
        }
        Debug.Log("Runtime items initialized from master.");

    }
    
    //マスターからスプライトデータを収集
    private Sprite SearchSpriteFromMaster(string itemId)
    {
        var master = GetMasterItem(itemId);
        if (master != null) return master.itemIcon;

        Debug.LogWarning($"Master item not found for ID: {itemId}");
        return null;
    }

    //ランタイムを作成(タイプごとに選出してくれます)
    public List<RuntimeItemData> PickItemRuntimeList(List<RuntimeItemData> runtimeItems, ItemTypeData.ItemType itemtype ,int currentLevel)
    {
        Debug.Log($"Itemのストック{runtimeItems}");
        List<RuntimeItemData> list = new List<RuntimeItemData>();
        foreach (var runtimeItem in runtimeItems)
        {
            if (runtimeItem.ItemType == itemtype && 
                runtimeItem.RequiredLevel.Value <= currentLevel)
            {
                list.Add(runtimeItem);
                Debug.Log($"Item: {runtimeItem.ItemId}, Type: {runtimeItem.ItemType}");
            }
        }
        return list;
    }
    
    //所持数のアイテムの数で選出
    public List<RuntimeItemData> PickItemRuntimeListForStock(List<RuntimeItemData> runtimeItems　,int minStock=0 ,int maxStock=99)
    {
        List<RuntimeItemData> list = new List<RuntimeItemData>();
        foreach (var runtimeItem in runtimeItems)
        {
            if (runtimeItem.Stock.Value >= minStock && runtimeItem.Stock.Value <= maxStock)
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
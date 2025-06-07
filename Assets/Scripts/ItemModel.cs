using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using R3;

public class ItemModel
{
    private readonly List<ItemData> masterItems;

    public ReactiveProperty<int> PlayerMoney { get; } = new ReactiveProperty<int>(1000);

    public List<RuntimeItemData> RuntimeItems { get; private set; } = new List<RuntimeItemData>();

    public ItemModel(List<ItemData> masterItems)
    {
        this.masterItems = masterItems;
    }

    public ItemData GetMasterItem(string itemId)
    {
        return masterItems.FirstOrDefault(item => item.itemId == itemId);
    }

    public void PurchaseItem(string itemId, int quantity)
    {
        var runtime = RuntimeItems.FirstOrDefault(item => item.ItemId == itemId);
        if (runtime != null)
        {
            int totalPrice = runtime.CurrentPrice.Value * quantity;
            if (PlayerMoney.Value >= totalPrice)
            {
                PlayerMoney.Value -= totalPrice;
                runtime.Stock.Value += quantity;
            }
        }
    }

    public void SellItem(string itemId, int quantity)
    {
        var runtime = RuntimeItems.FirstOrDefault(item => item.ItemId == itemId);
        if (runtime != null && runtime.Stock.Value >= quantity)
        {
            runtime.Stock.Value -= quantity;
            PlayerMoney.Value += runtime.CurrentPrice.Value * quantity;
        }
    }

    public void UpdateItemPrices(GamePhase phase)
    {
        foreach (var runtime in RuntimeItems)
        {
            var master = GetMasterItem(runtime.ItemId);
            if (master != null)
            {
                if (phase == GamePhase.Preparation)
                {
                    runtime.CurrentPrice.Value = Mathf.RoundToInt(master.basePrice * Random.Range(0.95f, 1.05f));
                }
                else if (phase == GamePhase.Battle)
                {
                    runtime.CurrentPrice.Value = Mathf.RoundToInt(master.basePrice * Random.Range(0.8f, 1.3f));
                }
            }
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
                .Select(master => new RuntimeItemData(master.itemId, master.basePrice, 0))
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

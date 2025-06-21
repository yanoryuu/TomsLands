using R3;
using UnityEngine;

[System.Serializable]
public class RuntimeItemData
{
    public string ItemId { get; private set; }
    public ReactiveProperty<int> CurrentPrice { get; private set; }
    public ReactiveProperty<int> Stock { get; private set; }
    public ReactiveProperty<float> Demand { get; private set; }
    public ReactiveProperty<bool> IsPopular { get; private set; }
    public ReactiveProperty<int> maxStock { get;private set; }

    public bool PurchasedThisTurn { get; set; }
    public Sprite ItemIcon { get; private set; }
    
    public ItemTypeData.ItemType ItemType { get; private set; }
    
    public RuntimeItemData(string itemId, int currentPrice,int maxstock, int stock, Sprite icon, ItemTypeData.ItemType itemType, float demand = 0.5f)
    {
        ItemId = itemId;
        CurrentPrice = new ReactiveProperty<int>(currentPrice);
        Stock = new ReactiveProperty<int>(stock);
        maxStock = new ReactiveProperty<int>(maxstock);
        ItemIcon = icon;
        Demand = new ReactiveProperty<float>(demand);
        IsPopular = new ReactiveProperty<bool>(demand >= 0.8f);
        ItemType = itemType;
    }

    public RuntimeItemData(RuntimeItemDataPlain plainData, Sprite icon)
    {
        ItemId = plainData.itemId;
        CurrentPrice = new ReactiveProperty<int>(plainData.currentPrice);
        Stock = new ReactiveProperty<int>(plainData.stock);
        Demand = new ReactiveProperty<float>(plainData.demand);
        IsPopular = new ReactiveProperty<bool>(plainData.demand >= 0.8f);
        ItemIcon = icon;
        PurchasedThisTurn = false;
    }

    public void UpdatePopularity()
    {
        IsPopular.Value = Demand.Value >= 0.8f;
    }

    public RuntimeItemDataPlain ToPlainData()
    {
        return new RuntimeItemDataPlain
        {
            itemId = ItemId,
            currentPrice = CurrentPrice.Value,
            stock = Stock.Value,
            demand = Demand.Value,
            isPopular = IsPopular.Value
        };
    }
}

[System.Serializable]
public class RuntimeItemDataPlain
{
    public string itemId;
    public int currentPrice;
    public int stock;
    public float demand;
    public bool isPopular;
    public ItemTypeData.ItemType itemType;
}

public class ItemTypeData
{
    public enum ItemType
    {
        Weapon,
        Armor,
        Tool
    }
}
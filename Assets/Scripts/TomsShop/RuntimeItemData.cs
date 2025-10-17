using R3;
using UnityEngine;

[System.Serializable]
public class RuntimeItemData
{
    public string ItemId { get; private set; }
    public ReactiveProperty<int> CurrentPrice { get; private set; }
    public ReactiveProperty<int> Stock { get; private set; }
    
    public ReactiveProperty<int> DisplayStock { get; private set; }
    public ReactiveProperty<float> Demand { get; private set; }
    public ReactiveProperty<bool> IsPopular { get; private set; }
    public ReactiveProperty<int> MaxStock { get;private set; }
    
    public ReactiveProperty<bool> IsDisplay {get; private set;}
    public ReactiveProperty<int> RequiredLevel { get; set; }
    public Sprite ItemIcon { get; private set; }
    
    public ItemTypeData.ItemType ItemType { get; private set; }
    public ItemTypeData.ItemAttribute ItemAttribute { get; private set; }
    
    public string ItemDescription { get; private set; }
    public RuntimeItemData(
        string itemId,
        int currentPrice,
        int maxStock, 
        int stock,
        int displayStock,
        Sprite icon, 
        ItemTypeData.ItemType itemType,
        ItemTypeData.ItemAttribute itemAttribute,
        int requiredLevel, 
        float demand = 0.5f ,
        string description = "")
    {
        ItemId = itemId;
        CurrentPrice = new ReactiveProperty<int>(currentPrice);
        Stock = new ReactiveProperty<int>(stock);
        DisplayStock = new ReactiveProperty<int>(displayStock);
        MaxStock = new ReactiveProperty<int>(maxStock);
        ItemIcon = icon;
        Demand = new ReactiveProperty<float>(demand);
        IsPopular = new ReactiveProperty<bool>(demand >= 0.8f);
        ItemType = itemType;
        ItemAttribute = itemAttribute; 
        RequiredLevel = new ReactiveProperty<int>(requiredLevel);
        IsDisplay = new ReactiveProperty<bool>(false);
        ItemDescription = description;
    }

    // 保存→復元CTor（Plain→Runtime）
    public RuntimeItemData(RuntimeItemDataPlain plainData, Sprite icon)
    {
        ItemId = plainData.itemId;
        CurrentPrice = new ReactiveProperty<int>(plainData.currentPrice);
        MaxStock     = new ReactiveProperty<int>(Mathf.Max(0, plainData.maxStock));
        Stock        = new ReactiveProperty<int>(Mathf.Clamp(plainData.stock, 0, MaxStock.Value));
        DisplayStock = new ReactiveProperty<int>(Mathf.Max(0, plainData.displayStock)); // 互換
        Demand       = new ReactiveProperty<float>(Mathf.Clamp01(plainData.demand));
        IsPopular    = new ReactiveProperty<bool>(plainData.isPopular || Demand.Value >= 0.8f);
        ItemIcon       = icon;
        ItemType       = plainData.itemType;
        ItemAttribute  = plainData.itemAttribute;
        RequiredLevel  = new ReactiveProperty<int>(Mathf.Max(0, plainData.requiredLevel));
        IsDisplay      = new ReactiveProperty<bool>(plainData.isDisplay);
        ItemDescription = plainData.description;
    }

    public void UpdatePopularity()
    {
        IsPopular.Value = Demand.Value >= 0.8f;
    }

    public void UpdatePrice(int price)
    {
        CurrentPrice.Value = price;
    }

    public void UpdateStock(int stock)
    {
        Stock.Value = Mathf.Clamp(stock, 0, MaxStock.Value);
    }

    public void UpdateDisplayStock(int stock)
    {
        DisplayStock.Value = stock;
    }

    public void UpdateDemand(float demand)
    {
        demand = Mathf.Clamp(demand, 0f, 1f);
    }

    public void UpdateIsPopular(bool isPopular)
    {
        isPopular = IsPopular.Value;
    }

    public void UpdateIsDisplay(bool isDisplay)
    {
        IsDisplay.Value = isDisplay;
    }
    
    // 便利：残り購入可能数（BlackSmithはこれのみでOK）
    public int RemainToMax() => Mathf.Max(0, MaxStock.Value - Stock.Value);

    public RuntimeItemDataPlain ToPlainData()
    {
        return new RuntimeItemDataPlain
        {
            itemId = ItemId,
            currentPrice = CurrentPrice.Value,
            stock = Stock.Value,
            demand = Demand.Value,
            isPopular = IsPopular.Value,
            itemType = ItemType,
            itemAttribute = ItemAttribute,
            displayStock = DisplayStock.Value,
            isDisplay =  IsDisplay.Value,
            description = ItemDescription
        };
    }
}

[System.Serializable]
public class RuntimeItemDataPlain
{
    public string itemId;
    public int currentPrice;
    public int stock;
    public int maxStock;         // ★ 追加：上限を永続化
    public int displayStock;     // 互換のため残す（BlackSmithは未使用）
    public float demand;
    public bool isPopular;
    public ItemTypeData.ItemType itemType;
    public ItemTypeData.ItemAttribute itemAttribute;
    public int requiredLevel;    // ★ 追加：必要Lvを永続化
    public bool isDisplay;
    public string description;
}

public class ItemTypeData
{
    public enum ItemType
    {
        Weapon,
        Armor,
        Tool
    }
    
    public enum ItemAttribute
    {
        Fire,
        Water,
        Earth,
        Wind,
        Light,
        Dark,
    }
}
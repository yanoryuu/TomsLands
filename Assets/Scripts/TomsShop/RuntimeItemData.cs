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
    public RuntimeItemData(string itemId, int currentPrice,int maxstock, int stock,int displayStock, Sprite icon, ItemTypeData.ItemType itemType,ItemTypeData.ItemAttribute itemAttribute,int requiredLevel, float demand = 0.5f ,string description = "")
    {
        ItemId = itemId;
        CurrentPrice = new ReactiveProperty<int>(currentPrice);
        Stock = new ReactiveProperty<int>(stock);
        DisplayStock = new ReactiveProperty<int>(displayStock);
        MaxStock = new ReactiveProperty<int>(maxstock);
        ItemIcon = icon;
        Demand = new ReactiveProperty<float>(demand);
        IsPopular = new ReactiveProperty<bool>(demand >= 0.8f);
        ItemType = itemType;
        ItemAttribute = itemAttribute; 
        RequiredLevel = new ReactiveProperty<int>(requiredLevel);
        IsDisplay = new ReactiveProperty<bool>(false);
        ItemDescription = description;
    }

    public RuntimeItemData(RuntimeItemDataPlain plainData, Sprite icon)
    {
        ItemId = plainData.itemId;
        CurrentPrice = new ReactiveProperty<int>(plainData.currentPrice);
        Stock = new ReactiveProperty<int>(plainData.stock);
        DisplayStock = new ReactiveProperty<int>(plainData.displayStock);
        Demand = new ReactiveProperty<float>(plainData.demand);
        IsPopular = new ReactiveProperty<bool>(plainData.demand >= 0.8f);
        ItemIcon = icon;
        ItemAttribute = plainData.itemAttribute;
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
        Stock.Value = stock;
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
    public int displayStock;
    public float demand;
    public bool isPopular;
    public ItemTypeData.ItemType itemType;
    public ItemTypeData.ItemAttribute itemAttribute;
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
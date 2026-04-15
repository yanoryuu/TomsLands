using R3;
using UnityEngine;

[System.Serializable]
public class RuntimeItemData
{
    public string ItemId { get; private set; }
    
    public string ItemName { get; private set; }
    
    public ReactiveProperty<int> CurrentPrice { get; private set; }
    public ReactiveProperty<int> Stock { get; private set; }
    
    public ReactiveProperty<int> DisplayStock { get; private set; }
    public ReactiveProperty<float> Demand { get; private set; }
    public ReactiveProperty<bool> IsPopular { get; private set; }
    public ReactiveProperty<int> MaxStock { get;private set; }
    
    public ReactiveProperty<bool> IsDisplay {get; private set;}
    public ReactiveProperty<int> RequiredLevel { get; set; }
    public Sprite ItemIcon { get; private set; }
    public Sprite ItemBackground { get; private set; }
    
    public ItemTypeData.ItemType ItemType { get; private set; }
    public ItemTypeData.ItemAttribute ItemAttribute { get; private set; }
    
    public string ItemDescription { get; private set; }
    
    /// <summary>
    /// アイテム固有の売れやすさ倍率。1.0が標準。高いほど1ターンで多く売れる。
    /// </summary>
    public float SalesRate { get; private set; }

    /// <summary>
    /// 前ターンの通常営業で売れたかどうか（S3 品出し販売結果フィードバック用）。
    /// シリアライズ不要（毎ターン SimulateShopSales で更新される）。
    /// </summary>
    public bool WasSoldLastTurn { get; set; }

    /// <summary>
    /// 前ターンの需要値（需要変動幅の表示用）。
    /// ApplyShopTurnEconomy の冒頭でスナップショットされる。
    /// </summary>
    public float PreviousDemand { get; set; }

    /// <summary>
    /// 前ターンの価格（価格変動幅の表示用）。
    /// ApplyShopTurnEconomy の冒頭でスナップショットされる。
    /// </summary>
    public int PreviousPrice { get; set; }

    public RuntimeItemData(
        string itemId,
        string itemName,
        int currentPrice,
        int maxStock,
        int stock,
        int displayStock,
        Sprite icon,
        Sprite backgroundSprite,
        ItemTypeData.ItemType itemType,
        ItemTypeData.ItemAttribute itemAttribute,
        int requiredLevel,
        float demand = 0.5f,
        string description = "",
        float salesRate = 1.0f)
    {
        ItemId = itemId;
        ItemName = itemName;
        CurrentPrice = new ReactiveProperty<int>(currentPrice);
        Stock = new ReactiveProperty<int>(stock);
        DisplayStock = new ReactiveProperty<int>(displayStock);
        MaxStock = new ReactiveProperty<int>(maxStock);
        ItemIcon = icon;
        ItemBackground = backgroundSprite;
        Demand = new ReactiveProperty<float>(demand);
        IsPopular = new ReactiveProperty<bool>(demand >= 0.8f);
        ItemType = itemType;
        ItemAttribute = itemAttribute;
        RequiredLevel = new ReactiveProperty<int>(requiredLevel);
        IsDisplay = new ReactiveProperty<bool>(false);
        ItemDescription = description;
        SalesRate = salesRate;
        PreviousDemand = demand;
        PreviousPrice = currentPrice;
    }

    // 保存→復元CTor（Plain→Runtime）
    public RuntimeItemData(RuntimeItemDataPlain plainData, Sprite icon, Sprite backgroundSprite = null)
    {
        ItemId = plainData.itemId;
        ItemName = plainData.itemName;
        CurrentPrice = new ReactiveProperty<int>(plainData.currentPrice);
        MaxStock     = new ReactiveProperty<int>(Mathf.Max(0, plainData.maxStock));
        Stock        = new ReactiveProperty<int>(Mathf.Clamp(plainData.stock, 0, MaxStock.Value));
        DisplayStock = new ReactiveProperty<int>(Mathf.Max(0, plainData.displayStock)); // 互換
        Demand       = new ReactiveProperty<float>(Mathf.Clamp01(plainData.demand));
        IsPopular    = new ReactiveProperty<bool>(plainData.isPopular || Demand.Value >= 0.8f);
        ItemIcon       = icon;
        ItemBackground = backgroundSprite;
        ItemType       = plainData.itemType;
        ItemAttribute  = plainData.itemAttribute;
        RequiredLevel  = new ReactiveProperty<int>(Mathf.Max(0, plainData.requiredLevel));
        IsDisplay      = new ReactiveProperty<bool>(plainData.isDisplay);
        ItemDescription = plainData.description;
        SalesRate = plainData.salesRate;
        // 古いセーブデータ互換: previousDemand が 0 以下なら現在値をコピー
        PreviousDemand = plainData.previousDemand > 0f ? plainData.previousDemand : Demand.Value;
        PreviousPrice = plainData.previousPrice > 0 ? plainData.previousPrice : CurrentPrice.Value;
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
            itemName = ItemName,
            currentPrice = CurrentPrice.Value,
            maxStock = MaxStock.Value,
            stock = Stock.Value,
            demand = Demand.Value,
            isPopular = IsPopular.Value,
            itemType = ItemType,
            itemAttribute = ItemAttribute,
            displayStock = DisplayStock.Value,
            isDisplay =  IsDisplay.Value,
            requiredLevel = RequiredLevel.Value,
            description = ItemDescription,
            salesRate = SalesRate,
            previousDemand = PreviousDemand,
            previousPrice = PreviousPrice
        };
    }
}

[System.Serializable]
public class RuntimeItemDataPlain
{
    public string itemId;
    public string itemName;
    public int currentPrice;
    public int stock;
    public int maxStock;         
    public int displayStock;     
    public float demand;
    public bool isPopular;
    public ItemTypeData.ItemType itemType;
    public ItemTypeData.ItemAttribute itemAttribute;
    public int requiredLevel;
    public bool isDisplay;
    public string description;
    public float salesRate;
    public float previousDemand;
    public int previousPrice;
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
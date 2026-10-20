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
    /// 在庫1個あたりの毎ターン配当（配当付き武器）。マスターデータ由来でセーブ対象外。
    /// </summary>
    public int DividendPerTurn { get; private set; }

    /// <summary>
    /// 期待収益（需要 × 価格 × SalesRate）。おすすめ計算の単一の基礎値。
    /// 仕入れ一覧の並べ替え・ダッシュボードはすべてこの値を基準にする。
    /// </summary>
    public float ExpectedRevenue => Demand.Value * CurrentPrice.Value * SalesRate;

    /// <summary>
    /// 前ターンの通常営業で売れたかどうか（S3 品出し販売結果フィードバック用）。
    /// シリアライズ不要（毎ターン SimulateShopSales で更新される）。
    /// </summary>
    public bool WasSoldLastTurn { get; set; }

    /// <summary>
    /// 直近の営業で店で売れた個数（売り圧力。翌朝の ApplyShopTurnEconomy が値段を押し下げて 0 に戻す）。
    /// 営業から日送りの間に終了しても圧力が消えないようセーブする。
    /// </summary>
    public int LastShopSoldQuantity { get; set; }

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

    /// <summary>
    /// 流行度（−1.0〜+1.0）。需要が自然と向かう均衡値を決める。
    /// Trend=0 → naturalDemand=0.5 で中性。毎ターンランダムウォーク＋0への減衰。
    /// </summary>
    public float Trend { get; set; }

    /// <summary>
    /// バトル中の価格履歴（ターンごとの価格スナップショット）。非永続・セーブ対象外。
    /// インデックス 0 がバトル開始時の価格、以降は各ターン終了後の価格。
    /// </summary>
    public System.Collections.Generic.List<int> BattlePriceHistory { get; private set; }
        = new System.Collections.Generic.List<int>();

    /// <summary>現在の価格を BattlePriceHistory に記録する。</summary>
    public void RecordBattlePrice()
    {
        BattlePriceHistory.Add(CurrentPrice.Value);
    }

    /// <summary>バトル開始時に価格履歴をリセットする。</summary>
    public void ClearBattlePriceHistory()
    {
        BattlePriceHistory.Clear();
    }

    /// <summary>ショップ価格チャート用の履歴保持ターン数の上限。</summary>
    public const int ShopHistoryCapacity = 12;

    /// <summary>
    /// ショップのターンごとの価格履歴（折れ線チャート用）。永続・セーブ対象。
    /// 末尾が最新ターン。上限 <see cref="ShopHistoryCapacity"/> を超えた分は先頭から破棄。
    /// </summary>
    public System.Collections.Generic.List<int> ShopPriceHistory { get; private set; }
        = new System.Collections.Generic.List<int>();

    /// <summary>
    /// ショップのターンごとの需要履歴（折れ線チャート用）。永続・セーブ対象。
    /// 末尾が最新ターン。上限 <see cref="ShopHistoryCapacity"/> を超えた分は先頭から破棄。
    /// </summary>
    public System.Collections.Generic.List<float> ShopDemandHistory { get; private set; }
        = new System.Collections.Generic.List<float>();

    /// <summary>
    /// 現在の価格・需要をショップ履歴へ記録する（ターン経済更新の確定後に呼ぶ）。
    /// 上限を超えたら先頭（最古）から破棄するリングバッファ運用。
    /// </summary>
    public void RecordShopHistory()
    {
        ShopPriceHistory.Add(CurrentPrice.Value);
        ShopDemandHistory.Add(Demand.Value);
        TrimShopHistory();
    }

    private void TrimShopHistory()
    {
        while (ShopPriceHistory.Count > ShopHistoryCapacity)
            ShopPriceHistory.RemoveAt(0);
        while (ShopDemandHistory.Count > ShopHistoryCapacity)
            ShopDemandHistory.RemoveAt(0);
    }

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
        float salesRate = 1.0f,
        int dividendPerTurn = 0,
        float? initialTrend = null)
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
        DividendPerTurn = dividendPerTurn;
        PreviousDemand = demand;
        PreviousPrice = currentPrice;

        // 流行度は通常ランダムに散らす。
        // initialTrend を渡した場合は UnityEngine.Random を触らないので、
        // メインスレッド外（キャリブレーションのバックグラウンド探索など）からも生成できる。
        Trend = initialTrend ?? UnityEngine.Random.Range(-0.5f, 0.5f);
    }

    // 保存→復元CTor（Plain→Runtime）
    // dividendPerTurn はマスターデータが真実の源のためセーブせず、復元時に外から渡す
    public RuntimeItemData(RuntimeItemDataPlain plainData, Sprite icon, Sprite backgroundSprite = null, int dividendPerTurn = 0)
    {
        DividendPerTurn = dividendPerTurn;
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
        ItemAttribute  = ItemTypeData.NormalizeAttribute(plainData.itemAttribute); // 旧 Wind(3) 互換
        RequiredLevel  = new ReactiveProperty<int>(Mathf.Max(0, plainData.requiredLevel));
        IsDisplay      = new ReactiveProperty<bool>(plainData.isDisplay);
        ItemDescription = plainData.description;
        SalesRate = plainData.salesRate;
        // 古いセーブデータ互換: previousDemand が 0 以下なら現在値をコピー
        PreviousDemand = plainData.previousDemand > 0f ? plainData.previousDemand : Demand.Value;
        PreviousPrice = plainData.previousPrice > 0 ? plainData.previousPrice : CurrentPrice.Value;
        Trend = plainData.trend;
        LastShopSoldQuantity = Mathf.Max(0, plainData.lastShopSold);

        // ショップ価格・需要履歴の復元。旧セーブ（履歴なし）は現在値1点でシードする。
        ShopPriceHistory = (plainData.shopPriceHistory != null && plainData.shopPriceHistory.Count > 0)
            ? new System.Collections.Generic.List<int>(plainData.shopPriceHistory)
            : new System.Collections.Generic.List<int> { CurrentPrice.Value };
        ShopDemandHistory = (plainData.shopDemandHistory != null && plainData.shopDemandHistory.Count > 0)
            ? new System.Collections.Generic.List<float>(plainData.shopDemandHistory)
            : new System.Collections.Generic.List<float> { Demand.Value };
        TrimShopHistory();
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
        // 在庫が減った場合、品出し数が在庫を超えないようにクランプ
        if (DisplayStock.Value > Stock.Value)
        {
            DisplayStock.Value = Stock.Value;
        }
    }

    public void UpdateDisplayStock(int stock)
    {
        DisplayStock.Value = stock;
    }

    public void UpdateDemand(float demand)
    {
        Demand.Value = Mathf.Clamp(demand, 0f, 1f);
        UpdatePopularity();
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
            previousPrice = PreviousPrice,
            trend = Trend,
            lastShopSold = LastShopSoldQuantity,
            shopPriceHistory = new System.Collections.Generic.List<int>(ShopPriceHistory),
            shopDemandHistory = new System.Collections.Generic.List<float>(ShopDemandHistory)
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
    public float trend;
    public int lastShopSold;   // 売り圧力（旧セーブは欠損時 0 = 圧力なし）
    public System.Collections.Generic.List<int> shopPriceHistory;
    public System.Collections.Generic.List<float> shopDemandHistory;
}

public class ItemTypeData
{
    public enum ItemType
    {
        Weapon,
        Armor,
        Tool
    }
    
    /// <summary>
    /// アイテム属性。敵側の ElementType（水・火・木・光・闇）と同じ5属性。
    /// 相性: 水>火、火>木、木>水、光⇄闇（ElementAttributeMapper 参照）。
    /// int 値は SO・セーブ・Remote Config の dungeons.requiredAttribute で int のまま保存されるため明示する。
    /// 旧体系の Earth(2) の値は Wood が引き継ぐ（旧ダンジョン弱点「土」= 氷霧の洞窟は新体系の木と一致するため）。
    /// 旧 Wind(3) は欠番。読み込み時は NormalizeAttribute / TryParseAttribute で Wood に寄せる。
    /// </summary>
    public enum ItemAttribute
    {
        Fire = 0,
        Water = 1,
        Wood = 2,
        // 3 は旧 Wind（欠番。読み込み時に Wood へ変換）
        Light = 4,
        Dark = 5,
    }

    /// <summary>旧 Wind(3) など未定義の int 値を新体系へ寄せる。</summary>
    public static ItemAttribute NormalizeAttribute(ItemAttribute attr)
    {
        if (System.Enum.IsDefined(typeof(ItemAttribute), attr)) return attr;
        return (int)attr == 3 ? ItemAttribute.Wood : ItemAttribute.Fire;
    }

    /// <summary>
    /// 文字列（Remote Config / 新聞 CSV など）から属性を読む。大文字小文字は無視。
    /// 旧名の互換: "Earth"(土) → Dark、"Wind"(風) → Wood（旧土アイテムは闇、旧風アイテムは木に移行したため）。
    /// 数値文字列は定義済みの値のみ受け付ける（"3" は旧 Wind として Wood）。
    /// </summary>
    public static bool TryParseAttribute(string value, out ItemAttribute attr)
    {
        attr = ItemAttribute.Fire;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var s = value.Trim();
        if (string.Equals(s, "Earth", System.StringComparison.OrdinalIgnoreCase)) { attr = ItemAttribute.Dark; return true; }
        if (string.Equals(s, "Wind", System.StringComparison.OrdinalIgnoreCase)) { attr = ItemAttribute.Wood; return true; }
        if (int.TryParse(s, out var n))
        {
            if (n == 3) { attr = ItemAttribute.Wood; return true; }
            if (!System.Enum.IsDefined(typeof(ItemAttribute), n)) return false;
            attr = (ItemAttribute)n;
            return true;
        }
        return System.Enum.TryParse(s, true, out attr) && System.Enum.IsDefined(typeof(ItemAttribute), attr);
    }

    /// <summary>アイテムの属性が文字列指定（旧名可）と一致するか。新聞の対象判定用。</summary>
    public static bool AttributeMatches(ItemAttribute itemAttr, string target)
        => TryParseAttribute(target, out var t) && NormalizeAttribute(itemAttr) == t;

    /// <summary>表示用の日本語ラベル（火・水・木・光・闇）。</summary>
    public static string AttributeLabel(ItemAttribute attr) => NormalizeAttribute(attr) switch
    {
        ItemAttribute.Fire  => "火",
        ItemAttribute.Water => "水",
        ItemAttribute.Wood  => "木",
        ItemAttribute.Light => "光",
        ItemAttribute.Dark  => "闇",
        _ => "？"
    };
}
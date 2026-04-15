using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class ItemModel
{
    public readonly List<ItemData> masterItems;
    private readonly ItemVisualSettings visualSettings;

    public List<RuntimeItemData> RuntimeItems { get; private set; } = new();

    public ItemModel(List<ItemData> masterItems, ItemVisualSettings visualSettings = null)
    {
        this.masterItems = masterItems;
        this.visualSettings = visualSettings;
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

    // ========================================
    // 通常営業時の販売シミュレーション
    // ========================================

    /// <summary>
    /// 通常営業ターン終了時の販売シミュレーション。
    /// 品出し中の全アイテムについて Demand × SalesRate × DisplayStock で販売数を算出し、
    /// Stock を減少させる。
    /// </summary>
    /// <returns>itemId → soldCount の辞書</returns>
    public Dictionary<string, int> SimulateShopSales()
    {
        var salesResult = new Dictionary<string, int>();

        foreach (var runtime in RuntimeItems)
        {
            // 品出し中かつ在庫ありのアイテムのみ対象
            if (!runtime.IsDisplay.Value || runtime.DisplayStock.Value <= 0 || runtime.Stock.Value <= 0)
            {
                runtime.WasSoldLastTurn = false;
                continue;
            }

            float demand = Mathf.Clamp01(runtime.Demand.Value);
            int displayStock = runtime.DisplayStock.Value;
            float salesRate = runtime.SalesRate;

            // 売れる上限 = 在庫と品出し数の小さい方
            int maxSellable = Mathf.Min(runtime.Stock.Value, displayStock);

            // 販売数を算出（Demand × SalesRate × DisplayStock）
            float rawSold = demand * salesRate * displayStock;
            int quantitySold;

            if (rawSold >= 1f)
            {
                quantitySold = Mathf.FloorToInt(rawSold);
            }
            else if (rawSold > 0f)
            {
                // 端数は確率的に1個売れるかどうかを判定
                quantitySold = Random.value < rawSold ? 1 : 0;
            }
            else
            {
                quantitySold = 0;
            }

            quantitySold = Mathf.Clamp(quantitySold, 0, maxSellable);

            if (quantitySold <= 0)
            {
                runtime.WasSoldLastTurn = false;
                continue;
            }

            runtime.Stock.Value -= quantitySold;
            runtime.WasSoldLastTurn = true;
            salesResult[runtime.ItemId] = quantitySold;

            Debug.Log($"[ShopSales] {runtime.ItemId} × {quantitySold}個 " +
                      $"(Demand={demand:F2}, SalesRate={salesRate:F2}, DisplayStock={displayStock})");
        }

        return salesResult;
    }

    // ========================================
    // 案D1: 戦闘結果の属性波及
    // ========================================

    /// <summary>
    /// 戦闘で使用した装備と同属性の全アイテムに需要を波及させる。
    /// BattleResultHandler から戦闘終了後に呼ばれる。
    /// </summary>
    public void ApplyBattleAttributeSpread(BattleResult result, List<string> usedItemIds, ShopEconomySettings settings, int blacksmithLevel)
    {
        if (settings == null || usedItemIds == null) return;

        // 使用装備の属性を収集
        var usedAttributes = new HashSet<ItemTypeData.ItemAttribute>();
        foreach (var id in usedItemIds)
        {
            var runtime = GetRuntimeItem(id);
            if (runtime != null)
            {
                usedAttributes.Add(runtime.ItemAttribute);
            }
        }

        if (usedAttributes.Count == 0) return;

        // 同属性の全アイテム（鍛冶屋レベル以内）に需要を波及
        float delta = result == BattleResult.Victory
            ? settings.victoryAttributeDemandUp
            : -settings.defeatAttributeDemandDown;

        foreach (var runtime in RuntimeItems)
        {
            // 鍛冶屋レベルで未表示のアイテムは除外
            if (runtime.RequiredLevel.Value > blacksmithLevel) continue;
            // 使用装備自体は既に ApplyBattleResult で処理済みなのでスキップ
            if (usedItemIds.Contains(runtime.ItemId)) continue;

            if (usedAttributes.Contains(runtime.ItemAttribute))
            {
                runtime.Demand.Value = Mathf.Clamp(runtime.Demand.Value + delta, settings.demandFloor, settings.demandCeiling);
                runtime.UpdatePopularity();
                Debug.Log($"[D1] {runtime.ItemId}（{runtime.ItemAttribute}属性）需要波及: {delta:+0.00;-0.00} → {runtime.Demand.Value:F2}");
            }
        }
    }

    // ========================================
    // ターン毎の経済更新（S1 + S3 + D2）
    // ========================================

    /// <summary>
    /// TomsShop のターン切り替え時に呼ばれる経済更新。
    /// S1: 需要連動型じわじわ価格変動
    /// S3: 品出し販売結果フィードバック
    /// D2: 品出し陳列効果（需要変動）
    /// </summary>
    public void ApplyShopTurnEconomy(ShopEconomySettings settings, int blacksmithLevel)
    {
        if (settings == null) return;

        foreach (var runtime in RuntimeItems)
        {
            var master = GetMasterItem(runtime.ItemId);
            if (master == null) continue;

            // 鍛冶屋レベルで未表示のアイテムは価格も需要も変動しない
            if (runtime.RequiredLevel.Value > blacksmithLevel) continue;

            // ------------------------------------------------
            // 需要・価格のスナップショット保存（ポップアップ表示用）
            // ------------------------------------------------
            runtime.PreviousDemand = runtime.Demand.Value;
            runtime.PreviousPrice = runtime.CurrentPrice.Value;

            // ------------------------------------------------
            // 案D2: 品出し陳列効果（需要変動）
            // ------------------------------------------------
            if (runtime.IsDisplay.Value && runtime.DisplayStock.Value > 0)
            {
                // 品出し中 → 需要微増
                runtime.Demand.Value = Mathf.Clamp(
                    runtime.Demand.Value + settings.displayDemandUp,
                    settings.demandFloor, settings.demandCeiling);
            }
            else
            {
                // 品出ししていない → 需要微減
                runtime.Demand.Value = Mathf.Clamp(
                    runtime.Demand.Value - settings.notDisplayDemandDown,
                    settings.demandFloor, settings.demandCeiling);
            }

            // ------------------------------------------------
            // 案S1: 需要連動型じわじわ価格変動
            // ------------------------------------------------
            float s1Rate;
            if (runtime.Demand.Value >= settings.highDemandThreshold)
            {
                s1Rate = Random.Range(settings.highDemandPriceRateMin, settings.highDemandPriceRateMax);
            }
            else if (runtime.Demand.Value <= settings.lowDemandThreshold)
            {
                s1Rate = Random.Range(settings.lowDemandPriceRateMin, settings.lowDemandPriceRateMax);
            }
            else
            {
                s1Rate = Random.Range(settings.normalDemandPriceRateMin, settings.normalDemandPriceRateMax);
            }

            // ------------------------------------------------
            // 案S3: 品出し販売結果フィードバック
            // ------------------------------------------------
            float s3Rate = 1.0f;
            if (runtime.IsDisplay.Value && runtime.DisplayStock.Value > 0)
            {
                // 品出し中のアイテム：前ターンで売れたかどうかで判定
                if (runtime.WasSoldLastTurn)
                {
                    // 売れた → 値上がり
                    s3Rate = Random.Range(settings.soldPriceRateMin, settings.soldPriceRateMax);
                    Debug.Log($"[S3] {runtime.ItemId} 品出し中＆売れた → 価格 {s3Rate:P0}");
                }
                else
                {
                    // 品出しているが売れていない → 値下がり
                    s3Rate = Random.Range(settings.unsoldPriceRateMin, settings.unsoldPriceRateMax);
                    Debug.Log($"[S3] {runtime.ItemId} 品出し中＆売れず → 価格 {s3Rate:P0}");
                }
            }

            // S1 と S3 を合成して価格を更新
            float combinedRate = s1Rate * s3Rate;
            int newPrice = Mathf.Max(1, Mathf.RoundToInt(runtime.CurrentPrice.Value * combinedRate));

            // ストップ高/ストップ安（元値ベース）
            int floor = Mathf.Max(1, Mathf.RoundToInt(master.basePrice * settings.shopPriceFloorRate));
            int ceiling = Mathf.RoundToInt(master.basePrice * settings.shopPriceCeilingRate);
            newPrice = Mathf.Clamp(newPrice, floor, ceiling);

            runtime.CurrentPrice.Value = newPrice;
            runtime.UpdatePopularity();

            Debug.Log($"[ShopEconomy] {runtime.ItemId}: S1={s1Rate:F3} S3={s3Rate:F3} → price={newPrice} demand={runtime.Demand.Value:F2}");
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
            .Select(item =>
            {
                // 古いセーブデータとの互換: maxStock/requiredLevel/itemName がデフォルト値ならマスターから復元
                var master = GetMasterItem(item.itemId);
                if (master != null)
                {
                    if (item.maxStock <= 0) item.maxStock = master.maxStock;
                    if (item.requiredLevel <= 0) item.requiredLevel = master.requiredLevel;
                    if (string.IsNullOrEmpty(item.itemName)) item.itemName = master.itemName;
                }
                return new RuntimeItemData(item, SearchSpriteFromMaster(item.itemId), SearchBackgroundSpriteFromMaster(item.requiredLevel));
            })
            .ToList();

        Debug.Log("Item data loaded.");
    }

    //初期データ
    public void InitializeRuntimeItemsFromMaster()
    {
        RuntimeItems = masterItems
            .Select(master => new RuntimeItemData(
                master.itemId,
                master.itemName,
                master.basePrice,
                master.maxStock,
                master.initialStock,
                master.initialDisplayStock,
                master.itemIcon,
                SearchBackgroundSpriteFromMaster(master.requiredLevel),
                master.itemType,
                master.itemAttribute,
                master.requiredLevel,
                Random.Range(0.3f, 0.7f),
                master.description,
                master.salesRate
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

    //レベルに応じた背景スプライトを取得
    private Sprite SearchBackgroundSpriteFromMaster(int requiredLevel)
    {
        if (visualSettings == null) return null;
        return visualSettings.GetBackground(requiredLevel);
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
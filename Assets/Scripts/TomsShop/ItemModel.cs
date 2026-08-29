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

    // ========================================
    // おすすめ計算（単一スコアの真実の源）
    // ========================================
    // 仕入れ一覧・自動陳列・自動仕入れ・Prophet のすべてが
    // この2つを基準にする（画面ごとに式がバラつかないようにする）。

    /// <summary>おすすめスコアの Trend 重み。0 で Trend 無視。</summary>
    private const float RecommendTrendWeight = 0.5f;
    /// <summary>次ダンジョンの弱点属性に一致するアイテムへの倍率（&gt;1 で優遇）。</summary>
    private const float RecommendAttributeBonus = 1.5f;

    /// <summary>
    /// 期待収益（需要 × 価格 × SalesRate）。陳列・収益順の基礎値。
    /// </summary>
    public static float ExpectedRevenueOf(RuntimeItemData r) => r.ExpectedRevenue;

    /// <summary>
    /// 前向きおすすめスコア。期待収益に Trend と次ダンジョン属性ボーナスを乗せたもの。
    /// 自動仕入れ・Prophet のおすすめに使う。<paramref name="nextDungeonAttr"/> が null なら属性ボーナスなし。
    /// </summary>
    public float GetRecommendScore(RuntimeItemData r, ItemTypeData.ItemAttribute? nextDungeonAttr)
    {
        float score = r.ExpectedRevenue * (1f + RecommendTrendWeight * r.Trend);
        if (nextDungeonAttr.HasValue && r.ItemAttribute == nextDungeonAttr.Value)
            score *= RecommendAttributeBonus;
        return score;
    }

    public void PurchaseItem(string itemId, int quantity)
    {
        var item = GetRuntimeItem(itemId);
        if (item != null) item.UpdateStock(item.Stock.Value + quantity);
    }

    public void SellItem(string itemId, int quantity)
    {
        var item = GetRuntimeItem(itemId);
        if (item != null && item.Stock.Value >= quantity)
            item.Stock.Value -= quantity;
    }
    
    /// <summary>
    /// 在庫を確定消費する（旧名: Settlement。売り注文の「約定」と紛らわしいためリネーム）。
    /// 在庫が尽きたら陳列状態も解除する。
    /// </summary>
    public void ConsumeStock(string itemId, int quantity)
    {
        var item = GetRuntimeItem(itemId);
        if (item != null && item.Stock.Value >= quantity)
        {
            item.Stock.Value -= quantity;
            if (item.Stock.Value <= 0)
            {
                item.IsDisplay.Value = false;
                item.DisplayStock.Value = 0;
            }
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
    /// 通常営業ターン終了時の販売処理。
    /// 【現行仕様（売り注文制）】品出し中のアイテムは陳列した数だけ必ず全部売れる
    /// （販売数 = min(Stock, DisplayStock)）。入金は即時ではなく、呼び出し側が
    /// 売り注文(SellOrder)として翌日の約定に回す。
    /// 【旧仕様】probabilistic=true で Demand × SalesRate × DisplayStock の確率販売に戻せる
    /// （ShopEconomySettings.useProbabilisticShopSales）。
    /// いずれの場合も Stock はここで減少する（売り注文の在庫引き当てを兼ねる）。
    /// </summary>
    /// <returns>itemId → soldCount の辞書</returns>
    public Dictionary<string, int> SimulateShopSales(bool probabilistic = false)
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

            int quantitySold;
            if (!probabilistic)
            {
                // 売り注文制: 陳列した分は必ず全部売れる
                quantitySold = maxSellable;
            }
            else
            {
                // 旧仕様: 販売数を算出（Demand × SalesRate × DisplayStock）
                float rawSold = demand * salesRate * displayStock;
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
            }

            quantitySold = Mathf.Clamp(quantitySold, 0, maxSellable);

            if (quantitySold <= 0)
            {
                runtime.WasSoldLastTurn = false;
                continue;
            }

            runtime.Stock.Value -= quantitySold;
            // 在庫が減ったら品出し数もクランプ
            if (runtime.DisplayStock.Value > runtime.Stock.Value)
            {
                runtime.DisplayStock.Value = runtime.Stock.Value;
            }
            // 売り切れたら陳列状態も解除する（ConsumeStock と同じ扱い）
            if (runtime.Stock.Value <= 0)
            {
                runtime.IsDisplay.Value = false;
                runtime.DisplayStock.Value = 0;
            }
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
    /// D2: 品出し陳列効果（需要変動）
    /// A1〜A5: 広告ステータス（Trust/Attention/Spread/Retention/Followers）連動
    /// status が null または各係数が 0 のとき、従来挙動と完全一致する。
    /// </summary>
    public void ApplyShopTurnEconomy(ShopEconomySettings settings, int blacksmithLevel, ShopStatusModel status = null)
    {
        if (settings == null) return;

        // ------------------------------------------------
        // Step 0: 広告ステータスの正規化（status==null なら全部 0 → 中性化）
        // ------------------------------------------------
        float statMax    = (status != null) ? Mathf.Max(1f, status.StatMax) : 100f;
        float trustN     = (status != null) ? Mathf.Clamp01(status.Trust.Value     / statMax) : 0f;
        float attentionN = (status != null) ? Mathf.Clamp01(status.Attention.Value / statMax) : 0f;
        float spreadN    = (status != null) ? Mathf.Clamp01(status.Spread.Value    / statMax) : 0f;
        float retentionN = (status != null) ? Mathf.Clamp01(status.Retention.Value / statMax) : 0f;
        int   followers  = (status != null) ? Mathf.Max(0, status.Followers.Value) : 0;

        // ------------------------------------------------
        // Step 1: 案A5 Followers 補正（全アイテム共通の事前計算）
        //   demandBias = followerWeight * Log10(1 + followers / followerScale)
        //   followers=0 → Log10(1)=0 で完全に無効化
        // ------------------------------------------------
        float safeScale = Mathf.Max(1f, settings.followerScale);
        float demandBias = settings.followerWeight * Mathf.Log10(1f + followers / safeScale);

        // ------------------------------------------------
        // Step 2: 案A3 Spread 増幅係数（D2 の Δdemand に乗算）
        // ------------------------------------------------
        float spreadFactor = 1f + settings.spreadDemandAmplify * spreadN;

        // ------------------------------------------------
        // Step 3: 案A4 Retention 安定化強度（S1 を 1.0 へ Lerp する t）
        // ------------------------------------------------
        float retentionStability = settings.retentionStabilizer * retentionN;

        // ------------------------------------------------
        // Step 4: 案A2 Attention 増幅係数（S1 の max 端を引き上げる倍率）
        // ------------------------------------------------
        float attentionFactor = 1f + settings.attentionPriceAmplify * attentionN;

        // ------------------------------------------------
        // Step 5: 案A1 Trust による Floor 倍率の底上げ
        //   floorRate = shopPriceFloorRate + trustFloorBoost * (Trust/100)
        //   安全のため Ceiling を超えないように Min クランプ
        // ------------------------------------------------
        float floorRate = Mathf.Min(
            settings.shopPriceFloorRate + settings.trustFloorBoost * trustN,
            settings.shopPriceCeilingRate);

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
            // 流行度（Trend）の更新: ランダムウォーク + 0 への減衰
            // ------------------------------------------------
            float drift = Random.Range(-settings.trendDriftMax, settings.trendDriftMax);
            float trendDecay = -runtime.Trend * settings.trendDecayRate;
            runtime.Trend = Mathf.Clamp(runtime.Trend + drift + trendDecay, -1f, 1f);

            // ------------------------------------------------
            // 案D2 改: 均衡値収束(β Trend) + 品出し効果 + Spread 増幅
            //   naturalDemand: Trend が引き寄せる均衡需要 (0.5 ± amplitude)
            //   convergenceDelta: 均衡値への接近分
            //   displayDelta: 品出し陳列効果（Spread 増幅付き）
            //   Followers の demandBias は「動的下限の底上げ」モデルで適用する。
            // ------------------------------------------------
            bool displaying = runtime.IsDisplay.Value && runtime.DisplayStock.Value > 0;

            float naturalDemand = Mathf.Clamp01(0.5f + runtime.Trend * settings.trendAmplitude);
            float convergenceDelta = (naturalDemand - runtime.Demand.Value) * settings.trendConvergenceRate;
            float displayDelta = displaying
                ? settings.displayDemandUp * spreadFactor
                : -settings.notDisplayDemandDown * spreadFactor;

            float dynamicDemandFloor = Mathf.Min(
                settings.demandFloor + demandBias,
                settings.demandCeiling);

            runtime.Demand.Value = Mathf.Clamp(
                runtime.Demand.Value + convergenceDelta + displayDelta,
                dynamicDemandFloor, settings.demandCeiling);

            // ------------------------------------------------
            // 案S1 改: 需要連動型じわじわ価格変動 + Attention 上振れ増幅
            //   閾値判定は従来通り。max 端のみ attentionFactor で乗算。
            //   low 需要レンジは attentionAffectsLowDemand フラグで切替可能。
            // ------------------------------------------------
            float s1Min, s1Max;
            if (runtime.Demand.Value >= settings.highDemandThreshold)
            {
                s1Min = settings.highDemandPriceRateMin;
                s1Max = settings.highDemandPriceRateMax * attentionFactor;
            }
            else if (runtime.Demand.Value <= settings.lowDemandThreshold)
            {
                s1Min = settings.lowDemandPriceRateMin;
                s1Max = settings.attentionAffectsLowDemand
                    ? settings.lowDemandPriceRateMax * attentionFactor
                    : settings.lowDemandPriceRateMax;
            }
            else
            {
                s1Min = settings.normalDemandPriceRateMin;
                s1Max = settings.normalDemandPriceRateMax * attentionFactor;
            }
            // 安全: Attention 増幅で min と max が逆転しないようガード
            if (s1Max < s1Min) s1Max = s1Min;
            float s1Rate = Random.Range(s1Min, s1Max);

            // ------------------------------------------------
            // 案A4: Retention 安定化 — S1 を 1.0 へ Lerp で寄せる
            //   retentionStability=0 → s1 そのまま（従来挙動）
            // ------------------------------------------------
            s1Rate = Mathf.Lerp(s1Rate, 1f, retentionStability);

            int newPrice = Mathf.Max(1, Mathf.RoundToInt(runtime.CurrentPrice.Value * s1Rate));

            // ストップ高/ストップ安（元値ベース、Trust で Floor を底上げ）
            int floor = Mathf.Max(1, Mathf.RoundToInt(master.basePrice * floorRate));
            int ceiling = Mathf.RoundToInt(master.basePrice * settings.shopPriceCeilingRate);
            newPrice = Mathf.Clamp(newPrice, floor, ceiling);

            runtime.CurrentPrice.Value = newPrice;
            runtime.UpdatePopularity();

            // 確定した価格・需要を価格チャート用の履歴へ記録
            runtime.RecordShopHistory();

            Debug.Log($"[ShopEconomy] {runtime.ItemId}: " +
                      $"trend={runtime.Trend:F2} natural={naturalDemand:F2} " +
                      $"S1={s1Rate:F3} (Att×{attentionFactor:F2}, Ret t={retentionStability:F2}) " +
                      $"→ price={newPrice} demand={runtime.Demand.Value:F2} " +
                      $"(floor={floor}, demandBias={demandBias:F3}, spread×{spreadFactor:F2})");
        }
    }

    // ========================================
    // ① オート購入
    // ========================================

    /// <summary>
    /// 予算内でおすすめスコア（GetRecommendScore）が高い順にアイテムを自動購入する。
    /// </summary>
    public List<AutoPurchaseResult> AutoPurchase(int budget, int blacksmithLevel, TomsModel tomsModel,
        ItemTypeData.ItemAttribute? nextDungeonAttr = null)
    {
        var results = new List<AutoPurchaseResult>();
        int remaining = Mathf.Min(budget, tomsModel.PlayerMoney.Value);

        var candidates = RuntimeItems
            .Where(r => r.RequiredLevel.Value <= blacksmithLevel
                     && r.RemainToMax() > 0
                     && r.CurrentPrice.Value > 0)
            .OrderByDescending(r => GetRecommendScore(r, nextDungeonAttr))
            .ToList();

        foreach (var item in candidates)
        {
            if (remaining <= 0) break;
            int canBuy = Mathf.Min(remaining / item.CurrentPrice.Value, item.RemainToMax());
            if (canBuy <= 0) continue;

            int cost = canBuy * item.CurrentPrice.Value;
            item.UpdateStock(item.Stock.Value + canBuy);
            tomsModel.PlayerMoney.Value -= cost;
            tomsModel.RecordProcurementSpend(cost);
            remaining -= cost;
            results.Add(new AutoPurchaseResult(item.ItemId, item.ItemName, canBuy, cost));
            Debug.Log($"[AutoPurchase] {item.ItemName} ×{canBuy} ({cost}G)");
        }

        if (results.Count > 0)
        {
            SaveData();
            tomsModel.SavePlayerMoney();
        }
        return results;
    }

    // ========================================
    // ③ おすすめ陳列
    // ========================================

    /// <summary>
    /// 期待収益（需要×価格×SalesRate）が高い順に最大maxSlots枠を自動陳列する。
    /// maxSlots / maxStockPerItem は店レベル（ShopLevelSettings）由来の値を渡すこと。
    /// </summary>
    public void AutoSetDisplay(int blacksmithLevel, int maxSlots, int maxStockPerItem = int.MaxValue)
    {
        foreach (var r in RuntimeItems)
            r.IsDisplay.Value = false;

        var top = RuntimeItems
            .Where(r => r.RequiredLevel.Value <= blacksmithLevel && r.Stock.Value > 0)
            .OrderByDescending(ExpectedRevenueOf)
            .Take(maxSlots);

        foreach (var item in top)
        {
            item.IsDisplay.Value = true;
            item.DisplayStock.Value = Mathf.Min(item.Stock.Value, maxStockPerItem);
            Debug.Log($"[AutoDisplay] {item.ItemName} 陳列設定 (score={ExpectedRevenueOf(item):F1})");
        }

        SaveData();
    }

    // ========================================
    // 陳列枠（店レベル）関連
    // ========================================

    /// <summary>現在陳列中の銘柄数。</summary>
    public int CountDisplayedKinds() => RuntimeItems.Count(r => r.IsDisplay.Value);

    /// <summary>あと1銘柄陳列できるか（maxKinds = 店レベル由来の同時陳列上限）。</summary>
    public bool CanDisplayMore(int maxKinds) => CountDisplayedKinds() < maxKinds;

    // ========================================
    // ⑤ ダッシュボード用: 期待収益順リスト取得
    // ========================================

    /// <summary>
    /// 全アイテムを期待収益（需要×価格×SalesRate）の降順で返す。
    /// </summary>
    public List<RuntimeItemData> GetItemsByExpectedRevenue(int blacksmithLevel)
    {
        return RuntimeItems
            .Where(r => r.RequiredLevel.Value <= blacksmithLevel)
            .OrderByDescending(ExpectedRevenueOf)
            .ToList();
    }

    //セーブ
    public void SaveData()
    {
        var dataList = new RuntimeItemDataList(
            RuntimeItems.Select(r => r.ToPlainData()).ToList()
        );
        string json = JsonUtility.ToJson(dataList, true);
        File.WriteAllText(SaveSlotManager.GetPath("itemData.json"), json);
        Debug.Log("Item data saved.");
    }

    //ここでロード
    public void LoadData()
    {
        string path = SaveSlotManager.GetPath("itemData.json");
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
                    item.maxStock = master.maxStock;
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
    public List<RuntimeItemData> PickItemRuntimeListForStock(List<RuntimeItemData> runtimeItems　,int minStock=0 ,int maxStock=int.MaxValue)
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

public class AutoPurchaseResult
{
    public string ItemId;
    public string ItemName;
    public int Quantity;
    public int TotalCost;

    public AutoPurchaseResult(string itemId, string itemName, int quantity, int totalCost)
    {
        ItemId = itemId;
        ItemName = itemName;
        Quantity = quantity;
        TotalCost = totalCost;
    }
}

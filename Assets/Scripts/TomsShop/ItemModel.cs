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
    // 仕入れ一覧・自動仕入れ・Prophet のすべてが
    // この1つを基準にする（画面ごとに式がバラつかないようにする）。

    /// <summary>
    /// 期待収益（需要 × 価格 × SalesRate）。陳列・収益順・おすすめのすべての基礎値。
    ///
    /// <b>これは「現在値」だけで決まる。未来の情報は一切含めない。</b>
    /// 以前は Trend（流行度＝需要が向かう均衡値）と次ダンジョンの弱点属性ボーナスを
    /// 乗せていたが、それらは「この先どう動くか」という未来の情報であり、
    /// 無料で開示すると「おすすめの上から買うだけ」でゲームが終わってしまう。
    /// 未来の手がかりはニュース（新聞）でのみ得られる。詳細は Docs/News_Spec.md §2。
    /// </summary>
    public static float ExpectedRevenueOf(RuntimeItemData r) => r.ExpectedRevenue;

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
            else if (item.DisplayStock.Value > item.Stock.Value)
            {
                // 配信で売れて在庫が減ったとき、陳列数が在庫を上回ったまま残らないようにする
                // （SimulateShopSales と同じクランプ。Jev AutoPlay の display_over_stock で検出）
                item.DisplayStock.Value = item.Stock.Value;
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
    // 価格変動エンジンは設定が変わるまで使い回す。
    // ABM はトレーダー群を生成時に確定させるため、毎ターン作り直すと相場の連続性が失われる。
    private IShopPriceEngine _priceEngine;
    private bool _priceEngineIsAbm;
    private int _priceEngineSeed;

    /// <summary>
    /// 次回のターン経済更新でエンジンを作り直させる。
    /// ABM のパラメータを実行中に変えた場合（デバッグメニュー等）に呼ぶ。
    /// </summary>
    public void InvalidatePriceEngine() => _priceEngine = null;

    /// <summary>
    /// 設定に応じた価格変動エンジンを返す。
    /// useAbmPriceEngine が false、またはプリセット未設定なら従来挙動（Legacy）。
    /// </summary>
    private IShopPriceEngine ResolvePriceEngine(ShopEconomySettings settings, int flowSeed)
    {
        bool wantAbm = settings.useAbmPriceEngine && settings.marketModelPreset != null;

        // シードの優先順: 設定で固定 > ランのシード（ラン再現と揃う） > 乱数
        int seed = settings.abmSeed != 0 ? settings.abmSeed
                 : flowSeed != 0 ? flowSeed
                 : Random.Range(int.MinValue, int.MaxValue);

        // ABM はシードが変わったら（別ランになったら）編成を作り直す
        bool sameSeed = !wantAbm || _priceEngineSeed == seed || (settings.abmSeed == 0 && flowSeed == 0);
        if (_priceEngine != null && _priceEngineIsAbm == wantAbm && sameSeed)
        {
            return _priceEngine;
        }

        if (wantAbm)
        {
            _priceEngine = new AbmShopPriceEngine(settings.marketModelPreset.abm, seed);
            _priceEngineSeed = seed;
            Debug.Log($"[ShopEconomy] 価格変動エンジン: ABM (seed={seed})");
        }
        else
        {
            _priceEngine = new LegacyShopPriceEngine();
            if (settings.useAbmPriceEngine)
            {
                Debug.LogWarning("[ShopEconomy] useAbmPriceEngine が true ですが marketModelPreset が未設定のため、" +
                                 "従来の価格変動（Legacy）で動作します。");
            }
        }

        _priceEngineIsAbm = wantAbm;
        return _priceEngine;
    }

    /// <param name="turnIndex">現在のターン番号。ABM がターン専用の乱数を作るのに使う（セーブ/ロード後も同じ系列になる）。</param>
    /// <param name="flowSeed">ランのシード。ABM のトレーダー編成に使う。0 なら設定または乱数にフォールバック。</param>
    public void ApplyShopTurnEconomy(ShopEconomySettings settings, int blacksmithLevel, ShopStatusModel status = null,
        float machineDemandFloorBonus = 0f, int turnIndex = 0, int flowSeed = 0,
        NewsEffectResolver news = null)
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

        // ------------------------------------------------
        // Step 6: 価格変動エンジンの解決（設定が変わったときだけ作り直す）
        // ------------------------------------------------
        var engine = ResolvePriceEngine(settings, flowSeed);
        engine.BeginTurn(turnIndex);

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

            // N1: ニュースの効果。Trend そのものには加算せず、ここで別枠として足す。
            //     Trend はランダムウォークと減衰を続けているので、直接足すと期間終了時に
            //     剥がせなくなる（Docs/News_Spec.md §7.2）。
            float newsTrendBias = news != null ? news.TrendBias(runtime, turnIndex) : 0f;
            float effectiveTrend = Mathf.Clamp(runtime.Trend + newsTrendBias, -1f, 1f);

            float naturalDemand = Mathf.Clamp01(0.5f + effectiveTrend * settings.trendAmplitude);
            float convergenceDelta = (naturalDemand - runtime.Demand.Value) * settings.trendConvergenceRate;
            float displayDelta = displaying
                ? settings.displayDemandUp * spreadFactor
                : -settings.notDisplayDemandDown * spreadFactor;

            // マシン設置（冷蔵ケース等）の需要下限ボーナスは加算方式（0 で従来挙動と完全一致）
            float dynamicDemandFloor = Mathf.Min(
                settings.demandFloor + demandBias + machineDemandFloorBonus,
                settings.demandCeiling);

            // 発効ターンだけ乗る直撃分。需要の収束が 15%/ターンと遅く、lead 1〜3 の
            // 短い窓では Trend バイアスだけでは体感に届かないため。
            float newsDemandKick = news != null ? news.DemandKick(runtime, turnIndex) : 0f;

            runtime.Demand.Value = Mathf.Clamp(
                runtime.Demand.Value + convergenceDelta + displayDelta + newsDemandKick,
                dynamicDemandFloor, settings.demandCeiling);

            // ------------------------------------------------
            // 価格変動率は差し替え可能なエンジンへ委譲する。
            //   既定 (LegacyShopPriceEngine) … 従来の需要帯ごとの一様乱数。挙動は完全に同一。
            //   AbmShopPriceEngine          … 仮想トレーダーの注文フローから決める。
            // Attention 増幅(A2) と Retention 安定化(A4) はどちらのエンジンでも適用される。
            // ------------------------------------------------
            // 層1: 適正値。需要から決まる「本来あるべき価格」。ABM の逆張り勢はここへ引き寄せる。
            //   前ターンの需要から求めた値も渡し、ファンダメンタル勢が差分（需要の変化）を見る。
            //   Legacy はこの2値を使わない。
            int fairFloor = Mathf.Max(1, Mathf.RoundToInt(master.basePrice * floorRate));
            int fairCeiling = Mathf.Max(fairFloor, Mathf.RoundToInt(master.basePrice * settings.shopPriceCeilingRate));
            float fairValue = ShopFairValue.Compute(
                master.basePrice, runtime.Demand.Value, settings.demandPricePremium, fairFloor, fairCeiling);
            float previousFairValue = ShopFairValue.Compute(
                master.basePrice, runtime.PreviousDemand, settings.demandPricePremium, fairFloor, fairCeiling);

            var context = new ShopPriceContext(
                runtime, master, settings, fairValue, previousFairValue, attentionFactor, retentionStability);
            float s1Rate = engine.GetPriceRate(in context);

            int newPrice = Mathf.Max(1, Mathf.RoundToInt(runtime.CurrentPrice.Value * s1Rate));

            // 掲載ターンの跳ね。世界中が同じ紙面を読んで飛びつくので、記事が出た時点で
            // 既に少し高い。誤報でもこれは起きる（そして実体が来ないので高値掴みになる）。
            // 発効ターンからは跳ねが数ターンかけて剥がれる（倍率が 1 未満で返る）。
            // 本物ならそこへ2段目の需要が来て値を支え、誤報なら値だけが落ちる。
            float newsHype = news != null ? news.HypeRate(runtime, turnIndex) : 1f;
            if (newsHype != 1f) newPrice = Mathf.Max(1, Mathf.RoundToInt(newPrice * newsHype));

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
    /// 予算内で現在の期待収益（ExpectedRevenueOf）が高い順にアイテムを自動購入する。
    /// </summary>
    public List<AutoPurchaseResult> AutoPurchase(int budget, int blacksmithLevel, TomsModel tomsModel,
        ItemTypeData.ItemAttribute? nextDungeonAttr = null, RelicEffectResolver relicResolver = null,
        AutoBuyStrategy strategy = AutoBuyStrategy.Recommend)
    {
        var results = new List<AutoPurchaseResult>();
        int remaining = Mathf.Min(budget, tomsModel.PlayerMoney.Value);

        var pool = RuntimeItems
            .Where(r => r.RequiredLevel.Value <= blacksmithLevel
                     && r.RemainToMax() > 0
                     && r.CurrentPrice.Value > 0);

        // 方針プリセットで優先順位を切り替える（同率以降はおすすめ順にフォールバック）
        var candidates = (strategy switch
        {
            AutoBuyStrategy.DungeonFocus => pool
                .OrderByDescending(r => nextDungeonAttr.HasValue && r.ItemAttribute == nextDungeonAttr.Value ? 1 : 0)
                .ThenByDescending(r => ExpectedRevenueOf(r)),
            AutoBuyStrategy.Bargain => pool
                .OrderBy(BargainRatioOf)
                .ThenByDescending(r => ExpectedRevenueOf(r)),
            AutoBuyStrategy.Dividend => pool
                .OrderByDescending(r => r.DividendPerTurn > 0
                    ? (float)r.DividendPerTurn / Mathf.Max(1, r.CurrentPrice.Value)
                    : 0f)
                .ThenByDescending(r => ExpectedRevenueOf(r)),
            _ => pool.OrderByDescending(r => ExpectedRevenueOf(r)),
        }).ToList();

        foreach (var item in candidates)
        {
            if (remaining <= 0) break;
            // レリックの仕入れ割引（ProcurementCostMul）は手動購入と同じ実効単価で適用
            int unitPrice = RelicPricing.GetBuyUnitPrice(item.CurrentPrice.Value, relicResolver);
            int canBuy = Mathf.Min(remaining / unitPrice, item.RemainToMax());
            if (canBuy <= 0) continue;

            int cost = canBuy * unitPrice;
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

    /// <summary>割安度（現在価格／基準価格。低いほど割安）。基準価格が引けない銘柄は割安扱いしない。</summary>
    private float BargainRatioOf(RuntimeItemData runtime)
    {
        var master = GetMasterItem(runtime.ItemId);
        if (master == null || master.basePrice <= 0) return float.MaxValue;
        return (float)runtime.CurrentPrice.Value / master.basePrice;
    }

    // ========================================
    // 陳列枠（店レベル）関連
    // ========================================

    /// <summary>現在陳列中の銘柄数。</summary>
    public int CountDisplayedKinds() => RuntimeItems.Count(r => r.IsDisplay.Value);

    /// <summary>
    /// 配当付き武器の毎ターン配当収入の合計（在庫 × 1個あたり配当）。
    /// GameFlowManager.NextTurn の朝に入金される。
    /// </summary>
    public int CalculateDividendIncome() =>
        RuntimeItems.Sum(r => r.DividendPerTurn > 0 ? r.DividendPerTurn * r.Stock.Value : 0);

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
                return new RuntimeItemData(item, SearchSpriteFromMaster(item.itemId), SearchBackgroundSpriteFromMaster(item.requiredLevel),
                    master != null ? master.dividendPerTurn : 0);
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
                master.salesRate,
                master.dividendPerTurn
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

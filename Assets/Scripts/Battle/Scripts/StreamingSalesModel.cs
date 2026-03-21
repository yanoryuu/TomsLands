using R3;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class StreamingSalesModel
{
    public ReadOnlyReactiveProperty<int> TotalSales => _totalSales;
    private readonly ReactiveProperty<int> _totalSales = new();
    public List<RuntimeItemData> ItemsForSale { get; private set; }

    public Subject<int> OnItemSold { get; } = new Subject<int>();
    public event Action<int> OnItemSoldEvent;

    private readonly float _baseSalesInterval;
    private readonly float _intervalRandomness;

    /// <summary>
    /// FightScene 専用の需要度管理。外部から設定する。
    /// </summary>
    public BattleDemandTracker DemandTracker { get; set; }

    /// <summary>
    /// 1ターンに1アイテムあたり最大何個売れるか。
    /// BattleDemand × MaxSellPerTurn で販売数が決まる。
    /// </summary>
    public int MaxSellPerTurn { get; set; } = 5;

    public Subject<Unit> OnItemsReordered { get; } = new Subject<Unit>();

    // リストを入れ替える
    public void SwapItems(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= ItemsForSale.Count || toIndex < 0 || toIndex >= ItemsForSale.Count)
        {
            return;
        }
        (ItemsForSale[toIndex], ItemsForSale[fromIndex]) = (ItemsForSale[fromIndex], ItemsForSale[toIndex]);

        // 並び替わったことを通知
        OnItemsReordered.OnNext(Unit.Default);
    }

    public StreamingSalesModel(float baseInterval, float randomness)
    {
        _baseSalesInterval = baseInterval;
        _intervalRandomness = randomness;
    }

    public void SetItemsForSale(List<RuntimeItemData> items)
    {
        ItemsForSale = items;
    }

    /// <summary>
    /// 自動販売の非同期ループを開始
    /// </summary>
    public async UniTask StartSalesLoopAsync(ItemModel mainItemModel, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // 1. 次の販売までの待ち時間を計算
                float delay = _baseSalesInterval + UnityEngine.Random.Range(-_intervalRandomness, _intervalRandomness);
                await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0, delay)), cancellationToken: token);

                if (ItemsForSale == null || ItemsForSale.Count == 0)
                {
                    continue; // 商品がなければ次のループへ
                }

                // 2. 商品を一つずつ、販売処理の専門メソッドに渡す
                for (int i = 0; i < ItemsForSale.Count; i++)
                {
                    ProcessSingleItemSale(mainItemModel, i);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // 正常な終了
            }
            catch (Exception ex)
            {
                Debug.LogException(ex); // ループがクラッシュするのを防ぐ
            }
        }
    }

    /// <summary>
    /// 毎ターン呼び出される即時販売処理。
    /// 売り場に出ている全アイテムに対して1回ずつ販売判定を行う。
    /// </summary>
    public void ExecuteSingleTurnSales(ItemModel mainItemModel)
    {
        if (ItemsForSale == null || ItemsForSale.Count == 0) return;

        for (int i = 0; i < ItemsForSale.Count; i++)
        {
            ProcessSingleItemSale(mainItemModel, i);
        }
    }

    /// <summary>
    /// 指定タイプのアイテムの価格を変動させる。
    /// 売り場（ItemsForSale）と在庫（inventoryItems）の両方に適用。
    /// ストップ高/ストップ安で元値に対する上下限を設ける。
    /// </summary>
    public void AdjustPricesByType(ItemTypeData.ItemType targetType, float rate, List<RuntimeItemData> inventoryItems,
        float priceFloorRate = 0f, float priceCeilingRate = 0f, ItemModel itemModel = null)
    {
        ApplyRateToItems(ItemsForSale, targetType, rate, priceFloorRate, priceCeilingRate, itemModel);
        ApplyRateToItems(inventoryItems, targetType, rate, priceFloorRate, priceCeilingRate, itemModel);
    }

    /// <summary>
    /// 敵の属性に応じて、有利属性のアイテムは値上がり、不利属性のアイテムは値下がりさせる。
    /// 売り場（ItemsForSale）と在庫（inventoryItems）の両方に適用。
    /// </summary>
    public void AdjustPricesByElementAffinity(ElementType enemyElement, float effectiveRate, float weakRate,
        List<RuntimeItemData> inventoryItems, float priceFloorRate, float priceCeilingRate, ItemModel itemModel)
    {
        ApplyElementAffinityToItems(ItemsForSale, enemyElement, effectiveRate, weakRate, priceFloorRate, priceCeilingRate, itemModel);
        ApplyElementAffinityToItems(inventoryItems, enemyElement, effectiveRate, weakRate, priceFloorRate, priceCeilingRate, itemModel);
    }

    /// <summary>
    /// リスト内のアイテムにタイプベースの倍率を適用する内部メソッド。
    /// </summary>
    private void ApplyRateToItems(List<RuntimeItemData> items, ItemTypeData.ItemType targetType, float rate,
        float priceFloorRate, float priceCeilingRate, ItemModel itemModel)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (item == null || item.ItemType != targetType) continue;
            int newPrice = Mathf.Max(1, Mathf.RoundToInt(item.CurrentPrice.Value * rate));
            newPrice = ClampToStopLimits(newPrice, item, priceFloorRate, priceCeilingRate, itemModel);
            item.CurrentPrice.Value = newPrice;
        }
    }

    /// <summary>
    /// リスト内のアイテムに属性相性ベースの倍率を適用する内部メソッド。
    /// </summary>
    private void ApplyElementAffinityToItems(List<RuntimeItemData> items, ElementType enemyElement,
        float effectiveRate, float weakRate, float priceFloorRate, float priceCeilingRate, ItemModel itemModel)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (item == null) continue;
            
            float rate;
            if (ElementAttributeMapper.IsEffective(item.ItemAttribute, enemyElement))
            {
                rate = effectiveRate;
            }
            else if (ElementAttributeMapper.IsWeak(item.ItemAttribute, enemyElement))
            {
                rate = weakRate;
            }
            else
            {
                continue; // 無関係な属性は変動なし
            }

            int newPrice = Mathf.Max(1, Mathf.RoundToInt(item.CurrentPrice.Value * rate));
            newPrice = ClampToStopLimits(newPrice, item, priceFloorRate, priceCeilingRate, itemModel);
            item.CurrentPrice.Value = newPrice;
        }
    }

    /// <summary>
    /// ストップ高/ストップ安の制限を適用する。
    /// 元値（masterデータの basePrice）に対する倍率で上下限を制限する。
    /// </summary>
    private int ClampToStopLimits(int price, RuntimeItemData item, float floorRate, float ceilingRate, ItemModel itemModel)
    {
        if (itemModel == null || (floorRate <= 0f && ceilingRate <= 0f)) return price;

        var master = itemModel.GetMasterItem(item.ItemId);
        if (master == null) return price;

        int basePrice = master.basePrice;
        if (floorRate > 0f)
        {
            int floor = Mathf.Max(1, Mathf.RoundToInt(basePrice * floorRate));
            price = Mathf.Max(price, floor);
        }
        if (ceilingRate > 0f)
        {
            int ceiling = Mathf.RoundToInt(basePrice * ceilingRate);
            price = Mathf.Min(price, ceiling);
        }
        return price;
    }

    /// <summary>
    /// 商品を売る具体的な処理。
    /// BattleDemandTracker が設定されている場合は BattleDemand に基づいて販売数量を決定する。
    /// 設定されていない場合は既存の Demand ベースの確率判定で 1個ずつ販売する（フォールバック）。
    /// </summary>
    private void ProcessSingleItemSale(ItemModel mainItemModel, int itemIndex)
    {
        var item = ItemsForSale[itemIndex];
        if (item == null || item.Stock.Value <= 0)
        {
            return;
        }

        int quantitySold;

        if (DemandTracker != null)
        {
            // BattleDemand ベース: 需要度 × MaxSellPerTurn で販売数を決定
            DemandTracker.EnsureTracked(item);
            float battleDemand = DemandTracker.GetDemand(item.ItemId);
            quantitySold = Mathf.Clamp(
                Mathf.FloorToInt(battleDemand * MaxSellPerTurn),
                0,
                item.Stock.Value
            );

            if (quantitySold <= 0) return;
        }
        else
        {
            // フォールバック: 既存の確率ベース判定
            if (UnityEngine.Random.value >= Mathf.Clamp01(item.Demand.Value))
            {
                return;
            }
            quantitySold = 1;
        }

        int earnings = quantitySold * item.CurrentPrice.Value;

        // 総売上を更新
        _totalSales.Value += earnings;

        // 在庫を減らす
        int beforeStock = item.Stock.Value;
        mainItemModel.SellItem(item.ItemId, quantitySold);

        var runtimeFromModel = mainItemModel.GetRuntimeItem(item.ItemId);
        if (runtimeFromModel == null)
        {
            item.Stock.Value = Mathf.Max(0, beforeStock - quantitySold);
        }
        else if (!ReferenceEquals(runtimeFromModel, item))
        {
            item.Stock.Value = runtimeFromModel.Stock.Value;
        }
        else if (runtimeFromModel.Stock.Value == beforeStock)
        {
            item.Stock.Value = Mathf.Max(0, beforeStock - quantitySold);
        }

        // 売れたことを通知
        OnItemSold.OnNext(itemIndex);
        OnItemSoldEvent?.Invoke(itemIndex);

        // BattleDemandTracker に販売記録（案C: トレンド用）
        if (DemandTracker != null)
        {
            DemandTracker.RecordSale(item.ItemId);
        }

        Debug.Log($"[販売] {item.ItemId} × {quantitySold}個 = {earnings}G (BattleDemand={DemandTracker?.GetDemand(item.ItemId):F2})");
    }
}
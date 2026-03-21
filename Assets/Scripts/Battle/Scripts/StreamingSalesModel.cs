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
    /// 商品を1つ売る具体的な処理
    /// </summary>
    private void ProcessSingleItemSale(ItemModel mainItemModel, int itemIndex)
    {
        var item = ItemsForSale[itemIndex];
        if (item == null || item.Stock.Value <= 0)
        {
            return;
        }

        // 需要に基づいて、売れるかどうかを判断
        if (UnityEngine.Random.value < Mathf.Clamp01(item.Demand.Value))
        {
            const int quantitySold = 1;
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
        }
    }
}
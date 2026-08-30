using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using R3;
using UnityEngine;

/// <summary>
/// 売り注文の保持・約定・永続化を担当するモデル。
/// - 陳列品は営業サマリー(TurnEndSummaryPresenter)で売り注文になる（在庫は作成時に引き当て済み）
/// - 約定予定ターンの営業サマリーで全量約定して入金される
/// - 配信日・イベント日などで営業サマリーが走らなかった注文は
///   GameFlowManager.NextTurn() の持ち越し精算(SettleOverdue)で処理される
/// お金(PlayerMoney)はこのクラスでは触らず、呼び出し側が結果を適用する。
/// </summary>
public class SellOrderModel
{
    private const string FileName = "sellOrderData.json";

    public List<SellOrder> PendingOrders { get; } = new();

    /// <summary>未約定注文の見込み入金額合計（注文時価格ベース）。UIバインド用。</summary>
    public ReactiveProperty<int> PendingTotalEstimate { get; } = new(0);

    public SellOrderModel()
    {
        LoadData();
    }

    /// <summary>
    /// 売り注文を作成する。在庫の引き当て（Stock減算）は呼び出し側で済ませておくこと。
    /// </summary>
    public SellOrder Place(RuntimeItemData item, int quantity, int currentTurn, int delayTurns)
    {
        if (item == null || quantity <= 0) return null;

        var order = new SellOrder
        {
            OrderId = Guid.NewGuid().ToString("N"),
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            Quantity = quantity,
            OrderedPrice = item.CurrentPrice.Value,
            OrderedTurn = currentTurn,
            SettleTurn = currentTurn + Mathf.Max(1, delayTurns),
        };
        PendingOrders.Add(order);
        RefreshEstimate();

        Debug.Log($"[SellOrder] 売り注文: {order.ItemName} ×{order.Quantity} @ {order.OrderedPrice}G (約定予定: Turn {order.SettleTurn})");
        return order;
    }

    /// <summary>
    /// 約定予定が当ターン以前の注文を全て約定する（営業サマリーから呼ぶ通常経路）。
    /// </summary>
    public SellSettlementResult SettleDue(int currentTurn, ItemModel itemModel, ShopEconomySettings settings, MarketingFacade marketing, RelicEffectResolver relicResolver = null)
        => SettleInternal(o => o.SettleTurn <= currentTurn, itemModel, settings, marketing, relicResolver);

    /// <summary>
    /// 約定予定日を過ぎてしまった注文だけを約定する（配信日・イベント日を挟んだときの
    /// 持ち越し精算。GameFlowManager.NextTurn() の朝に呼ぶ）。
    /// 当日約定分(SettleTurn == currentTurn)はその日の営業サマリーに任せるため対象外。
    /// </summary>
    public SellSettlementResult SettleOverdue(int currentTurn, ItemModel itemModel, ShopEconomySettings settings, MarketingFacade marketing, RelicEffectResolver relicResolver = null)
        => SettleInternal(o => o.SettleTurn < currentTurn, itemModel, settings, marketing, relicResolver);

    private SellSettlementResult SettleInternal(Func<SellOrder, bool> isDue, ItemModel itemModel, ShopEconomySettings settings, MarketingFacade marketing, RelicEffectResolver relicResolver)
    {
        var result = new SellSettlementResult();
        var due = PendingOrders.Where(isDue).ToList();
        if (due.Count == 0) return result;

        float clampRate = settings != null ? settings.sellOrderPriceClampRate : 0.2f;
        float feeRate = settings != null ? settings.sellOrderFeeRate : 0f;

        // レリック補正（相場師ビルド: SellClampAdd）。クランプ幅が±に広がり、値動きの恩恵もリスクも増える
        if (relicResolver != null)
            clampRate = Mathf.Clamp(relicResolver.Modify(RelicStatId.SellClampAdd, clampRate), 0f, 2f);

        foreach (var order in due)
        {
            // 約定価格 = 当日の市場価格を、注文時価格の ±clampRate にクランプ
            // （配信ボーナスの×5等で約定額が跳ねる事故を防ぐ）
            int marketPrice = itemModel?.GetRuntimeItem(order.ItemId)?.CurrentPrice.Value ?? order.OrderedPrice;
            int floor = Mathf.RoundToInt(order.OrderedPrice * (1f - clampRate));
            int ceiling = Mathf.RoundToInt(order.OrderedPrice * (1f + clampRate));
            int settledPrice = Mathf.Clamp(marketPrice, Mathf.Max(1, floor), Mathf.Max(1, ceiling));

            int income = Mathf.RoundToInt(settledPrice * order.Quantity * (1f - feeRate));

            result.Settled.Add(new SettledOrderInfo
            {
                ItemId = order.ItemId,
                ItemName = order.ItemName,
                Quantity = order.Quantity,
                OrderedPrice = order.OrderedPrice,
                SettledPrice = settledPrice,
                Income = income,
            });
            result.BaseIncome += income;

            PendingOrders.Remove(order);
        }

        // バズ等のマーケティング倍率は約定日（実際に売れた日）の状態を適用する
        result.TotalIncome = marketing != null
            ? marketing.CalculateFinalRevenue(result.BaseIncome)
            : result.BaseIncome;

        RefreshEstimate();

        Debug.Log($"[SellOrder] 約定: {result.Settled.Count}件 → 入金 {result.TotalIncome}G（基本 {result.BaseIncome}G）");
        return result;
    }

    private void RefreshEstimate()
    {
        PendingTotalEstimate.Value = PendingOrders.Sum(o => o.EstimatedIncome);
    }

    // ========================================
    // 永続化（各Model自前保存の既存流儀に合わせる）
    // ========================================

    public void SaveData()
    {
        var list = new SellOrderList
        {
            orders = PendingOrders.Select(o => o.ToPlain()).ToList()
        };
        string json = JsonUtility.ToJson(list, true);
        File.WriteAllText(SaveSlotManager.GetPath(FileName), json);
    }

    public void LoadData()
    {
        PendingOrders.Clear();

        string path = SaveSlotManager.GetPath(FileName);
        if (File.Exists(path))
        {
            var list = JsonUtility.FromJson<SellOrderList>(File.ReadAllText(path));
            if (list?.orders != null)
            {
                foreach (var plain in list.orders)
                {
                    if (string.IsNullOrEmpty(plain.itemId) || plain.quantity <= 0) continue;
                    PendingOrders.Add(SellOrder.FromPlain(plain));
                }
            }
        }
        // 旧セーブ（ファイルなし）は注文ゼロとして扱う
        RefreshEstimate();
    }

    /// <summary>ニューゲーム用リセット。</summary>
    public void Clear()
    {
        PendingOrders.Clear();
        RefreshEstimate();
    }
}

using System;
using System.Collections.Generic;

/// <summary>
/// 売り注文（1銘柄ぶん）。
/// 陳列した商品は営業フェーズで「売り注文」となり、翌日の営業で全量約定して入金される。
/// 在庫は注文作成時に既に引き当て済み（Stock から減算済み）。
/// </summary>
public class SellOrder
{
    public string OrderId;
    public string ItemId;
    public string ItemName;
    public int Quantity;
    /// <summary>注文時の単価。約定価格クランプと値洗い表示の基準。</summary>
    public int OrderedPrice;
    /// <summary>注文を出したターン番号。</summary>
    public int OrderedTurn;
    /// <summary>約定予定ターン番号（このターンの営業サマリーで入金される）。</summary>
    public int SettleTurn;

    /// <summary>注文時価格ベースの見込み入金額。</summary>
    public int EstimatedIncome => OrderedPrice * Quantity;

    public SellOrderPlain ToPlain() => new SellOrderPlain
    {
        orderId = OrderId,
        itemId = ItemId,
        itemName = ItemName,
        quantity = Quantity,
        orderedPrice = OrderedPrice,
        orderedTurn = OrderedTurn,
        settleTurn = SettleTurn,
    };

    public static SellOrder FromPlain(SellOrderPlain p) => new SellOrder
    {
        OrderId = p.orderId,
        ItemId = p.itemId,
        ItemName = p.itemName,
        Quantity = p.quantity,
        OrderedPrice = p.orderedPrice,
        OrderedTurn = p.orderedTurn,
        SettleTurn = p.settleTurn,
    };
}

/// <summary>JsonUtility 用のシリアライズDTO（RuntimeItemDataPlain と同じ作法）。</summary>
[Serializable]
public class SellOrderPlain
{
    public string orderId;
    public string itemId;
    public string itemName;
    public int quantity;
    public int orderedPrice;
    public int orderedTurn;
    public int settleTurn;
}

/// <summary>JsonUtility は List 直下をシリアライズできないためのラッパー。</summary>
[Serializable]
public class SellOrderList
{
    public List<SellOrderPlain> orders = new();
}

/// <summary>約定処理の結果。入金額と明細を持つ。</summary>
public class SellSettlementResult
{
    /// <summary>マーケティング倍率適用後の入金合計。</summary>
    public int TotalIncome;
    /// <summary>倍率適用前の入金合計。</summary>
    public int BaseIncome;
    public List<SettledOrderInfo> Settled = new();
}

/// <summary>約定した1注文ぶんの明細。</summary>
public class SettledOrderInfo
{
    public string ItemId;
    public string ItemName;
    public int Quantity;
    public int OrderedPrice;
    public int SettledPrice;
    public int Income;
}

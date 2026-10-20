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
    /// <summary>注文した品の平均取得単価（実現損益の計算用）。-1 = 不明（古いセーブ・取得単価を記録する前の在庫）。</summary>
    public int UnitCost = RuntimeItemData.UnknownCost;

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
        unitCost = UnitCost,
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
        UnitCost = p.unitCost,
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
    /// <summary>平均取得単価。項目の無い古いセーブは -1（不明）のまま読み込まれる。</summary>
    public int unitCost = RuntimeItemData.UnknownCost;
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

    /// <summary>取得単価が分かっている注文の入金（倍率適用後に按分）。</summary>
    public int KnownCostIncome;
    /// <summary>取得単価が分かっている注文の仕入れ額（取得単価×数量）。</summary>
    public int KnownCostBasis;
    /// <summary>取得単価が分からない注文の数。</summary>
    public int UnknownCostCount;

    /// <summary>実現損益を出せる注文があるか。</summary>
    public bool HasRealizedProfit => Settled.Count > UnknownCostCount;
    /// <summary>実現損益（取得単価が分かっている注文だけ）。</summary>
    public int RealizedProfit => KnownCostIncome - KnownCostBasis;
    /// <summary>実現損益の利回り（0.1 = +10%）。仕入れ額 0（製造機の品など）なら null。</summary>
    public float? RealizedYield => HasRealizedProfit && KnownCostBasis > 0 ? (float)RealizedProfit / KnownCostBasis : (float?)null;
    /// <summary>入金に掛かった倍率（バズ・フォロワー・マシン・レリックの合計）。</summary>
    public float Multiplier => BaseIncome > 0 ? (float)TotalIncome / BaseIncome : 1f;
}

/// <summary>約定した1注文ぶんの明細。</summary>
public class SettledOrderInfo
{
    public string ItemId;
    public string ItemName;
    public int Quantity;
    public int OrderedPrice;
    public int SettledPrice;
    /// <summary>倍率を掛ける前の入金（約定価格×数量−手数料）。</summary>
    public int Income;
    /// <summary>平均取得単価。-1 = 不明。</summary>
    public int UnitCost = RuntimeItemData.UnknownCost;
}

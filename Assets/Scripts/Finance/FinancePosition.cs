using System;
using System.Collections.Generic;

/// <summary>
/// 保有ポジション（1回の購入 = 1ポジション）。
/// JsonUtility は継承をシリアライズできないため、種別は kind フィールド + 汎用フィールドで
/// フラットに持つ（RuntimeItemDataPlain と同じ思想）。
/// </summary>
public class FinancePosition
{
    public string PositionId;
    public string ProductId;
    public FinancialProductKind Kind;
    /// <summary>保有口数。</summary>
    public int Units;
    /// <summary>購入時の1口価格（債券は額面）。</summary>
    public int UnitCost;
    public int AcquiredTurn;
    /// <summary>債券の満期ターン（このターンの朝に償還）。ファンドは 0。</summary>
    public int MaturityTurn;

    /// <summary>投下元本合計。</summary>
    public int Principal => UnitCost * Units;

    public FinancePositionPlain ToPlain() => new FinancePositionPlain
    {
        positionId = PositionId,
        productId = ProductId,
        kind = (int)Kind,
        units = Units,
        unitCost = UnitCost,
        acquiredTurn = AcquiredTurn,
        maturityTurn = MaturityTurn,
    };

    public static FinancePosition FromPlain(FinancePositionPlain p) => new FinancePosition
    {
        PositionId = p.positionId,
        ProductId = p.productId,
        Kind = (FinancialProductKind)p.kind,
        Units = p.units,
        UnitCost = p.unitCost,
        AcquiredTurn = p.acquiredTurn,
        MaturityTurn = p.maturityTurn,
    };
}

[Serializable]
public class FinancePositionPlain
{
    public string positionId;
    public string productId;
    public int kind;
    public int units;
    public int unitCost;
    public int acquiredTurn;
    public int maturityTurn;
}

/// <summary>ファンド基準価額の履歴（productId ごと）。JsonUtility は Dictionary 不可のためリストで持つ。</summary>
[Serializable]
public class FundNavHistoryPlain
{
    public string productId;
    public List<int> navHistory = new();
}

/// <summary>portfolioData.json のルート。</summary>
[Serializable]
public class PortfolioSaveData
{
    public List<FinancePositionPlain> positions = new();
    public List<FundNavHistoryPlain> navHistories = new();
}

/// <summary>ターン処理（償還・入金）の結果。</summary>
public class FinanceTurnResult
{
    /// <summary>満期償還による入金合計（元本+利息）。</summary>
    public int BondPayout;
    /// <summary>償還された債券の明細（商品名, 元本, 利息）。</summary>
    public List<(string productName, int principal, int interest)> MaturedBonds = new();
}

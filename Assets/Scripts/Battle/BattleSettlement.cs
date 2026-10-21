using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 配信リザルト（精算書）に出す内訳。お金の計算は BattleSceneStarter で済んでおり、ここは表示用の値だけ。
/// 純利益 = 売上 − 補充 + 売れ残り返金 − 介入（返金後） + 防衛報酬（BattleOutputData.TotalEarnings と同じ）。
/// </summary>
public sealed class BattleSettlement
{
    public BattleResult Result { get; }
    /// <summary>配信中の売上（売れたときの価格の合計）</summary>
    public int Sales { get; }
    /// <summary>配信中の補充に払った額</summary>
    public int RestockCost { get; }
    /// <summary>補充して売れ残った分の返金</summary>
    public int RestockRefund { get; }
    /// <summary>介入に払った額（未実行の返金を引いた純支出）</summary>
    public int InterventionNet { get; }
    /// <summary>防衛報酬（勇者が負けたときの魔王軍からの配当。レリック補正込み）</summary>
    public int DefenseReward { get; }
    /// <summary>純利益（所持金に入る額）</summary>
    public int NetProfit { get; }
    public IReadOnlyList<BattleOutputSoldItem> SoldItems { get; }
    /// <summary>勇者の装備の相場が勝敗でどう動くか（勇者側の配当）。途中終了なら空</summary>
    public IReadOnlyList<BattleMarketImpact> MarketImpacts { get; }

    /// <summary>魔王側に張って当たった（勇者が負け、防衛報酬を受け取った）</summary>
    public bool IsDemonPayout => Result == BattleResult.Defeat && DefenseReward > 0;

    public BattleSettlement(BattleResult result, int sales, int restockCost, int restockRefund, int interventionNet,
        int defenseReward, int netProfit, IReadOnlyList<BattleOutputSoldItem> soldItems, IReadOnlyList<BattleMarketImpact> marketImpacts)
    {
        Result = result;
        Sales = sales;
        RestockCost = restockCost;
        RestockRefund = restockRefund;
        InterventionNet = interventionNet;
        DefenseReward = defenseReward;
        NetProfit = netProfit;
        SoldItems = soldItems ?? new List<BattleOutputSoldItem>();
        MarketImpacts = marketImpacts ?? new List<BattleMarketImpact>();
    }
}

/// <summary>
/// 勇者の装備1つの相場の動き（配信リザルトの「相場への影響」1行）。
/// 左は配信前の店の値段、右は帰還後の店の値段（勝敗の倍率を掛け、ストップ高/安で丸めた値。ItemModel.PreviewPriceAfterBattle）。
/// 帰還処理（BattleResultHandler）は日送りのあとに装備の値段をこの値へ揃えるので、翌朝の店の値段と一致する。
/// </summary>
public readonly struct BattleMarketImpact
{
    public readonly string ItemName;
    public readonly Sprite Icon;
    public readonly int PriceBefore;
    public readonly int PriceAfter;
    /// <summary>帰還後に店に残る在庫（配信で売れた分を引いた数）</summary>
    public readonly int Stock;
    /// <summary>在庫の平均取得単価。記録が無ければ null（含み損益を出さない）</summary>
    public readonly int? UnitCost;

    public BattleMarketImpact(string itemName, Sprite icon, int priceBefore, int priceAfter, int stock, int? unitCost = null)
    {
        ItemName = itemName;
        Icon = icon;
        PriceBefore = priceBefore;
        PriceAfter = priceAfter;
        Stock = Mathf.Max(0, stock);
        UnitCost = unitCost.HasValue && unitCost.Value >= 0 ? unitCost : null;
    }

    /// <summary>在庫ぶんの含み損益（帰還後の値段 − 取得単価）× 在庫。取得単価が分からなければ null</summary>
    public int? StockGain => UnitCost.HasValue ? (PriceAfter - UnitCost.Value) * Stock : (int?)null;
}

using System.Collections.Generic;

/// <summary>
/// リザルト画面で表示するゲーム全体の統計データ。
/// ResultModel.BuildStatistics() で生成される。
/// </summary>
public class ResultStatisticsData
{
    /// <summary>最終所持金</summary>
    public int FinalMoney;

    /// <summary>初期所持金</summary>
    public int InitialMoney;

    /// <summary>所持金増減（FinalMoney - InitialMoney）</summary>
    public int MoneyDifference;

    /// <summary>到達ターン数</summary>
    public int TotalTurns;

    /// <summary>鍛冶屋レベル</summary>
    public int BlacksmithLevel;

    /// <summary>情報屋レベル</summary>
    public int InfoBrokerLevel;

    /// <summary>在庫に残っているアイテムの総数</summary>
    public int TotalRemainingStock;

    /// <summary>在庫アイテムの総資産額（各アイテムの現在価格×在庫数の合計）</summary>
    public int TotalStockValue;

    /// <summary>純資産（最終所持金 + 在庫総資産額）</summary>
    public int NetWorth;

    /// <summary>評価ランク（S/A/B/C/D）</summary>
    public string Rank;

    /// <summary>評価に応じたコメント</summary>
    public string Comment;

    /// <summary>各アイテムの最終状態リスト</summary>
    public List<ResultItemSummary> ItemSummaries;
}

/// <summary>
/// リザルト画面に表示する個別アイテムの最終状態
/// </summary>
public class ResultItemSummary
{
    public string ItemName;
    public int RemainingStock;
    public int CurrentPrice;
    public int StockValue; // CurrentPrice * RemainingStock
    public float Demand;
}


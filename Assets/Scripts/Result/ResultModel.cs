using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// リザルト画面のModel。
/// ゲーム全体の統計データを集計し、評価ランクを算出する。
/// </summary>
public class ResultModel
{
    private readonly TomsModel _tomsModel;
    private readonly ItemModel _itemModel;

    public ResultStatisticsData Statistics { get; private set; }

    public ResultModel(TomsModel tomsModel, ItemModel itemModel)
    {
        _tomsModel = tomsModel;
        _itemModel = itemModel;
    }

    /// <summary>
    /// ゲーム全体の統計データを集計する。
    /// ResultPresenter の Entry() から呼ばれる。
    /// TomsModel / ItemModel はセーブデータから自動ロード済み。
    /// </summary>
    public ResultStatisticsData BuildStatistics()
    {
        int finalMoney = _tomsModel.PlayerMoney.Value;
        int initialMoney = GameConst.InitMoney;
        int totalTurns = _tomsModel.CurrentTurn.Value;
        int blacksmithLevel = _tomsModel.BlacksmithLevel.Value;
        int infoBrokerLevel = _tomsModel.InfoBrokerLevel.Value;

        // --- アイテム在庫集計 ---
        int totalRemainingStock = 0;
        int totalStockValue = 0;
        var itemSummaries = new List<ResultItemSummary>();

        foreach (var runtime in _itemModel.RuntimeItems)
        {
            int stock = runtime.Stock.Value;
            int price = runtime.CurrentPrice.Value;
            int value = stock * price;

            totalRemainingStock += stock;
            totalStockValue += value;

            if (stock > 0)
            {
                itemSummaries.Add(new ResultItemSummary
                {
                    ItemName = runtime.ItemName,
                    RemainingStock = stock,
                    CurrentPrice = price,
                    StockValue = value,
                    Demand = runtime.Demand.Value
                });
            }
        }

        int netWorth = finalMoney + totalStockValue;
        string rank = EvaluateRank(netWorth);
        string comment = GetRankComment(rank);

        Statistics = new ResultStatisticsData
        {
            FinalMoney = finalMoney,
            InitialMoney = initialMoney,
            MoneyDifference = finalMoney - initialMoney,
            TotalTurns = totalTurns,
            BlacksmithLevel = blacksmithLevel,
            InfoBrokerLevel = infoBrokerLevel,
            TotalRemainingStock = totalRemainingStock,
            TotalStockValue = totalStockValue,
            NetWorth = netWorth,
            Rank = rank,
            Comment = comment,
            ItemSummaries = itemSummaries
        };

        Debug.Log($"[ResultModel] Statistics built: NetWorth={netWorth}, Rank={rank}");
        return Statistics;
    }

    /// <summary>
    /// 純資産に基づいて評価ランクを算出する。
    /// </summary>
    private string EvaluateRank(int netWorth)
    {
        // 初期資金の倍率で評価
        float ratio = (float)netWorth / GameConst.InitMoney;

        if (ratio >= 5.0f) return "S";
        if (ratio >= 3.0f) return "A";
        if (ratio >= 2.0f) return "B";
        if (ratio >= 1.0f) return "C";
        return "D";
    }

    /// <summary>
    /// ランクに応じたコメントを返す。
    /// </summary>
    private string GetRankComment(string rank)
    {
        return rank switch
        {
            "S" => "伝説の商人！ トムの名は大陸中に轟いた！",
            "A" => "素晴らしい経営手腕！ 街一番の店になった！",
            "B" => "堅実な商売で利益を出せた。立派な商人だ。",
            "C" => "なんとか黒字で終了。まだまだ伸びしろがある。",
            "D" => "赤字経営…。次はもっと上手くやれるはず！",
            _ => ""
        };
    }
}

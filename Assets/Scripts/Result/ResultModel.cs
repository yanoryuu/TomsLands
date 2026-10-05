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
    private readonly PortfolioModel _portfolioModel;

    public ResultStatisticsData Statistics { get; private set; }

    public ResultModel(TomsModel tomsModel, ItemModel itemModel, PortfolioModel portfolioModel)
    {
        _tomsModel = tomsModel;
        _itemModel = itemModel;
        _portfolioModel = portfolioModel;
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

        // 金融資産（債券元本+ファンド評価額）も純資産に算入する。
        // ※ 算入しないと村資金変換の基準から漏れ、「最終日に全解約」が作業最適解になってしまう
        int financeAssets = 0;
        if (_portfolioModel != null)
        {
            _portfolioModel.RefreshEstimate(_itemModel, _tomsModel.BlacksmithLevel.Value);
            financeAssets = _portfolioModel.TotalAssetsEstimate.Value;
        }

        int netWorth = finalMoney + totalStockValue + financeAssets;
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

        FillRunReview(Statistics, finalMoney);

        Debug.Log($"[ResultModel] Statistics built: NetWorth={netWorth}, Rank={rank}");
        return Statistics;
    }

#if UNITY_EDITOR
    /// <summary>【エディタ専用】ダミー統計に載せる適当な商品（アイコン確認用）。</summary>
    public RuntimeItemData PeekItemForDebug()
        => _itemModel != null && _itemModel.RuntimeItems.Count > 0
            ? _itemModel.RuntimeItems[Mathf.Min(5, _itemModel.RuntimeItems.Count - 1)]
            : null;
#endif

    /// <summary>
    /// ラン中の記録（RunHistory）から振り返り用の値を埋める。
    /// </summary>
    private void FillRunReview(ResultStatisticsData stats, int finalMoney)
    {
        var history = RunHistory.Load();

        stats.MoneyHistory = new List<int>(history.moneyValues);
        // 最後の記録と最終所持金がずれていれば（記録漏れ・旧セーブ）、終点を最終所持金に揃える
        if (stats.MoneyHistory.Count == 0 || stats.MoneyHistory[stats.MoneyHistory.Count - 1] != finalMoney)
            stats.MoneyHistory.Add(finalMoney);

        stats.StreamWins = history.streamWins;
        stats.StreamLosses = history.streamLosses;
        stats.BuzzCount = history.buzzCount;

        int best = -1;
        for (int i = 0; i < history.itemIds.Count && i < history.itemRevenues.Count; i++)
        {
            if (best < 0 || history.itemRevenues[i] > history.itemRevenues[best]) best = i;
        }
        if (best >= 0 && history.itemRevenues[best] > 0)
        {
            var runtime = _itemModel?.GetRuntimeItem(history.itemIds[best]);
            string storedName = best < history.itemNames.Count ? history.itemNames[best] : null;
            stats.BestItemName = runtime?.ItemName ?? (string.IsNullOrEmpty(storedName) ? history.itemIds[best] : storedName);
            stats.BestItemRevenue = history.itemRevenues[best];
            stats.BestItemIcon = runtime?.ItemIcon;
        }
    }

    /// <summary>
    /// 純資産に基づいて評価ランクを算出する。
    /// </summary>
    public static string EvaluateRank(int netWorth)
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
    public static string GetRankComment(string rank)
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

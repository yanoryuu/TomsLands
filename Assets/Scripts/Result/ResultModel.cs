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
    private readonly MetaProgressModel _metaProgress;

    public ResultStatisticsData Statistics { get; private set; }

    public ResultModel(TomsModel tomsModel, ItemModel itemModel, PortfolioModel portfolioModel, MetaProgressModel metaProgress)
    {
        _tomsModel = tomsModel;
        _itemModel = itemModel;
        _portfolioModel = portfolioModel;
        _metaProgress = metaProgress;
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

        // 村の銀行 Lv5「勝負の融資」をまだ返していない（最後の配信の前に借りた）なら、返す額（元本 + 利息）を差し引く。
        // 返し終えていれば所持金から引かれ済みなので、ここでは何もしない（二重に引かない）
        int unpaidLoan = Mathf.Max(0, _tomsModel.BankLoanOwed);
        int netWorth = finalMoney + totalStockValue + financeAssets - unpaidLoan;
        var history = RunHistory.Load();
        var mode = _tomsModel.GameMode;
        var profit = RunProfit.Compute(netWorth, history, _metaProgress);
        string rank = EvaluateRank(profit.Profit, mode);
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
            ItemSummaries = itemSummaries,
            Mode = mode,
            Profit = profit.Profit,
            Carried = profit.Carried,
            Advance = profit.Advance,
        };

        // 完走ボーナスは「期日が来た納付の予定額の合計」で決まる（実際に払った額ではない）
        Statistics.DueDebtCycles = DebtCalculator.CountDueCyclesOfClearedRun(totalTurns);
        Statistics.VillageFunds = MetaProgressModel.CalculateVillageFunds(true, profit.Profit, profit.Carried, mode, Statistics.DueDebtCycles);

        if (_metaProgress != null)
        {
            Statistics.HasPreviousBest = _metaProgress.TryGetBestProfit(mode, out int best);
            Statistics.PreviousBestProfit = best;
            Statistics.RunNumber = _metaProgress.TotalRuns + 1;
        }

        FillRunReview(Statistics, finalMoney, history);

        // 実績（クリア・ランク S・皆済など）。進行中のランが無いスロット（エディタの単体再生）では数えない
        if (SaveSlotManager.Exists(SaveSlotManager.CurrentSlot))
            AchievementEvents.ReportRunCleared(_tomsModel, rank);

        Debug.Log($"[ResultModel] Statistics built: NetWorth={netWorth}, Profit={profit.Profit} (持ち込み {profit.Carried} / 立て替え {profit.Advance}), Mode={mode}, Rank={rank}");
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
    private void FillRunReview(ResultStatisticsData stats, int finalMoney, RunHistoryData history)
    {
        history ??= RunHistory.Load();

        stats.MoneyHistory = new List<int>(history.moneyValues);
        // 最後の記録と最終所持金がずれていれば（記録漏れ・旧セーブ）、終点を最終所持金に揃える
        if (stats.MoneyHistory.Count == 0 || stats.MoneyHistory[stats.MoneyHistory.Count - 1] != finalMoney)
            stats.MoneyHistory.Add(finalMoney);

        stats.StreamWins = history.streamWins;
        stats.StreamLosses = history.streamLosses;
        stats.BuzzCount = history.buzzCount;
        stats.StreamEarningsTotal = history.streamEarningsTotal;
        stats.HasBestStream = history.hasBestStream;
        stats.BestStreamEarnings = history.bestStreamEarnings;
        stats.BestStreamDungeon = history.bestStreamDungeon ?? "";

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
    /// 儲けを難易度ごとの基準（GameConst.Village.rankBaseProfits）で割って評価ランクを決める。
    /// S = 基準×rankSRatio 以上 / A = 基準以上 / B = 基準×rankBRatio 以上 / C = 黒字 / D = 赤字。
    /// </summary>
    public static string EvaluateRank(int profit, GameModeId mode)
    {
        if (profit < 0) return "D";
        float ratio = profit / (float)RankBaseProfit(mode);
        var settings = GameConst.Village;
        if (ratio >= settings.rankSRatio) return "S";
        if (ratio >= 1f) return "A";
        if (ratio >= settings.rankBRatio) return "B";
        return "C";
    }

    /// <summary>
    /// 難易度ごとのランク A の基準（儲け）。
    /// = （village.rankBaseProfitShort/Medium/Long の上書き、無ければ rankBaseProfits）+ 村の総合Lv × village.rankBasePerVillageLevel。
    /// 既定（上書き 0・増分 0）は rankBaseProfits のまま。村の総合Lv はいまの施設レベル（ラン中は建て替わらないので出店時と同じ）。
    /// </summary>
    public static int RankBaseProfit(GameModeId mode)
    {
        var settings = GameConst.Village;
        var table = settings.rankBaseProfits;
        int i = (int)mode;
        int v = table != null && i >= 0 && i < table.Length ? table[i] : 0;
        int overrideValue = mode switch
        {
            GameModeId.Short => settings.rankBaseProfitShort,
            GameModeId.Medium => settings.rankBaseProfitMedium,
            GameModeId.Long => settings.rankBaseProfitLong,
            _ => 0,
        };
        int villageLevel = settings.rankBasePerVillageLevel != 0 ? VillageRank.TotalLevel(VillageEffects.Current) : 0;
        return StreamTuning.RankBase(v, overrideValue, settings.rankBasePerVillageLevel, villageLevel);
    }

    /// <summary>
    /// ランクに応じたコメントを返す。
    /// </summary>
    public static string GetRankComment(string rank) => rank switch
    {
        "S" => "伝説の商人！　トコの名は大陸中に轟いた！",
        "A" => "素晴らしい経営手腕！　町いちばんの店になった！",
        "B" => "堅実な商売で利益を出せた。立派な商人だ。",
        "C" => "なんとか黒字で終了。まだまだ伸びしろがある。",
        "D" => "赤字経営……。次はもっと上手くやれるはず！",
        _ => ""
    };
}

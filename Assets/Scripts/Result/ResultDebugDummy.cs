#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 【エディタ専用】ResultScene を単体再生したとき、進行中のランが無くても演出を確認できるダミー統計。
/// 使われるのは「選択中スロットにラン（tomsData.json）が無い」か <see cref="Force"/> が true のとき。
/// ダミー表示中は精算・セーブ削除・村への遷移を行わない（ResultPresenter 側でガード）。
/// eval 等から Force / Rank（EditorPrefs）を書き換えて任意のランクを確認できる。
/// </summary>
public static class ResultDebugDummy
{
    private const string ForceKey = "ResultDebugDummy.Force";
    private const string RankKey = "ResultDebugDummy.Rank";

    /// <summary>ランがあってもダミーで表示する（EditorPrefs に保存。Play 開始をまたいで残る）</summary>
    public static bool Force
    {
        get => UnityEditor.EditorPrefs.GetBool(ForceKey, false);
        set => UnityEditor.EditorPrefs.SetBool(ForceKey, value);
    }

    /// <summary>表示するランク（S/A/B/C/D）</summary>
    public static string Rank
    {
        get => UnityEditor.EditorPrefs.GetString(RankKey, "S");
        set => UnityEditor.EditorPrefs.SetString(RankKey, value);
    }

    public static bool ShouldUse() => Force || !SaveSlotManager.Exists(SaveSlotManager.CurrentSlot);

    public static ResultStatisticsData Create()
    {
        string rank = Rank;
        int init = GameConst.InitMoney;
        float ratio = rank switch { "S" => 6.2f, "A" => 3.6f, "B" => 2.4f, "C" => 1.3f, _ => 0.7f };
        int netWorth = Mathf.RoundToInt(init * ratio);
        int stock = Mathf.RoundToInt(netWorth * 0.18f);
        int final = netWorth - stock;

        // それらしい所持金推移（序盤は仕入れで凹み、配信日に跳ねる）
        var history = new List<int>();
        int turns = 30;
        var rng = new System.Random(7);
        for (int i = 0; i < turns; i++)
        {
            float t = (float)i / (turns - 1);
            float trend = Mathf.Lerp(init, final, t * t * 0.6f + t * 0.4f);
            float noise = (float)(rng.NextDouble() - 0.5) * init * 0.35f;
            if (i % 6 == 5) noise += init * 0.6f;
            if (i == 2 || i == 3) noise -= init * 0.4f;
            history.Add(Mathf.Max(0, Mathf.RoundToInt(trend + noise)));
        }
        history[turns - 1] = final;

        return new ResultStatisticsData
        {
            FinalMoney = final,
            InitialMoney = init,
            MoneyDifference = final - init,
            TotalTurns = turns,
            BlacksmithLevel = 4,
            InfoBrokerLevel = 3,
            TotalRemainingStock = 12,
            TotalStockValue = stock,
            NetWorth = netWorth,
            Rank = rank,
            Comment = ResultModel.GetRankComment(rank),
            ItemSummaries = new List<ResultItemSummary>(),
            MoneyHistory = history,
            StreamWins = 4,
            StreamLosses = 1,
            BuzzCount = 3,
            BestItemName = "鋼のロングソード",
            BestItemRevenue = Mathf.RoundToInt(netWorth * 0.32f),
            BestItemIcon = null,
        };
    }
}
#endif

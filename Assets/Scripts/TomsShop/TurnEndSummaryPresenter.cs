using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// ターン終了サマリーのPresenter。
/// ターン終了時に売上情報・トレンド・次配信までの日数を集計してViewに表示し、
/// 確認ボタンで実際にNextTurnを実行する。
/// </summary>
public class TurnEndSummaryPresenter : IStartable, IDisposable
{
    private readonly TurnEndSummaryView view;
    private readonly ItemModel itemModel;
    private readonly GameFlowManager gameFlowManager;
    private readonly TomsModel tomsModel;
    private readonly StateManager stateManager;
    private readonly MarketingFacade marketingFacade;
    private readonly SellOrderModel sellOrderModel;
    private readonly ShopEconomySettings economySettings;
    private readonly RelicEffectResolver relicResolver;
    private readonly CompositeDisposable disposables = new();

    // フェーズ再入（ForceNotify等）による販売処理の二重実行を防ぐガード
    private int lastProcessedTurn = -1;

    public TurnEndSummaryPresenter(
        TurnEndSummaryView view,
        ItemModel itemModel,
        GameFlowManager gameFlowManager,
        TomsModel tomsModel,
        StateManager stateManager,
        MarketingFacade marketingFacade,
        SellOrderModel sellOrderModel,
        ShopEconomySettings economySettings,
        RelicEffectResolver relicResolver)
    {
        this.view = view;
        this.itemModel = itemModel;
        this.gameFlowManager = gameFlowManager;
        this.tomsModel = tomsModel;
        this.stateManager = stateManager;
        this.marketingFacade = marketingFacade;
        this.sellOrderModel = sellOrderModel;
        this.economySettings = economySettings;
        this.relicResolver = relicResolver;

        stateManager.RegisterOnEnter(TomsShopGamePhase.TurnEndSummary, Entry);
    }

    public void Start()
    {
        // 確認ボタンが押されたらNextTurnを実行
        // （NextTurn内でChangeTomsShopPhaseが呼ばれ、Shop画面のEntry()が発火する）
        view.OnConfirmClicked
            .Subscribe(_ => gameFlowManager.NextTurn())
            .AddTo(disposables);
    }
    
    /// <summary>
    /// TurnEndSummaryフェーズに入った時に呼ばれる。
    /// 【売り注文制】①昨日以前の売り注文を約定して入金 → ②今日の陳列分を全量「売り注文」化
    /// （入金は明日）→ ③サマリー表示。
    /// </summary>
    public void Entry()
    {
        int currentTurn = gameFlowManager.CurrentTurn.Value;

        // フェーズ再入による販売処理・入金の二重実行を防ぐ
        if (lastProcessedTurn == currentTurn)
        {
            Debug.Log($"[TurnEndSummary] Turn {currentTurn} は処理済みのためスキップ（再入）");
            return;
        }
        lastProcessedTurn = currentTurn;

        bool probabilistic = economySettings != null && economySettings.useProbabilisticShopSales;
        int delayTurns = economySettings != null ? economySettings.sellOrderDelayTurns : 1;

        // 1. 約定日を迎えた売り注文を精算して入金（昨日陳列した分が今日売れた）
        var settlement = sellOrderModel.SettleDue(currentTurn, itemModel, economySettings, marketingFacade, relicResolver);
        if (settlement.TotalIncome > 0)
        {
            tomsModel.AddRevenue(settlement.TotalIncome);
            Debug.Log($"[TurnEndSummary] 売り注文の約定入金: +{settlement.TotalIncome}G → {tomsModel.PlayerMoney.Value}G");
        }

        // 2. 需要対応力：全アイテム中で需要が高い品がいくつ品出しされているか
        int trendUpTotal = 0;
        int trendUpDisplayed = 0;
        foreach (var r in itemModel.RuntimeItems)
        {
            if (r.Demand.Value >= 0.7f)
            {
                trendUpTotal++;
                if (r.IsDisplay.Value) trendUpDisplayed++;
            }
        }

        // 3. 今日の陳列分を販売処理（売り注文制では陳列数の全量が売れ、Stock が引き当てられる）
        var salesResult = itemModel.SimulateShopSales(probabilistic);

        // 4. 販売結果からサマリーを構築し、売り注文を作成（入金は約定日）
        var items = BuildSummaryItems(salesResult);
        int pendingEstimate = 0;
        int totalSoldCount = 0;

        foreach (var item in items)
        {
            pendingEstimate += item.Revenue;
            totalSoldCount += item.SoldCount;
        }

        foreach (var kv in salesResult)
        {
            var runtime = itemModel.GetRuntimeItem(kv.Key);
            if (runtime != null && kv.Value > 0)
                sellOrderModel.Place(runtime, kv.Value, currentTurn, delayTurns);
        }

        // 5. 在庫・注文・所持金を同時に保存（引き当て済み在庫と注文の整合を保つ）
        itemModel.SaveData();
        sellOrderModel.SaveData();
        tomsModel.SavePlayerMoney();

        if (totalSoldCount > 0 || settlement.TotalIncome > 0)
            SoundManager.Instance?.PlaySE("営業/SE_売上音");

        int daysUntilBattle = gameFlowManager.GetTurnsUntilNextBattle();

        Debug.Log($"[TurnEndSummary] 本日の入金: {settlement.TotalIncome}G / 売り注文（明日入金見込み）: {pendingEstimate}G, 売れた数: {totalSoldCount}, 次の配信まで: {daysUntilBattle}ターン");

        // 6. ターン評価を計算して表示
        var (grade, comment) = CalculateGrade(pendingEstimate, trendUpDisplayed, trendUpTotal, settlement);
        view.ShowGrade(grade, comment);

        view.UpdateContent(items, pendingEstimate, totalSoldCount, daysUntilBattle);
        view.UpdateSettlementInfo(settlement.TotalIncome, pendingEstimate);
    }

    /// <summary>
    /// 3軸（資金効率・需要対応力・バズ活用度）の加重平均でターン評価を算出する。
    /// 資金効率40% + 需要対応力35% + バズ活用度25%
    /// （売り注文制では陳列分が必ず全量売れるため、旧「売り切れ率」は資金効率に差し替え）
    /// </summary>
    private (TurnGrade grade, string comment) CalculateGrade(
        int pendingEstimate,
        int trendUpDisplayed, int trendUpTotal,
        SellSettlementResult settlement)
    {
        // 資金効率 (0~1): 今日仕入れに投じた額に対して、どれだけ売り注文（見込み額）に変えられたか
        int spend = tomsModel.TurnProcurementSpend;
        float capitalEfficiency = spend > 0
            ? UnityEngine.Mathf.Clamp01((float)pendingEstimate / spend)
            : (pendingEstimate > 0 ? 1f : 0f);

        // 需要対応力 (0~1): 需要が高いアイテム(Demand>=0.7)を何品品出しできていたか
        // 高需要品がないターンは満点扱い
        float demandResponse = trendUpTotal > 0
            ? UnityEngine.Mathf.Clamp01((float)trendUpDisplayed / trendUpTotal)
            : 1f;

        // バズ活用度 (0~1): 本日の約定入金に適用されたマーケティング倍率（2倍で満点）
        float buzzMultiplier = settlement != null && settlement.BaseIncome > 0
            ? (float)settlement.TotalIncome / settlement.BaseIncome
            : (marketingFacade != null ? marketingFacade.CalculateFinalRevenue(1000) / 1000f : 1f);
        float buzzScore = UnityEngine.Mathf.Clamp01((buzzMultiplier - 1f) / 1f);

        float score = capitalEfficiency * 0.4f + demandResponse * 0.35f + buzzScore * 0.25f;

        TurnGrade grade = score >= 0.85f ? TurnGrade.S
                        : score >= 0.70f ? TurnGrade.A
                        : score >= 0.55f ? TurnGrade.B
                        : score >= 0.40f ? TurnGrade.C
                        : score >= 0.25f ? TurnGrade.D
                        : TurnGrade.E;

        string comment = grade switch
        {
            TurnGrade.S => "在庫・市場・広告、全てが完璧に噛み合った日だ！",
            TurnGrade.A => "安定した経営センスを見せてくれた。",
            TurnGrade.B => "悪くはないが、まだ伸ばせる余地がある。",
            TurnGrade.C => "どこかで判断が一歩遅れた日だった。",
            TurnGrade.D => "今日は流れを掴めなかった。",
            _           => "明日こそ巻き返そう！",
        };

        Debug.Log($"[TurnGrade] CapitalEfficiency={capitalEfficiency:P0} DemandResponse={demandResponse:P0} Buzz={buzzScore:P0} → Score={score:F2} → {grade}");

        return (grade, comment);
    }

    /// <summary>
    /// サマリーパネルを表示する。
    /// TomsShopPresenter から営業フェーズの「営業開始」演出完了時に呼ばれる。
    /// </summary>
    public void ShowSummary()
    {
        stateManager.ChangeTomsShopPhase(TomsShopGamePhase.TurnEndSummary);
    }

    /// <summary>
    /// 品出し中のアイテムから売上データを集計する。
    /// SimulateShopSales の結果（itemId → soldCount）を使って正確な販売数を取得する。
    /// </summary>
    private List<TurnEndSummaryItemData> BuildSummaryItems(Dictionary<string, int> salesResult)
    {
        var result = new List<TurnEndSummaryItemData>();

        foreach (var runtime in itemModel.RuntimeItems)
        {
            // 販売結果に載っているアイテム、または品出し中で売れ残ったアイテムを対象にする。
            // ※ 全量売却で在庫が尽きると DisplayStock が 0 にクランプされるため、
            //    陳列状態だけで絞ると「売れたのにサマリーに出ない」ことになる。
            bool sold = salesResult.ContainsKey(runtime.ItemId);
            bool stillDisplayed = runtime.IsDisplay.Value && runtime.DisplayStock.Value > 0;
            if (!sold && !stillDisplayed)
                continue;

            // シミュレーション結果から販売数を取得
            int soldCount = salesResult.TryGetValue(runtime.ItemId, out var count) ? count : 0;
            int revenue = soldCount * runtime.CurrentPrice.Value;

            // 需要トレンドの判定
            DemandTrend trend;
            float demand = runtime.Demand.Value;
            if (demand >= 0.7f)
                trend = DemandTrend.Up;
            else if (demand <= 0.3f)
                trend = DemandTrend.Down;
            else
                trend = DemandTrend.Flat;

            // 需要変動の主因を判定
            float demandDelta = runtime.Demand.Value - runtime.PreviousDemand;
            DemandChangeCause cause;
            if (runtime.Trend > 0.4f && demandDelta > 0.02f)
                cause = DemandChangeCause.TrendUp;
            else if (demandDelta > 0.02f)
                cause = DemandChangeCause.Displayed;
            else if (demandDelta < -0.03f)
                cause = DemandChangeCause.TrendDown;
            else
                cause = DemandChangeCause.Stable;

            result.Add(new TurnEndSummaryItemData
            {
                ItemName = runtime.ItemName,
                SoldCount = soldCount,
                Revenue = revenue,
                Price = runtime.CurrentPrice.Value,
                Demand = demand,
                Trend = trend,
                ItemIcon = runtime.ItemIcon,
                Cause = cause,
                DemandDelta = demandDelta
            });
        }

        return result;
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}


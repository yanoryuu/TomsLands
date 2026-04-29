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
    private readonly CompositeDisposable disposables = new();

    public TurnEndSummaryPresenter(
        TurnEndSummaryView view,
        ItemModel itemModel,
        GameFlowManager gameFlowManager,
        TomsModel tomsModel,
        StateManager stateManager,
        MarketingFacade marketingFacade)
    {
        this.view = view;
        this.itemModel = itemModel;
        this.gameFlowManager = gameFlowManager;
        this.tomsModel = tomsModel;
        this.stateManager = stateManager;
        this.marketingFacade = marketingFacade;
        
        stateManager.RegisterOnEnter(TomsShopGamePhase.TurnEndSummary, Entry);
    }

    public void Start()
    {
        // 確認ボタンが押されたらNextTurnを実行
        // （NextTurn内でChangeTomsShopPhaseが呼ばれ、Shop画面のEntry()が発火する）
        view.OnConfirmClicked
            .Subscribe(_ =>
            {
                gameFlowManager.NextTurn();
            })
            .AddTo(disposables);
    }
    
    /// <summary>
    /// TurnEndSummaryフェーズに入った時に呼ばれる
    /// </summary>
    public void Entry()
    {
        // 1. 通常営業の販売シミュレーションを実行（Stock が減少する）
        var salesResult = itemModel.SimulateShopSales();

        // 2. 販売結果からサマリーを構築
        var items = BuildSummaryItems(salesResult);
        int baseRevenue = 0;
        int totalSoldCount = 0;

        foreach (var item in items)
        {
            baseRevenue += item.Revenue;
            totalSoldCount += item.SoldCount;
        }

        // 3. マーケティングボーナスを適用した最終売上を計算
        //    バズ倍率 × フォロワーボーナスを乗算
        int totalRevenue = baseRevenue;
        if (marketingFacade != null)
        {
            totalRevenue = marketingFacade.CalculateFinalRevenue(baseRevenue);
            if (totalRevenue != baseRevenue)
            {
                var breakdown = marketingFacade.GetSalesBreakdown(baseRevenue);
                Debug.Log($"[TurnEndSummary] マーケティングボーナス適用: {breakdown}");
            }
        }

        // 4. 売上を所持金に加算
        if (totalRevenue > 0)
        {
            tomsModel.PlayerMoney.Value += totalRevenue;
            Debug.Log($"[TurnEndSummary] 所持金に {totalRevenue}G を加算 → {tomsModel.PlayerMoney.Value}G");
        }

        int daysUntilBattle = gameFlowManager.GetTurnsUntilNextBattle();

        Debug.Log($"[TurnEndSummary] 売上合計: {totalRevenue}G（基本: {baseRevenue}G）, 売れた数: {totalSoldCount}, 次の配信まで: {daysUntilBattle}ターン");

        view.UpdateContent(items, totalRevenue, totalSoldCount, daysUntilBattle);
    }

    /// <summary>
    /// サマリーパネルを表示する。
    /// TomsShopPresenter から NextTurnButton クリック時に呼ばれる。
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
            // 品出し中のアイテムのみ対象
            if (!runtime.IsDisplay.Value || runtime.DisplayStock.Value <= 0)
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


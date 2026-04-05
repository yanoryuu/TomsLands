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
    private readonly CompositeDisposable disposables = new();

    public TurnEndSummaryPresenter(
        TurnEndSummaryView view,
        ItemModel itemModel,
        GameFlowManager gameFlowManager,
        TomsModel tomsModel,
        StateManager stateManager)
    {
        this.view = view;
        this.itemModel = itemModel;
        this.gameFlowManager = gameFlowManager;
        this.tomsModel = tomsModel;
        this.stateManager = stateManager;
        
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
        var items = BuildSummaryItems();
        int totalRevenue = 0;
        int totalSoldCount = 0;

        foreach (var item in items)
        {
            totalRevenue += item.Revenue;
            totalSoldCount += item.SoldCount;
        }

        int daysUntilBattle = gameFlowManager.GetTurnsUntilNextBattle();

        Debug.Log($"[TurnEndSummary] 売上合計: {totalRevenue}G, 売れた数: {totalSoldCount}, 次の配信まで: {daysUntilBattle}ターン");

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
    /// 品出し中のアイテムから売上データを集計する
    /// </summary>
    private List<TurnEndSummaryItemData> BuildSummaryItems()
    {
        var result = new List<TurnEndSummaryItemData>();

        foreach (var runtime in itemModel.RuntimeItems)
        {
            // 品出し中のアイテムのみ対象
            if (!runtime.IsDisplay.Value || runtime.DisplayStock.Value <= 0)
                continue;

            // 販売数量 = DisplayStock - Stock（DisplayStockが元の出品数、Stockが現在の残り）
            int soldCount = Mathf.Max(0, runtime.DisplayStock.Value - runtime.Stock.Value);
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

            result.Add(new TurnEndSummaryItemData
            {
                ItemName = runtime.ItemName,
                SoldCount = soldCount,
                Revenue = revenue,
                Price = runtime.CurrentPrice.Value,
                Demand = demand,
                Trend = trend
            });
        }

        return result;
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}


using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;
using VContainer.Unity;

public class ProphetPresenter : IDisposable, IStartable
{
    private readonly ProphetView prophetView;
    private readonly ItemModel itemModel;
    private readonly GameFlowManager gameFlowManager;
    private readonly DungeonRepository dungeonRepository;
    private readonly StateManager stateManager;
    private readonly CompositeDisposable disposables = new();

    public ProphetPresenter(
        ProphetView prophetView,
        ItemModel itemModel,
        GameFlowManager gameFlowManager,
        DungeonRepository dungeonRepository,
        StateManager stateManager)
    {
        this.prophetView = prophetView;
        this.itemModel = itemModel;
        this.gameFlowManager = gameFlowManager;
        this.dungeonRepository = dungeonRepository;
        this.stateManager = stateManager;

        stateManager.RegisterOnEnter(TomsShopGamePhase.Prophet, Entry);
    }

    public void Start()
    {
        prophetView.OnCloseClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposables);
    }

    private void Entry()
    {
        ShowTrend();
        ShowPriceRanking();
        ShowDungeonInfo();
    }

    // トレンド上位5件をTrend値降順で表示
    private void ShowTrend()
    {
        var rows = itemModel.RuntimeItems
            .OrderByDescending(r => r.Trend)
            .Take(5)
            .Select(r => (r.ItemIcon, r.ItemName, r.Trend, r.Demand.Value))
            .ToList();
        prophetView.ShowTrendRows(rows);
    }

    // 現在価格の高い順に上位5件を表示
    private void ShowPriceRanking()
    {
        var sorted = itemModel.RuntimeItems
            .OrderByDescending(r => r.CurrentPrice.Value)
            .Take(5)
            .ToList();

        var rows = new List<(int, Sprite, string, int)>();
        for (int i = 0; i < sorted.Count; i++)
        {
            var r = sorted[i];
            rows.Add((i + 1, r.ItemIcon, r.ItemName, r.CurrentPrice.Value));
        }
        prophetView.ShowRankingRows(rows);
    }

    // 次のダンジョン情報とおすすめ武器を表示
    private void ShowDungeonInfo()
    {
        int turnsUntil = gameFlowManager.GetTurnsUntilNextBattle();
        var nextKey = gameFlowManager.GetNextBattleDungeon();

        if (nextKey == null)
        {
            prophetView.ShowDungeonInfo("—", "—", 0, -1, null);
            prophetView.ShowRecommendedRows(new List<(Sprite, string, float, float)>());
            return;
        }

        var dungeon = dungeonRepository.GetById(nextKey.Value);
        if (dungeon == null)
        {
            prophetView.ShowDungeonInfo("不明", "—", 0, turnsUntil, null);
            prophetView.ShowRecommendedRows(new List<(Sprite, string, float, float)>());
            return;
        }

        string attributeName = AttributeToJapanese(dungeon.requiredAttribute);
        prophetView.ShowDungeonInfo(dungeon.dungeonName, attributeName, dungeon.difficulty, turnsUntil, dungeon.dungeonIcon);

        // 同属性アイテムをTrend+Demand合計で降順→上位3件
        var recommended = itemModel.RuntimeItems
            .Where(r => r.ItemAttribute == dungeon.requiredAttribute)
            .OrderByDescending(r => r.Trend + r.Demand.Value)
            .Take(3)
            .Select(r => (r.ItemIcon, r.ItemName, r.Trend, r.Demand.Value))
            .ToList();
        prophetView.ShowRecommendedRows(recommended);
    }

    private static string AttributeToJapanese(ItemTypeData.ItemAttribute attr) => attr switch
    {
        ItemTypeData.ItemAttribute.Fire  => "火",
        ItemTypeData.ItemAttribute.Water => "水",
        ItemTypeData.ItemAttribute.Earth => "土",
        ItemTypeData.ItemAttribute.Wind  => "風",
        ItemTypeData.ItemAttribute.Light => "光",
        ItemTypeData.ItemAttribute.Dark  => "闇",
        _ => attr.ToString()
    };

    public void Dispose()
    {
        disposables.Dispose();
    }
}

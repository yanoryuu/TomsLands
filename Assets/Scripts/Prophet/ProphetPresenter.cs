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
    private readonly TomsModel tomsModel;
    private readonly DungeonIntelModel dungeonIntel;
    private readonly CompositeDisposable disposables = new();
    private string currentDialogue = string.Empty;
    private int characterTalkIndex;

    public ProphetPresenter(
        ProphetView prophetView,
        ItemModel itemModel,
        GameFlowManager gameFlowManager,
        DungeonRepository dungeonRepository,
        StateManager stateManager,
        TomsModel tomsModel,
        DungeonIntelModel dungeonIntel)
    {
        this.prophetView = prophetView;
        this.itemModel = itemModel;
        this.gameFlowManager = gameFlowManager;
        this.dungeonRepository = dungeonRepository;
        this.stateManager = stateManager;
        this.tomsModel = tomsModel;
        this.dungeonIntel = dungeonIntel;

        stateManager.RegisterOnEnter(TomsShopGamePhase.Prophet, Entry);
    }

    public void Start()
    {
        prophetView.OnCloseClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposables);

        prophetView.OnCharacterClicked
            .Subscribe(_ => ShowCharacterTalk())
            .AddTo(disposables);

        prophetView.OnRowHoverEnter
            .Subscribe(id => prophetView.ShowDialogue(ProphetDialogueLoader.Get(id)))
            .AddTo(disposables);

        prophetView.OnRowHoverExit
            .Subscribe(_ => prophetView.ShowDialogue(currentDialogue))
            .AddTo(disposables);
    }

    private void Entry()
    {
        characterTalkIndex = 0;
        currentDialogue = ProphetDialogueLoader.GetDefault();
        prophetView.ShowDialogue(currentDialogue);
        ShowMarketHeat();
        ShowPriceRanking();
        ShowDungeonInfo();
    }

    private void ShowCharacterTalk()
    {
        characterTalkIndex = (characterTalkIndex % 3) + 1;
        currentDialogue = ProphetDialogueLoader.Get($"character_talk_{characterTalkIndex}");
        prophetView.ShowDialogue(currentDialogue);
    }

    // 相場が荒れている銘柄の上位3件。
    // 以前は Trend（未来の需要が向かう先）の降順で並べていたが、それは
    // 「次に上がる銘柄」の答えを無料で配ることに等しかった（Docs/News_Spec.md §2 C5）。
    // Heat は価格履歴だけから決まる現在の情報なので、未来は漏れない。
    private void ShowMarketHeat()
    {
        int level = tomsModel.BlacksmithLevel.Value;
        var rows = itemModel.RuntimeItems
            .Where(r => r.RequiredLevel.Value <= level)
            .Select(r => (r, heat: MarketHeat.Compute(r.ShopPriceHistory)))
            .OrderByDescending(x => x.heat)
            .Take(3)
            .Select(x => (x.r.ItemIcon, x.r.ItemName, x.heat, x.r.Demand.Value))
            .ToList();
        prophetView.ShowTrendRows(rows);
    }

    // 現在価格の高い順に上位3件を表示
    private void ShowPriceRanking()
    {
        int level = tomsModel.BlacksmithLevel.Value;
        var sorted = itemModel.RuntimeItems
            .Where(r => r.RequiredLevel.Value <= level)
            .OrderByDescending(r => r.CurrentPrice.Value)
            .Take(3)
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

        // 弱点は「知っている」ときだけ開示する（Docs/News_Spec.md §2 C4）。
        bool known = dungeonIntel != null && dungeonIntel.IsWeaknessKnown(dungeon.key);
        string attributeName = known ? $"弱点:{AttributeToJapanese(dungeon.requiredAttribute)}" : "弱点:?";
        prophetView.ShowDungeonInfo(dungeon.dungeonName, attributeName, dungeon.difficulty, turnsUntil, dungeon.dungeonIcon);

        // おすすめは現在の期待収益（ExpectedRevenueOf）だけで決める。
        // 弱点を知らないうちは属性で絞り込まない。絞り込むと、弱点を伏せていても
        // 並んだ顔ぶれから属性が読めてしまい、C4 のゲートが意味を失うため。
        int level = tomsModel.BlacksmithLevel.Value;
        var recommended = itemModel.RuntimeItems
            .Where(r => r.RequiredLevel.Value <= level)
            .Where(r => !known || r.ItemAttribute == dungeon.requiredAttribute)
            .OrderByDescending(r => ItemModel.ExpectedRevenueOf(r))
            .Take(3)
            .Select(r => (r.ItemIcon, r.ItemName, MarketHeat.Compute(r.ShopPriceHistory), r.Demand.Value))
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

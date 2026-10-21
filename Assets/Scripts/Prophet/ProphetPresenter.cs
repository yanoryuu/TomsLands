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
    private readonly NewsEffectResolver newsEffects;
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
        DungeonIntelModel dungeonIntel,
        NewsEffectResolver newsEffects)
    {
        this.newsEffects = newsEffects;
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

        prophetView.OnFortuneClicked
            .Subscribe(_ => BuyFortune())
            .AddTo(disposables);

        prophetView.OnRowHoverEnter
            .Subscribe(id =>
            {
                // 台詞の無い行は今の台詞のまま
                var line = string.IsNullOrEmpty(id) ? null : ProphetDialogueLoader.Get(id);
                if (!string.IsNullOrEmpty(line)) prophetView.ShowDialogue(line);
            })
            .AddTo(disposables);

        prophetView.OnRowHoverExit
            .Subscribe(_ => prophetView.ShowDialogue(currentDialogue))
            .AddTo(disposables);
    }

    private void Entry()
    {
        characterTalkIndex = 0;
        // 今日もう占っていれば結果を、まだなら荒れている品と「占う」ボタンを出す（I-11）
        bool bought = ProphetFortune.IsBought(tomsModel.FlowSeed, Turn);
        currentDialogue = Line(bought ? "fortune_after" : "fortune_before");
        prophetView.ShowDialogue(currentDialogue);
        if (bought) ShowFortune(false);
        else ShowMarketHeat();
        ShowPriceRanking();
        ShowDungeonInfo();
    }

    private void ShowCharacterTalk()
    {
        characterTalkIndex = (characterTalkIndex % 3) + 1;
        currentDialogue = ProphetDialogueLoader.Get($"character_talk_{characterTalkIndex}");
        prophetView.ShowDialogue(currentDialogue);
    }

    /// <summary>台詞 CSV の1行。{days}（占う先の日数）と {cost}（占いの値段）を埋める。行が無ければ default。</summary>
    private static string Line(string id)
    {
        var s = ProphetDialogueLoader.Get(id);
        if (string.IsNullOrEmpty(s)) s = ProphetDialogueLoader.GetDefault();
        return s.Replace("{days}", ProphetFortune.HorizonDays.ToString()).Replace("{cost}", ProphetFortune.CurrentCost.ToString("N0"));
    }

    private int Turn => gameFlowManager != null ? gameFlowManager.CurrentTurn.Value : tomsModel.CurrentTurn.Value;

    // =====================================================================
    // 占い（I-11・Q20）: 3日先の相場の向きを 15,000G（酒場 Lv4 で 10,000G）で占う。外れることもある（ProphetFortune.Accuracy）
    // =====================================================================

    private void BuyFortune()
    {
        int seed = tomsModel.FlowSeed, turn = Turn;
        if (ProphetFortune.IsBought(seed, turn)) { ShowFortune(false); return; }
        if (tomsModel.PlayerMoney.Value < ProphetFortune.CurrentCost)
        {
            currentDialogue = Line("fortune_short");
            prophetView.ShowDialogue(currentDialogue);
            return;
        }
        tomsModel.PurchaseItem(ProphetFortune.CurrentCost);
        tomsModel.SavePlayerMoney();
        ProphetFortune.MarkBought(seed, turn);
        var sound = SoundManager.Instance;
        if (sound != null) sound.PlaySE("営業/SE_仕入れ完了");
        ShowFortune(true);
    }

    private void ShowFortune(bool justBought)
    {
        var (ups, downs) = ProphetFortune.Read(itemModel.RuntimeItems, newsEffects, Turn, tomsModel.FlowSeed, tomsModel.BlacksmithLevel.Value);
        var rows = new List<(Sprite, string, int, bool)>();
        foreach (var r in ups) rows.Add((r.item.ItemIcon, r.item.ItemName, 1, r.strength >= NewsTuning.TrendMedium));
        foreach (var r in downs)
        {
            if (rows.Count >= ProphetFortune.MaxRows + 1) break;
            rows.Add((r.item.ItemIcon, r.item.ItemName, -1, r.strength >= NewsTuning.TrendMedium));
        }
        prophetView.SetTrendHeader("占いの結果");
        prophetView.ShowFortuneRows(rows);
        prophetView.ShowFortuneButton(false, false, string.Empty);
        if (justBought)
        {
            currentDialogue = Line(rows.Count > 0 ? "fortune_result" : "fortune_none");
            prophetView.ShowDialogue(currentDialogue);
        }
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
        prophetView.SetTrendHeader("荒れ相場");
        prophetView.ShowTrendRows(rows);
        prophetView.ShowFortuneButton(true, tomsModel.PlayerMoney.Value >= ProphetFortune.CurrentCost, $"{ProphetFortune.CurrentCost:N0}Gで占う");
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
        // 表示用の日数（今日 0・明日 1）と行き先は、地図・鍛冶屋と同じ取り方
        int turnsUntil = gameFlowManager.GetDaysUntilNextStream();
        var nextKey = gameFlowManager.GetUpcomingStreamDungeon();

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
        string attributeName = ItemAttributeText.Weakness(dungeon.requiredAttribute, known);
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

    public void Dispose() => disposables.Dispose();
}

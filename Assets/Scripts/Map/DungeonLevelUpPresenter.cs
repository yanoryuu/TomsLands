using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;
using VContainer.Unity;

public class DungeonLevelUpPresenter : IPresenter, IStartable, IDisposable
{
    private readonly DungeonLevelUpView  view;
    private readonly DungeonRepository   dungeonRepository;
    private readonly TomsModel           tomsModel;
    private readonly StateManager        stateManager;
    private readonly HeroModel           heroModel;
    private readonly ItemModel           itemModel;
    private readonly RelicEffectResolver relicResolver;
    private readonly GameFlowManager     gameFlowManager;

    private readonly CompositeDisposable disposables     = new();
    private          CompositeDisposable slotDisposables = new();

    private DungeonName? selectedKey;
    private int characterTalkIndex;

    public DungeonLevelUpPresenter(
        DungeonLevelUpView view,
        DungeonRepository  dungeonRepository,
        TomsModel          tomsModel,
        StateManager       stateManager,
        HeroModel          heroModel,
        ItemModel          itemModel,
        RelicEffectResolver relicResolver,
        GameFlowManager    gameFlowManager)
    {
        this.relicResolver = relicResolver;
        this.gameFlowManager = gameFlowManager;
        this.view              = view;
        this.dungeonRepository = dungeonRepository;
        this.tomsModel         = tomsModel;
        this.stateManager      = stateManager;
        this.heroModel         = heroModel;
        this.itemModel         = itemModel;

        stateManager.RegisterOnEnter(TomsShopGamePhase.DungeonLevelUp, Entry);
    }

    public void Start() => Bind();

    public void Entry()
    {
        selectedKey = null;
        characterTalkIndex = 0;
        view.ShowDialogue(DungeonLevelUpDialogueLoader.Get("open"));
        view.ClearDungeonDetail();
        RefreshList();

        // 先頭（＝次の配信先）を自動選択して、空の詳細パネルを見せない
        // （会話は開店挨拶を維持したいので HandleSlotSelected は使わない）
        var first = OrderedDungeons().FirstOrDefault();
        if (first != null)
        {
            selectedKey = first.key;
            view.ShowDungeonDetail(BuildDetailData(first));
        }
    }

    private void Bind()
    {
        view.OnCloseRequested
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposables);

        view.OnCharacterClicked
            .Subscribe(_ => ShowCharacterTalk())
            .AddTo(disposables);
    }

    private void ShowCharacterTalk()
    {
        characterTalkIndex = (characterTalkIndex % 3) + 1;
        view.ShowDialogue(DungeonLevelUpDialogueLoader.GetCharacterTalk(characterTalkIndex));
    }

    private void RefreshList()
    {
        slotDisposables.Dispose();
        slotDisposables = new CompositeDisposable();

        var dungeons = OrderedDungeons();
        var slotData = dungeons.Select(BuildSlotData).ToList();
        var slots    = view.PopulateDungeonList(slotData);

        foreach (var slot in slots)
        {
            slot.OnSlotSelected
                .Subscribe(key => HandleSlotSelected(key))
                .AddTo(slotDisposables);

            slot.OnLevelUpClicked
                .Subscribe(key => HandleLevelUp(key))
                .AddTo(slotDisposables);

            tomsModel.PlayerMoney
                .Subscribe(money =>
                {
                    var data = dungeonRepository.GetById(slot.DungeonKey);
                    if (data == null) return;

                    bool isMax    = data.currentDungeonLevel >= GameConst.MaxDungeonLevel;
                    int  cost     = isMax ? 0 : data.levelUpCost;
                    bool canAfford = !isMax && money >= cost;
                    slot.SetAffordable(canAfford);

                    // 選択中スロットなら詳細パネルも更新
                    if (selectedKey == slot.DungeonKey)
                        view.ShowDungeonDetail(BuildDetailData(data));
                })
                .AddTo(slotDisposables);
        }
    }

    private void HandleSlotSelected(DungeonName key)
    {
        selectedKey = key;
        var data = dungeonRepository.GetById(key);
        if (data == null) return;

        view.ShowDungeonDetail(BuildDetailData(data));
        view.ShowDialogue(data.isShowedInfo ? "情報入手済み" : "敵情報: 未調査");
    }

    private void HandleLevelUp(DungeonName key)
    {
        var data = dungeonRepository.GetById(key);
        if (data == null) return;

        if (data.currentDungeonLevel >= GameConst.MaxDungeonLevel)
        {
            view.ShowDialogue(DungeonLevelUpDialogueLoader.Get("max", data.dungeonName));
            return;
        }

        int cost = data.levelUpCost;
        if (tomsModel.PlayerMoney.Value < cost)
        {
            view.ShowDialogue(DungeonLevelUpDialogueLoader.Get("shortage", data.dungeonName, cost));
            return;
        }

        tomsModel.PurchaseItem(cost);
        tomsModel.SavePlayerMoney();
        data.currentDungeonLevel++;
        dungeonRepository.Save();

        view.ShowDialogue(DungeonLevelUpDialogueLoader.Get("success", data.dungeonName, data.currentDungeonLevel));

        RefreshList();

        // リスト再生成後も選択中ダンジョンの詳細を表示し続ける
        if (selectedKey.HasValue)
            HandleSlotSelected(selectedKey.Value);
    }

    // ─────────────────────────────────────────────────────────

    /// <summary>次の配信先（地図の NEXT と同じ取り方）。無ければ null。</summary>
    private DungeonName? UpcomingKey => gameFlowManager != null ? gameFlowManager.GetUpcomingStreamDungeon() : null;

    /// <summary>一覧の並び。次の配信先を先頭に置き、残りはデータの順のまま。</summary>
    private List<DungeonData> OrderedDungeons()
    {
        var dungeons = new List<DungeonData>(dungeonRepository.availableDungeons.Where(d => d != null));
        var next = UpcomingKey;
        if (!next.HasValue) return dungeons;

        int index = dungeons.FindIndex(d => d.key == next.Value);
        if (index > 0)
        {
            var item = dungeons[index];
            dungeons.RemoveAt(index);
            dungeons.Insert(0, item);
        }
        return dungeons;
    }

    private DungeonLevelUpSlotData BuildSlotData(DungeonData data)
    {
        bool isMax       = data.currentDungeonLevel >= GameConst.MaxDungeonLevel;
        int  currentLevel = data.currentDungeonLevel;
        int  nextLevel   = isMax ? currentLevel : Mathf.Min(currentLevel + 1, GameConst.MaxDungeonLevel);
        int  cost        = isMax ? 0 : data.levelUpCost;
        int  shortage    = Mathf.Max(0, cost - tomsModel.PlayerMoney.Value);

        return new DungeonLevelUpSlotData
        {
            DungeonKey   = data.key,
            DungeonName  = data.dungeonName,
            Icon         = data.dungeonIcon,
            CurrentLevel = currentLevel,
            NextLevel    = nextLevel,
            Cost         = cost,
            Shortage     = shortage,
            CanAfford    = !isMax && tomsModel.PlayerMoney.Value >= cost,
            IsMaxLevel   = isMax,
            IsNext       = UpcomingKey == data.key,
        };
    }

    private DungeonLevelUpDetailData BuildDetailData(DungeonData data)
    {
        bool isMax        = data.currentDungeonLevel >= GameConst.MaxDungeonLevel;
        int  currentLevel = data.currentDungeonLevel;
        int  nextLevel    = isMax ? currentLevel : Mathf.Min(currentLevel + 1, GameConst.MaxDungeonLevel);

        var currentMonsters = data.isShowedInfo
            ? data.GetLevelData(currentLevel)?.monsters ?? new List<EnemyData>()
            : new List<EnemyData>();

        var nextMonsters = data.isShowedInfo && !isMax
            ? data.GetLevelData(nextLevel)?.monsters ?? new List<EnemyData>()
            : new List<EnemyData>();

        return new DungeonLevelUpDetailData
        {
            DungeonName          = data.dungeonName,
            LevelText            = isMax ? $"Lv.{currentLevel}（MAX）" : $"Lv.{currentLevel} → Lv.{nextLevel}",
            RewardText           = BuildRewardText(data, currentLevel, nextLevel, isMax),
            ClearProbabilityText = BuildClearProbabilityText(data, currentLevel, nextLevel, isMax),
            CurrentMonsters      = currentMonsters,
            NextMonsters         = nextMonsters,
        };
    }

    /// <summary>
    /// 防衛報酬（勇者敗北時に受け取る rewardGold）の変化テキストを作る。
    /// </summary>
    private string BuildRewardText(DungeonData data, int currentLevel, int nextLevel, bool isMax)
    {
        if (!data.isShowedInfo) return "防衛報酬: ？？？";

        int currentReward = data.GetLevelData(currentLevel)?.rewardGold ?? 0;
        if (isMax) return $"防衛報酬 {currentReward:N0}G";

        int nextReward = data.GetLevelData(nextLevel)?.rewardGold ?? currentReward;
        return $"防衛報酬 {currentReward:N0}G → {nextReward:N0}G";
    }

    /// <summary>
    /// 勇者のクリア見込み（5段階）の変化テキストを作る。例: 「勇者のクリア見込み 互角 → 不利」。
    /// レベルアップで見込みが下がる＝防衛（敗北時報酬）を狙いやすくなることを示す。
    /// </summary>
    private string BuildClearProbabilityText(DungeonData data, int currentLevel, int nextLevel, bool isMax)
    {
        const string label = "勇者のクリア見込み";
        if (!data.isShowedInfo) return $"{label} ？";

        float currentProb = ClearProbabilityCalculator.Calculate(heroModel.heroData, itemModel, data, currentLevel, relicResolver);
        string current = ClearProbabilityCalculator.FormatOutlook(currentProb);
        if (isMax) return $"{label} {current}";

        float nextProb = ClearProbabilityCalculator.Calculate(heroModel.heroData, itemModel, data, nextLevel, relicResolver);
        return $"{label} {current} → {ClearProbabilityCalculator.FormatOutlook(nextProb)}";
    }

    public void Dispose()
    {
        slotDisposables.Dispose();
        disposables.Dispose();
    }
}

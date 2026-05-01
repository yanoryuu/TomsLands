using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;
using VContainer.Unity;

public class HeroPanelPresenter : IDisposable, IPresenter, IStartable
{
    private readonly HeroPanelView view;
    private readonly HeroModel heroModel;
    private readonly ItemModel itemModel;
    private readonly TomsModel tomsModel;
    private readonly StateManager stateManager;
    private readonly BattleInputData battleInputData;
    private readonly DungeonRepository dungeonRepository;
    private readonly CompositeDisposable disposables = new();

    /// <summary>現在表示中のセリフ。ホバー終了時に復元するために保持する。</summary>
    private string currentDialogue = string.Empty;
    private int characterTalkIndex;

    public HeroPanelPresenter(
        HeroPanelView view,
        HeroModel heroModel,
        ItemModel itemModel,
        TomsModel tomsModel,
        StateManager stateManager,
        BattleInputData battleInputData,
        DungeonRepository dungeonRepository)
    {
        this.view = view;
        this.heroModel = heroModel;
        this.itemModel = itemModel;
        this.tomsModel = tomsModel;
        this.stateManager = stateManager;
        this.battleInputData = battleInputData;
        this.dungeonRepository = dungeonRepository;

        stateManager.RegisterOnEnter(TomsShopGamePhase.Hero, Entry);
    }

    public void Start()
    {
        Bind();
    }

    public void Entry()
    {
        characterTalkIndex = 0;
        view.SetStaticHoverDescriptions(
            HeroDialogueLoader.Get("hover_weapon"),
            HeroDialogueLoader.Get("hover_armor"),
            HeroDialogueLoader.Get("hover_clear_probability")
        );
        Refresh(HeroDialogueLoader.Get("open"));
    }

    private void Bind()
    {
        view.OnCloseRequested
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposables);

        view.OnWeaponChanged
            .Subscribe(SetWeapon)
            .AddTo(disposables);

        view.OnArmorChanged
            .Subscribe(SetArmor)
            .AddTo(disposables);

        view.OnTacticsChanged
            .Subscribe(SetTactics)
            .AddTo(disposables);

        view.OnCharacterClicked
            .Subscribe(_ => ShowCharacterTalk())
            .AddTo(disposables);

        // ホバー時に dialogueText へ説明を表示し、離れたら直前のセリフへ戻す
        view.OnHoverEnter
            .Subscribe(msg => view.SetDialogue(msg))
            .AddTo(disposables);

        view.OnHoverExit
            .Subscribe(_ => view.SetDialogue(currentDialogue))
            .AddTo(disposables);
    }

    private void ShowCharacterTalk()
    {
        characterTalkIndex = (characterTalkIndex % 3) + 1;
        currentDialogue = HeroDialogueLoader.Get($"character_talk_{characterTalkIndex}");
        view.SetDialogue(currentDialogue);
    }

    private void SetWeapon(string itemId)
    {
        if (!CanEquip(itemId, ItemTypeData.ItemType.Weapon)) return;

        var hero = heroModel.heroData;
        var item = itemModel.GetRuntimeItem(itemId);

        hero.weaponId.Value = itemId;
        hero.weaponName.Value = item != null ? item.ItemName : string.Empty;

        SaveEquipment();
        Refresh(HeroDialogueLoader.Get("weapon_changed"));
    }

    private void SetArmor(string itemId)
    {
        if (!CanEquip(itemId, ItemTypeData.ItemType.Armor)) return;

        var hero = heroModel.heroData;
        var item = itemModel.GetRuntimeItem(itemId);

        hero.armorId.Value = itemId;
        hero.armorName.Value = item != null ? item.ItemName : string.Empty;

        SaveEquipment();
        Refresh(HeroDialogueLoader.Get("armor_changed"));
    }

    private void SetTactics(HeroTactics tactics)
    {
        heroModel.heroData.tactics.Value = tactics;
        heroModel.SaveHeroData();
        Refresh(HeroDialogueLoader.Get("tactics_changed"));
    }

    private bool CanEquip(string itemId, ItemTypeData.ItemType expectedType)
    {
        if (string.IsNullOrEmpty(itemId)) return true;

        var item = itemModel.GetRuntimeItem(itemId);
        return item != null && item.ItemType == expectedType;
    }

    private void SaveEquipment()
    {
        heroModel.ClearEquippedItems();

        var hero = heroModel.heroData;
        if (!string.IsNullOrEmpty(hero.weaponId.Value))
        {
            heroModel.EquipItem(hero.weaponId.Value);
        }

        if (!string.IsNullOrEmpty(hero.armorId.Value))
        {
            heroModel.EquipItem(hero.armorId.Value);
        }

        battleInputData.EquippedItemIds = new List<string>(heroModel.EquippedItemIds);
        heroModel.SaveHeroData();
    }

    private void Refresh(string dialogue)
    {
        var hero = heroModel.heroData;
        if (hero == null) return;

        currentDialogue = dialogue;

        ResolveSavedEquipmentNames();

        var weapon = itemModel.GetRuntimeItem(hero.weaponId.Value);
        var armor = itemModel.GetRuntimeItem(hero.armorId.Value);

        view.SetHeroStatus(hero);
        view.SetWeaponOptions(BuildEquipmentOptions(ItemTypeData.ItemType.Weapon, hero.weaponId.Value), hero.weaponId.Value);
        view.SetArmorOptions(BuildEquipmentOptions(ItemTypeData.ItemType.Armor, hero.armorId.Value), hero.armorId.Value);
        view.SetTacticsOptions(hero.tactics.Value);
        view.SetTacticsHoverDescription(GetTacticsHoverDesc(hero.tactics.Value));
        view.SetEquipmentIcons(weapon?.ItemIcon, armor?.ItemIcon);
        view.SetDialogue(dialogue);
        UpdateClearProbability();
    }

    /// <summary>
    /// クリア確率を計算して View に反映する。
    /// ダンジョン情報が未購入の場合は非表示にする。
    /// </summary>
    private void UpdateClearProbability()
    {
        if (!TryGetPurchasedDungeon(out var dungeon))
        {
            view.SetClearProbability(0f, false);
            return;
        }

        var hero = heroModel.heroData;
        float probability = ClearProbabilityCalculator.Calculate(
            hero,
            itemModel,
            dungeon,
            battleInputData.DungeonLevel);

        view.SetClearProbability(probability, true);
    }

    /// <summary>
    /// 現在選択中のダンジョンの情報が購入済みであれば <paramref name="dungeonData"/> に返す。
    /// 未購入または未選択の場合は false を返す。
    /// </summary>
    private bool TryGetPurchasedDungeon(out DungeonData dungeonData)
    {
        dungeonData = null;
        if (dungeonRepository == null || battleInputData == null) return false;

        dungeonData = dungeonRepository.GetById(battleInputData.DungeonKey);
        if (dungeonData == null) return false;

        return dungeonData.isShowedInfo;
    }

    private void ResolveSavedEquipmentNames()
    {
        var hero = heroModel.heroData;
        SetSavedEquipmentName(hero.weaponId.Value, hero.weaponName, ItemTypeData.ItemType.Weapon);
        SetSavedEquipmentName(hero.armorId.Value, hero.armorName, ItemTypeData.ItemType.Armor);
    }

    private void SetSavedEquipmentName(string itemId, ReactiveProperty<string> targetName, ItemTypeData.ItemType type)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            targetName.Value = string.Empty;
            return;
        }

        var item = itemModel.GetRuntimeItem(itemId);
        if (item == null || item.ItemType != type)
        {
            targetName.Value = string.Empty;
            return;
        }

        targetName.Value = item.ItemName;
    }

    private IReadOnlyList<HeroEquipmentOption> BuildEquipmentOptions(ItemTypeData.ItemType type, string selectedItemId)
    {
        int blacksmithLevel = tomsModel.BlacksmithLevel.Value;
        var options = itemModel.RuntimeItems
            .Where(item => item.ItemType == type && item.RequiredLevel.Value <= blacksmithLevel)
            .OrderBy(item => item.RequiredLevel.Value)
            .ThenBy(item => item.CurrentPrice.Value)
            .Select(item => new HeroEquipmentOption(item.ItemId, BuildEquipmentLabel(item)))
            .ToList();

        if (!string.IsNullOrEmpty(selectedItemId) && options.All(option => option.ItemId != selectedItemId))
        {
            var selected = itemModel.GetRuntimeItem(selectedItemId);
            if (selected != null && selected.ItemType == type)
            {
                options.Insert(0, new HeroEquipmentOption(selected.ItemId, $"{selected.ItemName}（未所持）"));
            }
        }

        return options;
    }

    private static string BuildEquipmentLabel(RuntimeItemData item) => item.ItemName;

    private static string GetTacticsHoverDesc(HeroTactics tactics) => tactics switch
    {
        HeroTactics.Aggressive   => HeroDialogueLoader.Get("hover_tactics_aggressive"),
        HeroTactics.Defensive    => HeroDialogueLoader.Get("hover_tactics_defensive"),
        HeroTactics.Stealth      => HeroDialogueLoader.Get("hover_tactics_stealth"),
        HeroTactics.MagicFocused => HeroDialogueLoader.Get("hover_tactics_magic"),
        _                        => HeroDialogueLoader.Get("hover_tactics_balanced"),
    };

    public void Dispose()
    {
        disposables.Dispose();
    }
}

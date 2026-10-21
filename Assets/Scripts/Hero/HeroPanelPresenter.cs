using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using VContainer.Unity;

public class HeroPanelPresenter : IDisposable, IPresenter, IStartable
{
    private readonly HeroPanelView view;
    private readonly HeroModel heroModel;
    private readonly ItemModel itemModel;
    private readonly RelicEffectResolver relicResolver;
    private readonly TomsModel tomsModel;
    private readonly StateManager stateManager;
    private readonly BattleInputData battleInputData;
    private readonly DungeonRepository dungeonRepository;
    private readonly GameFlowManager gameFlowManager;
    private readonly DungeonIntelModel dungeonIntel;
    private readonly CompositeDisposable disposables = new();

    /// <summary>
    /// 候補ラベルで弱点に合う属性を強調する色（濃い赤茶）。
    /// プルダウンの行は中間の茶色（10説明ボックス）なので、UI の金（#E8A33D）では沈んで読めない。
    /// </summary>
    private const string MatchColor = "#7A2E10";
    /// <summary>在庫0の候補の色（選べるが目立たせない）。在庫ありの文字（#3A261C）より一段淡い濃茶。灰色は茶の行に沈むので使わない。</summary>
    private const string NoStockColor = "#5A3A24";

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
        DungeonRepository dungeonRepository,
        RelicEffectResolver relicResolver,
        GameFlowManager gameFlowManager,
        DungeonIntelModel dungeonIntel)
    {
        this.gameFlowManager = gameFlowManager;
        this.dungeonIntel = dungeonIntel;
        this.relicResolver = relicResolver;
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
        FixEquipmentSlotTypes();
        Bind();
    }

    /// <summary>
    /// 種類が変わった品を装備したままの古いセーブを直す（例: 聖光の盾 SH1 は武器→防具になった）。
    /// 武器の枠に防具が入っていれば、防具の枠が空いていればそこへ移し、空いていなければ外す。
    /// 防具の枠に武器が入っていれば外す。店に入るたびに呼ぶ（直すものが無ければ何もしない）。
    /// </summary>
    private void FixEquipmentSlotTypes()
    {
        var hero = heroModel?.heroData;
        if (hero == null || itemModel == null) return;
        bool changed = false;

        var weapon = string.IsNullOrEmpty(hero.weaponId.Value) ? null : itemModel.GetRuntimeItem(hero.weaponId.Value);
        if (weapon != null && weapon.ItemType != ItemTypeData.ItemType.Weapon)
        {
            if (weapon.ItemType == ItemTypeData.ItemType.Armor && string.IsNullOrEmpty(hero.armorId.Value))
            {
                hero.armorId.Value = weapon.ItemId;
                hero.armorName.Value = weapon.ItemName;
            }
            hero.weaponId.Value = string.Empty;
            hero.weaponName.Value = string.Empty;
            changed = true;
        }

        var armor = string.IsNullOrEmpty(hero.armorId.Value) ? null : itemModel.GetRuntimeItem(hero.armorId.Value);
        if (armor != null && armor.ItemType != ItemTypeData.ItemType.Armor)
        {
            hero.armorId.Value = string.Empty;
            hero.armorName.Value = string.Empty;
            changed = true;
        }

        if (!changed) return;
        UnityEngine.Debug.Log($"[HeroPanelPresenter] 種類の合わない装備を直した: 武器={hero.weaponId.Value} 防具={hero.armorId.Value}");
        SaveEquipment();
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

        view.OnCraftRequested
            .Subscribe(Craft)
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
        // 変えた直後に倍率が見えるよう、相場への影響の倍率を添える
        Refresh($"{HeroDialogueLoader.Get("tactics_changed")}\n{TacticsEffectLine(tactics)}");
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
            heroModel.EquipItem(hero.weaponId.Value);

        if (!string.IsNullOrEmpty(hero.armorId.Value))
            heroModel.EquipItem(hero.armorId.Value);

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
        var stats = ClearProbabilityCalculator.GetEffectiveStats(hero, itemModel, relicResolver);
        view.SetCombatStats(stats.Attack, stats.Defense);

        var weakness = GetKnownWeakness();
        view.SetWeaponOptions(BuildEquipmentOptions(ItemTypeData.ItemType.Weapon, hero.weaponId.Value, weakness), hero.weaponId.Value);
        view.SetArmorOptions(BuildEquipmentOptions(ItemTypeData.ItemType.Armor, hero.armorId.Value, weakness), hero.armorId.Value);
        view.SetTacticsOptions(hero.tactics.Value);
        view.SetTacticsHoverDescription(GetTacticsHoverDesc(hero.tactics.Value));
        view.SetEquipmentIcons(weapon?.ItemIcon, armor?.ItemIcon);
        view.SetDialogue(dialogue);
        UpdateClearProbability();
        RefreshCraft(false);
    }

    // ───────── 細工（職人組合 Lv5。ArtisanCraft） ─────────

    /// <summary>次の配信がこのランの何回目か（配信が残っていなければ 0）。細工は1つの「配信の間」に1回。</summary>
    private int UpcomingStreamNumber()
    {
        if (gameFlowManager == null) return 0;
        gameFlowManager.GetStreamProgress(out int upcoming, out _);
        return upcoming;
    }

    private void RefreshCraft(bool reveal)
    {
        bool enabled = VillageEffects.Current.CanCraftHeroWeapon;
        int stream = UpcomingStreamNumber();
        var kind = enabled ? ArtisanCraft.KindFor(tomsModel.FlowSeed, stream) : ArtisanCraftKind.None;
        bool hasWeapon = !string.IsNullOrEmpty(heroModel.heroData?.weaponId.Value);
        bool canCraft = enabled && stream > 0 && kind == ArtisanCraftKind.None && hasWeapon;
        // 業物で付く属性は、弱点を知っているときだけ印に出す（知らなければ「業物」の字だけ）
        var shownAttribute = kind == ArtisanCraftKind.Fine ? GetKnownWeakness() : null;
        string used = kind != ArtisanCraftKind.None ? $"\n{CraftLine("hover_craft_used")}" : string.Empty;
        view.SetCraft(enabled && stream > 0, canCraft, kind, shownAttribute,
            CraftLine("hover_craft_fine") + used, CraftLine("hover_craft_dull") + used, reveal);
    }

    private void Craft(ArtisanCraftKind kind)
    {
        if (!VillageEffects.Current.CanCraftHeroWeapon || kind == ArtisanCraftKind.None) return;
        if (string.IsNullOrEmpty(heroModel.heroData?.weaponId.Value)) return;
        int stream = UpcomingStreamNumber();
        if (!ArtisanCraft.Apply(tomsModel.FlowSeed, stream, kind)) return;
        // 勇者は細工に気づかない（なまくらでも礼を言う）
        currentDialogue = CraftLine(kind == ArtisanCraftKind.Fine ? "craft_fine_done" : "craft_dull_done");
        view.SetDialogue(currentDialogue);
        RefreshCraft(true);
    }

    /// <summary>細工の台詞（HeroDialogues.csv。無ければ空）。</summary>
    private static string CraftLine(string id) => HeroDialogueLoader.Get(id);

    /// <summary>
    /// クリア見込み（5段階）を計算して View に反映する。
    /// ダンジョン情報が未購入の場合は「情報未購入」にする。
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
            dungeon.currentDungeonLevel,
            relicResolver);

        view.SetClearProbability(probability, true);
    }

    /// <summary>次の配信先の弱点を知っていればその属性（情報屋・新聞で知ったか。地図の名札と同じ判定）。</summary>
    private ItemTypeData.ItemAttribute? GetKnownWeakness()
    {
        if (dungeonRepository == null || gameFlowManager == null || dungeonIntel == null) return null;
        var nextKey = gameFlowManager.GetUpcomingStreamDungeon();
        if (!nextKey.HasValue || !dungeonIntel.IsWeaknessKnown(nextKey.Value)) return null;
        var data = dungeonRepository.GetById(nextKey.Value);
        return data != null ? data.requiredAttribute : (ItemTypeData.ItemAttribute?)null;
    }

    /// <summary>
    /// 次に配信するダンジョンの情報が購入済みであれば <paramref name="dungeonData"/> に返す。
    /// 未購入または次の配信が無い場合は false を返す。
    /// 行き先は地図の名札と同じ取り方（BattleInputData は配信の日にしか書かれず、前回の配信先のまま残っている）。
    /// </summary>
    private bool TryGetPurchasedDungeon(out DungeonData dungeonData)
    {
        dungeonData = null;
        if (dungeonRepository == null || gameFlowManager == null) return false;

        var nextKey = gameFlowManager.GetUpcomingStreamDungeon();
        if (!nextKey.HasValue) return false;

        dungeonData = dungeonRepository.GetById(nextKey.Value);
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

    /// <summary>
    /// 装備の候補。並びは「在庫あり → 弱点に合う（弱点を知っているときだけ）→ 強い順（ランク・値段）」。
    /// 在庫0の品も選べるが、淡い色で末尾に置く（装備に賭ける儲けは持っている数に比例するため、在庫を先に見せる）。
    /// </summary>
    private IReadOnlyList<HeroEquipmentOption> BuildEquipmentOptions(ItemTypeData.ItemType type, string selectedItemId, ItemTypeData.ItemAttribute? weakness)
    {
        int blacksmithLevel = tomsModel.BlacksmithLevel.Value;
        var options = itemModel.RuntimeItems
            .Where(item => item.ItemType == type && item.RequiredLevel.Value <= blacksmithLevel)
            .OrderByDescending(item => item.Stock.Value > 0)
            .ThenByDescending(item => IsWeaknessMatch(item, weakness))
            .ThenByDescending(item => item.RequiredLevel.Value)
            .ThenByDescending(item => item.CurrentPrice.Value)
            .Select(item => new HeroEquipmentOption(item.ItemId, BuildEquipmentLabel(item, weakness), BuildEquipmentCaption(item)))
            .ToList();

        if (!string.IsNullOrEmpty(selectedItemId) && options.All(option => option.ItemId != selectedItemId))
        {
            var selected = itemModel.GetRuntimeItem(selectedItemId);
            if (selected != null && selected.ItemType == type)
                options.Insert(0, new HeroEquipmentOption(selected.ItemId, $"{selected.ItemName}（未所持）"));
        }

        return options;
    }

    private static bool IsWeaknessMatch(RuntimeItemData item, ItemTypeData.ItemAttribute? weakness)
        => weakness.HasValue && ItemTypeData.NormalizeAttribute(item.ItemAttribute) == ItemTypeData.NormalizeAttribute(weakness.Value);

    /// <summary>
    /// プルダウンの1行: 「炎の剣　火　在庫12　1,240G　攻撃+10%」。
    /// 弱点に合う属性は赤茶、在庫0の行は淡い濃茶。
    /// </summary>
    private static string BuildEquipmentLabel(RuntimeItemData item, ItemTypeData.ItemAttribute? weakness)
    {
        string attribute = ItemAttributeText.ToJapanese(item.ItemAttribute);
        if (IsWeaknessMatch(item, weakness)) attribute = $"<color={MatchColor}>{attribute}</color>";

        int tier = HeroEquipmentBonus.TierOf(item);
        string bonus = item.ItemType == ItemTypeData.ItemType.Weapon
            ? $"攻撃+{HeroEquipmentBonus.WeaponAttackBonus(tier) * 100f:0}%"
            : $"防御+{HeroEquipmentBonus.ArmorDefenseBonus(tier) * 100f:0}% HP+{HeroEquipmentBonus.ArmorHpBonus(tier) * 100f:0}%";

        string label = $"{item.ItemName}　{attribute}　在庫{item.Stock.Value}　{item.CurrentPrice.Value:N0}G　{bonus}";
        return item.Stock.Value > 0 ? label : $"<color={NoStockColor}>{label}</color>";
    }

    /// <summary>閉じたプルダウンに出す短い表記（枠が狭いので名前と属性だけ）。</summary>
    private static string BuildEquipmentCaption(RuntimeItemData item)
        => $"{item.ItemName}（{ItemAttributeText.ToJapanese(item.ItemAttribute)}）";

    /// <summary>
    /// 作戦の説明。1行目に相場への影響の倍率（HeroBattleInfluence と同じ数字）を出す。
    /// 作戦は配信中の値動き専用で、クリア見込み（勝ち負け）には効かない。
    /// </summary>
    private static string GetTacticsHoverDesc(HeroTactics tactics)
    {
        string body = tactics switch
        {
            HeroTactics.Aggressive   => HeroDialogueLoader.Get("hover_tactics_aggressive"),
            HeroTactics.Defensive    => HeroDialogueLoader.Get("hover_tactics_defensive"),
            HeroTactics.Stealth      => HeroDialogueLoader.Get("hover_tactics_stealth"),
            HeroTactics.MagicFocused => HeroDialogueLoader.Get("hover_tactics_magic"),
            _                        => HeroDialogueLoader.Get("hover_tactics_balanced"),
        };
        return $"{TacticsEffectLine(tactics)}\n{body}";
    }

    private static string TacticsEffectLine(HeroTactics tactics)
        => $"<size=80%>相場への影響: {HeroBattleInfluence.BuildTacticsMultiplierText(tactics)}</size>";

    public void Dispose() => disposables.Dispose();
}

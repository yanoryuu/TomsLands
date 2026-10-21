using System;
using System.Collections.Generic;
using TMPro;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HeroPanelView : MonoBehaviour
{
    [Header("操作")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button characterButton;
    [SerializeField] private TMP_Dropdown weaponDropdown;
    [SerializeField] private TMP_Dropdown armorDropdown;
    [SerializeField] private TMP_Dropdown tacticsDropdown;

    [Header("表示")]
    [Tooltip("戦うときの攻撃・防御（装備・レリック込み）")]
    [SerializeField] private TextMeshProUGUI heroStatusText;
    [SerializeField] private TextMeshProUGUI heroLevelText;
    [SerializeField] private TextMeshProUGUI heroHpText;
    [SerializeField] private TextMeshProUGUI heroMpText;
    [SerializeField] private TextMeshProUGUI dialogueText;

    [Header("装備アイコン")]
    [SerializeField] private Image weaponIconImage;
    [SerializeField] private Image armorIconImage;

    [Header("クリア確率")]
    [SerializeField] private TextMeshProUGUI clearProbabilityText;

    [Header("説明ボタン")]
    [SerializeField] private Button weaponInfoButton;
    [SerializeField] private Button armorInfoButton;
    [SerializeField] private Button tacticsInfoButton;
    [SerializeField] private Button clearProbabilityInfoButton;

    [Header("細工（職人組合 Lv5。任意）")]
    [Tooltip("「業物」「なまくら」のボタンをまとめた枠。職人組合が Lv5 のときだけ出す")]
    [SerializeField] private GameObject craftRoot;
    [SerializeField] private Button craftFineButton;
    [SerializeField] private Button craftDullButton;
    [Tooltip("武器アイコンに重ねる細工の印（業物／なまくら）")]
    [SerializeField] private GameObject craftMark;
    [SerializeField] private Image craftMarkBackground;
    [SerializeField] private TextMeshProUGUI craftMarkText;
    [Tooltip("業物の属性のアイコン（弱点を知っているときだけ出す）")]
    [SerializeField] private Image craftAttributeIcon;
    [Tooltip("属性アイコン（火・水・木・光・闇の順。金融の属性ファンドの絵）")]
    [SerializeField] private Sprite[] attributeIcons = new Sprite[5];
    [Tooltip("業物のときだけ出す金縁（札に重ねる）")]
    [SerializeField] private GameObject craftMarkGoldRim;
    [Tooltip("札（木の板）に掛ける色。業物はそのまま、なまくらはくすませる")]
    [SerializeField] private Color craftFineColor = Color.white;
    [SerializeField] private Color craftDullColor = new Color32(0xB4, 0xA4, 0x94, 0xFF);
    [Tooltip("札の文字の色（業物は金、なまくらは色あせた生成り）")]
    [SerializeField] private Color craftFineTextColor = new Color32(0xFF, 0xD3, 0x5A, 0xFF);
    [SerializeField] private Color craftDullTextColor = new Color32(0xE6, 0xD8, 0xC6, 0xFF);

    public Subject<Unit>        OnCloseRequested { get; } = new();
    public Subject<string>      OnWeaponChanged  { get; } = new();
    public Subject<string>      OnArmorChanged   { get; } = new();
    public Subject<HeroTactics> OnTacticsChanged { get; } = new();
    public Subject<Unit>        OnCharacterClicked { get; } = new();
    public Subject<string>      OnHoverEnter     { get; } = new();
    public Subject<Unit>        OnHoverExit      { get; } = new();
    /// <summary>細工（職人組合 Lv5）のボタン。業物 / なまくら。</summary>
    public Subject<ArtisanCraftKind> OnCraftRequested { get; } = new();

    private readonly List<string>              weaponIds          = new();
    private readonly List<string>              armorIds           = new();
    private readonly List<HeroTactics>         tacticsValues      = new();
    private readonly Dictionary<Button, string> buttonDescriptions = new();
    private readonly HashSet<TextMeshProUGUI>  poppedTexts        = new(); // 1回目の表示（プレハブの仮の文字から）はポップしない
    private bool suppressEvents;

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));

        if (characterButton != null)
        {
            characterButton.onClick.AddListener(() => OnCharacterClicked.OnNext(Unit.Default));
            characterButton.navigation = new Navigation { mode = Navigation.Mode.None }; // 立ち絵はパッドで選ばない（画面より大きい枠が出るため）
        }

        if (weaponDropdown != null)
        {
            weaponDropdown.onValueChanged.AddListener(index =>
            {
                if (suppressEvents || index < 0 || index >= weaponIds.Count) return;
                OnWeaponChanged.OnNext(weaponIds[index]);
            });
        }

        if (armorDropdown != null)
        {
            armorDropdown.onValueChanged.AddListener(index =>
            {
                if (suppressEvents || index < 0 || index >= armorIds.Count) return;
                OnArmorChanged.OnNext(armorIds[index]);
            });
        }

        if (tacticsDropdown != null)
        {
            tacticsDropdown.onValueChanged.AddListener(index =>
            {
                if (suppressEvents || index < 0 || index >= tacticsValues.Count) return;
                OnTacticsChanged.OnNext(tacticsValues[index]);
            });
        }

        RegisterInfoButton(weaponInfoButton);
        RegisterInfoButton(armorInfoButton);
        RegisterInfoButton(tacticsInfoButton);
        RegisterInfoButton(clearProbabilityInfoButton);

        if (craftFineButton != null)
        {
            craftFineButton.onClick.AddListener(() => OnCraftRequested.OnNext(ArtisanCraftKind.Fine));
            RegisterInfoButton(craftFineButton);
            RegisterNavDescription(craftFineButton, craftFineButton);
        }
        if (craftDullButton != null)
        {
            craftDullButton.onClick.AddListener(() => OnCraftRequested.OnNext(ArtisanCraftKind.Dull));
            RegisterInfoButton(craftDullButton);
            RegisterNavDescription(craftDullButton, craftDullButton);
        }
        if (craftRoot != null) craftRoot.SetActive(false);
        if (craftMark != null) craftMark.SetActive(false);

        // パッド/キー: ドロップダウン（とクリア確率欄）を選んだら、対応する説明をホバー時と同じく表示する
        RegisterNavDescription(weaponDropdown, weaponInfoButton);
        RegisterNavDescription(armorDropdown, armorInfoButton);
        RegisterNavDescription(tacticsDropdown, tacticsInfoButton);
        RegisterNavDescription(clearProbabilityInfoButton, clearProbabilityInfoButton);
    }

    private void RegisterNavDescription(Selectable selectable, Button descriptionSource)
    {
        if (selectable == null || descriptionSource == null) return;
        var relay = ControllerSelectRelay.GetOrAdd(selectable);
        relay.Selected += () =>
        {
            if (buttonDescriptions.TryGetValue(descriptionSource, out var desc))
                OnHoverEnter.OnNext(desc);
        };
        relay.Deselected += () => OnHoverExit.OnNext(Unit.Default);
    }

    private void RegisterInfoButton(Button button)
    {
        if (button == null) return;

        // UnityEngine.Object に ?? は使わない（エディタの GetComponent は「偽 null」を返すことがある）
        if (!button.TryGetComponent<EventTrigger>(out var trigger))
            trigger = button.gameObject.AddComponent<EventTrigger>();

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ =>
        {
            if (buttonDescriptions.TryGetValue(button, out var desc))
                OnHoverEnter.OnNext(desc);
        });
        trigger.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => OnHoverExit.OnNext(Unit.Default));
        trigger.triggers.Add(exit);
    }

    // ─────────────────────────────────────────────────────────
    // Public set methods
    // ─────────────────────────────────────────────────────────

    public void SetHeroStatus(RuntimeHeroData hero)
    {
        if (hero == null) return;

        string expText = hero.expToNextLevel.Value > 0
            ? $"{hero.experience.Value}/{hero.expToNextLevel.Value}"
            : "MAX";

        if (heroLevelText != null)
            heroLevelText.text = $"Lv {hero.level.Value}\nEXP {expText}";

        // 「HP」「MP」の静的ラベルがパネル側にあるため、ここでは数値のみを表示する
        if (heroHpText != null)
            heroHpText.text = $"{hero.hp.Value}";

        if (heroMpText != null)
            heroMpText.text = $"{hero.mp.Value}";
    }

    /// <summary>戦うときの攻撃・防御（装備・レリック込み）。装備を付け替えて数字が変わったら軽くポップする。</summary>
    public void SetCombatStats(int attack, int defense)
    {
        if (heroStatusText == null) return;
        SetTextWithPop(heroStatusText, $"攻撃 {attack}　防御 {defense}");
    }

    public void SetEquipmentIcons(Sprite weaponIcon, Sprite armorIcon)
    {
        SetIcon(weaponIconImage, weaponIcon);
        SetIcon(armorIconImage, armorIcon);
    }

    /// <summary>クリア見込みを5段階（楽勝〜絶望）で出す。見出し「クリア見込み」はパネル側にある。</summary>
    public void SetClearProbability(float probability, bool infoAvailable)
    {
        if (clearProbabilityText == null) return;
        SetTextWithPop(clearProbabilityText, infoAvailable
            ? ClearProbabilityCalculator.FormatOutlook(probability)
            : "情報未購入");
    }

    /// <summary>文字が変わったときだけ 0.2 秒ポップさせる（最初の表示ではポップしない）。</summary>
    private void SetTextWithPop(TextMeshProUGUI text, string value)
    {
        bool changed = poppedTexts.Contains(text) && text.text != value && text.gameObject.activeInHierarchy;
        poppedTexts.Add(text);
        text.text = value;
        if (changed) UIFx.Pop(text.transform, 0.85f, 0.2f);
    }

    public void SetDialogue(string message)
    {
        if (dialogueText != null)
            dialogueText.text = message ?? string.Empty;
    }

    public void SetWeaponOptions(IReadOnlyList<HeroEquipmentOption> options, string selectedItemId)
        => SetEquipmentOptions(weaponDropdown, weaponIds, options, selectedItemId);

    public void SetArmorOptions(IReadOnlyList<HeroEquipmentOption> options, string selectedItemId)
        => SetEquipmentOptions(armorDropdown, armorIds, options, selectedItemId);

    public void SetTacticsOptions(HeroTactics selected)
    {
        if (tacticsDropdown == null) return;

        suppressEvents = true;
        tacticsValues.Clear();
        tacticsDropdown.ClearOptions();

        var labels = new List<string>();
        foreach (HeroTactics tactics in Enum.GetValues(typeof(HeroTactics)))
        {
            tacticsValues.Add(tactics);
            labels.Add(GetTacticsLabel(tactics));
        }

        tacticsDropdown.AddOptions(labels);
        int selectedIndex = Mathf.Max(0, tacticsValues.IndexOf(selected));
        tacticsDropdown.SetValueWithoutNotify(selectedIndex);
        tacticsDropdown.RefreshShownValue();
        suppressEvents = false;
    }

    public void SetTacticsHoverDescription(string description)
        => SetButtonDescription(tacticsInfoButton, description);

    public void SetStaticHoverDescriptions(string weapon, string armor, string clearProbability)
    {
        SetButtonDescription(weaponInfoButton, weapon);
        SetButtonDescription(armorInfoButton, armor);
        SetButtonDescription(clearProbabilityInfoButton, clearProbability);
    }

    /// <summary>
    /// 細工（職人組合 Lv5）。show = ボタンの枠を出すか、canCraft = 押せるか（この配信の間にまだ細工していない・武器がある）。
    /// kind = この配信の間にした細工（武器アイコンに印を出す）。fineAttribute = 業物で付く属性（弱点を知っているときだけ。知らなければ null）。
    /// reveal = いま細工した（印を跳ねさせる）。
    /// </summary>
    public void SetCraft(bool show, bool canCraft, ArtisanCraftKind kind, ItemTypeData.ItemAttribute? fineAttribute,
        string fineDescription, string dullDescription, bool reveal = false)
    {
        if (craftRoot != null) craftRoot.SetActive(show);
        if (craftFineButton != null) craftFineButton.interactable = canCraft;
        if (craftDullButton != null) craftDullButton.interactable = canCraft;
        SetButtonDescription(craftFineButton, fineDescription);
        SetButtonDescription(craftDullButton, dullDescription);

        if (craftMark == null) return;
        bool marked = kind != ArtisanCraftKind.None;
        craftMark.SetActive(marked);
        if (!marked) return;
        bool fine = kind == ArtisanCraftKind.Fine;
        if (craftMarkText != null)
        {
            craftMarkText.text = fine ? "業物" : "なまくら";
            craftMarkText.color = fine ? craftFineTextColor : craftDullTextColor;
        }
        if (craftMarkBackground != null) craftMarkBackground.color = fine ? craftFineColor : craftDullColor;
        if (craftMarkGoldRim != null) craftMarkGoldRim.SetActive(fine);
        if (craftAttributeIcon != null)
        {
            var icon = fine && fineAttribute.HasValue ? AttributeIcon(fineAttribute.Value) : null;
            craftAttributeIcon.sprite = icon;
            craftAttributeIcon.gameObject.SetActive(icon != null);
        }
        if (reveal) UIFx.Pop(craftMark.transform, 1.6f, 0.22f);
    }

    private Sprite AttributeIcon(ItemTypeData.ItemAttribute attr)
    {
        if (attributeIcons == null) return null;
        int index = ItemTypeData.NormalizeAttribute(attr) switch
        {
            ItemTypeData.ItemAttribute.Fire => 0,
            ItemTypeData.ItemAttribute.Water => 1,
            ItemTypeData.ItemAttribute.Wood => 2,
            ItemTypeData.ItemAttribute.Light => 3,
            ItemTypeData.ItemAttribute.Dark => 4,
            _ => -1,
        };
        return index >= 0 && index < attributeIcons.Length ? attributeIcons[index] : null;
    }

    // ─────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────

    private void SetButtonDescription(Button button, string description)
    {
        if (button == null) return;
        buttonDescriptions[button] = description;
    }

    private void SetEquipmentOptions(
        TMP_Dropdown dropdown,
        List<string> ids,
        IReadOnlyList<HeroEquipmentOption> options,
        string selectedItemId)
    {
        if (dropdown == null) return;

        suppressEvents = true;
        ids.Clear();
        dropdown.ClearOptions();

        var labels = new List<string> { "なし" };
        ids.Add(string.Empty);

        int selectedIndex = 0;
        for (int i = 0; i < options.Count; i++)
        {
            var option = options[i];
            ids.Add(option.ItemId);
            labels.Add(option.Label);
            if (option.ItemId == selectedItemId)
                selectedIndex = i + 1;
        }

        dropdown.AddOptions(labels);
        dropdown.SetValueWithoutNotify(selectedIndex);
        dropdown.RefreshShownValue();
        // 閉じた枠は狭いので短い表記にする（一覧の行には在庫・値段などの詳しい表記を出す）。
        // 選び直すと TMP_Dropdown が一覧の表記で上書きするが、選択 → Presenter.Refresh → ここ、で毎回戻る
        if (dropdown.captionText != null && selectedIndex > 0)
        {
            string caption = options[selectedIndex - 1].Caption;
            if (!string.IsNullOrEmpty(caption)) dropdown.captionText.text = caption;
        }
        suppressEvents = false;
    }

    private static void SetIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite  = sprite;
        image.enabled = sprite != null;
        image.preserveAspect = true; // 装備の絵は縦長・横長がまちまちなので、枠に合わせて引き伸ばさない
    }

    /// <summary>「攻撃重視（武器↑）」。呼び名は HeroBattleInfluence に揃え、いちばん強く上がる値動きを添える。</summary>
    private static string GetTacticsLabel(HeroTactics tactics)
    {
        string tag = HeroBattleInfluence.GetStrongestEffectTag(tactics);
        string name = HeroBattleInfluence.GetDisplayName(tactics);
        return string.IsNullOrEmpty(tag) ? name : $"{name}（{tag}）";
    }
}

public readonly struct HeroEquipmentOption
{
    public string ItemId  { get; }
    /// <summary>一覧の行の表記（名前・属性・在庫・値段・能力値）。</summary>
    public string Label   { get; }
    /// <summary>閉じた枠に出す短い表記。空なら Label のまま。</summary>
    public string Caption { get; }

    public HeroEquipmentOption(string itemId, string label, string caption = null)
    {
        ItemId  = itemId;
        Label   = label;
        Caption = caption;
    }
}

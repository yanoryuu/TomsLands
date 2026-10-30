using System;
using System.Collections.Generic;
using TMPro;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 勇者の画面（案A「着せ替え型」）。左に勇者の立ち絵と装備の枠（武器＝手元・防具＝胴）、その下に能力値の板。
/// 右に品物の棚（武器／防具のタブ・2 列のカード）、その下に作戦のプルダウンと勇者のセリフ。
/// 装備は棚のカードを勇者の枠へドラッグして付ける（ドラッグ中は付けた後の攻撃・防御・HP の変化を能力値の板に出す）。
/// 近道: カードを 2 回押す（ダブルクリック・決定 2 回）。パッド: カードで決定 → 持ち上げて置き先の枠へ → 決定で置く（B でやめる）。
/// 配線と組み方は Docs/Hero_UnityWiring.md、プレハブは Assets/Scripts/Editor/HeroUIBuilder.cs が組む。
/// AutoPlay は tacticsDropdown / tacticsValues / closeButton を名前で見る（変えるときは AutoPlayController.Procurement.cs も）。
/// </summary>
public class HeroPanelView : MonoBehaviour
{
    [Header("操作")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button characterButton;
    [SerializeField] private TMP_Dropdown tacticsDropdown;

    [Header("表示")]
    [Tooltip("戦うときの攻撃（装備・レリック込み）。defenseText が無ければ「攻撃 n　防御 n」を 1 つで出す")]
    [SerializeField] private TextMeshProUGUI heroStatusText;
    [Tooltip("戦うときの防御（装備・レリック込み）")]
    [SerializeField] private TextMeshProUGUI defenseText;
    [SerializeField] private TextMeshProUGUI heroLevelText;
    [SerializeField] private TextMeshProUGUI heroHpText;
    [SerializeField] private TextMeshProUGUI heroMpText;
    [SerializeField] private TextMeshProUGUI dialogueText;

    [Header("付けた後の見込み（ドラッグ中だけ出す札。親が札の板）")]
    [SerializeField] private TextMeshProUGUI attackDeltaText;
    [SerializeField] private TextMeshProUGUI defenseDeltaText;
    [SerializeField] private TextMeshProUGUI hpDeltaText;

    [Header("装備の枠（立ち絵に重ねる）")]
    [SerializeField] private HeroEquipSlot weaponSlot;
    [SerializeField] private HeroEquipSlot armorSlot;
    [Tooltip("ここで離しても装備する（立ち絵の体のあたり）")]
    [SerializeField] private RectTransform heroDropArea;

    [Header("装備アイコン")]
    [SerializeField] private Image weaponIconImage;
    [SerializeField] private Image armorIconImage;

    [Header("品物の棚")]
    [SerializeField] private Button weaponTabButton;
    [SerializeField] private Button armorTabButton;
    [Tooltip("選んでいるタブの印（金の縁）")]
    [SerializeField] private GameObject weaponTabOn;
    [SerializeField] private GameObject armorTabOn;
    [SerializeField] private ScrollRect shelfScroll;
    [SerializeField] private RectTransform shelfContent;
    [Tooltip("カードの見本（非表示）。複製して使い回す")]
    [SerializeField] private HeroEquipmentCard cardTemplate;

    [Header("クリア確率")]
    [SerializeField] private TextMeshProUGUI clearProbabilityText;
    [Tooltip("5 段階の目盛り（左から。楽勝で 5 つ灯る）")]
    [SerializeField] private Image[] clearMeterPips = Array.Empty<Image>();
    [SerializeField] private Color clearPipOffColor = new Color32(0x4A, 0x30, 0x20, 0xFF);

    [Header("作戦")]
    [Tooltip("閉じた枠の「作戦: 攻撃重視（武器↑）」（TMP_Dropdown の captionText は使わない。一覧の 2 行の表記で上書きされるため）")]
    [SerializeField] private TextMeshProUGUI tacticsCaptionText;
    [SerializeField] private Image tacticsCaptionIcon;
    [Tooltip("閉じた枠の下に出す一言の説明")]
    [SerializeField] private TextMeshProUGUI tacticsDescText;
    [Tooltip("作戦のアイコン（HeroTactics の順: 攻撃重視・防御重視・バランス・慎重・属性重視）")]
    [SerializeField] private Sprite[] tacticsIcons = new Sprite[5];

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

    private readonly List<HeroTactics>         tacticsValues      = new();
    private readonly Dictionary<Button, string> buttonDescriptions = new();
    private readonly HashSet<TextMeshProUGUI>  poppedTexts        = new(); // 1回目の表示（プレハブの仮の文字から）はポップしない
    private readonly List<HeroEquipmentCard>   cards              = new();
    private bool suppressEvents;
    private bool initialized;

    // 棚の中身（Presenter から）
    private IReadOnlyList<HeroEquipmentOption> weaponOptions = Array.Empty<HeroEquipmentOption>();
    private IReadOnlyList<HeroEquipmentOption> armorOptions  = Array.Empty<HeroEquipmentOption>();
    private string weaponSelectedId = string.Empty;
    private string armorSelectedId  = string.Empty;
    private ClearProbabilityCalculator.HeroStats weaponNoneAfter;
    private ClearProbabilityCalculator.HeroStats armorNoneAfter;
    private bool shelfIsWeapon = true;

    // いまの戦う強さ（付けた後との差を出すため）
    private int currentAttack, currentDefense, currentHp = -1;

    // 持ち上げ中
    private HeroEquipmentCard carried;
    private RectTransform ghost;
    private UIFocusScope focusScope;

    private static readonly Color DeltaUp = new Color32(0x7C, 0xD3, 0x6A, 0xFF);
    private static readonly Color DeltaDown = new Color32(0xFF, 0x7A, 0x66, 0xFF);
    private static readonly Color DeltaZero = new Color32(0xF5, 0xE9, 0xD5, 0xFF);

    private void Awake() => Initialize();

    private void OnDisable() => EndCarry(silent: true);

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        focusScope = GetComponent<UIFocusScope>();

        if (closeButton != null)
            closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));

        if (characterButton != null)
        {
            characterButton.onClick.AddListener(() => OnCharacterClicked.OnNext(Unit.Default));
            characterButton.navigation = new Navigation { mode = Navigation.Mode.None }; // 立ち絵はパッドで選ばない（画面より大きい枠が出るため）
            ButtonFeel.Disable(characterButton); // 立ち絵を押しても縮ませない
        }

        if (tacticsDropdown != null)
        {
            tacticsDropdown.onValueChanged.AddListener(index =>
            {
                if (suppressEvents || index < 0 || index >= tacticsValues.Count) return;
                OnTacticsChanged.OnNext(tacticsValues[index]);
            });
        }

        // 説明ボタン（見出しの文字）はマウスのホバー用。パッドは枠・プルダウンを選んだときに同じ説明を出す
        RegisterInfoButton(weaponInfoButton);
        RegisterInfoButton(armorInfoButton);
        RegisterInfoButton(tacticsInfoButton);
        RegisterInfoButton(clearProbabilityInfoButton);
        NoNavigation(weaponInfoButton);
        NoNavigation(armorInfoButton);
        NoNavigation(tacticsInfoButton);

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

        // 装備の枠: 持ち上げ中なら置く、でなければ棚をその種類にする
        foreach (var slot in new[] { weaponSlot, armorSlot })
        {
            if (slot == null || slot.Button == null) continue;
            var s = slot;
            s.Button.onClick.AddListener(() => OnSlotPressed(s));
            ButtonFeel.Disable(s.Button); // 置いたときのつぶれる演出と重ねない
        }
        if (weaponSlot != null) RegisterNavDescription(weaponSlot.Button, weaponInfoButton);
        if (armorSlot != null) RegisterNavDescription(armorSlot.Button, armorInfoButton);

        // 棚のタブ（LB / RB でも切り替える）
        if (weaponTabButton != null) weaponTabButton.onClick.AddListener(() => ShowShelf(true));
        if (armorTabButton != null) armorTabButton.onClick.AddListener(() => ShowShelf(false));
        if (focusScope != null)
        {
            focusScope.OnTabLeftEvent.AddListener(() => ShowShelf(true));
            focusScope.OnTabRightEvent.AddListener(() => ShowShelf(false));
        }

        // パッド/キー: プルダウン（とクリア確率欄）を選んだら、対応する説明をホバー時と同じく表示する
        RegisterNavDescription(tacticsDropdown, tacticsInfoButton);
        RegisterNavDescription(clearProbabilityInfoButton, clearProbabilityInfoButton);

        if (cardTemplate != null) cardTemplate.gameObject.SetActive(false);
        SetPreviewVisible(false);
        UpdateTabs();
    }

    private static void NoNavigation(Selectable s)
    {
        if (s != null) s.navigation = new Navigation { mode = Navigation.Mode.None };
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
            heroLevelText.text = $"Lv {hero.level.Value}  <size=62%>EXP {expText}</size>";

        // 「HP」「MP」の静的ラベルがパネル側にあるため、ここでは数値のみを表示する
        if (heroHpText != null)
            heroHpText.text = $"{hero.hp.Value}";

        if (heroMpText != null)
            heroMpText.text = $"{hero.mp.Value}";
    }

    /// <summary>戦うときの攻撃・防御（装備・レリック込み）。hp は付けた後の HP の差を出すため（-1 なら差を出さない）。数字が変わったら軽くポップする。</summary>
    public void SetCombatStats(int attack, int defense, int hp = -1)
    {
        currentAttack = attack;
        currentDefense = defense;
        currentHp = hp;
        if (defenseText != null)
        {
            if (heroStatusText != null) SetTextWithPop(heroStatusText, $"攻撃 {attack}");
            SetTextWithPop(defenseText, $"防御 {defense}");
        }
        else if (heroStatusText != null)
            SetTextWithPop(heroStatusText, $"攻撃 {attack}　防御 {defense}");
    }

    public void SetEquipmentIcons(Sprite weaponIcon, Sprite armorIcon)
    {
        SetIcon(weaponIconImage, weaponIcon);
        SetIcon(armorIconImage, armorIcon);
    }

    /// <summary>クリア見込みを5段階（楽勝〜絶望）で出す。見出し「クリア見込み」はパネル側にある。目盛りも段階の数だけ灯す（数字は出さない）。</summary>
    public void SetClearProbability(float probability, bool infoAvailable)
    {
        if (clearProbabilityText != null)
            SetTextWithPop(clearProbabilityText, infoAvailable
                ? ClearProbabilityCalculator.FormatOutlook(probability)
                : "情報未購入");

        int lit = 0;
        Color on = clearPipOffColor;
        if (infoAvailable)
        {
            var outlook = ClearProbabilityCalculator.ToOutlook(probability);
            lit = (int)outlook + 1;
            if (!ColorUtility.TryParseHtmlString(ClearProbabilityCalculator.OutlookColorHex(outlook), out on)) on = Color.white;
        }
        if (clearMeterPips == null) return;
        for (int i = 0; i < clearMeterPips.Length; i++)
            if (clearMeterPips[i] != null) clearMeterPips[i].color = i < lit ? on : clearPipOffColor;
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

    /// <summary>棚の「なし（外す）」を置いたときの戦う強さ（武器を外す・防具を外す）。</summary>
    public void SetUnequipPreview(ClearProbabilityCalculator.HeroStats weaponNone, ClearProbabilityCalculator.HeroStats armorNone)
    {
        weaponNoneAfter = weaponNone;
        armorNoneAfter = armorNone;
    }

    public void SetWeaponOptions(IReadOnlyList<HeroEquipmentOption> options, string selectedItemId)
    {
        weaponOptions = options ?? Array.Empty<HeroEquipmentOption>();
        weaponSelectedId = selectedItemId ?? string.Empty;
        if (weaponSlot != null) weaponSlot.SetItemName(NameOf(weaponOptions, weaponSelectedId));
        if (shelfIsWeapon) RebuildShelf(false);
    }

    public void SetArmorOptions(IReadOnlyList<HeroEquipmentOption> options, string selectedItemId)
    {
        armorOptions = options ?? Array.Empty<HeroEquipmentOption>();
        armorSelectedId = selectedItemId ?? string.Empty;
        if (armorSlot != null) armorSlot.SetItemName(NameOf(armorOptions, armorSelectedId));
        if (!shelfIsWeapon) RebuildShelf(false);
    }

    public void SetTacticsOptions(HeroTactics selected)
    {
        if (tacticsDropdown == null) return;
        Initialize();

        suppressEvents = true;
        tacticsValues.Clear();
        tacticsDropdown.ClearOptions();

        // 開いた一覧の行: アイコン・名前（いちばん上がる値動き）・一言の説明
        var options = new List<TMP_Dropdown.OptionData>();
        foreach (HeroTactics tactics in Enum.GetValues(typeof(HeroTactics)))
        {
            tacticsValues.Add(tactics);
            string desc = TacticsShortDescription(tactics);
            string text = string.IsNullOrEmpty(desc)
                ? GetTacticsLabel(tactics)
                : $"{GetTacticsLabel(tactics)}\n<size=70%><color=#F1DDB8>{desc}</color></size>";
            options.Add(new TMP_Dropdown.OptionData(text, TacticsIcon(tactics), Color.white));
        }

        tacticsDropdown.AddOptions(options);
        int selectedIndex = Mathf.Max(0, tacticsValues.IndexOf(selected));
        tacticsDropdown.SetValueWithoutNotify(selectedIndex);
        tacticsDropdown.RefreshShownValue();
        // 閉じた枠は「作戦: 攻撃重視（武器↑）」・アイコン・一言の説明を View が出す
        if (tacticsCaptionText != null) tacticsCaptionText.text = $"作戦: {GetTacticsLabel(selected)}";
        if (tacticsCaptionIcon != null)
        {
            tacticsCaptionIcon.sprite = TacticsIcon(selected);
            tacticsCaptionIcon.enabled = tacticsCaptionIcon.sprite != null;
        }
        if (tacticsDescText != null) tacticsDescText.text = TacticsShortDescription(selected);
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

    /// <summary>
    /// 棚のカードのボタン（itemId が空なら「なし」）。棚がもう一方の種類なら切り替えてから探す。無ければ null。
    /// カードを 2 回押すと装備する（1 回目で持ち上げ、2 回目で置く）。AutoPlay もこの経路で押す。
    /// </summary>
    public Button ShelfCardButton(string itemId, bool weapon)
    {
        Initialize();
        if (shelfIsWeapon != weapon) ShowShelf(weapon);
        itemId ??= string.Empty;
        foreach (var card in cards)
            if (card != null && card.gameObject.activeSelf && card.ItemId == itemId) return card.Button;
        return null;
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
    // 品物の棚
    // ─────────────────────────────────────────────────────────

    /// <summary>棚を武器／防具に切り替える（並んで出る演出つき）。</summary>
    private void ShowShelf(bool weapon)
    {
        bool changed = shelfIsWeapon != weapon;
        EndCarry(silent: false);
        shelfIsWeapon = weapon;
        UpdateTabs();
        RebuildShelf(changed);
    }

    private void UpdateTabs()
    {
        if (weaponTabOn != null) weaponTabOn.SetActive(shelfIsWeapon);
        if (armorTabOn != null) armorTabOn.SetActive(!shelfIsWeapon);
        TintTab(weaponTabButton, shelfIsWeapon);
        TintTab(armorTabButton, !shelfIsWeapon);
    }

    private static void TintTab(Button tab, bool on)
    {
        if (tab == null || tab.targetGraphic == null) return;
        tab.targetGraphic.color = on ? Color.white : new Color(0.72f, 0.66f, 0.6f, 1f);
    }

    private void RebuildShelf(bool animate)
    {
        Initialize(); // Presenter.Entry がパネルを出す前（Awake の前）に呼ぶこともある
        if (cardTemplate == null || shelfContent == null) return;
        EndCarry(silent: true);

        var options = shelfIsWeapon ? weaponOptions : armorOptions;
        string selected = shelfIsWeapon ? weaponSelectedId : armorSelectedId;
        int needed = options.Count + 1;
        while (cards.Count < needed) cards.Add(CreateCard());

        // 並びは Presenter のまま（在庫あり → 弱点に合う → 強い順。在庫0は末尾）。「なし（外す）」はいちばん最後
        for (int i = 0; i < options.Count; i++)
        {
            var card = cards[i];
            card.gameObject.SetActive(true);
            card.Bind(options[i], shelfIsWeapon, options[i].ItemId == selected, AttributeIcon(options[i].Attribute));
            card.transform.SetSiblingIndex(i);
        }
        var none = cards[options.Count];
        none.gameObject.SetActive(true);
        none.BindNone(shelfIsWeapon, string.IsNullOrEmpty(selected), shelfIsWeapon ? weaponNoneAfter : armorNoneAfter);
        none.transform.SetSiblingIndex(options.Count);
        for (int i = needed; i < cards.Count; i++) cards[i].gameObject.SetActive(false);

        if (focusScope != null) focusScope.FirstSelected = cards[0].Button;
        if (animate)
        {
            if (shelfScroll != null) shelfScroll.verticalNormalizedPosition = 1f;
            UIMotion.Stagger(shelfContent, 0.03f, 24f, 0.2f);
            if (ControllerInput.IsNavigating.CurrentValue && focusScope != null) focusScope.Select(cards[0].Button);
        }
    }

    private HeroEquipmentCard CreateCard()
    {
        var card = Instantiate(cardTemplate, shelfContent, false);
        card.gameObject.SetActive(true);
        card.Button.onClick.AddListener(() => OnCardPressed(card));
        card.DragStarted += OnCardDragStarted;
        card.Dragging += OnCardDragging;
        card.DragEnded += OnCardDragEnded;
        return card;
    }

    private static string NameOf(IReadOnlyList<HeroEquipmentOption> options, string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return string.Empty;
        foreach (var o in options)
            if (o.ItemId == itemId) return string.IsNullOrEmpty(o.Name) ? o.Label : o.Name;
        return string.Empty;
    }

    // ─────────────────────────────────────────────────────────
    // 持ち上げて置く（ドラッグ・クリック 2 回・パッド）
    // ─────────────────────────────────────────────────────────

    private HeroEquipSlot SlotFor(HeroEquipmentCard card) => card != null && card.IsWeapon ? weaponSlot : armorSlot;

    private void OnCardPressed(HeroEquipmentCard card)
    {
        if (card == null) return;
        // 2 回目（ダブルクリック・決定 2 回）はそのまま付ける
        if (carried == card)
        {
            Place(SlotFor(card));
            return;
        }
        BeginCarry(card, null);
    }

    private void OnSlotPressed(HeroEquipSlot slot)
    {
        if (carried != null)
        {
            if (SlotFor(carried) == slot) Place(slot);
            else EndCarry(silent: false);
            return;
        }
        // 何も持っていなければ、棚をこの枠の種類にする
        if (shelfIsWeapon != slot.IsWeapon) ShowShelf(slot.IsWeapon);
        else if (ControllerInput.IsNavigating.CurrentValue && focusScope != null && cards.Count > 0) focusScope.Select(cards[0].Button);
    }

    private void OnCardDragStarted(HeroEquipmentCard card, PointerEventData e)
    {
        if (carried != null && carried != card) EndCarry(silent: false);
        if (carried != card) BeginCarry(card, e);
        else MoveGhost(e); // クリックで持ち上げたまま掴み直した
    }

    private void OnCardDragging(HeroEquipmentCard card, PointerEventData e)
    {
        if (carried == card) MoveGhost(e);
    }

    private void OnCardDragEnded(HeroEquipmentCard card, PointerEventData e)
    {
        if (carried != card) return;
        var slot = SlotFor(card);
        if (IsOver(slot != null ? slot.transform as RectTransform : null, e) || IsOver(heroDropArea, e)) Place(slot);
        else EndCarry(silent: false);
    }

    private static bool IsOver(RectTransform rt, PointerEventData e)
        => rt != null && rt.gameObject.activeInHierarchy
           && RectTransformUtility.RectangleContainsScreenPoint(rt, e.position, e.pressEventCamera);

    /// <summary>持ち上げる。drag が null ならクリック・パッド（ゴーストは置き先の枠の上に浮かせる）。</summary>
    private void BeginCarry(HeroEquipmentCard card, PointerEventData drag)
    {
        var slot = SlotFor(card);
        if (slot == null) return;
        EndCarry(silent: true);

        carried = card;
        card.SetCarried(true);
        ghost = CreateGhost(card);
        if (drag != null) MoveGhost(drag);
        else HoverGhostOver(slot);
        DragFeel.Lift(ghost);
        slot.SetCarryTarget(true);
        ShowPreview(card);
        OnHoverEnter.OnNext(CarryHint); // セリフの板に置き方を出す（やめる・置くと元のセリフに戻る）
        if (focusScope != null)
        {
            focusScope.SetCancelAction(() => EndCarry(silent: false));
            if (drag == null) focusScope.Select(slot.Button); // パッド: 置き先の枠へ（マウス中は覚えるだけ）
        }
    }

    /// <summary>持ち上げた品を slot に置いて装備する（置くとつぶれて収まる）。</summary>
    private void Place(HeroEquipSlot slot)
    {
        var card = carried;
        if (card == null || slot == null) return;
        string id = card.ItemId;
        bool weapon = card.IsWeapon;
        var g = ghost;
        ghost = null;
        ClearCarry();
        if (g != null) UIMotion.DestroySafe(g.gameObject);
        DragFeel.Drop(slot.IconRoot, true);

        string current = weapon ? weaponSelectedId : armorSelectedId;
        if (id == current) return; // 付けているものを置き直しただけ（今のプルダウンと同じく何も起こさない）
        if (weapon) OnWeaponChanged.OnNext(id);
        else OnArmorChanged.OnNext(id);
    }

    /// <summary>持ち上げをやめる。silent=false ならゴーストが揺れて消え、パッドの選択をカードへ戻す。</summary>
    private void EndCarry(bool silent)
    {
        var card = carried;
        if (card == null) return;
        var g = ghost;
        ghost = null;
        ClearCarry();
        if (g == null) return;
        if (silent || UIMotion.IsSkip) UIMotion.DestroySafe(g.gameObject);
        else DragFeel.Drop(g, false, () => { if (g != null) UIMotion.DestroySafe(g.gameObject); });
        if (!silent && focusScope != null && card != null && card.gameObject.activeInHierarchy) focusScope.Select(card.Button);
    }

    private void ClearCarry()
    {
        var card = carried;
        carried = null;
        if (card != null)
        {
            card.SetCarried(false);
            var slot = SlotFor(card);
            if (slot != null) slot.SetCarryTarget(false);
            OnHoverExit.OnNext(Unit.Default);
        }
        SetPreviewVisible(false);
        if (focusScope != null) focusScope.SetCancelAction(null);
    }

    private const string CarryHint = "光っている枠に置くと装備する（カードをもう一度押しても付く。やめるときは B / Esc）";

    private RectTransform CreateGhost(HeroEquipmentCard card)
    {
        var src = card.IconBox != null ? card.IconBox : (RectTransform)card.transform;
        var canvas = GetComponentInParent<Canvas>();
        var root = canvas != null ? (RectTransform)canvas.rootCanvas.transform : (RectTransform)transform;
        var g = Instantiate(src, root, false);
        g.name = "HeroEquipGhost";
        g.anchorMin = g.anchorMax = g.pivot = new Vector2(0.5f, 0.5f);
        g.sizeDelta = src.rect.size * 1.25f;
        g.localScale = Vector3.one;
        g.localRotation = Quaternion.identity;
        foreach (var gr in g.GetComponentsInChildren<Graphic>(true)) gr.raycastTarget = false;
        g.SetAsLastSibling();
        return g;
    }

    private void MoveGhost(PointerEventData e)
    {
        if (ghost == null) return;
        var root = ghost.parent as RectTransform;
        if (root != null && RectTransformUtility.ScreenPointToWorldPointInRectangle(root, e.position, e.pressEventCamera, out var world))
            ghost.position = world;
    }

    private void HoverGhostOver(HeroEquipSlot slot)
    {
        if (ghost == null || slot == null) return;
        var rt = slot.GlowTarget;
        var r = rt.rect;
        // 枠の右上の角に重ねて浮かせる（種類の札と名前の板を隠さない）
        ghost.position = rt.TransformPoint(new Vector3(r.xMax + r.width * 0.05f, r.yMax - r.height * 0.1f, 0f));
    }

    // ───────── 付けた後の見込み ─────────

    private void ShowPreview(HeroEquipmentCard card)
    {
        var after = card.After;
        SetDelta(attackDeltaText, after.Attack - currentAttack, card.IsWeapon);
        SetDelta(defenseDeltaText, after.Defense - currentDefense, !card.IsWeapon);
        SetDelta(hpDeltaText, currentHp >= 0 ? after.Hp - currentHp : 0, false);
    }

    private static void SetDelta(TextMeshProUGUI text, int delta, bool alwaysShow)
    {
        if (text == null) return;
        var chip = ChipOf(text);
        bool show = delta != 0 || alwaysShow;
        chip.gameObject.SetActive(show);
        if (!show) return;
        text.text = delta > 0 ? $"+{delta}" : delta < 0 ? $"{delta}" : "±0";
        text.color = delta > 0 ? DeltaUp : delta < 0 ? DeltaDown : DeltaZero;
        UIMotion.Bump(chip, 1.15f, 0.18f, unscaled: true);
    }

    /// <summary>差の札（文字の親が札の板。親が無ければ文字そのもの）。</summary>
    private static Transform ChipOf(TextMeshProUGUI text)
        => text.transform.parent != null && text.transform.parent.name.EndsWith("Chip") ? text.transform.parent : text.transform;

    private void SetPreviewVisible(bool on)
    {
        foreach (var t in new[] { attackDeltaText, defenseDeltaText, hpDeltaText })
            if (t != null) ChipOf(t).gameObject.SetActive(on);
    }

    // ─────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────

    private void SetButtonDescription(Button button, string description)
    {
        if (button == null) return;
        buttonDescriptions[button] = description;
    }

    private static void SetIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite  = sprite;
        image.enabled = sprite != null;
        image.preserveAspect = true; // 装備の絵は縦長・横長がまちまちなので、枠に合わせて引き伸ばさない
    }

    private Sprite TacticsIcon(HeroTactics tactics)
    {
        int i = (int)tactics;
        return tacticsIcons != null && i >= 0 && i < tacticsIcons.Length ? tacticsIcons[i] : null;
    }

    /// <summary>「攻撃重視（武器↑）」。呼び名は HeroBattleInfluence に揃え、いちばん強く上がる値動きを添える。</summary>
    private static string GetTacticsLabel(HeroTactics tactics)
    {
        string tag = HeroBattleInfluence.GetStrongestEffectTag(tactics);
        string name = HeroBattleInfluence.GetDisplayName(tactics);
        return string.IsNullOrEmpty(tag) ? name : $"{name}（{tag}）";
    }

    /// <summary>
    /// 作戦の一言の説明。HeroDialogues.csv の hover_tactics_* の 2 文目（1 文目は「攻撃重視だ。」などの名乗り）。
    /// 例: 攻撃重視 →「敵を倒すたびの武器の値上がりと、話題性が強くなる」。
    /// </summary>
    private static string TacticsShortDescription(HeroTactics tactics)
    {
        string id = tactics switch
        {
            HeroTactics.Aggressive   => "hover_tactics_aggressive",
            HeroTactics.Defensive    => "hover_tactics_defensive",
            HeroTactics.Stealth      => "hover_tactics_stealth",
            HeroTactics.MagicFocused => "hover_tactics_magic",
            _                        => "hover_tactics_balanced",
        };
        string text = HeroDialogueLoader.Get(id);
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sentences = text.Split('。', StringSplitOptions.RemoveEmptyEntries);
        return sentences.Length >= 2 ? sentences[1].Trim() : sentences[0].Trim();
    }
}

public struct HeroEquipmentOption
{
    public string ItemId  { get; }
    /// <summary>一覧の行の表記（名前・属性・在庫・値段・能力値）。</summary>
    public string Label   { get; }
    /// <summary>閉じた枠に出す短い表記。空なら Label のまま。</summary>
    public string Caption { get; }

    // 棚のカードに出すもの（Presenter.BuildOption が入れる）
    public string Name { get; set; }
    public Sprite Icon { get; set; }
    public ItemTypeData.ItemAttribute Attribute { get; set; }
    /// <summary>「火」「水」…（空なら属性の札を出さない）。</summary>
    public string AttributeLabel { get; set; }
    public int Stock { get; set; }
    public int Price { get; set; }
    /// <summary>「攻撃+10%」／「防御+15% HP+5%」。</summary>
    public string BonusText { get; set; }
    /// <summary>次の配信先の弱点に合う（弱点を知っているときだけ true）。</summary>
    public bool IsWeakness { get; set; }
    /// <summary>付けているが店に無い品（「未所持」）。</summary>
    public bool NotOwned { get; set; }
    /// <summary>付けた後の戦う強さ（ドラッグ中に能力値の板へ差を出す）。</summary>
    public ClearProbabilityCalculator.HeroStats After { get; set; }

    public HeroEquipmentOption(string itemId, string label, string caption = null)
    {
        ItemId  = itemId;
        Label   = label;
        Caption = caption;
        Name = null;
        Icon = null;
        Attribute = default;
        AttributeLabel = null;
        Stock = 0;
        Price = 0;
        BonusText = null;
        IsWeakness = false;
        NotOwned = false;
        After = default;
    }
}

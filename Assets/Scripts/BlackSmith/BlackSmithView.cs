using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using R3;
using TMPro;

public class BlackSmithView : MonoBehaviour
{
    [Header("ScrollView")]
    [SerializeField] private ScrollRect scrollRect;            // ★ 追加
    [SerializeField] private GameObject blackSmithContent;     // scrollRect.content と一致させるのが望ましい

    [Header("Tabs")]
    [SerializeField] private GameObject weaponTab;
    [SerializeField] private GameObject armorTab;
    [SerializeField] private GameObject developmentTab;
    [SerializeField] private GameObject specialTab;

    [Header("Prefabs & Buttons")]
    [SerializeField] private GameObject itemShopSlotPrefab;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button weaponButton;
    [SerializeField] private Button armorButton;
    [SerializeField] private Button developButton;
    [SerializeField] private Button specialWeaponButton;

    [Header("オート購入")]
    [SerializeField] private Button autoBuyButton;
    [SerializeField] private TextMeshProUGUI autoBuyResultText;
    [SerializeField] private AutoBuyBudgetPopup autoBuyBudgetPopup;

    [Header("Dialogue")]
    [SerializeField] private TextMeshProUGUI dialogueText;
    [SerializeField] private Button characterButton;

    [Header("キャラ表情（バズ連動・任意）")]
    [SerializeField] private CharacterExpression characterExpression;

    [Header("Description")]
    [SerializeField] private TextMeshProUGUI itemDescriptionText;

    [Header("次ダンジョン情報バナー")]
    [SerializeField] private ProcurementHeaderView procurementHeader;

    [Tooltip("スクラップの付箋と朝刊の入り口（I-6・I-7。任意）")]
    [SerializeField] private ScrapStickyNotesUI stickyNotes;
    public ScrapStickyNotesUI StickyNotes => stickyNotes;

    [Header("所持金表示")]
    [Tooltip("鍛冶屋専用の所持金テキスト（鍛冶屋表示中はCommonViewを出さないため）")]
    [SerializeField] private TextMeshProUGUI playerMoneyText;

    [Header("選択銘柄 詳細パネル")]
    [SerializeField] private ItemDetailPanel itemDetailPanel;

    [Header("並べ替え")]
    [Tooltip("収益/需要/価格 の並べ替えドロップダウン（任意。0:収益 1:需要 2:価格）")]
    [SerializeField] private TMP_Dropdown sortDropdown;

    [Header("Development Panel")]
    [SerializeField] private GameObject developmentPanel;         // 開発タブ専用パネル（レベルアップUI）
    [SerializeField] private TextMeshProUGUI blackSmithLevelText; // 現在レベル表示
    [SerializeField] private TextMeshProUGUI levelUpCostText;     // レベルアップ費用表示
    [SerializeField] private Button levelUpButton;                // レベルアップボタン
    [SerializeField] private TextMeshProUGUI levelUpButtonText;   // ボタン内テキスト

    [Header("Development Panel - 次レベル解放プレビュー")]
    [SerializeField] private TextMeshProUGUI unlockHeaderText;    // 見出し（Lv.X で追加される商品）
    [SerializeField] private Transform unlockListRoot;            // エントリの親（GridLayoutGroup）
    [SerializeField] private GameObject unlockEntryTemplate;      // エントリ雛形（非アクティブで配置）

    [Header("Development Panel - 解放商品の詳細（レベルアップフレーム上部）")]
    [SerializeField] private GameObject unlockDetailContent;             // 詳細の中身（選択時に表示）
    [SerializeField] private TextMeshProUGUI unlockDetailPlaceholder;    // 未選択時の案内テキスト
    [SerializeField] private Image unlockDetailIcon;
    [SerializeField] private TextMeshProUGUI unlockDetailName;
    [SerializeField] private TextMeshProUGUI unlockDetailInfo;
    [SerializeField] private TextMeshProUGUI unlockDetailDescription;

    public Subject<Unit> OnCloseRequested { get; } = new();
    public Subject<BlackSmithTab> OnChangePanel { get; } = new();
    public Subject<Unit> OnLevelUpRequested { get; } = new();
    public Subject<Unit> OnAutoBuyRequested { get; } = new();
    public Subject<Unit> OnCharacterClicked { get; } = new();
    /// <summary>予算設定ポップアップで購入ボタンが押されたときに予算額と方針プリセットを通知する。</summary>
    public Subject<(int budget, AutoBuyStrategy strategy)> OnAutoBuyBudgetConfirmed { get; } = new();
    /// <summary>並べ替えモードが変更されたときに通知する。</summary>
    public Subject<BlackSmithSortMode> OnSortChanged { get; } = new();

    /// <summary>選択銘柄の詳細パネル。Presenter が選択時に結線する。</summary>
    public ItemDetailPanel DetailPanel => itemDetailPanel;

    /// <summary>次ダンジョン情報バナー。Presenter が Entry 時に更新する。</summary>
    public ProcurementHeaderView Header => procurementHeader;

    private readonly List<ItemShopSlot> activeSlots = new();

    private BlackSmithTab _currentTab = BlackSmithTab.Weapon;
    
    private readonly Dictionary<BlackSmithTab, Vector3> initTabPos = new();

    [VContainer.Inject]
    public void Construct(BuzzSystem buzzSystem)
    {
        if (characterExpression != null) characterExpression.Bind(buzzSystem);
    }

    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
        weaponButton.onClick.AddListener(() => { _currentTab = BlackSmithTab.Weapon; OnChangePanel.OnNext(BlackSmithTab.Weapon); });
        armorButton.onClick.AddListener(() => { _currentTab = BlackSmithTab.Armor; OnChangePanel.OnNext(BlackSmithTab.Armor); });
        developButton.onClick.AddListener(() => { _currentTab = BlackSmithTab.Development; OnChangePanel.OnNext(BlackSmithTab.Development); });
        specialWeaponButton.onClick.AddListener(() => { _currentTab = BlackSmithTab.Special; OnChangePanel.OnNext(BlackSmithTab.Special); });

        if (levelUpButton)
            levelUpButton.onClick.AddListener(() => OnLevelUpRequested.OnNext(Unit.Default));

        if (autoBuyButton != null)
            autoBuyButton.onClick.AddListener(() => OnAutoBuyRequested.OnNext(Unit.Default));

        // TomsShop.unity の鍛冶屋インスタンスで予算ポップアップが「表示」に上書きされている（#77 以来）ため、
        // シーンを読み込むたびに Show を通らない（予算欄が空・所持金が既定の文字の）ポップアップが出ていた。
        // まだ Show で開いていなければここで閉じる（チュートリアルで先に Show された場合は閉じない）
        if (autoBuyBudgetPopup != null && !autoBuyBudgetPopup.IsOpenedByShow)
            autoBuyBudgetPopup.gameObject.SetActive(false);

        if (autoBuyBudgetPopup != null)
            autoBuyBudgetPopup.OnConfirmClicked
                .Subscribe(budget => OnAutoBuyBudgetConfirmed.OnNext((budget, autoBuyBudgetPopup.SelectedStrategy)))
                .AddTo(this);

        if (characterButton != null)
        {
            characterButton.onClick.AddListener(() => OnCharacterClicked.OnNext(Unit.Default));
            characterButton.navigation = new Navigation { mode = Navigation.Mode.None }; // 立ち絵はパッドで選ばない（画面より大きい枠が出るため）
        }

        if (sortDropdown != null)
            sortDropdown.onValueChanged.AddListener(v => OnSortChanged.OnNext((BlackSmithSortMode)v));

        // 開発パネルは初期非表示
        if (developmentPanel)
            developmentPanel.SetActive(false);

        SetupPadFocus();

        initTabPos[BlackSmithTab.Weapon] = weaponTab.transform.localPosition;
        initTabPos[BlackSmithTab.Armor] = armorTab.transform.localPosition;
        initTabPos[BlackSmithTab.Development] = developmentTab.transform.localPosition;
        initTabPos[BlackSmithTab.Special] = specialTab.transform.localPosition;
    }

    // ------------------------------------------------------------------ パッド操作

    private UIFocusScope focusScope;

    /// <summary>
    /// 鍛冶屋画面をフォーカス単位にする。B=もどる、LB/RB=タブ切替。
    /// 初期フォーカスは一覧の先頭行（PopulateItemList で更新）。
    /// </summary>
    private void SetupPadFocus()
    {
        focusScope = UIFocusScope.GetOrAdd(gameObject);
        if (focusScope == null) return;
        // B: 個数設定にフォーカスがあれば一覧の同じ行へ戻る。それ以外は従来どおり「もどる」
        if (!focusScope.HasCancel)
        {
            focusScope.SetCancelAction(() =>
            {
                if (TryReturnToRow()) return;
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null && closeButton != null && closeButton.IsInteractable())
                    UnityEngine.EventSystems.ExecuteEvents.Execute(closeButton.gameObject,
                        new UnityEngine.EventSystems.BaseEventData(es), UnityEngine.EventSystems.ExecuteEvents.submitHandler);
                else if (closeButton != null)
                    closeButton.onClick.Invoke();
            });
        }
        focusScope.OnButton
            .Where(b => b == ControllerButton.LB || b == ControllerButton.RB)
            .Subscribe(b => CycleTab(b == ControllerButton.RB ? 1 : -1))
            .AddTo(this);
        focusScope.OnButton
            .Where(b => b == ControllerButton.X)
            .Subscribe(_ => FocusQuantityFromRow())
            .AddTo(this);

        // 一覧はパッドの選択に自動スクロール
        if (scrollRect != null && scrollRect.GetComponent<ScrollRectAutoFollow>() == null)
            scrollRect.gameObject.AddComponent<ScrollRectAutoFollow>();
    }

    /// <summary>表示中のタブボタンを左右に巡回して押す（LB/RB）。</summary>
    private void CycleTab(int dir)
    {
        var tabs = new List<(Button button, BlackSmithTab tab)>();
        void Add(Button b, BlackSmithTab t) { if (b != null && b.gameObject.activeInHierarchy && b.IsInteractable()) tabs.Add((b, t)); }
        Add(weaponButton, BlackSmithTab.Weapon);
        Add(armorButton, BlackSmithTab.Armor);
        Add(developButton, BlackSmithTab.Development);
        Add(specialWeaponButton, BlackSmithTab.Special);
        if (tabs.Count < 2) return;
        int index = Mathf.Max(0, tabs.FindIndex(t => t.tab == _currentTab));
        int next = (index + dir + tabs.Count) % tabs.Count;
        tabs[next].button.onClick.Invoke();
        StartCoroutine(FocusAfterTabChange());
    }

    /// <summary>
    /// タブ切替後、新しいタブの先頭（一覧の先頭行 / 開発タブはタブ自身）へフォーカスを移す。
    /// 一覧の再構築・旧行の破棄が1フレーム遅れても確実に移るよう、即時＋次フレームで行う。
    /// マウス操作中は UIFocusScope.Select が記憶だけにする。
    /// </summary>
    private IEnumerator FocusAfterTabChange()
    {
        FocusTabContent();
        yield return null;
        FocusTabContent();
    }

    private void FocusTabContent()
    {
        if (focusScope == null) return;
        var first = focusScope.FirstSelected;
        if (first == null || !UIFocusManager.IsUsable(first.gameObject)) return;
        padReturnSlot = null;
        focusScope.Select(first);
    }

    // ------------------------------------------------------------------ X: 個数設定へ / B: 一覧へ戻る

    private ItemShopSlot padReturnSlot;

    /// <summary>一覧の行にフォーカスがある状態で X → その行を選択（A と同じ）し、個数設定へフォーカスを移す。</summary>
    private void FocusQuantityFromRow()
    {
        if (focusScope == null || itemDetailPanel == null) return;
        var es = UnityEngine.EventSystems.EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        if (sel == null) return;

        ItemShopSlot row = null;
        foreach (var s in activeSlots)
        {
            if (s != null && sel.transform.IsChildOf(s.transform)) { row = s; break; }
        }
        if (row == null) return;

        // A（Submit）と同じ: 行クリック → Presenter が銘柄選択・詳細パネルを結線
        row.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(es));

        var target = itemDetailPanel.QuantityFocusTarget;
        if (target == null) return;
        padReturnSlot = row;
        focusScope.Select(target);
    }

    /// <summary>個数設定（詳細パネル）にフォーカスがあれば、記憶した一覧の行へ戻る。戻したら true。</summary>
    private bool TryReturnToRow()
    {
        if (itemDetailPanel == null) return false;
        var es = UnityEngine.EventSystems.EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        if (!itemDetailPanel.Contains(sel)) return false;

        var row = padReturnSlot;
        if (row == null) row = activeSlots.Find(s => s != null && s.IsSelected);
        padReturnSlot = null;
        var selectable = row != null ? row.GetComponent<Selectable>() : null;
        if (selectable == null || !UIFocusManager.IsUsable(selectable.gameObject)) return false;
        focusScope.Select(selectable);
        return true;
    }

    /// <summary>
    /// 初期フォーカスを更新し、必要ならそこへ選択を移す。
    /// タブ・並べ替えを操作中（選択がそれらのボタン）なら選択は動かさない。
    /// </summary>
    private void UpdatePadFocus(Selectable first)
    {
        if (focusScope == null) return;
        focusScope.FirstSelected = first;
        if (first == null) return;

        var es = UnityEngine.EventSystems.EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        bool keep = sel != null && UIFocusManager.IsUsable(sel)
            && (IsObjectOf(sel, weaponButton) || IsObjectOf(sel, armorButton)
                || IsObjectOf(sel, developButton) || IsObjectOf(sel, specialWeaponButton)
                || (sortDropdown != null && sel.transform.IsChildOf(sortDropdown.transform)));
        if (!keep) focusScope.Select(first);
    }

    private static bool IsObjectOf(GameObject go, Component component) =>
        component != null && go == component.gameObject;

    /// <summary>
    /// 商品リストを表示
    /// </summary>
    public List<ItemShopSlot> PopulateItemList(List<RuntimeItemData> runtimeItems)
    {
        ClearCatalog();

        // 再生成（上から順にフェード＋ポップで登場させる）
        List<ItemShopSlot> slots = new();
        int index = 0;
        foreach (var item in runtimeItems)
        {
            var slot = CreateCatalogSlot(index++);
            slot.SetItem(
                item.ItemId,
                item.ItemName,
                item.ItemIcon,
                item.ItemBackground,
                item.CurrentPrice.Value,
                item.MaxStock.Value,
                item.Stock.Value,
                item.IsPopular.Value
            );
            slots.Add(slot);
            activeSlots.Add(slot);
        }

        ResetScroll();
        // #3 商品棚の行を上から順に出す（画面に収まる先頭の行だけ。後ろの行は最初から出しておく）
        var rows = new List<RectTransform>();
        for (int i = 0; i < slots.Count && rows.Count < StaggerRowLimit; i++)
            if (slots[i] != null && slots[i].transform is RectTransform rt) rows.Add(rt);
        UIMotion.Stagger(rows, 0.035f);
        UpdatePadFocus(slots.Count > 0 ? slots[0].GetComponent<Selectable>() : weaponButton);
        return slots;
    }

    /// <summary>順に出す行の上限（それ以降はスクロールしないと見えないので待たせない）。</summary>
    private const int StaggerRowLimit = 10;

    /// <summary>カタログリストの全行を破棄する。</summary>
    private void ClearCatalog()
    {
        for (int i = blackSmithContent.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(blackSmithContent.transform.GetChild(i).gameObject);
        }
        activeSlots.Clear();
    }

    /// <summary>行を1つ生成する（登場演出は PopulateItemList でまとめて #3 を掛ける）。</summary>
    private ItemShopSlot CreateCatalogSlot(int index)
    {
        var slotObj = Instantiate(itemShopSlotPrefab, blackSmithContent.transform);
        return slotObj.GetComponent<ItemShopSlot>();
    }

    /// <summary>スクロール位置を先頭にリセットする。</summary>
    private void ResetScroll()
    {
        if (scrollRect)
            scrollRect.normalizedPosition = new Vector2(0, 1);
    }

    private int displayedMoney;
    private bool moneyInitialized;
    private Tween moneyTween;

    /// <summary>鍛冶屋専用の所持金表示を更新する（カウントアップ演出付き）。</summary>
    public void UpdatePlayerMoney(int money)
    {
        if (playerMoneyText == null) return;

        // 初回は即時反映（画面を開いた瞬間に0からカウントさせない）
        if (!moneyInitialized || !playerMoneyText.gameObject.activeInHierarchy)
        {
            moneyInitialized = true;
            displayedMoney = money;
            playerMoneyText.text = $"{money:N0}G";
            return;
        }

        if (displayedMoney == money) return;

        // #2 数字を回して財布を弾ませる（共通部品。省略モードでは即最終の額）
        int from = displayedMoney;
        displayedMoney = money;
        moneyTween = UIMotion.CountTo(playerMoneyText, from, money, v => $"{v:N0}G", 0.35f);
        UIMotion.Bump(playerMoneyText.transform, 1.1f, 0.25f);
    }

    public void ShowDialogue(string message)
    {
        if (dialogueText != null) dialogueText.text = message;
    }

    public void SetDescription(string description)
    {
        if (itemDescriptionText != null) itemDescriptionText.text = description;
    }

    public void ShowBudgetPopup(int playerMoney, bool locked = false)
    {
        if (autoBuyBudgetPopup != null)
            autoBuyBudgetPopup.Show(playerMoney, locked);
    }

    // ========================================
    // 会話チュートリアル用の操作制限
    // ========================================

    private bool tutorialAutoBuyOnly;
    private Tween autoBuyPulseTween;
    private Vector3 autoBuyBaseScale = Vector3.one;

    /// <summary>
    /// 初回チュートリアルの「オート購入だけ押せる」状態を切り替える。
    /// 閉じる・タブ・並べ替え・商品一覧・詳細パネル（個別の仕入れ）を押せなくし、
    /// オート購入ボタンを脈動させて誘導する。Presenter 側でも入力を無視する（二重の安全策）。
    /// </summary>
    public void SetTutorialAutoBuyOnly(bool on)
    {
        tutorialAutoBuyOnly = on;
        bool free = !on;

        SetInteractableSafe(closeButton, free);
        SetInteractableSafe(weaponButton, free);
        SetInteractableSafe(armorButton, free);
        SetInteractableSafe(developButton, free);
        SetInteractableSafe(specialWeaponButton, free);
        if (sortDropdown != null) sortDropdown.interactable = free;

        // 商品一覧（行クリックは IPointerClickHandler なのでレイキャストごと止める）と詳細パネル
        if (scrollRect != null) SetCanvasGroupLock(scrollRect.gameObject, on);
        if (itemDetailPanel != null) SetCanvasGroupLock(itemDetailPanel.gameObject, on);

        // オート購入ボタンの脈動
        if (autoBuyButton != null)
        {
            autoBuyButton.interactable = true;
            var t = autoBuyButton.transform;
            if (autoBuyPulseTween != null)
            {
                // 脈動を止めて元の大きさへ戻す（脈動していないときは触らない）
                autoBuyPulseTween.Kill();
                autoBuyPulseTween = null;
                t.localScale = autoBuyBaseScale;
            }
            if (on)
            {
                autoBuyBaseScale = t.localScale;
                autoBuyPulseTween = t.DOScale(autoBuyBaseScale * 1.12f, 0.45f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetLink(autoBuyButton.gameObject);
            }
        }
    }

    /// <summary>チュートリアルの「オート購入だけ」状態か。</summary>
    public bool IsTutorialAutoBuyOnly => tutorialAutoBuyOnly;

    private static void SetInteractableSafe(Selectable selectable, bool interactable)
    {
        if (selectable != null) selectable.interactable = interactable;
    }

    private static void SetCanvasGroupLock(GameObject target, bool locked)
    {
        if (!target.TryGetComponent<CanvasGroup>(out var cg))
        {
            if (!locked) return; // 解除時にわざわざ足す必要はない
            cg = target.AddComponent<CanvasGroup>();
        }
        cg.interactable = !locked;
        cg.blocksRaycasts = !locked;
    }

    public void HideBudgetPopup()
    {
        if (autoBuyBudgetPopup != null)
            autoBuyBudgetPopup.Hide();
    }

    public void ShowAutoBuyResult(List<AutoPurchaseResult> results, int playerMoney)
    {
        if (autoBuyResultText == null) return;

        if (results == null || results.Count == 0)
        {
            autoBuyResultText.text = "購入できるアイテムがありません";
            return;
        }

        int total = 0;
        var sb = new System.Text.StringBuilder("【オート購入】\n");
        foreach (var r in results)
        {
            sb.AppendLine($"  {r.ItemName} ×{r.Quantity}  {r.TotalCost:N0}G");
            total += r.TotalCost;
        }
        sb.Append($"合計 {total:N0}G  残金 {playerMoney:N0}G");
        autoBuyResultText.text = sb.ToString();
    }

    public void SortItemTab(BlackSmithTab type)
    {
        MoveTab(weaponTab, BlackSmithTab.Weapon, type);
        MoveTab(armorTab, BlackSmithTab.Armor, type);
        MoveTab(developmentTab, BlackSmithTab.Development, type);
        MoveTab(specialTab, BlackSmithTab.Special, type);
    }

    /// <summary>
    /// 選択タブだけ少し持ち上げ、他は基準位置へ戻す。
    /// 既存Tweenを必ず殺してから動かす（多重Tweenでタブが浮きっぱなしになるのを防ぐ）。
    /// </summary>
    private void MoveTab(GameObject tab, BlackSmithTab tabType, BlackSmithTab selected)
    {
        if (tab == null) return;

        tab.transform.DOKill();
        float baseY = initTabPos[tabType].y;
        bool isSelected = tabType == selected;
        tab.transform.DOLocalMoveY(isSelected ? baseY + 10 : baseY, isSelected ? 0.2f : 0.1f).SetLink(tab);
    }

    public void SwitchPanel(BlackSmithTab tab)
    {
        bool isDevelopment = tab == BlackSmithTab.Development;

        if (scrollRect)        scrollRect.gameObject.SetActive(!isDevelopment);
        if (developmentPanel)  developmentPanel.SetActive(isDevelopment);

        // #7 タブを替えたら中身を横から滑り込ませる（右のタブへ移ったら右から）。初回・同じタブは動かさない
        if (lastSwitchedTab.HasValue && lastSwitchedTab.Value != tab)
        {
            var content = isDevelopment
                ? (developmentPanel != null ? developmentPanel.transform as RectTransform : null)
                : (scrollRect != null ? scrollRect.transform as RectTransform : null);
            TabSwitcher.SlideContent(content, TabOrder(tab) > TabOrder(lastSwitchedTab.Value) ? 1 : -1);
        }
        lastSwitchedTab = tab;
        // 並べ替えUIは武具の指標（収益/需要/価格）専用なので武具タブ以外では隠す
        if (sortDropdown)      sortDropdown.gameObject.SetActive(tab == BlackSmithTab.Weapon || tab == BlackSmithTab.Armor);
        // 開発タブ: レベルアップ（支払い）は初期フォーカスにしない。タブボタン自体に置く
        if (isDevelopment) UpdatePadFocus(developButton);
    }

    private BlackSmithTab? lastSwitchedTab;
    private int shownSmithLevel;

    /// <summary>タブの並び（画面の左から）。</summary>
    private static int TabOrder(BlackSmithTab tab) => tab switch
    {
        BlackSmithTab.Weapon => 0,
        BlackSmithTab.Armor => 1,
        BlackSmithTab.Special => 2,
        BlackSmithTab.Development => 3,
        _ => 0,
    };

    private readonly List<GameObject> unlockEntries = new();

    /// <summary>
    /// 次レベルで解放される商品のプレビューを更新する。
    /// </summary>
    public void UpdateUnlockPreview(int nextLevel, bool isMax, List<UnlockItemDisplayData> items)
    {
        foreach (var e in unlockEntries) Destroy(e);
        unlockEntries.Clear();

        if (unlockHeaderText)
        {
            if (isMax)
                unlockHeaderText.text = "最大レベル：これ以上追加される商品はありません";
            else if (items == null || items.Count == 0)
                unlockHeaderText.text = $"Lv.{nextLevel} で追加される商品はありません";
            else
                unlockHeaderText.text = $"Lv.{nextLevel} で追加される商品";
        }

        // リストを作り直すタイミングで詳細は未選択状態に戻す
        ResetUnlockDetail();

        if (isMax || items == null || unlockListRoot == null || unlockEntryTemplate == null) return;

        foreach (var item in items)
        {
            var entry = Instantiate(unlockEntryTemplate, unlockListRoot);
            entry.SetActive(true);

            var icon = FindChildComponent<Image>(entry.transform, "Icon");
            var nameText = FindChildComponent<TextMeshProUGUI>(entry.transform, "Name");
            var infoText = FindChildComponent<TextMeshProUGUI>(entry.transform, "Info");

            if (icon)
            {
                icon.sprite = item.Icon;
                icon.preserveAspect = true;
                icon.enabled = item.Icon != null;
            }
            if (nameText) nameText.text = item.Name;
            if (infoText) infoText.text = item.Info;

            // タップで詳細を表示
            var button = entry.GetComponent<Button>();
            if (button != null)
            {
                var captured = item;
                button.onClick.AddListener(() => ShowUnlockDetail(captured));
                // パッド操作: 選択しただけで詳細を出す（マウスのタップと同じ表示）
                var relay = PadSelectRelay.GetOrAdd(entry);
                if (relay != null) relay.Selected += () => ShowUnlockDetail(captured);
            }

            unlockEntries.Add(entry);
        }

        // #3 次のレベルで並ぶ商品を順に出す
        var rows = new List<RectTransform>();
        foreach (var e in unlockEntries) if (e != null && e.transform is RectTransform rt) rows.Add(rt);
        if (unlockListRoot.gameObject.activeInHierarchy) UIMotion.Stagger(rows, 0.05f);
    }

    private static T FindChildComponent<T>(Transform parent, string childName) where T : Component
    {
        var child = parent.Find(childName);
        return child != null ? child.GetComponent<T>() : null;
    }

    /// <summary>解放商品の詳細を表示する（レベルアップフレーム上部の詳細欄）。</summary>
    public void ShowUnlockDetail(UnlockItemDisplayData item)
    {
        if (unlockDetailContent) unlockDetailContent.SetActive(true);
        if (unlockDetailPlaceholder) unlockDetailPlaceholder.gameObject.SetActive(false);

        if (unlockDetailIcon)
        {
            unlockDetailIcon.sprite = item.Icon;
            unlockDetailIcon.preserveAspect = true;
            unlockDetailIcon.enabled = item.Icon != null;
        }
        if (unlockDetailName) unlockDetailName.text = item.Name;
        if (unlockDetailInfo) unlockDetailInfo.text = item.Info;
        if (unlockDetailDescription) unlockDetailDescription.text = item.Description;
    }

    /// <summary>解放商品の詳細を未選択状態（案内表示）に戻す。</summary>
    private void ResetUnlockDetail()
    {
        if (unlockDetailContent) unlockDetailContent.SetActive(false);
        if (unlockDetailPlaceholder) unlockDetailPlaceholder.gameObject.SetActive(true);
    }

    /// <summary>
    /// 開発パネルのレベル・コスト表示を更新する
    /// </summary>
    public void UpdateDevelopmentPanel(int currentLevel, int maxLevel, int cost, int playerMoney)
    {
        bool isMax = currentLevel >= maxLevel;

        if (blackSmithLevelText)
        {
            blackSmithLevelText.text = $"鍛冶屋 Lv.{currentLevel}";
            // レベルが上がった瞬間だけ弾ませる
            if (shownSmithLevel > 0 && currentLevel > shownSmithLevel) UIMotion.Bump(blackSmithLevelText.transform, 1.25f, 0.3f);
            shownSmithLevel = currentLevel;
        }

        if (isMax)
        {
            if (levelUpCostText) levelUpCostText.text = "MAX";
            if (levelUpButtonText) levelUpButtonText.text = "最大レベル";
            if (levelUpButton) levelUpButton.interactable = false;
        }
        else
        {
            if (levelUpCostText) levelUpCostText.text = $"{cost:N0}G";
            if (levelUpButtonText) levelUpButtonText.text = "レベルアップ";
            if (levelUpButton) levelUpButton.interactable = playerMoney >= cost;
        }
    }
}

/// <summary>次レベル解放プレビュー1件分の表示データ。</summary>
public class UnlockItemDisplayData
{
    public Sprite Icon;
    public string Name;
    public string Info;        // 例: 「武器・火属性・1,200G」
    public string Description; // アイテム説明文（詳細表示用）
}

public enum BlackSmithTab
{
    Weapon,
    Armor,
    Development,
    Special
}

/// <summary>仕入れ一覧の並べ替えモード（ドロップダウンの index と一致）。</summary>
public enum BlackSmithSortMode
{
    Recommend = 0, // 期待収益（おすすめ順）
    Demand = 1,    // 需要
    Price = 2      // 価格
}

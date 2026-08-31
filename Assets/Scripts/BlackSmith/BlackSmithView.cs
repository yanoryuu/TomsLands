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
    [SerializeField] private TMPro.TextMeshProUGUI autoBuyResultText;
    [SerializeField] private AutoBuyBudgetPopup autoBuyBudgetPopup;

    [Header("Dialogue")]
    [SerializeField] private TextMeshProUGUI dialogueText;
    [SerializeField] private Button characterButton;

    [Header("Description")]
    [SerializeField] private TextMeshProUGUI itemDescriptionText;

    [Header("次ダンジョン情報バナー")]
    [SerializeField] private ProcurementHeaderView procurementHeader;

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

    public Subject<Unit> OnCloseRequested { get; private set; } = new();
    public Subject<BlackSmithTab> OnChangePanel { get; private set; } = new();
    public Subject<Unit> OnLevelUpRequested { get; private set; } = new();
    public Subject<Unit> OnAutoBuyRequested { get; private set; } = new();
    public Subject<Unit> OnCharacterClicked { get; private set; } = new();
    /// <summary>予算設定ポップアップで購入ボタンが押されたときに予算額と方針プリセットを通知する。</summary>
    public Subject<(int budget, AutoBuyStrategy strategy)> OnAutoBuyBudgetConfirmed { get; private set; } = new();
    /// <summary>並べ替えモードが変更されたときに通知する。</summary>
    public Subject<BlackSmithSortMode> OnSortChanged { get; private set; } = new();

    /// <summary>選択銘柄の詳細パネル。Presenter が選択時に結線する。</summary>
    public ItemDetailPanel DetailPanel => itemDetailPanel;

    /// <summary>次ダンジョン情報バナー。Presenter が Entry 時に更新する。</summary>
    public ProcurementHeaderView Header => procurementHeader;

    private readonly List<ItemShopSlot> activeSlots = new();

    private BlackSmithTab _currentTab = BlackSmithTab.Weapon;
    
    private readonly Dictionary<BlackSmithTab, Vector3> initTabPos = new();

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

        if (autoBuyBudgetPopup != null)
            autoBuyBudgetPopup.OnConfirmClicked.Subscribe(budget =>
                OnAutoBuyBudgetConfirmed.OnNext((budget, autoBuyBudgetPopup.SelectedStrategy)));

        if (characterButton != null)
            characterButton.onClick.AddListener(() => OnCharacterClicked.OnNext(Unit.Default));

        if (sortDropdown != null)
            sortDropdown.onValueChanged.AddListener(v => OnSortChanged.OnNext((BlackSmithSortMode)v));

        // 開発パネルは初期非表示
        if (developmentPanel)
            developmentPanel.SetActive(false);

        initTabPos[BlackSmithTab.Weapon] = weaponTab.transform.localPosition;
        initTabPos[BlackSmithTab.Armor] = armorTab.transform.localPosition;
        initTabPos[BlackSmithTab.Development] = developmentTab.transform.localPosition;
        initTabPos[BlackSmithTab.Special] = specialTab.transform.localPosition;
    }

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
        return slots;
    }

    /// <summary>カタログリストの全行を破棄する。</summary>
    private void ClearCatalog()
    {
        for (int i = blackSmithContent.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(blackSmithContent.transform.GetChild(i).gameObject);
        }
        activeSlots.Clear();
    }

    /// <summary>行を1つ生成し、登場演出（フェード＋ポップ）を付ける。</summary>
    private ItemShopSlot CreateCatalogSlot(int index)
    {
        var slotObj = Instantiate(itemShopSlotPrefab, blackSmithContent.transform);
        var slot = slotObj.GetComponent<ItemShopSlot>();

        var cg = slotObj.GetComponent<CanvasGroup>();
        if (cg == null) cg = slotObj.AddComponent<CanvasGroup>();
        float delay = Mathf.Min(index * 0.035f, 0.35f); // 後半はまとめて出す
        cg.alpha = 0f;
        cg.DOFade(1f, 0.18f).SetDelay(delay).SetLink(slotObj);
        slotObj.transform.localScale = Vector3.one * 0.94f;
        slotObj.transform.DOScale(1f, 0.22f).SetDelay(delay).SetEase(Ease.OutCubic).SetLink(slotObj);

        return slot;
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

        moneyTween?.Kill();
        moneyTween = DOTween.To(() => displayedMoney, x =>
            {
                displayedMoney = x;
                playerMoneyText.text = $"{x:N0}G";
            }, money, 0.35f)
            .SetEase(Ease.OutCubic)
            .SetLink(playerMoneyText.gameObject);

        playerMoneyText.transform.DOKill(true);
        playerMoneyText.transform.DOPunchScale(Vector3.one * 0.1f, 0.25f, 6, 0.7f)
            .SetLink(playerMoneyText.gameObject);
    }

    public void ShowDialogue(string message)
    {
        if (dialogueText != null) dialogueText.text = message;
    }

    public void SetDescription(string description)
    {
        if (itemDescriptionText != null) itemDescriptionText.text = description;
    }

    public void ShowBudgetPopup(int playerMoney)
    {
        if (autoBuyBudgetPopup != null)
            autoBuyBudgetPopup.Show(playerMoney);
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
        tab.transform.DOLocalMoveY(isSelected ? baseY + 10 : baseY, isSelected ? 0.2f : 0.1f);
    }

    public void SwitchPanel(BlackSmithTab tab)
    {
        bool isDevelopment = tab == BlackSmithTab.Development;

        if (scrollRect)        scrollRect.gameObject.SetActive(!isDevelopment);
        if (developmentPanel)  developmentPanel.SetActive(isDevelopment);
        // 並べ替えUIは武具の指標（収益/需要/価格）専用なので武具タブ以外では隠す
        if (sortDropdown)      sortDropdown.gameObject.SetActive(tab == BlackSmithTab.Weapon || tab == BlackSmithTab.Armor);
    }

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

            var icon = entry.transform.Find("Icon")?.GetComponent<Image>();
            var nameText = entry.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            var infoText = entry.transform.Find("Info")?.GetComponent<TextMeshProUGUI>();

            if (icon)
            {
                icon.sprite = item.Icon;
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
            }

            unlockEntries.Add(entry);
        }
    }

    /// <summary>解放商品の詳細を表示する（レベルアップフレーム上部の詳細欄）。</summary>
    public void ShowUnlockDetail(UnlockItemDisplayData item)
    {
        if (unlockDetailContent) unlockDetailContent.SetActive(true);
        if (unlockDetailPlaceholder) unlockDetailPlaceholder.gameObject.SetActive(false);

        if (unlockDetailIcon)
        {
            unlockDetailIcon.sprite = item.Icon;
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
            blackSmithLevelText.text = $"鍛冶屋 Lv.{currentLevel}";

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

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

    [Header("Development Panel")]
    [SerializeField] private GameObject developmentPanel;         // 開発タブ専用パネル（レベルアップUI）
    [SerializeField] private TextMeshProUGUI blackSmithLevelText; // 現在レベル表示
    [SerializeField] private TextMeshProUGUI levelUpCostText;     // レベルアップ費用表示
    [SerializeField] private Button levelUpButton;                // レベルアップボタン
    [SerializeField] private TextMeshProUGUI levelUpButtonText;   // ボタン内テキスト

    public Subject<Unit> OnCloseRequested { get; private set; } = new();
    public Subject<BlackSmithTab> OnChangePanel { get; private set; } = new();
    public Subject<Unit> OnLevelUpRequested { get; private set; } = new();
    public Subject<Unit> OnAutoBuyRequested { get; private set; } = new();
    public Subject<Unit> OnCharacterClicked { get; private set; } = new();
    /// <summary>予算設定ポップアップで購入ボタンが押されたときに予算額を通知する。</summary>
    public Subject<int> OnAutoBuyBudgetConfirmed { get; private set; } = new();

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
            autoBuyBudgetPopup.OnConfirmClicked.Subscribe(budget => OnAutoBuyBudgetConfirmed.OnNext(budget));

        if (characterButton != null)
            characterButton.onClick.AddListener(() => OnCharacterClicked.OnNext(Unit.Default));

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
        // 既存スロット破棄（Content配下の全子オブジェクトを削除）
        for (int i = blackSmithContent.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(blackSmithContent.transform.GetChild(i).gameObject);
        }
        activeSlots.Clear();

        // 再生成
        List<ItemShopSlot> slots = new();
        foreach (var item in runtimeItems)
        {
            var slotObj = Instantiate(itemShopSlotPrefab, blackSmithContent.transform);
            var slot = slotObj.GetComponent<ItemShopSlot>();
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

        // スクロール位置を先頭にリセット
        if (scrollRect)
            scrollRect.normalizedPosition = new Vector2(0, 1);

        return slots;
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
        var weaponSeq =  weaponTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Weapon].y, 0.1f);
        var armorSeq = armorTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Armor].y, 0.1f);
        var developmentSeq = developmentTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Development].y, 0.1f);
        var specialSeq = specialTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Special].y, 0.1f);
        
        // タブを一番上に持ってくる動作はそのまま
        switch (type)
        {
            case BlackSmithTab.Weapon:
                weaponSeq.Kill();
                weaponTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Weapon].y+10, 0.2f);
                break;
            case BlackSmithTab.Armor:
                armorSeq.Kill();
                armorTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Armor].y + 10, 0.2f);
                break;
            case BlackSmithTab.Development:
                developmentSeq.Kill();
                developmentTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Development].y + 10, 0.2f);
                break;
            case BlackSmithTab.Special:
                specialSeq.Kill();
                specialTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Special].y + 10, 0.2f);
                break;
        }
    }

    public void SwitchPanel(BlackSmithTab tab)
    {
        bool isDevelopment = tab == BlackSmithTab.Development;

        if (scrollRect)        scrollRect.gameObject.SetActive(!isDevelopment);
        if (developmentPanel)  developmentPanel.SetActive(isDevelopment);
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

public enum BlackSmithTab
{
    Weapon,
    Armor,
    Development,
    Special
}

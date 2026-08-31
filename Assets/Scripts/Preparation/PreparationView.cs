using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 準備シーン（出撃準備）の View。
/// 借入（初期資金レバレッジ）・難易度・スターターレリック・スタートダッシュを設定して出撃する。
/// 参照は未配線（null）でも動作する。departButton が未配線の間、Presenter は旧挙動
/// （即 TomsShop へ遷移）にフォールバックする。
/// </summary>
public class PreparationView : MonoBehaviour
{
    [Header("ヘッダー")]
    [SerializeField] private TextMeshProUGUI metaCurrencyText;
    [SerializeField] private TextMeshProUGUI difficultyText;
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("借入（初期資金レバレッジ）")]
    [SerializeField] private TextMeshProUGUI borrowAmountText;
    [SerializeField] private Button borrowPlusButton;
    [SerializeField] private Button borrowMinusButton;
    [SerializeField] private TextMeshProUGUI creditLineText;
    [SerializeField] private Button creditUpgradeButton;
    [SerializeField] private TextMeshProUGUI creditUpgradeCostText;

    [Header("難易度選択")]
    [SerializeField] private Button easyButton;
    [SerializeField] private GameObject easyCheck;
    [SerializeField] private Button normalButton;
    [SerializeField] private GameObject normalCheck;
    [SerializeField] private Button hardButton;
    [SerializeField] private GameObject hardCheck;

    [Header("カタログ共通")]
    [SerializeField] private GameObject choiceSlotPrefab;

    [Header("スターターレリック")]
    [SerializeField] private Transform relicCatalogParent;

    [Header("スタートダッシュ")]
    [SerializeField] private Button flyerButton;
    [SerializeField] private TextMeshProUGUI flyerLabelText;
    [SerializeField] private GameObject flyerCheck;
    [SerializeField] private Button appraisalButton;
    [SerializeField] private TextMeshProUGUI appraisalLabelText;
    [SerializeField] private GameObject appraisalCheck;
    [SerializeField] private Button graceButton;
    [SerializeField] private TextMeshProUGUI graceLabelText;
    [SerializeField] private GameObject graceCheck;

    [Header("出撃 / 戻る")]
    [SerializeField] private Button departButton;
    [SerializeField] private Button backButton;

    public Subject<Unit> OnBorrowPlus { get; } = new();
    public Subject<Unit> OnBorrowMinus { get; } = new();
    public Subject<GameModeId> OnDifficultySelected { get; } = new();
    public Subject<Unit> OnCreditUpgrade { get; } = new();
    public Subject<Unit> OnFlyerToggled { get; } = new();
    public Subject<Unit> OnAppraisalToggled { get; } = new();
    public Subject<Unit> OnGraceToggled { get; } = new();
    public Subject<Unit> OnDepart { get; } = new();
    public Subject<Unit> OnBack { get; } = new();

    /// <summary>UIが最低限配線されているか（未配線なら Presenter が旧挙動にフォールバック）。</summary>
    public bool IsInteractiveReady => departButton != null;

    private void Awake()
    {
        if (borrowPlusButton != null) borrowPlusButton.onClick.AddListener(() => OnBorrowPlus.OnNext(Unit.Default));
        if (borrowMinusButton != null) borrowMinusButton.onClick.AddListener(() => OnBorrowMinus.OnNext(Unit.Default));
        if (easyButton != null) easyButton.onClick.AddListener(() => OnDifficultySelected.OnNext(GameModeId.Short));
        if (normalButton != null) normalButton.onClick.AddListener(() => OnDifficultySelected.OnNext(GameModeId.Medium));
        if (hardButton != null) hardButton.onClick.AddListener(() => OnDifficultySelected.OnNext(GameModeId.Long));
        if (creditUpgradeButton != null) creditUpgradeButton.onClick.AddListener(() => OnCreditUpgrade.OnNext(Unit.Default));
        if (flyerButton != null) flyerButton.onClick.AddListener(() => OnFlyerToggled.OnNext(Unit.Default));
        if (appraisalButton != null) appraisalButton.onClick.AddListener(() => OnAppraisalToggled.OnNext(Unit.Default));
        if (graceButton != null) graceButton.onClick.AddListener(() => OnGraceToggled.OnNext(Unit.Default));
        if (departButton != null) departButton.onClick.AddListener(() => OnDepart.OnNext(Unit.Default));
        if (backButton != null) backButton.onClick.AddListener(() => OnBack.OnNext(Unit.Default));
    }

    public void UpdateMetaCurrency(int amount)
    {
        if (metaCurrencyText != null) metaCurrencyText.text = $"信用 {amount:N0}";
    }

    public void UpdateDifficulty(string label)
    {
        if (difficultyText != null) difficultyText.text = $"難易度: {label}";
    }

    public void ShowMessage(string message)
    {
        if (messageText != null) messageText.text = message;
    }

    public void UpdateBorrow(int amount, int creditLine, int upgradeCost, bool canUpgrade)
    {
        if (borrowAmountText != null) borrowAmountText.text = $"借入 {amount:N0}G";
        if (creditLineText != null) creditLineText.text = $"借入枠 {creditLine:N0}G";
        if (creditUpgradeCostText != null)
            creditUpgradeCostText.text = upgradeCost >= 0 ? $"枠拡張: 信用{upgradeCost}" : "枠拡張: MAX";
        if (creditUpgradeButton != null) creditUpgradeButton.interactable = canUpgrade;
    }

    /// <summary>難易度の選択ハイライトを更新する。</summary>
    public void UpdateDifficultySelection(GameModeId selected)
    {
        if (easyCheck != null) easyCheck.SetActive(selected == GameModeId.Short);
        if (normalCheck != null) normalCheck.SetActive(selected == GameModeId.Medium);
        if (hardCheck != null) hardCheck.SetActive(selected == GameModeId.Long);
    }

    public void UpdateStartDash(
        string flyerLabel, bool flyerOn,
        string appraisalLabel, bool appraisalOn,
        string graceLabel, bool graceOn)
    {
        if (flyerLabelText != null) flyerLabelText.text = flyerLabel;
        if (flyerCheck != null) flyerCheck.SetActive(flyerOn);
        if (appraisalLabelText != null) appraisalLabelText.text = appraisalLabel;
        if (appraisalCheck != null) appraisalCheck.SetActive(appraisalOn);
        if (graceLabelText != null) graceLabelText.text = graceLabel;
        if (graceCheck != null) graceCheck.SetActive(graceOn);
    }

    /// <summary>カタログ（スターターレリック）を再構築する。Setup と購読は Presenter 側。</summary>
    public List<PreparationChoiceSlot> PopulateCatalog(Transform parent, int count)
    {
        var slots = new List<PreparationChoiceSlot>();
        if (parent == null || choiceSlotPrefab == null) return slots;

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Destroy(parent.GetChild(i).gameObject);
        }

        for (int i = 0; i < count; i++)
        {
            var obj = Instantiate(choiceSlotPrefab, parent);
            var slot = obj.GetComponent<PreparationChoiceSlot>();
            if (slot != null) slots.Add(slot);
        }
        return slots;
    }

    public Transform RelicCatalogParent => relicCatalogParent;
}

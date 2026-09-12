using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 準備シーン（出店準備）の View。
/// 持ち込み資金（銀行預金から持っていく）・難易度・スターターレリック・スタートダッシュを設定して出店する。
/// 参照は未配線（null）でも動作する。departButton が未配線の間、Presenter は旧挙動
/// （即 TomsShop へ遷移）にフォールバックする。
/// </summary>
public class PreparationView : MonoBehaviour
{
    [Header("ヘッダー")]
    [Tooltip("銀行預金の残高表示（旧: メタ通貨）")]
    [SerializeField] private TextMeshProUGUI metaCurrencyText;
    [SerializeField] private TextMeshProUGUI difficultyText;
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("持ち込み資金（銀行預金から持っていく）")]
    [SerializeField] private TextMeshProUGUI borrowAmountText;
    [SerializeField] private Button borrowPlusButton;
    [SerializeField] private Button borrowMinusButton;
    [Tooltip("持ち込み上限（銀行レベル依存）の表示")]
    [SerializeField] private TextMeshProUGUI creditLineText;
    [Tooltip("旧・枠拡張ボタン。銀行レベル制に移行したため Awake で非表示にする")]
    [SerializeField] private Button creditUpgradeButton;
    [Tooltip("上限の上げ方の案内表示（旧: 枠拡張コスト）")]
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

    [Header("出店 / 戻る")]
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
        // 枠拡張（旧・のれん消費）は廃止。持ち込み上限は村の銀行レベルで決まる
        if (creditUpgradeButton != null) creditUpgradeButton.gameObject.SetActive(false);
        if (flyerButton != null) flyerButton.onClick.AddListener(() => OnFlyerToggled.OnNext(Unit.Default));
        if (appraisalButton != null) appraisalButton.onClick.AddListener(() => OnAppraisalToggled.OnNext(Unit.Default));
        if (graceButton != null) graceButton.onClick.AddListener(() => OnGraceToggled.OnNext(Unit.Default));
        if (departButton != null) departButton.onClick.AddListener(() => OnDepart.OnNext(Unit.Default));
        if (backButton != null) backButton.onClick.AddListener(() => OnBack.OnNext(Unit.Default));

        // ラベル類のはみ出し防止（折り返し＋枠に収まるまで自動縮小）。
        // シーン側の設定に依らずコードで揃える。
        FitText(messageText);
        FitText(metaCurrencyText);
        FitText(difficultyText);
        FitText(borrowAmountText);
        FitText(creditLineText);
        FitText(creditUpgradeCostText);
        FitText(flyerLabelText);
        FitText(appraisalLabelText);
        FitText(graceLabelText);
    }

    /// <summary>
    /// テキストのはみ出し対策: 折り返しを有効化し、枠に収まるようオートサイズで縮小、
    /// それでも収まらない分は省略記号にする。
    /// ※ オートサイズON かつ fontSizeMax=0 だと文字が消える罠があるため必ず max を入れる。
    /// </summary>
    public static void FitText(TMP_Text t)
    {
        if (t == null) return;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Ellipsis;
        if (!t.enableAutoSizing)
        {
            float max = t.fontSize > 0 ? t.fontSize : 24f;
            t.fontSizeMax = max;
            t.fontSizeMin = Mathf.Max(10f, max * 0.5f);
            t.enableAutoSizing = true;
        }
        else if (t.fontSizeMax <= 0)
        {
            t.fontSizeMax = 36f;
        }
    }

    /// <summary>銀行預金の残高表示を更新する。</summary>
    public void UpdateBankedGold(int amount)
    {
        if (metaCurrencyText != null) metaCurrencyText.text = $"銀行預金 {amount:N0}G";
    }

    public void UpdateDifficulty(string label)
    {
        if (difficultyText != null) difficultyText.text = $"難易度: {label}";
    }

    public void ShowMessage(string message)
    {
        if (messageText != null) messageText.text = message;
    }

    /// <summary>持ち込み資金の表示を更新する。</summary>
    public void UpdateCarry(int amount, int carryLimit, int bankLevel, bool isLimitMax)
    {
        if (borrowAmountText != null) borrowAmountText.text = $"持ち込み {amount:N0}G";
        if (creditLineText != null)
            creditLineText.text = $"上限 {carryLimit:N0}G（銀行Lv{bankLevel}）";
        if (creditUpgradeCostText != null)
            creditUpgradeCostText.text = isLimitMax
                ? "持ち込み上限は最大"
                : "村の銀行を増築すると上限が上がる";
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

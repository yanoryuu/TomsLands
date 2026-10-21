using System.Collections.Generic;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 準備シーン（出店準備）の View。
/// 持ち込み資金（村資金から持っていく）・難易度・スターターレリック・スタートダッシュを設定して出店する。
/// 参照は未配線（null）でも動作する。departButton が未配線の間、Presenter は旧挙動
/// （即 TomsShop へ遷移）にフォールバックする。
/// </summary>
public class PreparationView : MonoBehaviour
{
    [Header("ヘッダー")]
    [Tooltip("村資金の残高表示（旧: メタ通貨）")]
    [SerializeField] private TextMeshProUGUI metaCurrencyText;
    [Tooltip("任意: 「村資金」の固定ラベル。配線時は metaCurrencyText に金額だけを出す")]
    [SerializeField] private TextMeshProUGUI villageFundsLabelText;
    [SerializeField] private TextMeshProUGUI difficultyText;
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("持ち込み資金（村資金から持っていく）")]
    [SerializeField] private TextMeshProUGUI borrowAmountText;
    [Tooltip("任意: 「持ち込み」の固定ラベル。配線時は borrowAmountText に金額だけを出す")]
    [SerializeField] private TextMeshProUGUI carryLabelText;
    [SerializeField] private Button borrowPlusButton;
    [SerializeField] private Button borrowMinusButton;
    [Tooltip("任意: 持ち込み額のスライダー（1,000G 刻み）。未配線なら±ボタンのみ")]
    [SerializeField] private Slider carrySlider;
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
    [Tooltip("任意: 難易度ボタンの選択中/未選択の見た目。両方配線時のみボタン画像を切り替える")]
    [SerializeField] private Sprite difficultySelectedSprite;
    [SerializeField] private Sprite difficultyUnselectedSprite;

    [Header("カタログ共通")]
    [SerializeField] private GameObject choiceSlotPrefab;

    [Header("スターターレリック")]
    [SerializeField] private Transform relicCatalogParent;
    [Tooltip("任意: 左右ボタンでレリック一覧を送る場合の ScrollRect とボタン")]
    [SerializeField] private ScrollRect relicScroll;
    [SerializeField] private Button relicPrevButton;
    [SerializeField] private Button relicNextButton;

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
    [Tooltip("任意: 価格を名前と分けて表示する場合の TMP。未配線なら名前ラベルに「名前（価格）」の1行で出す")]
    [SerializeField] private TextMeshProUGUI flyerPriceText;
    [SerializeField] private TextMeshProUGUI appraisalPriceText;
    [SerializeField] private TextMeshProUGUI gracePriceText;

    [Header("出店 / 戻る")]
    [SerializeField] private Button departButton;
    [SerializeField] private Button backButton;

    public Subject<Unit> OnBorrowPlus { get; } = new();
    public Subject<Unit> OnBorrowMinus { get; } = new();
    /// <summary>スライダー操作で指定された持ち込み額（G、刻み丸め前の目安値）。</summary>
    public Subject<int> OnCarrySliderChanged { get; } = new();
    public Subject<GameModeId> OnDifficultySelected { get; } = new();
    public Subject<Unit> OnFlyerToggled { get; } = new();
    public Subject<Unit> OnAppraisalToggled { get; } = new();
    public Subject<Unit> OnGraceToggled { get; } = new();
    public Subject<Unit> OnDepart { get; } = new();
    public Subject<Unit> OnBack { get; } = new();

    /// <summary>UIが最低限配線されているか（未配線なら Presenter が旧挙動にフォールバック）。</summary>
    public bool IsInteractiveReady => departButton != null;

    private int carrySliderStep = 1000;
    private int carrySliderMax;

    private CanvasGroup uiLockGroup;
    private bool uiLockPrevBlocks = true;
    private bool uiLocked;

    /// <summary>
    /// 準備画面のクリックを止める/戻す（会話チュートリアル中に使う）。
    /// 出店ボタンが載っているルートキャンバスの CanvasGroup.blocksRaycasts を切り替える（見た目は変えない）。
    /// </summary>
    public void SetUiInputLocked(bool locked)
    {
        if (locked == uiLocked) return;
        if (locked)
        {
            var anchor = departButton != null ? departButton.transform : transform;
            var canvas = anchor.GetComponentInParent<Canvas>(true);
            if (canvas == null) return;
            canvas = canvas.rootCanvas;
            if (!canvas.TryGetComponent(out uiLockGroup)) uiLockGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            uiLockPrevBlocks = uiLockGroup.blocksRaycasts;
            uiLockGroup.blocksRaycasts = false;
            uiLocked = true;
        }
        else
        {
            if (uiLockGroup != null) uiLockGroup.blocksRaycasts = uiLockPrevBlocks;
            uiLockGroup = null;
            uiLocked = false;
        }
    }

    private void Awake()
    {
        if (borrowPlusButton != null) borrowPlusButton.onClick.AddListener(() => OnBorrowPlus.OnNext(Unit.Default));
        if (borrowMinusButton != null) borrowMinusButton.onClick.AddListener(() => OnBorrowMinus.OnNext(Unit.Default));
        if (carrySlider != null)
        {
            carrySlider.wholeNumbers = true;
            carrySlider.minValue = 0;
            carrySlider.onValueChanged.AddListener(v =>
            {
                int amount = Mathf.Min(Mathf.RoundToInt(v) * carrySliderStep, carrySliderMax);
                OnCarrySliderChanged.OnNext(amount);
            });
        }
        if (easyButton != null) easyButton.onClick.AddListener(() => { UIFx.Pop(easyButton.transform); OnDifficultySelected.OnNext(GameModeId.Short); });
        if (normalButton != null) normalButton.onClick.AddListener(() => { UIFx.Pop(normalButton.transform); OnDifficultySelected.OnNext(GameModeId.Medium); });
        if (hardButton != null) hardButton.onClick.AddListener(() => { UIFx.Pop(hardButton.transform); OnDifficultySelected.OnNext(GameModeId.Long); });
        // 枠拡張（旧・のれん消費）は廃止。持ち込み上限は村の銀行レベルで決まる
        if (creditUpgradeButton != null) creditUpgradeButton.gameObject.SetActive(false);
        if (flyerButton != null) flyerButton.onClick.AddListener(() => { UIFx.Pop(flyerButton.transform); OnFlyerToggled.OnNext(Unit.Default); });
        if (appraisalButton != null) appraisalButton.onClick.AddListener(() => { UIFx.Pop(appraisalButton.transform); OnAppraisalToggled.OnNext(Unit.Default); });
        if (graceButton != null) graceButton.onClick.AddListener(() => { UIFx.Pop(graceButton.transform); OnGraceToggled.OnNext(Unit.Default); });
        if (relicPrevButton != null) relicPrevButton.onClick.AddListener(() => ScrollRelics(-1));
        if (relicNextButton != null) relicNextButton.onClick.AddListener(() => ScrollRelics(1));
        if (departButton != null) departButton.onClick.AddListener(() => OnDepart.OnNext(Unit.Default));
        if (backButton != null) backButton.onClick.AddListener(() => OnBack.OnNext(Unit.Default));

        SetupFocusScope();

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
        FitText(flyerPriceText);
        FitText(appraisalPriceText);
        FitText(gracePriceText);
    }

    private void Start()
    {
        UIFx.PanelOpen(gameObject);
    }

    /// <summary>レリック一覧を左右に1画面弱ぶん送る（ScrollRect 未配線・はみ出し無しなら何もしない）。</summary>
    private void ScrollRelics(int dir)
    {
        if (relicScroll == null || relicScroll.content == null) return;
        var viewport = relicScroll.viewport != null ? relicScroll.viewport : (RectTransform)relicScroll.transform;
        float overflow = relicScroll.content.rect.width - viewport.rect.width;
        if (overflow <= 0f) return;
        float step = viewport.rect.width * 0.8f / overflow;
        float target = Mathf.Clamp01(relicScroll.horizontalNormalizedPosition + dir * step);
        relicScroll.StopMovement();
        DOTween.To(() => relicScroll.horizontalNormalizedPosition,
                v => relicScroll.horizontalNormalizedPosition = v, target, 0.25f)
            .SetEase(Ease.OutCubic).SetLink(gameObject);
    }

    /// <summary>パッド/キー用のフォーカス単位。Cancel=戻る。出店ボタンは誤爆防止のため最初には選ばない。</summary>
    private void SetupFocusScope()
    {
        var scope = UIFocusScope.GetOrAdd(gameObject);
        scope.FirstSelected = normalButton != null ? normalButton : (easyButton != null ? easyButton : borrowPlusButton);
        scope.SetCancelButton(backButton);

        // レリック一覧が ScrollRect 内なら、選択に合わせて自動スクロール
        var scroll = relicCatalogParent != null ? relicCatalogParent.GetComponentInParent<ScrollRect>() : null;
        if (scroll != null && scroll.GetComponent<ScrollRectAutoFollow>() == null)
            scroll.gameObject.AddComponent<ScrollRectAutoFollow>();
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

    /// <summary>村資金の残高表示を更新する。</summary>
    public void UpdateVillageFunds(int amount)
    {
        if (metaCurrencyText != null)
            metaCurrencyText.text = villageFundsLabelText != null ? $"{amount:N0}G" : $"村資金 {amount:N0}G";
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
        if (borrowAmountText != null)
            borrowAmountText.text = carryLabelText != null ? $"{amount:N0}G" : $"持ち込み {amount:N0}G";
        if (creditLineText != null)
            creditLineText.text = $"上限 {carryLimit:N0}G（銀行Lv{bankLevel}）";
        if (creditUpgradeCostText != null)
            creditUpgradeCostText.text = isLimitMax
                ? "持ち込み上限は最大"
                : "村の銀行を増築すると上限が上がる";
    }

    /// <summary>
    /// 持ち込みスライダーの範囲と位置を同期する（onValueChanged は発火させない）。
    /// 目盛り数 = ceil(上限/刻み)。最後の目盛りは上限ちょうどに対応する。上限0なら操作不可。
    /// </summary>
    public void UpdateCarrySlider(int amount, int carryMax, int step)
    {
        if (carrySlider == null) return;
        carrySliderStep = Mathf.Max(1, step);
        carrySliderMax = Mathf.Max(0, carryMax);
        int steps = (carrySliderMax + carrySliderStep - 1) / carrySliderStep;
        carrySlider.maxValue = Mathf.Max(1, steps);
        int pos = amount >= carrySliderMax ? steps : Mathf.RoundToInt(amount / (float)carrySliderStep);
        carrySlider.SetValueWithoutNotify(pos);
        carrySlider.interactable = carrySliderMax > 0;
    }

    /// <summary>難易度ボタンの文字の下に中身（配信の回数と日数）を小さく添える。ラベルが無いボタンは何もしない。</summary>
    public void SetDifficultyDetails(string easy, string normal, string hard)
    {
        SetDifficultyDetail(easyButton, easy);
        SetDifficultyDetail(normalButton, normal);
        SetDifficultyDetail(hardButton, hard);
    }

    private static void SetDifficultyDetail(Button button, string detail)
    {
        if (button == null || string.IsNullOrEmpty(detail)) return;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null) return;
        string name = label.text;
        int nl = name.IndexOf('\n');
        if (nl >= 0) name = name.Substring(0, nl);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.text = $"{name}\n<size=55%>{detail}</size>";
    }

    /// <summary>難易度の選択ハイライトを更新する。</summary>
    public void UpdateDifficultySelection(GameModeId selected)
    {
        if (easyCheck != null) easyCheck.SetActive(selected == GameModeId.Short);
        if (normalCheck != null) normalCheck.SetActive(selected == GameModeId.Medium);
        if (hardCheck != null) hardCheck.SetActive(selected == GameModeId.Long);
        SetDifficultySprite(easyButton, selected == GameModeId.Short);
        SetDifficultySprite(normalButton, selected == GameModeId.Medium);
        SetDifficultySprite(hardButton, selected == GameModeId.Long);
    }

    private void SetDifficultySprite(Button button, bool selected)
    {
        if (button == null || difficultySelectedSprite == null || difficultyUnselectedSprite == null) return;
        if (button.targetGraphic is Image img) img.sprite = selected ? difficultySelectedSprite : difficultyUnselectedSprite;
    }

    /// <summary>
    /// スタートダッシュ3種の表示を更新する。価格TMPが配線されていれば名前と価格を分けて表示し、
    /// 未配線なら名前ラベルに「名前（価格G）」の1行で表示する。
    /// </summary>
    public void UpdateStartDash(
        string flyerName, int flyerCost, bool flyerOn,
        string appraisalName, int appraisalCost, bool appraisalOn,
        string graceName, int graceCost, bool graceOn)
    {
        SetStartDashEntry(flyerLabelText, flyerPriceText, flyerCheck, flyerName, flyerCost, flyerOn);
        SetStartDashEntry(appraisalLabelText, appraisalPriceText, appraisalCheck, appraisalName, appraisalCost, appraisalOn);
        SetStartDashEntry(graceLabelText, gracePriceText, graceCheck, graceName, graceCost, graceOn);
    }

    private static void SetStartDashEntry(
        TextMeshProUGUI label, TextMeshProUGUI price, GameObject check, string name, int cost, bool on)
    {
        if (price != null)
        {
            price.text = $"{cost:N0}G";
            if (label != null) label.text = name;
        }
        else if (label != null)
        {
            label.text = $"{name}（{cost:N0}G）";
        }
        if (check != null) check.SetActive(on);
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
            if (obj.TryGetComponent<PreparationChoiceSlot>(out var slot)) slots.Add(slot);
        }
        return slots;
    }

    public Transform RelicCatalogParent => relicCatalogParent;
}

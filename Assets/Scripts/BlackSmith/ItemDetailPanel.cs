using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using R3;

/// <summary>
/// 仕入れ画面の「選択銘柄」詳細パネル（株のトレード画面の右ペイン相当）。
/// 価格チャート・市場分析・注文（数量＋仕入れ）を1か所に集約する。
/// 注文の予約数管理は BlackSmithModel が持ち、Presenter が選択銘柄ごとに結線する。
/// </summary>
public class ItemDetailPanel : MonoBehaviour
{
    [Header("ルート")]
    [Tooltip("選択中だけ表示するパネル本体。未指定なら自分のGameObjectを使う")]
    [SerializeField] private GameObject root;

    [Header("基本情報")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI attributeText;

    [Header("チャート")]
    [SerializeField] private PriceChartView priceChart;

    [Header("市場分析")]
    [SerializeField] private TextMeshProUGUI demandText;       // 需要%
    [SerializeField] private TextMeshProUGUI basePriceText;    // 基準価
    [SerializeField] private TextMeshProUGUI currentPriceText; // 現在価格
    [SerializeField] private TextMeshProUGUI salesRateText;    // 売れやすさ
    [SerializeField] private TextMeshProUGUI wasSoldText;      // 前ターン販売
    [SerializeField] private TextMeshProUGUI recommendText;    // おすすめ度
    [SerializeField] private TextMeshProUGUI dividendText;     // 配当/日（配当付き武器のみ・未配線可）

    [Header("注文")]
    [SerializeField] private Slider quantitySlider;
    [SerializeField] private TextMeshProUGUI quantityText;
    [SerializeField] private Button plusButton;
    [SerializeField] private Button minusButton;
    [SerializeField] private TextMeshProUGUI totalCostText;
    [SerializeField] private Button purchaseButton;

    // 通知（Presenter が購読し BlackSmithModel に反映）
    public Subject<int> OnDisplayQuantityChanged { get; } = new();
    public Subject<int> OnStepClicked { get; } = new();
    public Subject<Unit> OnPurchaseClicked { get; } = new();

    private int unitPrice;
    private int displayQuantity;
    private int maxQuantity;
    private bool suppress;

    private void Awake()
    {
        if (quantitySlider)
        {
            quantitySlider.wholeNumbers = true;
            quantitySlider.onValueChanged.AddListener(OnSliderValueChanged);
        }
        if (plusButton)  plusButton.onClick.AddListener(() => OnStepClicked.OnNext(+1));
        if (minusButton) minusButton.onClick.AddListener(() => OnStepClicked.OnNext(-1));
        if (purchaseButton) purchaseButton.onClick.AddListener(() => OnPurchaseClicked.OnNext(Unit.Default));
    }

    private GameObject Root => root != null ? root : gameObject;

    /// <summary>選択された銘柄の情報をパネルに表示する（チャート・市場分析を更新）。</summary>
    /// <param name="useBattleHistory">true=配信中の価格履歴（BattlePriceHistory）をチャートに表示する。バトル中の補充ポップアップ用</param>
    public void ShowItem(RuntimeItemData runtime, int basePrice, float recommendScore, bool useBattleHistory = false)
    {
        Root.SetActive(true);

        // 銘柄切替のフィードバック（短いポップ）
        UIFx.Pop(Root.transform, 0.98f, 0.15f);
        if (icon != null)
        {
            icon.transform.DOKill(true);
            icon.transform.DOPunchScale(Vector3.one * 0.12f, 0.2f, 5, 0.6f).SetLink(icon.gameObject);
        }

        if (icon) icon.sprite = runtime.ItemIcon;
        if (nameText) nameText.text = runtime.ItemName;
        if (attributeText) attributeText.text = runtime.ItemAttribute.ToString();

        if (priceChart)
        {
            if (useBattleHistory)
                priceChart.SetData(runtime.BattlePriceHistory); // 配信中の価格変動（需要系列なし）
            else
                priceChart.SetData(runtime.ShopPriceHistory, runtime.ShopDemandHistory);
        }

        unitPrice = runtime.CurrentPrice.Value;
        RefreshMarket(runtime, basePrice, recommendScore);
        UpdateTotalText();
    }

    /// <summary>市場分析テキストのみ更新する（需要・価格・おすすめ度の変化時）。</summary>
    public void RefreshMarket(RuntimeItemData runtime, int basePrice, float recommendScore)
    {
        if (demandText)     demandText.text = $"需要 {runtime.Demand.Value:P0}";
        if (basePriceText)  basePriceText.text = $"基準価 {basePrice:N0}G";
        if (currentPriceText) currentPriceText.text = $"現在 {runtime.CurrentPrice.Value:N0}G";
        if (salesRateText)  salesRateText.text = $"売率 ×{runtime.SalesRate:0.0}";
        if (wasSoldText)    wasSoldText.text = runtime.WasSoldLastTurn ? "前ターン販売 ✓" : "前ターン販売 —";
        if (recommendText)  recommendText.text = $"おすすめ度 {recommendScore:N0}";
        if (dividendText)
        {
            bool hasDividend = runtime.DividendPerTurn > 0;
            dividendText.gameObject.SetActive(hasDividend);
            if (hasDividend)
                dividendText.text = $"配当 {runtime.DividendPerTurn:N0}G/日・個（保有で毎日入金）";
        }
    }

    // === 注文表示 ===

    public void SetPrice(int price)
    {
        unitPrice = price;
        UpdateTotalText();
    }

    public void SetQuantity(int q)
    {
        suppress = true;
        displayQuantity = Mathf.Clamp(q, 0, maxQuantity);
        if (quantitySlider) quantitySlider.value = displayQuantity;
        quantityText?.SetText($"{displayQuantity}");
        UpdateTotalText();
        suppress = false;
        UpdateButtons();
    }

    public void SetMaxQuantity(int max)
    {
        maxQuantity = Mathf.Max(0, max);
        if (quantitySlider) quantitySlider.maxValue = maxQuantity;
        if (displayQuantity > maxQuantity) SetQuantity(maxQuantity);
        UpdateButtons();
    }

    public void Hide() => Root.SetActive(false);

    private void OnSliderValueChanged(float value)
    {
        if (suppress) return;
        int next = Mathf.RoundToInt(value);
        if (next == displayQuantity) return;
        displayQuantity = next;
        quantityText?.SetText($"{displayQuantity}");
        UpdateTotalText();
        OnDisplayQuantityChanged.OnNext(displayQuantity);
        UpdateButtons();
    }

    private void UpdateTotalText()
    {
        int total = unitPrice * Mathf.Max(displayQuantity, 1);
        totalCostText?.SetText($"{total:N0}G");
    }

    private void UpdateButtons()
    {
        if (purchaseButton) purchaseButton.interactable = displayQuantity > 0 && maxQuantity > 0;
        if (plusButton) plusButton.interactable = displayQuantity < maxQuantity;
        if (minusButton) minusButton.interactable = displayQuantity > 0;
    }

    private void OnDestroy()
    {
        quantitySlider?.onValueChanged.RemoveListener(OnSliderValueChanged);
        plusButton?.onClick.RemoveAllListeners();
        minusButton?.onClick.RemoveAllListeners();
        purchaseButton?.onClick.RemoveAllListeners();
    }
}

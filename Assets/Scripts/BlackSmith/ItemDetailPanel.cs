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
    [Tooltip("保有数（旧: 売れやすさ。全銘柄 1.0 で意味が無かったので置き換えた）")]
    [SerializeField] private TextMeshProUGUI salesRateText;
    [Tooltip("旧: 前ターン販売。陳列すれば必ず✓になり判断に使えないので出さない")]
    [SerializeField] private TextMeshProUGUI wasSoldText;
    [SerializeField] private TextMeshProUGUI recommendText;    // 現在の期待収益（未来は含まない）
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

    private System.IDisposable stockSubscription;
    private int unitPrice;
    private int displayQuantity;
    private int maxQuantity;
    private bool suppress;

    private void Awake()
    {
        if (quantitySlider)
        {
            quantitySlider.wholeNumbers = true;
            if (quantitySlider.navigation.mode == Navigation.Mode.None)
                quantitySlider.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            quantitySlider.onValueChanged.AddListener(OnSliderValueChanged);
        }
        if (plusButton)  plusButton.onClick.AddListener(() => OnStepClicked.OnNext(+1));
        if (minusButton) minusButton.onClick.AddListener(() => OnStepClicked.OnNext(-1));
        if (purchaseButton) purchaseButton.onClick.AddListener(() => OnPurchaseClicked.OnNext(Unit.Default));
    }

    private GameObject Root => root != null ? root : gameObject;

    /// <summary>パッドで個数設定へ移るときのフォーカス先（スライダー → ＋ボタン → 購入ボタンの順）。</summary>
    public Selectable QuantityFocusTarget
    {
        get
        {
            if (quantitySlider != null && UIFocusManager.IsUsable(quantitySlider.gameObject)) return quantitySlider;
            if (plusButton != null && UIFocusManager.IsUsable(plusButton.gameObject)) return plusButton;
            if (purchaseButton != null && UIFocusManager.IsUsable(purchaseButton.gameObject)) return purchaseButton;
            return null;
        }
    }

    /// <summary>
    /// スライダーから下で購入ボタンへ移れるようにする（Automatic だと真下寄りのキャンセル等を拾い、購入ボタンに届かない）。
    /// 左右は数量の増減に使うので、上は Automatic の結果をそのまま引き継ぐ。
    /// </summary>
    private void ApplyPadNavigation()
    {
        if (quantitySlider == null || purchaseButton == null) return;
        quantitySlider.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        var up = quantitySlider.FindSelectableOnUp();
        quantitySlider.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnUp = up,
            selectOnDown = purchaseButton,
        };
    }

    /// <summary>選択 GameObject がこのパネル内か。</summary>
    public bool Contains(GameObject go) => go != null && go.transform.IsChildOf(transform);

    /// <summary>選択された銘柄の情報をパネルに表示する（チャート・市場分析を更新）。</summary>
    /// <param name="useBattleHistory">true=配信中の価格履歴（BattlePriceHistory）をチャートに表示する。バトル中の補充ポップアップ用</param>
    /// <param name="markers">チャートに立てる出来事の印（配信・貼った記事。G-12）。null なら印なし</param>
    public void ShowItem(RuntimeItemData runtime, int basePrice, float recommendScore, bool useBattleHistory = false,
        System.Collections.Generic.IReadOnlyList<PriceChartView.Marker> markers = null)
    {
        Root.SetActive(true);
        ApplyPadNavigation();

        // 銘柄切替のフィードバック（短いポップ）
        UIFx.Pop(Root.transform, 0.98f, 0.15f);
        if (icon != null)
        {
            icon.transform.DOKill(true);
            icon.transform.DOPunchScale(Vector3.one * 0.12f, 0.2f, 5, 0.6f).SetLink(icon.gameObject);
        }

        if (icon) icon.sprite = runtime.ItemIcon;
        if (nameText) nameText.text = runtime.ItemName;
        if (attributeText) attributeText.text = ItemAttributeText.WithSuffix(runtime.ItemAttribute);

        // 保有数は仕入れで増えるので、この銘柄を出している間は追従させる
        stockSubscription?.Dispose();
        stockSubscription = runtime.Stock.Subscribe(_ =>
        {
            if (salesRateText) salesRateText.text = HoldingText(runtime);
        });

        if (priceChart)
        {
            if (useBattleHistory)
                priceChart.SetData(runtime.BattlePriceHistory); // 配信中の価格変動（需要系列なし）
            else
            {
                priceChart.SetData(runtime.ShopPriceHistory, runtime.ShopDemandHistory);
                // 基準価格の点線・平均取得単価の線・出来事の印（G-12）。縦軸は基準価格の 0.5〜1.8 倍に固定
                priceChart.SetGuides(basePrice, runtime.HasKnownCost ? runtime.AverageCost : (int?)null, markers);
            }
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
        if (currentPriceText)
        {
            // いま基準価格より高いのか安いのかを数字でも（G-12）
            float vsBase = basePrice > 0 ? (float)(runtime.CurrentPrice.Value - basePrice) / basePrice : 0f;
            currentPriceText.text = basePrice > 0 && Mathf.Abs(vsBase) >= 0.005f
                ? $"現在 {runtime.CurrentPrice.Value:N0}G <size=75%>({FormatRate(vsBase)})</size>"
                : $"現在 {runtime.CurrentPrice.Value:N0}G";
        }
        if (salesRateText)  salesRateText.text = HoldingText(runtime);
        if (wasSoldText)    wasSoldText.gameObject.SetActive(false);
        if (recommendText)  recommendText.text = $"現在の期待収益 {recommendScore:N0}";
        if (dividendText)
        {
            bool hasDividend = runtime.DividendPerTurn > 0;
            dividendText.gameObject.SetActive(hasDividend);
            if (hasDividend)
                dividendText.text = $"配当 {runtime.DividendPerTurn:N0}G/日・個（保有で毎日入金）";
        }
    }

    /// <summary>
    /// 「保有 12個 平均540G +13%」。取得単価が分からない在庫（古いセーブ）は「保有 12個」だけ（F-1）。
    /// 騰落は金色（含み益）・赤（含み損）。
    /// </summary>
    private static string HoldingText(RuntimeItemData runtime)
    {
        string head = $"保有 {runtime.Stock.Value:N0}個";
        if (!runtime.HasKnownCost) return head;
        var rate = runtime.CostChangeRate;
        if (!rate.HasValue) return $"{head} 平均{runtime.AverageCost:N0}G";
        string color = rate.Value >= 0f ? "#E8A33D" : "#E06A5A";
        return $"{head} 平均{runtime.AverageCost:N0}G <color={color}>{FormatRate(rate.Value)}</color>";
    }

    private static string FormatRate(float rate) => $"{(rate >= 0 ? "+" : "")}{rate * 100f:0}%";

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
        if (quantityText != null) quantityText.SetText($"{displayQuantity}");
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
        if (quantityText != null) quantityText.SetText($"{displayQuantity}");
        UpdateTotalText();
        OnDisplayQuantityChanged.OnNext(displayQuantity);
        UpdateButtons();
    }

    /// <summary>注文の合計（単価 × 数量）。数量0なら 0G（単価と見間違えないよう、単価にはしない）。</summary>
    private void UpdateTotalText()
    {
        int total = unitPrice * displayQuantity;
        if (totalCostText != null) totalCostText.SetText($"{total:N0}G");
    }

    private void UpdateButtons()
    {
        if (purchaseButton) purchaseButton.interactable = displayQuantity > 0 && maxQuantity > 0;
        if (plusButton) plusButton.interactable = displayQuantity < maxQuantity;
        if (minusButton) minusButton.interactable = displayQuantity > 0;
    }

    private void OnDestroy()
    {
        stockSubscription?.Dispose();
        if (quantitySlider != null) quantitySlider.onValueChanged.RemoveListener(OnSliderValueChanged);
        if (plusButton != null) plusButton.onClick.RemoveAllListeners();
        if (minusButton != null) minusButton.onClick.RemoveAllListeners();
        if (purchaseButton != null) purchaseButton.onClick.RemoveAllListeners();
    }
}

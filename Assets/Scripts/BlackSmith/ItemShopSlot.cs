using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using R3;

public class ItemShopSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image rowBackground;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private Slider quantitySlider;
    [SerializeField] private TextMeshProUGUI quantityText;
    [SerializeField] private Button infoButton;
    [SerializeField] private Button purchaseButton;

    [Header("市況（ティッカー行）")]
    [Tooltip("需要を%表示するテキスト")]
    [SerializeField] private TextMeshProUGUI demandText;
    [Tooltip("需要を 0〜1 のバーで表示するスライダー（任意）")]
    [SerializeField] private Slider demandBar;
    [Tooltip("前回比トレンド矢印（↑→↓）")]
    [SerializeField] private TextMeshProUGUI priceTrendText;
    [Tooltip("人気バッジ（Demand高 or 前ターン販売）")]
    [SerializeField] private GameObject popularBadge;
    [Tooltip("品薄バッジ（在庫1〜2）")]
    [SerializeField] private GameObject lowStockBadge;
    [Tooltip("選択中のハイライト（任意）")]
    [SerializeField] private GameObject selectedHighlight;

    [Header("Step Buttons")]
    [SerializeField] private Button plusButton;
    [SerializeField] private Button minusButton;
    [SerializeField] private Button iconButton; // アイコン画像上に配置する透明ボタン

    [Header("Data")]
    public string itemId { get; private set; }

    // 通知系
    public Subject<int> OnDisplayQuantityChanged { get; } = new(); // Slider 由来の変更
    public Subject<int> OnStepClicked { get; } = new();            // +1 / -1 の要求
    public Subject<string> OnInfoRequested { get; } = new();
    public Subject<Unit> OnPurchaseClicked { get; } = new();
    public Subject<string> OnIconClicked { get; } = new();         // アイコン押下 → 市場分析ポップアップ
    public Subject<string> OnHoverEnter { get; } = new();          // ホバー開始 → アイテムID
    public Subject<Unit> OnHoverExit { get; } = new();             // ホバー終了
    public Subject<string> OnRowSelected { get; } = new();         // 行クリック → 詳細パネル表示

    // 内部状態（UI表示用）
    private int displayQuantity;
    private int maxQuantity;
    private int unitPrice;
    private bool suppress; // UI更新時にイベントを抑止

    private void Awake()
    {
        if (quantitySlider)
        {
            quantitySlider.wholeNumbers = true; // 予約数は整数運用
            quantitySlider.onValueChanged.AddListener(OnSliderValueChanged);
        }

        infoButton?.onClick.AddListener(() => OnInfoRequested.OnNext(itemId));
        purchaseButton?.onClick.AddListener(() => OnPurchaseClicked.OnNext(Unit.Default));

        // ＋／－ボタン
        if (plusButton) plusButton.onClick.AddListener(() => OnStepClicked.OnNext(+1));
        if (minusButton) minusButton.onClick.AddListener(() => OnStepClicked.OnNext(-1));

        // アイコンボタン（市場分析ポップアップ）
        if (iconButton) iconButton.onClick.AddListener(() => OnIconClicked.OnNext(itemId));
    }

    public void SetItem(string itemId,string itemName, Sprite sprite, Sprite background, int price, int maxStock, int currentStock, bool isPopular)
    {
        this.itemId = itemId;
        if (icon) icon.sprite = sprite;
        if (backgroundImage)
        {
            if (background != null)
            {
                backgroundImage.sprite = background;
                backgroundImage.enabled = true;
            }
            else
            {
                backgroundImage.enabled = false;
            }
        }
        if (nameText) nameText.text = itemName;
        SetPrice(price);
        SetCurrentStock(currentStock);
        if (popularBadge) popularBadge.SetActive(isPopular);

        SetMaxDisplayQuantity(Mathf.Max(0, maxStock));
        SetDisplayQuantity(0);
        UpdateButtonsInteractable();
    }

    /// <summary>
    /// 金融商品モードで行を設定する（武具と同列の見た目に統一するための共用化）。
    /// 在庫・数量系のUIを隠し、需要欄には利回り/前日比などの市況ラベルを表示する。
    /// </summary>
    public void SetFinance(string productId, string productName, Sprite sprite, int unitPrice,
        int heldUnits, string marketLabel, Color marketColor, int previousPrice, bool unlocked)
    {
        itemId = productId;
        if (icon)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
        if (backgroundImage) backgroundImage.enabled = false;
        if (nameText) nameText.text = productName;
        SetPrice(unitPrice);
        SetPriceTrend(unitPrice, previousPrice);

        // 行内の固定キャプション（プレハブの静的Text）を金融向けの文言に差し替える
        var demandLabel = transform.Find("DemandLabel");
        if (demandLabel != null)
        {
            var label = demandLabel.GetComponent<TextMeshProUGUI>();
            if (label != null) label.text = "市況";
        }
        var stockLabel = transform.Find("StockLabel");
        if (stockLabel != null)
        {
            var label = stockLabel.GetComponent<TextMeshProUGUI>();
            if (label != null) label.text = "保有";
        }

        if (stockText) stockText.text = heldUnits > 0 ? $"{heldUnits}口" : "-";
        if (demandText)
        {
            demandText.text = marketLabel;
            demandText.color = marketColor;
        }

        // 金融商品に無い概念のUIを隠す
        if (demandBar) demandBar.gameObject.SetActive(false);
        if (popularBadge) popularBadge.SetActive(false);
        if (lowStockBadge) lowStockBadge.SetActive(false);
        if (quantitySlider) quantitySlider.gameObject.SetActive(false);
        if (quantityText) quantityText.gameObject.SetActive(false);
        if (plusButton) plusButton.gameObject.SetActive(false);
        if (minusButton) minusButton.gameObject.SetActive(false);
        if (purchaseButton) purchaseButton.gameObject.SetActive(false);

        // 未解禁は行ごと薄くする（選択は可能にして解禁条件の案内を出す）。
        // 登場フェード（CanvasGroupをDOFadeで1へ）と競合するため、未解禁時はTweenを止めてから設定する
        if (!unlocked)
        {
            var cg = GetComponent<CanvasGroup>();
            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
            DG.Tweening.DOTween.Kill(cg);
            cg.alpha = 0.55f;
        }
    }

    /// <summary>需要(0〜1)を温度色付きの%表示と人気バッジに反映する。</summary>
    public void SetDemand(float demand, bool isPopular)
    {
        demand = Mathf.Clamp01(demand);
        if (demandText != null)
        {
            demandText.text = $"{demand:P0}";
            demandText.color = DemandColor(demand);
        }
        if (demandBar) demandBar.value = demand; // バー未使用時はnull（旧レイアウト互換）
        if (popularBadge) popularBadge.SetActive(isPopular);
    }

    /// <summary>需要の高さを温度で表す色（低=青灰 / 中=白 / 高=オレンジ）。</summary>
    private static Color DemandColor(float demand)
    {
        if (demand < 0.4f) return new Color(0.55f, 0.68f, 0.8f);  // 低: 青灰
        if (demand < 0.7f) return Color.white;                     // 中: 白
        return new Color(1f, 0.6f, 0.25f);                          // 高: オレンジ
    }

    /// <summary>前回比トレンド矢印（↑赤／→灰／↓水色）を更新する。</summary>
    public void SetPriceTrend(int current, int previous)
    {
        if (priceTrendText == null) return;
        if (current > previous)
        {
            priceTrendText.text = "↑";
            priceTrendText.color = Color.red;
        }
        else if (current < previous)
        {
            priceTrendText.text = "↓";
            priceTrendText.color = Color.cyan;
        }
        else
        {
            priceTrendText.text = "→";
            priceTrendText.color = Color.gray;
        }
    }

    /// <summary>この行の選択ハイライトを切り替える。</summary>
    public void SetSelected(bool selected)
    {
        if (selectedHighlight) selectedHighlight.SetActive(selected);
    }

    public void SetPrice(int price)
    {
        unitPrice = price;
        UpdatePriceText();
    }

    private void UpdatePriceText()
    {
        int total = unitPrice * Mathf.Max(displayQuantity, 1);
        priceText?.SetText($"{total:N0}G");
    }

    // === 表示だけ更新（通知しない） ===
    public void SetDisplayQuantity(int q)
    {
        suppress = true;
        displayQuantity = Mathf.Clamp(q, 0, maxQuantity);

        if (quantitySlider)
        {
            quantitySlider.value = displayQuantity;
        }
        quantityText?.SetText($"{displayQuantity}");
        UpdatePriceText();

        suppress = false;
        UpdateButtonsInteractable();
    }

    public void SetCurrentStock(int currentStock)
    {
        stockText?.SetText($"{currentStock}");
        if (lowStockBadge) lowStockBadge.SetActive(currentStock > 0 && currentStock <= 2);
    }

    // === 最大値変更（通知しない） ===
    public void SetMaxDisplayQuantity(int max)
    {
        maxQuantity = Mathf.Max(0, max);
        if (quantitySlider)
        {
            quantitySlider.maxValue = maxQuantity;
        }
        if (displayQuantity > maxQuantity)
        {
            SetDisplayQuantity(maxQuantity);
        }
        UpdateButtonsInteractable();
    }

    // === ユーザー操作によるスライダー変更 ===
    private void OnSliderValueChanged(float value)
    {
        int next = Mathf.RoundToInt(value);
        if (suppress) return;
        if (next == displayQuantity) return;

        displayQuantity = next;
        quantityText?.SetText($"{displayQuantity}");
        UpdatePriceText();
        OnDisplayQuantityChanged.OnNext(displayQuantity);
        UpdateButtonsInteractable();
    }

    private void UpdateButtonsInteractable()
    {
        // 購入ボタン
        if (purchaseButton)
            purchaseButton.interactable = displayQuantity > 0 && maxQuantity > 0;

        // ＋／－の活性状態（予約可能範囲に合わせて制御）
        if (plusButton)
            plusButton.interactable = displayQuantity < maxQuantity;
        if (minusButton)
            minusButton.interactable = displayQuantity > 0;
    }

    public void OnPointerEnter(PointerEventData eventData) => OnHoverEnter.OnNext(itemId);
    public void OnPointerExit(PointerEventData eventData) => OnHoverExit.OnNext(Unit.Default);
    public void OnPointerClick(PointerEventData eventData) => OnRowSelected.OnNext(itemId);

    private void OnDestroy()
    {
        quantitySlider?.onValueChanged.RemoveListener(OnSliderValueChanged);
        infoButton?.onClick.RemoveAllListeners();
        purchaseButton?.onClick.RemoveAllListeners();
        plusButton?.onClick.RemoveAllListeners();
        minusButton?.onClick.RemoveAllListeners();
        iconButton?.onClick.RemoveAllListeners();
    }
}

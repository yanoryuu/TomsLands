using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 取引所の右ペイン（ItemDetailPanel の金融版）。
/// 商品詳細・基準価額チャート・保有状況・数量指定つきの買い/売りを提供する。
/// 参照は未配線（null）でも動作する。
/// </summary>
public class FinanceDetailPanel : MonoBehaviour
{
    [Header("商品情報")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI unitPriceText;
    [SerializeField] private TextMeshProUGUI detailText;    // 利率/満期 or NAV説明
    [SerializeField] private TextMeshProUGUI holdingsText;

    [Header("チャート（ファンドの基準価額）")]
    [SerializeField] private PriceChartView chartView;

    [Header("注文")]
    [SerializeField] private TextMeshProUGUI quantityText;
    [SerializeField] private Slider quantitySlider;
    [SerializeField] private Button plusButton;
    [SerializeField] private Button minusButton;
    [SerializeField] private TextMeshProUGUI totalCostText;
    [SerializeField] private Button buyButton;
    [SerializeField] private Button sellButton;

    public Subject<int> OnBuyClicked { get; } = new();   // 数量つき
    public Subject<int> OnSellClicked { get; } = new();  // 数量つき

    private int quantity = 1;
    private int maxQuantity = 99;
    private int unitPrice;
    private float buyFeeRate;

    private void Awake()
    {
        if (plusButton != null)
            plusButton.onClick.AddListener(() => { quantity = Mathf.Min(quantity + 1, maxQuantity); RefreshQuantity(); });
        if (minusButton != null)
            minusButton.onClick.AddListener(() => { quantity = Mathf.Max(quantity - 1, 1); RefreshQuantity(); });
        if (quantitySlider != null)
        {
            quantitySlider.wholeNumbers = true;
            quantitySlider.onValueChanged.AddListener(v =>
            {
                int next = Mathf.Clamp(Mathf.RoundToInt(v), 1, maxQuantity);
                if (next == quantity) return;
                quantity = next;
                RefreshQuantity();
            });
        }
        if (buyButton != null)
            buyButton.onClick.AddListener(() => OnBuyClicked.OnNext(quantity));
        if (sellButton != null)
            sellButton.onClick.AddListener(() => OnSellClicked.OnNext(quantity));
    }

    public void Show(FinancialProductData product, int currentUnitPrice, int heldUnits,
        System.Collections.Generic.IReadOnlyList<int> navHistory, float fundBuyFeeRate, bool canAffordOne,
        int maxOrderQuantity = 99)
    {
        gameObject.SetActive(true);

        unitPrice = currentUnitPrice;
        buyFeeRate = product.kind == FinancialProductKind.IndexFund ? fundBuyFeeRate : 0f;
        maxQuantity = Mathf.Max(1, maxOrderQuantity);
        quantity = 1;
        if (quantitySlider != null)
        {
            quantitySlider.minValue = 1;
            quantitySlider.maxValue = maxQuantity;
        }

        if (iconImage != null)
        {
            iconImage.sprite = product.icon;
            iconImage.enabled = product.icon != null;
        }
        if (nameText != null) nameText.text = product.productName;
        if (descriptionText != null) descriptionText.text = product.description;
        if (unitPriceText != null) unitPriceText.text = $"{currentUnitPrice:N0}G / 口";

        if (detailText != null)
        {
            detailText.text = product.kind == FinancialProductKind.Bond
                ? $"満期: {product.bondMaturityTurns}日後\n利率: {product.bondInterestRate:P0}\n※満期まで資金はロックされる"
                : (product.useAttributeFilter
                    ? $"{AttributeLabel(product.attribute)}属性銘柄の市場価格に連動\nいつでも解約可能"
                    : "全銘柄の市場価格に連動（市場指数）\nいつでも解約可能");
        }

        if (holdingsText != null)
            holdingsText.text = heldUnits > 0 ? $"保有: {heldUnits}口" : "保有なし";

        if (chartView != null)
        {
            bool hasChart = product.kind == FinancialProductKind.IndexFund && navHistory != null && navHistory.Count > 0;
            // チャート枠（NavChart）ごと隠す。債券選択時に空の枠だけ残さないため
            var chartRoot = chartView.transform.parent != null && chartView.transform.parent != transform
                ? chartView.transform.parent.gameObject
                : chartView.gameObject;
            chartRoot.SetActive(hasChart);
            chartView.gameObject.SetActive(hasChart);
            if (hasChart) chartView.SetData(navHistory);
        }

        if (sellButton != null)
            sellButton.gameObject.SetActive(product.kind == FinancialProductKind.IndexFund);
        UpdateInteractable(heldUnits, canAffordOne);

        RefreshQuantity();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void UpdateInteractable(int heldUnits, bool canAffordQuantity)
    {
        if (buyButton != null) buyButton.interactable = canAffordQuantity;
        if (sellButton != null) sellButton.interactable = heldUnits > 0;
    }

    public int CurrentQuantity => quantity;

    public static string AttributeLabel(ItemTypeData.ItemAttribute attr) => attr switch
    {
        ItemTypeData.ItemAttribute.Fire => "火",
        ItemTypeData.ItemAttribute.Water => "水",
        ItemTypeData.ItemAttribute.Earth => "土",
        ItemTypeData.ItemAttribute.Wind => "風",
        ItemTypeData.ItemAttribute.Light => "光",
        ItemTypeData.ItemAttribute.Dark => "闇",
        _ => attr.ToString()
    };

    private void RefreshQuantity()
    {
        if (quantityText != null) quantityText.text = quantity.ToString();
        if (quantitySlider != null) quantitySlider.SetValueWithoutNotify(quantity);
        if (totalCostText != null)
        {
            int total = Mathf.RoundToInt(unitPrice * quantity * (1f + buyFeeRate));
            totalCostText.text = $"{total:N0}G";
        }
    }
}

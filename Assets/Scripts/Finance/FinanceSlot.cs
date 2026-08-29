using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 取引所（BlackSmith Special タブ）の商品1行。ItemShopSlot の金融版。
/// 参照は未配線（null）でも動作する。
/// </summary>
public class FinanceSlot : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI infoText;      // 利率/満期 or 構成の説明
    [SerializeField] private TextMeshProUGUI holdingsText;  // 保有口数
    [SerializeField] private Button selectButton;
    [SerializeField] private GameObject lockedOverlay;      // 未解放表示
    [SerializeField] private GameObject selectionHighlight;

    public Subject<string> OnSelected { get; } = new();

    public string ProductId { get; private set; }

    private void Awake()
    {
        if (selectButton != null)
            selectButton.onClick.AddListener(() =>
            {
                if (!string.IsNullOrEmpty(ProductId))
                    OnSelected.OnNext(ProductId);
            });
    }

    public void Setup(FinancialProductData product, int unitPrice, int heldUnits, bool unlocked)
    {
        ProductId = product.productId;

        if (iconImage != null)
        {
            iconImage.sprite = product.icon;
            iconImage.enabled = product.icon != null;
        }
        if (nameText != null) nameText.text = product.productName;
        if (priceText != null) priceText.text = $"{unitPrice:N0}G";
        if (infoText != null)
        {
            infoText.text = product.kind == FinancialProductKind.Bond
                ? $"利率{product.bondInterestRate:P0} / {product.bondMaturityTurns}日満期"
                : (product.useAttributeFilter ? $"{AttributeLabel(product.attribute)}属性ファンド" : "全銘柄ファンド");
        }
        if (holdingsText != null)
            holdingsText.text = heldUnits > 0 ? $"保有 {heldUnits}" : "";
        if (lockedOverlay != null) lockedOverlay.SetActive(!unlocked);
        if (selectButton != null) selectButton.interactable = unlocked;
    }

    public void SetSelected(bool selected)
    {
        if (selectionHighlight != null) selectionHighlight.SetActive(selected);
    }

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
}

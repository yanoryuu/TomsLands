using UnityEngine;
using UnityEngine.UI;
using TMPro;
using R3;

/// <summary>
/// 需要ダッシュボードの1アイテムスロット。
/// アイコン・需要・価格・在庫・陳列トグル・バッジを表示する。
/// </summary>
public class DemandDashboardSlot : MonoBehaviour
{
    [Header("基本情報")]
    [SerializeField] private Image itemIcon;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI attributeText;

    [Header("需要")]
    [SerializeField] private Slider demandSlider;
    [SerializeField] private TextMeshProUGUI demandText;

    [Header("価格")]
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI priceTrendText;

    [Header("在庫")]
    [SerializeField] private TextMeshProUGUI stockText;

    [Header("バッジ")]
    [SerializeField] private GameObject badgeHighDemand;
    [SerializeField] private GameObject badgeTrendUp;
    [SerializeField] private GameObject badgePriceUp;
    [SerializeField] private GameObject badgeLowStock;

    public string ItemId { get; private set; }

    public void Setup(RuntimeItemData data)
    {
        ItemId = data.ItemId;

        if (itemIcon != null) itemIcon.sprite = data.ItemIcon;
        if (itemNameText != null) itemNameText.text = data.ItemName;
        if (attributeText != null) attributeText.text = data.ItemAttribute.ToString();

        float demand = data.Demand.Value;
        if (demandSlider != null) demandSlider.value = demand;
        if (demandText != null) demandText.text = $"{demand:P0}";

        if (priceText != null) priceText.text = $"{data.CurrentPrice.Value:N0}G";
        UpdatePriceTrend(data.CurrentPrice.Value, data.PreviousPrice);

        if (stockText != null) stockText.text = $"×{data.Stock.Value}";

        // バッジ
        if (badgeHighDemand != null)  badgeHighDemand.SetActive(demand >= 0.7f);
        if (badgeTrendUp != null)     badgeTrendUp.SetActive(demand > data.PreviousDemand + 0.05f);
        if (badgePriceUp != null)     badgePriceUp.SetActive(data.CurrentPrice.Value > data.PreviousPrice);
        if (badgeLowStock != null)    badgeLowStock.SetActive(data.Stock.Value > 0 && data.Stock.Value <= 2);
    }

    private void UpdatePriceTrend(int current, int previous)
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
}

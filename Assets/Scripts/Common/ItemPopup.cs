using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ItemPopupScene 内に配置する市場分析ポップアップUI。
/// ItemPopUpData の型付きフィールドを受け取り、各UIパーツに反映する。
/// </summary>
public class ItemPopup : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Header("Demand")]
    [SerializeField] private TextMeshProUGUI demandValueText;
    [SerializeField] private TextMeshProUGUI demandDeltaText;
    [SerializeField] private TextMeshProUGUI demandLevelText;

    [Header("Price")]
    [SerializeField] private TextMeshProUGUI priceValueText;
    [SerializeField] private TextMeshProUGUI priceDeltaText;
    [SerializeField] private TextMeshProUGUI priceLevelText;

    [Header("Stock")]
    [SerializeField] private TextMeshProUGUI stockText;
    [Header("Sales")]
    [SerializeField] private TextMeshProUGUI salesRateText;

    [Header("Recommendation")]
    [SerializeField] private TextMeshProUGUI recommendationText;

    [Header("Buttons")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private TextMeshProUGUI confirmButtonText;

    /// <summary>
    /// ItemPopUpData を受け取って全UIを更新する。
    /// </summary>
    public void SetData(ItemPopUpData data)
    {
        // ---- ヘッダー ----
        if (titleText) titleText.text = data.ItemName ?? "";
        if (iconImage && data.Icon != null) iconImage.sprite = data.Icon;
        if (descriptionText) descriptionText.text = data.Description ?? "";

        // ---- 需要 ----
        float demandPct = data.Demand * 100f;
        float prevDemandPct = data.PreviousDemand * 100f;
        float demandDelta = demandPct - prevDemandPct;

        if (demandValueText) demandValueText.text = $"{demandPct:F0}%";
        if (demandDeltaText)
        {
            if (demandDelta >= 0)
            {
                demandDeltaText.text = $"▲ +{demandDelta:F1}%";
                demandDeltaText.color = new Color32(0, 204, 0, 255);   // 緑
            }
            else
            {
                demandDeltaText.text = $"▼ {demandDelta:F1}%";
                demandDeltaText.color = new Color32(255, 68, 68, 255); // 赤
            }
        }

        if (demandLevelText)
        {
            if (data.Demand >= 0.8f)
            {
                demandLevelText.text = "★★★ 大人気";
                demandLevelText.color = new Color32(255, 102, 0, 255);
            }
            else if (data.Demand >= 0.5f)
            {
                demandLevelText.text = "★★ 好調";
                demandLevelText.color = new Color32(255, 204, 0, 255);
            }
            else if (data.Demand >= 0.3f)
            {
                demandLevelText.text = "★ 普通";
                demandLevelText.color = new Color32(170, 170, 170, 255);
            }
            else
            {
                demandLevelText.text = "☆ 低調";
                demandLevelText.color = new Color32(102, 102, 255, 255);
            }
        }

        // ---- 価格 ----
        float priceRatio = data.BasePrice > 0 ? (float)data.CurrentPrice / data.BasePrice * 100f : 100f;
        int priceDelta = data.CurrentPrice - data.PreviousPrice;

        if (priceValueText) priceValueText.text = $"{data.CurrentPrice}G";
        if (priceDeltaText)
        {
            if (priceDelta >= 0)
            {
                priceDeltaText.text = $"▲ +{priceDelta}G";
                priceDeltaText.color = new Color32(0, 204, 0, 255);
            }
            else
            {
                priceDeltaText.text = $"▼ {priceDelta}G";
                priceDeltaText.color = new Color32(255, 68, 68, 255);
            }
        }

        if (priceLevelText)
        {
            if (priceRatio >= 150f)
            {
                priceLevelText.text = $"基準比 {priceRatio:F0}% — 高騰中";
                priceLevelText.color = new Color32(255, 68, 68, 255);
            }
            else if (priceRatio >= 110f)
            {
                priceLevelText.text = $"基準比 {priceRatio:F0}% — やや高め";
                priceLevelText.color = new Color32(255, 204, 0, 255);
            }
            else if (priceRatio >= 90f)
            {
                priceLevelText.text = $"基準比 {priceRatio:F0}% — 適正価格";
                priceLevelText.color = Color.white;
            }
            else if (priceRatio >= 50f)
            {
                priceLevelText.text = $"基準比 {priceRatio:F0}% — お買い得";
                priceLevelText.color = new Color32(0, 204, 0, 255);
            }
            else
            {
                priceLevelText.text = $"基準比 {priceRatio:F0}% — 底値付近";
                priceLevelText.color = new Color32(0, 204, 255, 255);
            }
        }

        // ---- 在庫 ----
        if (stockText) stockText.text = $"{data.Stock}";

        // ---- 売れやすさ ----
        if (salesRateText)
        {
            if (data.SalesRate >= 2.0f)
            {
                salesRateText.text = "非常に売れやすい";
                salesRateText.color = new Color32(255, 102, 0, 255);
            }
            else if (data.SalesRate >= 1.2f)
            {
                salesRateText.text = "売れやすい";
                salesRateText.color = new Color32(0, 204, 0, 255);
            }
            else if (data.SalesRate >= 0.8f)
            {
                salesRateText.text = "普通";
                salesRateText.color = Color.white;
            }
            else
            {
                salesRateText.text = "売れにくい";
                salesRateText.color = new Color32(255, 68, 68, 255);
            }
        }

        // ---- 仕入れ判定 ----
        if (recommendationText)
        {
            if (data.Demand >= 0.6f && priceRatio <= 110f)
            {
                recommendationText.text = "◎ 今が仕入れ時！";
                recommendationText.color = new Color32(0, 255, 0, 255);
            }
            else if (data.Demand >= 0.5f && priceRatio <= 130f)
            {
                recommendationText.text = "○ 悪くない";
                recommendationText.color = new Color32(255, 204, 0, 255);
            }
            else if (data.Demand < 0.3f || priceRatio >= 150f)
            {
                recommendationText.text = "△ 見送り推奨";
                recommendationText.color = new Color32(255, 68, 68, 255);
            }
            else
            {
                recommendationText.text = "― 様子見";
                recommendationText.color = new Color32(170, 170, 170, 255);
            }
        }

        // ---- ボタン ----
        if (confirmButtonText) confirmButtonText.text = data.ConfirmButtonText ?? "閉じる";
        if (confirmButton)
        {
            confirmButton.onClick.AddListener(() =>
            {
                data.OnConfirm?.Invoke();
            });
        }
    }
}


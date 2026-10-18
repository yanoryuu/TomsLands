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

    private static readonly Color32 Green = new(0, 204, 0, 255);
    private static readonly Color32 Red = new(255, 68, 68, 255);
    private static readonly Color32 Orange = new(255, 102, 0, 255);
    private static readonly Color32 Yellow = new(255, 204, 0, 255);
    private static readonly Color32 Gray = new(170, 170, 170, 255);
    private static readonly Color32 Blue = new(102, 102, 255, 255);
    private static readonly Color32 Cyan = new(0, 204, 255, 255);
    private static readonly Color32 BrightGreen = new(0, 255, 0, 255);

    /// <summary>
    /// ItemPopUpData を受け取って全UIを更新する。
    /// </summary>
    public void SetData(ItemPopUpData data)
    {
        ApplyHeader(data);
        ApplyDemand(data);

        float priceRatio = data.BasePrice > 0 ? (float)data.CurrentPrice / data.BasePrice * 100f : 100f;
        ApplyPrice(data, priceRatio);

        // ---- 在庫 ----
        if (stockText) stockText.text = $"{data.Stock}";

        ApplySalesRate(data);
        ApplyRecommendation(data, priceRatio);

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

    private void ApplyHeader(ItemPopUpData data)
    {
        if (titleText) titleText.text = data.ItemName ?? "";
        if (iconImage && data.Icon != null) iconImage.sprite = data.Icon;
        if (descriptionText) descriptionText.text = data.Description ?? "";
    }

    private void ApplyDemand(ItemPopUpData data)
    {
        float demandPct = data.Demand * 100f;
        float prevDemandPct = data.PreviousDemand * 100f;
        float demandDelta = demandPct - prevDemandPct;

        if (demandValueText) demandValueText.text = $"{demandPct:F0}%";
        if (demandDeltaText)
        {
            if (demandDelta >= 0) SetLabel(demandDeltaText, $"▲ +{demandDelta:F1}%", Green);
            else SetLabel(demandDeltaText, $"▼ {demandDelta:F1}%", Red);
        }

        if (demandLevelText)
        {
            if (data.Demand >= 0.8f) SetLabel(demandLevelText, "★★★ 大人気", Orange);
            else if (data.Demand >= 0.5f) SetLabel(demandLevelText, "★★ 好調", Yellow);
            else if (data.Demand >= 0.3f) SetLabel(demandLevelText, "★ 普通", Gray);
            else SetLabel(demandLevelText, "☆ 低調", Blue);
        }
    }

    private void ApplyPrice(ItemPopUpData data, float priceRatio)
    {
        int priceDelta = data.CurrentPrice - data.PreviousPrice;

        if (priceValueText) priceValueText.text = $"{data.CurrentPrice:N0}G";
        if (priceDeltaText)
        {
            if (priceDelta >= 0) SetLabel(priceDeltaText, $"▲ +{priceDelta:N0}G", Green);
            else SetLabel(priceDeltaText, $"▼ {priceDelta:N0}G", Red);
        }

        if (priceLevelText)
        {
            if (priceRatio >= 150f) SetLabel(priceLevelText, $"基準比 {priceRatio:F0}% — 高騰中", Red);
            else if (priceRatio >= 110f) SetLabel(priceLevelText, $"基準比 {priceRatio:F0}% — やや高め", Yellow);
            else if (priceRatio >= 90f) SetLabel(priceLevelText, $"基準比 {priceRatio:F0}% — 適正価格", Color.white);
            else if (priceRatio >= 50f) SetLabel(priceLevelText, $"基準比 {priceRatio:F0}% — お買い得", Green);
            else SetLabel(priceLevelText, $"基準比 {priceRatio:F0}% — 底値付近", Cyan);
        }
    }

    private void ApplySalesRate(ItemPopUpData data)
    {
        if (!salesRateText) return;

        if (data.SalesRate >= 2.0f) SetLabel(salesRateText, "非常に売れやすい", Orange);
        else if (data.SalesRate >= 1.2f) SetLabel(salesRateText, "売れやすい", Green);
        else if (data.SalesRate >= 0.8f) SetLabel(salesRateText, "普通", Color.white);
        else SetLabel(salesRateText, "売れにくい", Red);
    }

    /// <summary>仕入れ判定（需要と基準比価格から）。</summary>
    private void ApplyRecommendation(ItemPopUpData data, float priceRatio)
    {
        if (!recommendationText) return;

        if (data.Demand >= 0.6f && priceRatio <= 110f) SetLabel(recommendationText, "◎ 今が仕入れ時！", BrightGreen);
        else if (data.Demand >= 0.5f && priceRatio <= 130f) SetLabel(recommendationText, "○ 悪くない", Yellow);
        else if (data.Demand < 0.3f || priceRatio >= 150f) SetLabel(recommendationText, "△ 見送り推奨", Red);
        else SetLabel(recommendationText, "― 様子見", Gray);
    }

    private static void SetLabel(TMP_Text label, string text, Color color)
    {
        label.text = text;
        label.color = color;
    }
}

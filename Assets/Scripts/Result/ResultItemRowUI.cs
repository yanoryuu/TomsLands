using TMPro;
using UnityEngine;

/// <summary>
/// リザルト画面のアイテム行UI。
/// 各アイテムの最終状態（名前、残在庫、単価、資産額）を表示する。
/// ResultItemRowプレハブにアタッチして使用する。
/// </summary>
public class ResultItemRowUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI valueText;
    [SerializeField] private TextMeshProUGUI demandText;

    /// <summary>
    /// 行の内容をセットアップする
    /// </summary>
    public void Setup(ResultItemSummary data)
    {
        if (data == null) return;

        if (itemNameText != null)
            itemNameText.text = data.ItemName;

        if (stockText != null)
            stockText.text = $"{data.RemainingStock}個";

        if (priceText != null)
            priceText.text = $"{data.CurrentPrice:#,0}G";

        if (valueText != null)
            valueText.text = $"{data.StockValue:#,0}G";

        if (demandText != null)
        {
            if (data.Demand >= 0.7f)
            {
                demandText.text = "↑";
                demandText.color = new Color(0.9f, 0.2f, 0.2f); // 赤
            }
            else if (data.Demand <= 0.3f)
            {
                demandText.text = "↓";
                demandText.color = new Color(0.2f, 0.4f, 0.9f); // 青
            }
            else
            {
                demandText.text = "→";
                demandText.color = Color.white;
            }
        }
    }
}


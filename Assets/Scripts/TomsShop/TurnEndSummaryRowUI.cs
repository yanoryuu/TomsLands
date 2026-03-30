using TMPro;
using UnityEngine;

/// <summary>
/// ターン終了サマリーパネルの1行表示用UIコンポーネント
/// </summary>
public class TurnEndSummaryRowUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI soldCountText;
    [SerializeField] private TextMeshProUGUI revenueText;
    [SerializeField] private TextMeshProUGUI trendText;

    /// <summary>
    /// 行データを設定して表示を更新する
    /// </summary>
    public void Setup(TurnEndSummaryItemData data)
    {
        if (itemNameText != null)
            itemNameText.text = data.ItemName;

        if (soldCountText != null)
            soldCountText.text = $"{data.SoldCount}個";

        if (revenueText != null)
            revenueText.text = $"{data.Revenue}G";

        if (trendText != null)
        {
            switch (data.Trend)
            {
                case DemandTrend.Up:
                    trendText.text = "↑";
                    trendText.color = Color.red;
                    break;
                case DemandTrend.Down:
                    trendText.text = "↓";
                    trendText.color = Color.blue;
                    break;
                default:
                    trendText.text = "→";
                    trendText.color = Color.gray;
                    break;
            }
        }
    }
}


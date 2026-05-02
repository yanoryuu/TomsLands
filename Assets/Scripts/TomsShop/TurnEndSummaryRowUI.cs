using UnityEngine.UI;
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
    [SerializeField] private TextMeshProUGUI causeText;
    [SerializeField] private Image itemIcon;

    [Header("需要トレンドアイコン")]
    [SerializeField] private Image trendIcon;
    [SerializeField] private Sprite trendUpSprite;
    [SerializeField] private Sprite trendDownSprite;
    [SerializeField] private Sprite trendNeutralSprite;

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
        
        if (itemIcon != null)
            itemIcon.sprite = data.ItemIcon;

        if (trendIcon != null)
        {
            trendIcon.sprite = data.Trend switch
            {
                DemandTrend.Up   => trendUpSprite,
                DemandTrend.Down => trendDownSprite,
                _                => trendNeutralSprite,
            };
        }

        if (causeText != null)
        {
            switch (data.Cause)
            {
                case DemandChangeCause.TrendUp:
                    causeText.text = "流行中！";
                    causeText.color = new Color(1f, 0.55f, 0f);
                    break;
                case DemandChangeCause.Displayed:
                    causeText.text = "陳列効果";
                    causeText.color = new Color(0.2f, 0.75f, 0.2f);
                    break;
                case DemandChangeCause.TrendDown:
                    causeText.text = "流行下落";
                    causeText.color = new Color(0.4f, 0.4f, 1f);
                    break;
                default:
                    causeText.text = "安定";
                    causeText.color = Color.gray;
                    break;
            }
        }
    }
}


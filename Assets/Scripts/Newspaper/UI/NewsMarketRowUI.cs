using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 右下ペインの相場1行（Docs/News_Spec.md §10.2）。
/// アイコン・種別名・スパークライン・変動率・Heat ドットと語（凪〜荒れ）。
/// どの種別を集計するかはプレハブ側で行ごとに決める。
/// </summary>
public class NewsMarketRowUI : MonoBehaviour
{
    [Tooltip("集計する種別。allItems なら全銘柄")]
    [SerializeField] private ItemTypeData.ItemType itemType = ItemTypeData.ItemType.Weapon;
    [SerializeField] private bool allItems;

    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private Image iconImage;
    [SerializeField] private PriceChartView sparkline;     // 27スパークライン枠の中に置く（drawDemand=false）
    [SerializeField] private TextMeshProUGUI changeText;
    [SerializeField] private Image heatDot;
    [Tooltip("26Heat 凪 / 静か / 平常 / 活況 / 荒れ の順")]
    [SerializeField] private Sprite[] heatSprites = new Sprite[5];
    [SerializeField] private TextMeshProUGUI heatText;
    [SerializeField] private Color upColor = new Color(0.30f, 0.62f, 0.25f);
    [SerializeField] private Color downColor = new Color(0.66f, 0.20f, 0.17f);
    [SerializeField] private Color flatColor = new Color(0.37f, 0.23f, 0.13f);

    public ItemTypeData.ItemType? Filter => allItems ? (ItemTypeData.ItemType?)null : itemType;

    /// <summary>種別の表示名。ラベルが空ならこれを入れる。</summary>
    public static string LabelOf(ItemTypeData.ItemType? type) => type switch
    {
        ItemTypeData.ItemType.Weapon => "武器",
        ItemTypeData.ItemType.Armor => "防具",
        ItemTypeData.ItemType.Tool => "道具",
        _ => "全体",
    };

    public void SetData(NewsMarketSummary.Row row)
    {
        if (iconImage != null) iconImage.enabled = iconImage.sprite != null;
        if (labelText != null && string.IsNullOrEmpty(labelText.text)) labelText.text = LabelOf(Filter);

        if (sparkline != null)
        {
            if (row.hasData) sparkline.SetData(row.spark);
            else sparkline.Clear();
        }

        if (changeText != null)
        {
            int pct = Mathf.RoundToInt(row.changeRate * 100f);
            changeText.text = row.hasData ? (pct > 0 ? $"+{pct}%" : pct < 0 ? $"-{-pct}%" : "0%") : "-";
            changeText.color = pct > 0 ? upColor : pct < 0 ? downColor : flatColor;
        }

        if (heatDot != null && heatSprites != null && row.heatLevel >= 0 && row.heatLevel < heatSprites.Length
            && heatSprites[row.heatLevel] != null)
            heatDot.sprite = heatSprites[row.heatLevel];
        if (heatText != null) heatText.text = row.heatLabel;
    }
}

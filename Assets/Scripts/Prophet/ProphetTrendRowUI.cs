using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ProphetTrendRowUI : MonoBehaviour
{
    [SerializeField] private Image itemIcon;
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private TMP_Text trendText;
    [SerializeField] private TMP_Text demandText;

    public void SetData(Sprite icon, string itemName, float trend, float demand)
    {
        if (itemIcon != null && icon != null) itemIcon.sprite = icon;
        itemNameText.text = itemName;
        trendText.text = trend >= 0f ? $"▲{trend:F2}" : $"▼{Mathf.Abs(trend):F2}";
        demandText.text = $"{demand * 100f:F0}%";
    }
}

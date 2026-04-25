using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ProphetRankingRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private Image itemIcon;
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private TMP_Text priceText;

    public void SetData(int rank, Sprite icon, string itemName, int price)
    {
        rankText.text = rank.ToString();
        if (itemIcon != null && icon != null) itemIcon.sprite = icon;
        itemNameText.text = itemName;
        priceText.text = $"{price}G";
    }
}

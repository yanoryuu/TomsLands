using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BattleResultSoldItemSlot : MonoBehaviour
{
    [SerializeField] private Image itemIcon;
    [SerializeField] private TextMeshProUGUI countText;

    public void Setup(Sprite icon, int count)
    {
        if (itemIcon != null)
            itemIcon.sprite = icon;

        if (countText != null)
            countText.text = $"x{count}";
    }
}

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BattleResultSoldItemSlot : MonoBehaviour
{
    [SerializeField] private Image itemIcon;
    [SerializeField] private TextMeshProUGUI countText;

    public void Setup(Sprite icon, int count) => Setup(icon, count, 0);

    /// <summary>revenue はこの商品の配信中の売上（売れたときの価格の合計）。0 なら個数だけ出す。</summary>
    public void Setup(Sprite icon, int count, int revenue)
    {
        if (itemIcon != null)
            itemIcon.sprite = icon;

        if (countText == null) return;
        if (revenue <= 0)
        {
            countText.text = $"x{count}";
            return;
        }
        // 個数の枠（自動縮小）に2行入れると読めない大きさになるので、縮めずに枠の外まで出す
        countText.enableAutoSizing = false;
        countText.fontSize = 22f;
        countText.textWrappingMode = TextWrappingModes.NoWrap;
        countText.overflowMode = TextOverflowModes.Overflow;
        countText.text = $"x{count}\n{revenue:N0}G";
    }
}

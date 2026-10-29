using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 朝レポートの入金1行（アイコン・出どころの名前・台数・額）。
/// 見た目はレリック3択のカード（RelicChoiceSlot）と同じ部品（アイコン枠・アイコン背景・説明ボックス）を使う。
/// 参照は未配線（null）でも動作する。
/// </summary>
public class MorningIncomeRowView : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [Tooltip("台数（2台以上のときだけ「×2」）")]
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private TextMeshProUGUI amountText;

    /// <summary>行の中身を差し替える。icon が null なら fallbackIcon（金貨）を出す。</summary>
    public void Setup(MorningIncomeEntry entry, Sprite fallbackIcon)
    {
        if (entry == null) return;
        if (nameText != null)
        {
            // 長い名前（債券名など）は枠内で縮めて、それでも溢れたら省略
            nameText.textWrappingMode = TextWrappingModes.NoWrap;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.text = entry.DisplayName;
        }
        if (countText != null)
        {
            countText.text = entry.CountLabel;
            countText.gameObject.SetActive(!string.IsNullOrEmpty(entry.CountLabel));
        }
        if (amountText != null) amountText.text = entry.AmountLabel;
        if (iconImage != null)
        {
            var icon = entry.Icon != null ? entry.Icon : fallbackIcon;
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            iconImage.enabled = icon != null;
        }
    }
}

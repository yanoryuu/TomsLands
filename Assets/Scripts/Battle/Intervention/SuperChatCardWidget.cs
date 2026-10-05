using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>チャット欄に積むスパチャカード1枚（金額ピル＋短いコメント）。</summary>
public class SuperChatCardWidget : MonoBehaviour
{
    [SerializeField] private Image body;
    [SerializeField] private Image amountPill;
    [SerializeField] private TMP_Text amountLabel;
    [SerializeField] private TMP_Text messageLabel;
    [Tooltip("赤スパ・自分のスパチャに付ける金縁")]
    [SerializeField] private Image rim;

    private static readonly Color DarkText = new(0.23f, 0.14f, 0.08f, 1f);

    public void Bind(SuperChatInfo info)
    {
        GetColors(info.Color, out var header, out var bodyCol, out bool darkText);
        if (body != null) body.color = bodyCol;
        if (amountPill != null) amountPill.color = header;
        if (amountLabel != null)
        {
            amountLabel.text = info.Amount.ToString("N0") + "G";
            amountLabel.color = darkText ? DarkText : Color.white;
        }
        if (messageLabel != null)
        {
            bool has = !string.IsNullOrEmpty(info.Message);
            messageLabel.gameObject.SetActive(has);
            messageLabel.text = has ? info.Message : string.Empty;
            messageLabel.color = darkText ? DarkText : Color.white;
        }
        if (rim != null)
        {
            bool showRim = info.Color == SuperChatColor.Red || info.IsPlayer;
            rim.gameObject.SetActive(showRim);
            rim.color = info.Color == SuperChatColor.Red ? Color.white : new Color(1f, 1f, 1f, 0.75f);
        }
    }

    /// <summary>YouTube 準拠の7色（ヘッダー＝濃、本体＝淡）。店の暖色トーンに寄せて彩度を少し落としている。</summary>
    public static void GetColors(SuperChatColor c, out Color header, out Color body, out bool darkText)
    {
        switch (c)
        {
            case SuperChatColor.Blue:
                header = Hex(0x1C5FB0); body = Hex(0x2F86D6); darkText = false; break;
            case SuperChatColor.Cyan:
                header = Hex(0x13A9C2); body = Hex(0x5ED6E6); darkText = true; break;
            case SuperChatColor.Green:
                header = Hex(0x1E8E4A); body = Hex(0x48B95E); darkText = false; break;
            case SuperChatColor.Yellow:
                header = Hex(0xE8A33D); body = Hex(0xF6CB5A); darkText = true; break;
            case SuperChatColor.Orange:
                header = Hex(0xD2601A); body = Hex(0xEE8A2E); darkText = false; break;
            case SuperChatColor.Magenta:
                header = Hex(0xB51F62); body = Hex(0xDC3D83); darkText = false; break;
            default: // Red
                header = Hex(0xA8241C); body = Hex(0xD63A2C); darkText = false; break;
        }
    }

    private static Color Hex(int rgb) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
}

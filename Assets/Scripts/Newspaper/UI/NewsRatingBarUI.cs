using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 星（またはコマ）を並べた段階表示。社カードの 信頼 / 速報 / 網羅 に使う。
/// 星の枚数はプレハブ側で並べた pips の数（通常5）。
/// </summary>
public class NewsRatingBarUI : MonoBehaviour
{
    [SerializeField] private Image[] pips;
    [SerializeField] private Sprite onSprite;    // 3−1星
    [SerializeField] private Sprite offSprite;   // 3−0星

    public void SetValue(int value)
    {
        if (pips == null) return;
        for (int i = 0; i < pips.Length; i++)
        {
            var img = pips[i];
            if (img == null) continue;
            bool on = i < value;
            var sprite = on ? onSprite : offSprite;
            if (sprite != null) img.sprite = sprite;
            else img.color = on ? Color.white : new Color(1f, 1f, 1f, 0.25f);
        }
    }
}

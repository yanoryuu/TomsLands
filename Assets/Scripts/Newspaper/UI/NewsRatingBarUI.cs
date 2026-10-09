using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 星（またはコマ）を並べた段階表示。社カードの 信頼 / 速報 / 網羅 に使う。
/// 星の枚数はプレハブ側で並べた pips の数（通常5）。
/// スプライトを切り替える（on/offSprite）か、同じスプライトの色を切り替える（on/offColor）。
/// </summary>
public class NewsRatingBarUI : MonoBehaviour
{
    [SerializeField] private Image[] pips;
    [SerializeField] private Sprite onSprite;
    [SerializeField] private Sprite offSprite;
    [SerializeField] private Color onColor = Color.white;
    [SerializeField] private Color offColor = new Color(1f, 1f, 1f, 0.25f);

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
            img.color = on ? onColor : offColor;
        }
    }
}

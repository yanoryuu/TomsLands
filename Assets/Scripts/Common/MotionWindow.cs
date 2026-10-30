using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 小窓の「ぽよん開閉」（UIMotion.Open / Close）で拡縮する窓を探す小さな助け。
/// 小窓のルートが画面いっぱい（暗幕つきのポップアップなど）のときは、ルートを拡縮すると暗幕まで縮んで見えるので、
/// 直下の「画面いっぱいでない」子のうち一番大きいもの（＝窓の枠）を返す。
/// ルート自体が窓の大きさなら null（UIMotion がルートを拡縮する）。シーン・プレハブには触らない。
/// </summary>
public static class MotionWindow
{
    public static RectTransform Of(GameObject panel)
    {
        if (panel == null || !(panel.transform is RectTransform root)) return null;
        bool rootCanvas = panel.TryGetComponent<Canvas>(out var canvas) && canvas.isRootCanvas;
        if (!rootCanvas && !IsFullStretch(root)) return null;

        RectTransform best = null;
        float bestArea = 0f;
        foreach (Transform child in root)
        {
            if (!child.gameObject.activeSelf || !(child is RectTransform rt) || IsFullStretch(rt)) continue;
            if (!rt.TryGetComponent<Graphic>(out _) && rt.childCount == 0) continue;
            float area = Mathf.Abs(rt.rect.width * rt.rect.height);
            if (area > bestArea)
            {
                best = rt;
                bestArea = area;
            }
        }
        return best;
    }

    private static bool IsFullStretch(RectTransform rt) =>
        rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one;
}

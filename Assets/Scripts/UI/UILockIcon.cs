using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 未解放のボタンに付ける鍵の印。専用の素材が無いので、木と金のトーン（濃茶の縁・金の本体）で
/// コードから小さなスプライトを1回だけ描いて使い回す。素材ができたら <see cref="Attach"/> に渡すスプライトを差し替える。
/// </summary>
public static class UILockIcon
{
    private const int Size = 128;
    private static Sprite _sprite;

    private static readonly Color Outline = new(0.29f, 0.17f, 0.09f, 1f);   // 濃茶
    private static readonly Color BodyFill = new(0.96f, 0.74f, 0.25f, 1f);  // 金
    private static readonly Color BodyShade = new(0.85f, 0.58f, 0.16f, 1f); // 金の影
    private static readonly Color ShackleFill = new(0.82f, 0.80f, 0.76f, 1f);

    /// <summary>鍵のスプライト（初回に描く）。</summary>
    public static Sprite Sprite => _sprite != null ? _sprite : (_sprite = Draw());

    /// <summary>
    /// target の子に鍵の印を付ける（すでにあれば表示を切り替えるだけ）。size は表示の一辺、anchor は親の中の位置（0〜1）。
    /// </summary>
    public static GameObject Attach(Transform target, bool visible, float size = 40f, Vector2? anchor = null, Vector2? offset = null)
    {
        if (target == null) return null;
        const string childName = "LockIcon";
        var existing = target.Find(childName);
        if (existing == null && !visible) return null;

        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(childName, typeof(RectTransform), typeof(Image));
            go.layer = target.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(target, false);
            var a = anchor ?? new Vector2(0f, 1f);
            rt.anchorMin = rt.anchorMax = a;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = offset ?? new Vector2(size * 0.45f, -size * 0.45f);
            var img = go.GetComponent<Image>();
            img.sprite = Sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }
        go.SetActive(visible);
        if (visible) go.transform.SetAsLastSibling();
        return go;
    }

    private static Sprite Draw()
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "UILockIcon",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var pixels = new Color[Size * Size];

        // 座標は下が0。本体: 角丸の四角、つる: 上半分の輪＋左右の脚、鍵穴: 丸＋下に細い四角
        var bodyCenter = new Vector2(64f, 42f);
        var bodyHalf = new Vector2(42f, 32f);
        const float bodyRadius = 12f;
        var shackleCenter = new Vector2(64f, 80f);
        const float shackleR = 25f;
        const float shackleHalfWidth = 7f;
        const float outlineWidth = 5f;

        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            Color c = Color.clear;

            float shackle = ShackleDistance(p, shackleCenter, shackleR) - shackleHalfWidth;
            c = Over(c, Outline, Coverage(shackle - outlineWidth));
            c = Over(c, ShackleFill, Coverage(shackle));

            float body = RoundBox(p - bodyCenter, bodyHalf, bodyRadius);
            c = Over(c, Outline, Coverage(body - outlineWidth));
            c = Over(c, BodyFill, Coverage(body));
            // 本体の下側に少し影（平たいカートゥーンの段差）
            float shade = Mathf.Max(body, (p.y - (bodyCenter.y - bodyHalf.y + 14f)));
            c = Over(c, BodyShade, Coverage(shade) * Coverage(body));

            float hole = Mathf.Min(
                (p - new Vector2(64f, 48f)).magnitude - 8f,
                Box(p - new Vector2(64f, 34f), new Vector2(3.5f, 10f)));
            c = Over(c, Outline, Coverage(hole));

            pixels[y * Size + x] = c;
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "UILockIcon";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    // U 字のつるの中心線までの距離（上は半円、下は左右の脚。脚は本体の中まで伸ばす）
    private static float ShackleDistance(Vector2 p, Vector2 c, float r)
    {
        if (p.y >= c.y) return Mathf.Abs((p - c).magnitude - r);
        if (p.y < c.y - 30f) return 999f;
        return Mathf.Min(Mathf.Abs(p.x - (c.x - r)), Mathf.Abs(p.x - (c.x + r)));
    }

    private static float RoundBox(Vector2 p, Vector2 half, float r)
    {
        var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + new Vector2(r, r);
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
    }

    private static float Box(Vector2 p, Vector2 half) => RoundBox(p, half, 0f);

    private static float Coverage(float sdf) => Mathf.Clamp01(0.5f - sdf);

    private static Color Over(Color dst, Color src, float a)
    {
        if (a <= 0f) return dst;
        float sa = src.a * a;
        float outA = sa + dst.a * (1f - sa);
        if (outA <= 0f) return Color.clear;
        var rgb = (new Vector3(src.r, src.g, src.b) * sa + new Vector3(dst.r, dst.g, dst.b) * dst.a * (1f - sa)) / outA;
        return new Color(rgb.x, rgb.y, rgb.z, outA);
    }
}

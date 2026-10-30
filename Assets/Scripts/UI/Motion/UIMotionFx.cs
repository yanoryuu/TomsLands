using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI アニメーション部品の下回り: コード生成の演出用 Canvas（層）、コードで描く絵、UI 要素の生成、座標の変換。
/// 演出用の層は入力を受けない（GraphicRaycaster を付けない）ので、オートプレイや操作を邪魔しない。
/// </summary>
public static class UIMotionFx
{
    // =====================================================
    // 層（コード生成の Canvas。DontDestroyOnLoad）
    // =====================================================

    /// <summary>ダメージ数字（#14）。店・配信の UI より上、ポップアップより下の目安。</summary>
    public const int DamageOrder = 1200;
    /// <summary>コイン・浮き文字（#2）など、画面の上に一瞬重ねるもの。</summary>
    public const int OverlayOrder = 30000;
    /// <summary>祝い（#5）。</summary>
    public const int CelebrationOrder = 31000;
    /// <summary>トースト（#9）。ローディング（32700）よりは下。</summary>
    public const int ToastOrder = 31500;

    private static readonly Dictionary<int, RectTransform> Layers = new();

    /// <summary>指定の並び順の演出用レイヤーを返す（無ければ作る）。子は中央基準で置く。</summary>
    public static RectTransform Layer(int sortingOrder)
    {
        if (Layers.TryGetValue(sortingOrder, out var layer) && layer != null) return layer;

        var go = new GameObject($"UIMotionLayer_{sortingOrder}", typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        var cam = UIMotion.CaptureCamera;
        if (cam != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 50f - sortingOrder / 1000f; // 撮影時: 並び順が大きいほど手前
        }
        else
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        if (Application.isPlaying) Object.DontDestroyOnLoad(go);

        layer = (RectTransform)go.transform;
        Layers[sortingOrder] = layer;
        return layer;
    }

    /// <summary>層の参照を捨てる（Play の開始時・撮影で新しいシーンにしたとき）。</summary>
    public static void ResetLayers() => Layers.Clear();

    /// <summary>別の Canvas にある UI の中心を、演出用レイヤーの座標（中央基準）に直す。</summary>
    public static Vector2 LayerPos(RectTransform layer, RectTransform source, Vector2 offset = default)
    {
        if (source == null) return offset;
        var canvas = source.GetComponentInParent<Canvas>();
        Camera srcCam = null;
        if (canvas != null)
        {
            var root = canvas.rootCanvas;
            if (root.renderMode != RenderMode.ScreenSpaceOverlay) srcCam = root.worldCamera;
        }
        Vector3 world = source.TransformPoint(source.rect.center);
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(srcCam, world);
        return ScreenToLayer(layer, screen) + offset;
    }

    /// <summary>スクリーン座標 → 演出用レイヤーの座標。</summary>
    public static Vector2 ScreenToLayer(RectTransform layer, Vector2 screen)
    {
        var canvas = layer.GetComponent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screen, cam, out var local);
        return local;
    }

    // =====================================================
    // UI 要素の生成
    // =====================================================

    /// <summary>入力を受けない Image を作る（中央基準）。</summary>
    public static Image NewImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent != null ? parent.gameObject.layer : 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
        return img;
    }

    /// <summary>入力を受けない TMP の文字を作る（既定フォント・中央揃え・折り返し無し）。</summary>
    public static TextMeshProUGUI NewText(string name, Transform parent, string text, float fontSize, Color color, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent != null ? parent.gameObject.layer : 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>全面に広げる。</summary>
    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>レイアウトグループの並びに入らないようにする（演出用に足した子）。</summary>
    public static void IgnoreLayout(GameObject go)
    {
        if (!go.TryGetComponent<LayoutElement>(out var le)) le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
    }

    private static Material outlinedMaterial;

    /// <summary>濃い茶の縁取り付きの文字マテリアル（ダメージ数字・浮き文字。全部で 1 枚を共有）。</summary>
    public static Material OutlinedMaterial(TMP_FontAsset font)
    {
        if (font == null) return null;
        if (outlinedMaterial != null) return outlinedMaterial;
        outlinedMaterial = new Material(font.material) { name = "UIMotionOutlined", hideFlags = HideFlags.DontSave };
        outlinedMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
        outlinedMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.28f);
        outlinedMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(0x3a, 0x24, 0x14, 0xff));
        outlinedMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0.15f);
        return outlinedMaterial;
    }

    // =====================================================
    // 色（proposals.html と村の部品の配色）
    // =====================================================

    public static readonly Color Wood = new Color32(0x5a, 0x34, 0x20, 0xff);
    public static readonly Color Wood2 = new Color32(0x7b, 0x4a, 0x2c, 0xff);
    public static readonly Color Gold = new Color32(0xe9, 0xb9, 0x49, 0xff);
    public static readonly Color Gold2 = new Color32(0xff, 0xd9, 0x70, 0xff);
    public static readonly Color Cream = new Color32(0xff, 0xf3, 0xd6, 0xff);
    public static readonly Color Ink = new Color32(0x3a, 0x24, 0x14, 0xff);
    public static readonly Color Red = new Color32(0xd9, 0x57, 0x3b, 0xff);
    public static readonly Color Green = new Color32(0x7c, 0xd3, 0x6a, 0xff);

    // =====================================================
    // 絵（既存スプライト、無ければコードで描く）
    // =====================================================

    public static Sprite CoinSprite => Or(UIMotionAssets.Instance.coin, ref coinFallback, () => MakeCircle(64, Gold, Gold2, 0.08f));
    public static Sprite SparkleSprite => Or(UIMotionAssets.Instance.sparkle, ref sparkleFallback, () => MakeStar(128, 4, 0.18f, Gold2, Cream));
    public static Sprite GlowSprite => Or(UIMotionAssets.Instance.glow, ref glowFallback, () => SoftDotSprite);
    public static Sprite RaysSprite => Or(UIMotionAssets.Instance.rays, ref raysFallback, () => SoftDotSprite);
    public static Sprite GoldPlateSprite => Or(UIMotionAssets.Instance.goldPlate, ref plateFallback, () => RoundSprite);
    public static Sprite RoundSprite => Or(UIMotionAssets.Instance.roundWhite, ref roundFallback, () => MakeRoundRect(64, 16, 0, Color.white, 20));

    private static Sprite coinFallback, sparkleFallback, glowFallback, raysFallback, plateFallback, roundFallback;
    private static Sprite white, softDot, seal, wood, triangle, frame, softBar, star5;

    /// <summary>真っ白の四角（フラッシュ・紙吹雪・下線）。</summary>
    public static Sprite WhiteSprite => white != null ? white : white = MakeSolid();
    /// <summary>ふちがぼやけた丸（ほこり・光）。</summary>
    public static Sprite SoftDotSprite => softDot != null ? softDot : softDot = MakeSoftDot(64);
    /// <summary>金の紋章（#5 ハンコ・#6 幕の中央）。</summary>
    public static Sprite SealSprite => seal != null ? seal : seal = MakeSeal(256);
    /// <summary>木の板を縦に並べた幕（#6）。</summary>
    public static Sprite WoodCurtainSprite => wood != null ? wood : wood = MakeWoodPlanks(256, 512);
    /// <summary>上向きの三角（#12 矢印。下向きは 180 度回す）。</summary>
    public static Sprite TriangleSprite => triangle != null ? triangle : triangle = MakeTriangle(64);
    /// <summary>角丸の枠線だけ（9 スライス。#13 置ける枠・#15 コメント枠）。</summary>
    public static Sprite FrameSprite => frame != null ? frame : frame = MakeRoundRect(64, 16, 5, Color.white, 20);
    /// <summary>横に両端が透ける帯（#4・#8 の光の筋）。</summary>
    public static Sprite SoftBarSprite => softBar != null ? softBar : softBar = MakeSoftBar(64, 16);
    /// <summary>五芒星（#5 の星）。</summary>
    public static Sprite Star5Sprite => star5 != null ? star5 : star5 = MakeStar(128, 5, 0.45f, Gold2, Cream);

    private static Sprite Or(Sprite preferred, ref Sprite fallback, System.Func<Sprite> make)
    {
        if (preferred != null) return preferred;
        if (fallback == null) fallback = make();
        return fallback;
    }

    private static Texture2D NewTex(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };
        return tex;
    }

    private static Sprite ToSprite(Texture2D tex, Vector4 border = default)
    {
        tex.Apply(false, false);
        var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        s.hideFlags = HideFlags.DontSave;
        return s;
    }

    private static Sprite MakeSolid()
    {
        var tex = NewTex(4, 4);
        var px = new Color32[16];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px);
        return ToSprite(tex);
    }

    private static Sprite MakeSoftDot(int size)
    {
        var tex = NewTex(size, size);
        var px = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = Mathf.Clamp01(1f - d);
                px[y * size + x] = new Color(1f, 1f, 1f, a * a);
            }
        tex.SetPixels(px);
        return ToSprite(tex);
    }

    private static Sprite MakeCircle(int size, Color edge, Color center, float edgeWidth)
    {
        var tex = NewTex(size, size);
        var px = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = Mathf.Clamp01((1f - d) * r);
                var c = d > 1f - edgeWidth ? edge : Color.Lerp(center, edge, d);
                c.a = a;
                px[y * size + x] = c;
            }
        tex.SetPixels(px);
        return ToSprite(tex);
    }

    /// <summary>星（points 本の角。inner は内側の半径の比）。</summary>
    private static Sprite MakeStar(int size, int points, float inner, Color edge, Color center)
    {
        var tex = NewTex(size, size);
        var px = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f - r, y + 0.5f - r);
                float d = p.magnitude / (r * 0.96f);
                float ang = Mathf.Atan2(p.y, p.x) + Mathf.PI * 0.5f;
                float seg = Mathf.PI * 2f / points;
                float f = Mathf.Abs(Mathf.Repeat(ang, seg) / seg - 0.5f) * 2f; // 0 = 角の先, 1 = 谷
                float limit = Mathf.Lerp(1f, inner, f);
                float a = Mathf.Clamp01((limit - d) * r * 0.5f);
                var c = Color.Lerp(center, edge, Mathf.Clamp01(d / limit));
                c.a = a;
                px[y * size + x] = c;
            }
        tex.SetPixels(px);
        return ToSprite(tex);
    }

    /// <summary>金の紋章: 濃い金の縁・金の円盤・細い内輪・中央にクリーム色の星。</summary>
    private static Sprite MakeSeal(int size)
    {
        var tex = NewTex(size, size);
        var px = new Color[size * size];
        float r = size * 0.5f;
        Color rim = new Color32(0x8a, 0x55, 0x14, 0xff);
        Color starEdge = new Color32(0xb8, 0x76, 0x1c, 0xff);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f - r, y + 0.5f - r);
                float d = p.magnitude / r;
                float alpha = Mathf.Clamp01((0.98f - d) * r);
                Color c;
                if (d > 0.86f) c = rim;
                else if (d > 0.80f) c = Gold2;
                else if (d > 0.74f) c = Color.Lerp(Gold, rim, 0.25f);
                else if (d > 0.70f) c = Cream;
                else
                {
                    // 円盤（上が明るい）＋ 中央の五芒星
                    c = Color.Lerp(Gold2, Gold, Mathf.Clamp01(0.5f - p.y / (r * 1.4f)));
                    float ang = Mathf.Atan2(p.y, p.x) - Mathf.PI * 0.5f;
                    float seg = Mathf.PI * 2f / 5f;
                    float f = Mathf.Abs(Mathf.Repeat(ang, seg) / seg - 0.5f) * 2f;
                    float limit = Mathf.Lerp(0.52f, 0.22f, f);
                    if (d < limit) c = d > limit - 0.05f ? starEdge : Cream;
                }
                c.a = alpha;
                px[y * size + x] = c;
            }
        tex.SetPixels(px);
        return ToSprite(tex);
    }

    /// <summary>縦板の幕: 板ごとに少し色を変え、木目の筋と板の継ぎ目を描く。</summary>
    private static Sprite MakeWoodPlanks(int w, int h)
    {
        var tex = NewTex(w, h);
        var px = new Color[w * h];
        const int planks = 3;
        Color[] tones = { Wood, Wood2, Color.Lerp(Wood, Wood2, 0.5f) };
        Color gap = new Color32(0x2a, 0x16, 0x0c, 0xff);
        int plankW = w / planks;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int k = Mathf.Min(planks - 1, x / plankW);
                int lx = x - k * plankW;
                Color c;
                if (lx < 3) c = gap;
                else if (lx < 6) c = Color.Lerp(gap, tones[k], 0.5f);
                else
                {
                    float grain = Mathf.Sin((lx * 0.35f + Mathf.Sin(y * 0.02f + k * 1.7f) * 4f + k * 13f)) * 0.5f + 0.5f;
                    float knot = Mathf.Sin(y * 0.011f + k) * 0.5f + 0.5f;
                    c = Color.Lerp(tones[k], tones[k] * 0.82f, grain * 0.35f + knot * 0.1f);
                }
                c.a = 1f;
                px[y * w + x] = c;
            }
        tex.SetPixels(px);
        return ToSprite(tex);
    }

    private static Sprite MakeTriangle(int size)
    {
        var tex = NewTex(size, size);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fy = (y + 0.5f) / size;          // 0 = 下, 1 = 上
                float half = (1f - fy) * 0.5f;         // 上ほど細い
                float dx = Mathf.Abs((x + 0.5f) / size - 0.5f);
                float a = Mathf.Clamp01((half - dx) * size * 0.8f) * Mathf.Clamp01(fy * size * 0.8f) * Mathf.Clamp01((0.98f - fy) * size);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        return ToSprite(tex);
    }

    /// <summary>角丸の四角（line=0 なら塗りつぶし、>0 なら枠線だけ）。9 スライスの境界は border。</summary>
    private static Sprite MakeRoundRect(int size, int radius, int line, Color color, int border)
    {
        var tex = NewTex(size, size);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float outer = Mathf.Clamp01(radius - d + 0.5f);
                float a = outer;
                if (line > 0) a *= Mathf.Clamp01(d - (radius - line) + 0.5f);
                var c = color;
                c.a = a;
                px[y * size + x] = c;
            }
        tex.SetPixels(px);
        return ToSprite(tex, new Vector4(border, border, border, border));
    }

    private static Sprite MakeSoftBar(int w, int h)
    {
        var tex = NewTex(w, h);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float t = Mathf.Sin((x + 0.5f) / w * Mathf.PI);
                px[y * w + x] = new Color(1f, 1f, 1f, t * t);
            }
        tex.SetPixels(px);
        return ToSprite(tex);
    }
}

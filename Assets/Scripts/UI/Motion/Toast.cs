using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>#9 トーストの色合い。</summary>
public enum ToastKind
{
    /// <summary>お知らせ（金）。</summary>
    Info = 0,
    /// <summary>良い知らせ（緑）。実績・解放など。</summary>
    Good = 1,
    /// <summary>注意（赤）。在庫切れなど。</summary>
    Warn = 2,
}

/// <summary>
/// #9 トーストの置き場所。既定（<see cref="Default"/>）は画面上部の中央に積み上げ。
/// <see cref="Band"/> は「指定した帯（スクリーン座標の矩形）の中央に 1 行・幅は帯に合わせる・同時 1 件で置き換え」。
/// 帯は出すたびに測り直す（解像度・Game ビューの大きさが変わっても合う）。帯が測れない（null）ときは既定の置き場所で出す。
/// </summary>
public sealed class ToastPlacement
{
    /// <summary>画面上部の中央・上から降りて積み上がる（実績・鍛冶屋・品出し画面など）。</summary>
    public static readonly ToastPlacement Default = new(null);

    /// <summary>帯（スクリーン座標・左下原点の矩形）を返す関数。null なら既定。</summary>
    internal readonly System.Func<Rect?> ScreenBand;

    private ToastPlacement(System.Func<Rect?> screenBand) => ScreenBand = screenBand;

    /// <summary>帯の置き場所。screenBand は出すたびに呼ばれ、帯のスクリーン座標の矩形（測れなければ null）を返す。</summary>
    public static ToastPlacement Band(System.Func<Rect?> screenBand) => new(screenBand);
}

/// <summary>
/// #9 上から降りてくるお知らせ。画面上部の中央に、上から弾んで降りる（0.3 秒）→ 残り時間の線が縮む（既定 2.5 秒）→ 上へ消える（0.25 秒）。
/// 続けて出すと新しいものが一番上に来て、古いものは下へずれる（最大 4 枚。超えたら一番古いものを消す）。
/// DontDestroyOnLoad の演出用レイヤーにコード生成するので、どの画面からでも 1 行で出せる。unscaled（一時停止中も消える）。
/// 入力は受けない。省略モードでは何も出さない。
/// 置き場所は <see cref="SetPlacement"/> で切り替える（配信画面は映像の下・売り場の棚の上の帯に 1 行で、新しいもので置き換え）。
/// </summary>
public static class Toast
{
    private const int MaxVisible = 4;
    private const float Width = 780f;
    private const float Height = 92f;
    private const float Gap = 12f;
    private const float TopMargin = 36f;

    // 帯の置き場所（演出用レイヤーの単位。基準 1920x1080）
    /// <summary>帯に置くときの板の高さの下限（これより狭い帯でも文字が読める高さは保つ）。</summary>
    public const float BandMinHeight = 40f;
    private const float BandMaxHeight = 64f;
    /// <summary>帯の中の文字の最小の大きさ（読める下限）。</summary>
    private const float BandMinFont = 20f;
    private const float BandMinWidth = 360f;

    private sealed class Entry
    {
        public RectTransform Rect;
        public CanvasGroup Group;
        public bool Leaving;
        /// <summary>帯に置いたもの（積み上げの並べ直しに入れない）。</summary>
        public bool InBand;
    }

    private static readonly List<Entry> Entries = new();
    private static ToastPlacement placement = ToastPlacement.Default;

    /// <summary>今の置き場所。</summary>
    public static ToastPlacement Placement => placement;

    /// <summary>
    /// 置き場所を切り替える（null は既定）。出ているトーストは置き場所が混ざらないよう演出なしで消す。
    /// 画面を出るときは必ず <see cref="ToastPlacement.Default"/> に戻す。
    /// </summary>
    public static void SetPlacement(ToastPlacement next)
    {
        next ??= ToastPlacement.Default;
        if (ReferenceEquals(next, placement)) return;
        ClearAll();
        placement = next;
    }

    /// <summary>スクリーン座標の高さが、演出用レイヤーの単位で帯の下限（<see cref="BandMinHeight"/>）以上あるか。</summary>
    public static bool FitsBand(float screenHeight)
    {
        var layer = UIMotionFx.Layer(UIMotionFx.ToastOrder);
        float h = (UIMotionFx.ScreenToLayer(layer, new Vector2(0f, screenHeight)) - UIMotionFx.ScreenToLayer(layer, Vector2.zero)).y;
        return h >= BandMinHeight;
    }

    /// <summary>お知らせを出す。icon は左に出す小さな絵（null なら出さない）。</summary>
    public static void Show(string text, Sprite icon = null, ToastKind kind = ToastKind.Info, float seconds = 2.5f)
    {
        if (UIMotion.IsSkip || string.IsNullOrEmpty(text)) return;
        Entries.RemoveAll(e => e.Rect == null);

        var layer = UIMotionFx.Layer(UIMotionFx.ToastOrder);
        Rect? band = placement.ScreenBand?.Invoke();
        if (band.HasValue && band.Value.width > 0f)
        {
            ShowInBand(layer, band.Value, text, icon, kind, seconds);
            return;
        }

        var entry = Build(layer, text, icon, kind, Width, Height);
        Entries.Insert(0, entry);

        // 多すぎたら古いものから下げる
        int alive = 0;
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Leaving) continue;
            if (++alive > MaxVisible) Leave(Entries[i]);
        }
        Relayout(entry);

        // 降りてくる
        var rt = entry.Rect;
        float y = SlotY(0);
        rt.anchoredPosition = new Vector2(0f, y + Height + TopMargin + 40f);
        // 位置の Tween は単独で（後から来たトーストが並べ直すときに Kill(rt) で止められるように）
        UIMotion.Run(rt.DOAnchorPosY(y, 0.3f).SetEase(Ease.OutBack, 1.6f).SetTarget(rt), rt.gameObject, unscaled: true);
        var seq = DOTween.Sequence();
        var timer = (RectTransform)rt.Find("Timer/Fill");
        if (timer != null) seq.Insert(0.3f, timer.DOScaleX(0f, Mathf.Max(0.5f, seconds)).SetEase(Ease.Linear));
        seq.InsertCallback(0.3f + Mathf.Max(0.5f, seconds), () => Leave(entry));
        UIMotion.Run(seq, rt.gameObject, unscaled: true);
    }

    /// <summary>出ているトーストを全部消す（演出なし）。</summary>
    public static void ClearAll()
    {
        foreach (var e in Entries)
            if (e.Rect != null) UIMotion.DestroySafe(e.Rect.gameObject);
        Entries.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Entries.Clear();
        placement = ToastPlacement.Default;
    }

    /// <summary>
    /// 帯の中央に 1 行で出す。出ているものは新しいものに置き換える（その場で薄れて消える）。
    /// 板の高さは帯に合わせて縮める（下限 <see cref="BandMinHeight"/>。帯がそれより狭いときは帯の中央から上下にはみ出す）。
    /// </summary>
    private static void ShowInBand(RectTransform layer, Rect screenBand, string text, Sprite icon, ToastKind kind, float seconds)
    {
        // 置き換え: 出ているものは全部下げる（帯には 1 件だけ）
        for (int i = 0; i < Entries.Count; i++)
            if (!Entries[i].Leaving) Leave(Entries[i]);

        // スクリーン座標 → 演出用レイヤーの座標（UIMotionFx.LayerPos と同じ経路）
        Vector2 min = UIMotionFx.ScreenToLayer(layer, screenBand.min);
        Vector2 max = UIMotionFx.ScreenToLayer(layer, screenBand.max);
        float bandW = Mathf.Abs(max.x - min.x);
        float bandH = Mathf.Abs(max.y - min.y);
        float h = Mathf.Clamp(bandH - 4f, BandMinHeight, BandMaxHeight);
        float w = Mathf.Max(BandMinWidth, bandW - 16f);

        var entry = Build(layer, text, icon, kind, w, h);
        entry.InBand = true;
        Entries.Insert(0, entry);

        var rt = entry.Rect;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); // レイヤーの中央基準（ScreenToLayer の座標）
        rt.anchoredPosition = (min + max) * 0.5f;
        rt.localScale = new Vector3(1f, 0.6f, 1f);
        entry.Group.alpha = 0f;

        // 帯の中で開く（上下に動かすと映像・棚にかかるので、その場で縦に開いて現れる）
        var seq = DOTween.Sequence();
        seq.Insert(0f, rt.DOScaleY(1f, 0.22f).SetEase(Ease.OutBack, 1.4f));
        seq.Insert(0f, entry.Group.DOFade(1f, 0.15f));
        var timer = (RectTransform)rt.Find("Timer/Fill");
        if (timer != null) seq.Insert(0.22f, timer.DOScaleX(0f, Mathf.Max(0.5f, seconds)).SetEase(Ease.Linear));
        seq.InsertCallback(0.22f + Mathf.Max(0.5f, seconds), () => Leave(entry));
        seq.SetTarget(rt);
        UIMotion.Run(seq, rt.gameObject, unscaled: true);
    }

    private static float SlotY(int index) => -TopMargin - Height * 0.5f - index * (Height + Gap);

    /// <summary>残っているものを上から詰めて並べ直す（新しく来たもの以外を動かす）。帯のものは並べ直さない。</summary>
    private static void Relayout(Entry exclude)
    {
        int slot = 0;
        foreach (var e in Entries)
        {
            if (e.Leaving || e.Rect == null || e.InBand) continue;
            if (e != exclude)
            {
                DOTween.Kill(e.Rect, false);
                UIMotion.Run(e.Rect.DOAnchorPosY(SlotY(slot), 0.22f).SetEase(Ease.OutBack, 1.2f).SetTarget(e.Rect), e.Rect.gameObject, unscaled: true);
            }
            slot++;
        }
    }

    private static void Leave(Entry entry)
    {
        if (entry.Leaving || entry.Rect == null) return;
        entry.Leaving = true;
        var rt = entry.Rect;
        DOTween.Kill(rt, false);
        var seq = DOTween.Sequence();
        if (entry.InBand)
        {
            // 帯: その場で縦に閉じて薄れる（置き換えのときに次のものと重なる時間を短く）
            seq.Insert(0f, rt.DOScaleY(0.6f, 0.16f).SetEase(Ease.InQuad));
            seq.Insert(0f, entry.Group.DOFade(0f, 0.16f));
            seq.OnComplete(() =>
            {
                Entries.Remove(entry);
                UIMotion.DestroySafe(rt.gameObject);
            });
            UIMotion.Run(seq, rt.gameObject, unscaled: true);
            return;
        }
        seq.Insert(0f, rt.DOAnchorPosY(rt.anchoredPosition.y + 70f, 0.25f).SetEase(Ease.InBack));
        seq.Insert(0f, entry.Group.DOFade(0f, 0.25f));
        seq.OnComplete(() =>
        {
            Entries.Remove(entry);
            UIMotion.DestroySafe(rt.gameObject);
        });
        UIMotion.Run(seq, rt.gameObject, unscaled: true);
        Relayout(null);
    }

    /// <summary>
    /// 板を作る。width/height が既定（780x92）なら今までと同じ見た目。
    /// 帯に置く低い板は、高さの比で縁・アイコン・文字を縮めて 1 行にする（文字は読める下限まで縮め、それでも入らなければ末尾を「…」）。
    /// </summary>
    private static Entry Build(RectTransform layer, string text, Sprite icon, ToastKind kind, float width, float height)
    {
        bool compact = height < Height - 0.5f;
        Color accent = kind switch
        {
            ToastKind.Good => UIMotionFx.Green,
            ToastKind.Warn => UIMotionFx.Red,
            _ => UIMotionFx.Gold2,
        };

        var go = new GameObject("MotionToast", typeof(RectTransform));
        go.layer = layer.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(layer, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(width, height);
        var group = go.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        // 影 → 金の板 → アイコン → 文字 → 残り時間の線
        var shadow = UIMotionFx.NewImage("Shadow", rt, UIMotionFx.RoundSprite, new Color(0f, 0f, 0f, 0.35f), new Vector2(width, height));
        shadow.pixelsPerUnitMultiplier = compact ? 1.5f * Height / height : 1.5f;
        shadow.rectTransform.anchoredPosition = compact ? new Vector2(2f, -3f) : new Vector2(4f, -6f);
        var plate = UIMotionFx.NewImage("Plate", rt, UIMotionFx.GoldPlateSprite, Color.white, new Vector2(width, height));
        // 低い板は 9 スライスの縁も細くしないと、縁が上下でつぶれる
        if (plate.type == Image.Type.Sliced) plate.pixelsPerUnitMultiplier = compact ? 1.4f * Height / height : 1.4f;

        float textLeft = compact ? 18f : 34f;
        if (icon != null)
        {
            float frameSize = compact ? height - 8f : 64f;
            var frame = UIMotionFx.NewImage("IconFrame", rt, UIMotionFx.RoundSprite, new Color(accent.r, accent.g, accent.b, 0.9f), new Vector2(frameSize, frameSize));
            frame.pixelsPerUnitMultiplier = 2f * 64f / frameSize;
            frame.rectTransform.anchoredPosition = new Vector2(-width * 0.5f + (compact ? 12f + frameSize * 0.5f : 52f), 0f);
            float iconSize = frameSize * 52f / 64f;
            var img = UIMotionFx.NewImage("Icon", frame.rectTransform, icon, Color.white, new Vector2(iconSize, iconSize));
            img.preserveAspect = true;
            textLeft = compact ? 12f + frameSize + 10f : 96f;
        }

        float rightPad = compact ? 18f : 30f;
        var label = UIMotionFx.NewText("Text", rt, text, 32f, UIMotionFx.Cream, new Vector2(width - textLeft - rightPad, height - (compact ? 6f : 16f)));
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableAutoSizing = true;
        if (compact)
        {
            // 帯は 1 行。入らなければ読める下限まで縮め、それでも入らなければ末尾を「…」にする
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.fontSizeMin = BandMinFont;
            label.fontSizeMax = Mathf.Clamp(height * 0.6f, BandMinFont, 32f);
            label.fontSize = label.fontSizeMax;
        }
        else
        {
            label.textWrappingMode = TextWrappingModes.Normal;
            label.fontSizeMin = 24f;
            label.fontSizeMax = 32f;
        }
        label.fontSharedMaterial = UIMotionFx.OutlinedMaterial(label.font);
        label.rectTransform.anchoredPosition = new Vector2((textLeft - rightPad) * 0.5f, compact ? 1f : 2f);

        float timerW = width - (compact ? 30f : 60f);
        float timerH = compact ? 3f : 5f;
        var timerBg = UIMotionFx.NewImage("Timer", rt, UIMotionFx.WhiteSprite, new Color(0f, 0f, 0f, 0.3f), new Vector2(timerW, timerH));
        timerBg.rectTransform.anchoredPosition = new Vector2(0f, -height * 0.5f + (compact ? 5f : 12f));
        var fill = UIMotionFx.NewImage("Fill", timerBg.rectTransform, UIMotionFx.WhiteSprite, accent, new Vector2(timerW, timerH));
        fill.rectTransform.pivot = new Vector2(0f, 0.5f);
        fill.rectTransform.anchoredPosition = new Vector2(-timerW * 0.5f, 0f);

        return new Entry { Rect = rt, Group = group };
    }
}

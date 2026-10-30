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
/// #9 上から降りてくるお知らせ。画面上部の中央に、上から弾んで降りる（0.3 秒）→ 残り時間の線が縮む（既定 2.5 秒）→ 上へ消える（0.25 秒）。
/// 続けて出すと新しいものが一番上に来て、古いものは下へずれる（最大 4 枚。超えたら一番古いものを消す）。
/// DontDestroyOnLoad の演出用レイヤーにコード生成するので、どの画面からでも 1 行で出せる。unscaled（一時停止中も消える）。
/// 入力は受けない。省略モードでは何も出さない。
/// </summary>
public static class Toast
{
    private const int MaxVisible = 4;
    private const float Width = 780f;
    private const float Height = 92f;
    private const float Gap = 12f;
    private const float TopMargin = 36f;

    private sealed class Entry
    {
        public RectTransform Rect;
        public CanvasGroup Group;
        public bool Leaving;
    }

    private static readonly List<Entry> Entries = new();

    /// <summary>お知らせを出す。icon は左に出す小さな絵（null なら出さない）。</summary>
    public static void Show(string text, Sprite icon = null, ToastKind kind = ToastKind.Info, float seconds = 2.5f)
    {
        if (UIMotion.IsSkip || string.IsNullOrEmpty(text)) return;
        Entries.RemoveAll(e => e.Rect == null);

        var layer = UIMotionFx.Layer(UIMotionFx.ToastOrder);
        var entry = Build(layer, text, icon, kind);
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
    private static void ResetStatics() => Entries.Clear();

    private static float SlotY(int index) => -TopMargin - Height * 0.5f - index * (Height + Gap);

    /// <summary>残っているものを上から詰めて並べ直す（新しく来たもの以外を動かす）。</summary>
    private static void Relayout(Entry exclude)
    {
        int slot = 0;
        foreach (var e in Entries)
        {
            if (e.Leaving || e.Rect == null) continue;
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

    private static Entry Build(RectTransform layer, string text, Sprite icon, ToastKind kind)
    {
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
        rt.sizeDelta = new Vector2(Width, Height);
        var group = go.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        // 影 → 金の板 → アイコン → 文字 → 残り時間の線
        var shadow = UIMotionFx.NewImage("Shadow", rt, UIMotionFx.RoundSprite, new Color(0f, 0f, 0f, 0.35f), new Vector2(Width, Height));
        shadow.pixelsPerUnitMultiplier = 1.5f;
        shadow.rectTransform.anchoredPosition = new Vector2(4f, -6f);
        var plate = UIMotionFx.NewImage("Plate", rt, UIMotionFx.GoldPlateSprite, Color.white, new Vector2(Width, Height));
        if (plate.type == Image.Type.Sliced) plate.pixelsPerUnitMultiplier = 1.4f;

        float textLeft = 34f;
        if (icon != null)
        {
            var frame = UIMotionFx.NewImage("IconFrame", rt, UIMotionFx.RoundSprite, new Color(accent.r, accent.g, accent.b, 0.9f), new Vector2(64f, 64f));
            frame.pixelsPerUnitMultiplier = 2f;
            frame.rectTransform.anchoredPosition = new Vector2(-Width * 0.5f + 52f, 0f);
            var img = UIMotionFx.NewImage("Icon", frame.rectTransform, icon, Color.white, new Vector2(52f, 52f));
            img.preserveAspect = true;
            textLeft = 96f;
        }

        var label = UIMotionFx.NewText("Text", rt, text, 32f, UIMotionFx.Cream, new Vector2(Width - textLeft - 30f, Height - 16f));
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.enableAutoSizing = true;
        label.fontSizeMin = 24f;
        label.fontSizeMax = 32f;
        label.fontSharedMaterial = UIMotionFx.OutlinedMaterial(label.font);
        label.rectTransform.anchoredPosition = new Vector2((textLeft - 30f) * 0.5f, 2f);

        var timerBg = UIMotionFx.NewImage("Timer", rt, UIMotionFx.WhiteSprite, new Color(0f, 0f, 0f, 0.3f), new Vector2(Width - 60f, 5f));
        timerBg.rectTransform.anchoredPosition = new Vector2(0f, -Height * 0.5f + 12f);
        var fill = UIMotionFx.NewImage("Fill", timerBg.rectTransform, UIMotionFx.WhiteSprite, accent, new Vector2(Width - 60f, 5f));
        fill.rectTransform.pivot = new Vector2(0f, 0.5f);
        fill.rectTransform.anchoredPosition = new Vector2(-(Width - 60f) * 0.5f, 0f);

        return new Entry { Rect = rt, Group = group };
    }
}

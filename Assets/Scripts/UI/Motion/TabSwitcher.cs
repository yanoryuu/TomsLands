using DG.Tweening;
using UnityEngine;

/// <summary>
/// #7 タブ切替。
/// ・<see cref="MoveUnderline"/>: 下線が選んだタブへ少し行き過ぎて戻る（0.2 秒）
/// ・<see cref="SlideContent"/>: 中身が横に滑って入る（0.25 秒。右のタブへ移ったら右から、左なら左から）
/// ・下線が無い画面は <see cref="CreateUnderline"/> で金の下線を足せる（レイアウトには入らない）
/// 省略モードでは即座に最終位置。
/// </summary>
public static class TabSwitcher
{
    /// <summary>金の下線を tab の親の中に作る（最初は tab の下に置く）。</summary>
    public static RectTransform CreateUnderline(RectTransform tab, float height = 6f, float widthRatio = 0.8f)
    {
        if (tab == null || tab.parent == null) return null;
        var img = UIMotionFx.NewImage("TabUnderline", tab.parent, UIMotionFx.RoundSprite, UIMotionFx.Gold2, new Vector2(tab.rect.width * widthRatio, height));
        img.type = UnityEngine.UI.Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 6f;
        UIMotionFx.IgnoreLayout(img.gameObject);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = tab.anchorMin == tab.anchorMax ? tab.anchorMin : new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 1f);
        MoveUnderline(rt, tab, widthRatio, instant: true);
        return rt;
    }

    /// <summary>下線を tab の下へ動かす（OutBack で少し行き過ぎて戻る）。下線と tab は親が違ってもよい。</summary>
    public static void MoveUnderline(RectTransform underline, RectTransform tab, float widthRatio = 0.8f, bool instant = false, float duration = 0.2f)
    {
        if (underline == null || tab == null) return;
        var parent = underline.parent as RectTransform;
        if (parent == null) return;
        // tab の下辺の中央 → 下線の親の座標 → anchoredPosition
        Vector3 world = tab.TransformPoint(new Vector3(tab.rect.center.x, tab.rect.yMin, 0f));
        Vector2 local = parent.InverseTransformPoint(world);
        Vector2 anchorRef = Vector2.Scale(parent.rect.size, underline.anchorMin - parent.pivot);
        Vector2 target = local - anchorRef;
        float width = tab.rect.width * widthRatio;

        DOTween.Kill(underline);
        if (instant || UIMotion.IsSkip)
        {
            underline.anchoredPosition = target;
            underline.sizeDelta = new Vector2(width, underline.sizeDelta.y);
            return;
        }
        var seq = DOTween.Sequence().SetTarget(underline);
        seq.Insert(0f, underline.DOAnchorPos(target, duration).SetEase(Ease.OutBack, 2.2f));
        seq.Insert(0f, underline.DOSizeDelta(new Vector2(width, underline.sizeDelta.y), duration).SetEase(Ease.OutQuad));
        UIMotion.Run(seq, underline.gameObject);
    }

    /// <summary>
    /// 中身を横から滑り込ませる。direction は +1（右のタブへ移った＝右から入る）/ -1（左から）。
    /// 中身の最終位置は呼んだときの位置（動いている途中なら元の位置）。alpha は CanvasGroup の alpha だけ使う。
    /// </summary>
    public static void SlideContent(RectTransform content, int direction, float distance = 60f, float duration = 0.25f)
    {
        if (content == null) return;
        // 前の滑り込みの途中なら止める（その OnKill がずらしを外す）。位置は覚えず、横のずらしだけを持つ
        // （覚えた座標へ戻すと、途中で閉じた・並びが変わった後に古い座標へ戻してしまう。UIMotion.Nudge 参照）
        DOTween.Kill(content.gameObject);
        var group = UIMotion.EnsureGroup(content.gameObject);
        var nudge = UIMotion.NudgeOf(content);
        nudge.Clear();
        if (UIMotion.IsSkip || !content.gameObject.activeInHierarchy)
        {
            group.alpha = 1f;
            return;
        }
        float x = Mathf.Sign(direction == 0 ? 1 : direction) * distance;
        nudge.Set(new Vector2(x, 0f));
        group.alpha = 0.3f;
        var seq = DOTween.Sequence().SetId(content.gameObject);
        seq.Insert(0f, DOTween.To(() => x, v => { x = v; nudge.Set(new Vector2(v, 0f)); }, 0f, duration).SetEase(Ease.OutCubic));
        seq.Insert(0f, group.DOFade(1f, duration * 0.7f));
        seq.OnKill(() =>
        {
            nudge.Clear();
            if (group != null) group.alpha = 1f;
        });
        UIMotion.Run(seq, content.gameObject);
    }
}

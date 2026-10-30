using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// #16 日付めくり。日数・ターン数の表示を、日めくりのように古い数字の紙が上へめくれて、下から新しい数字が出る形で替える（0.4 秒）。
/// 古い数字は一時的な複製（文字だけの TMP を子に作る）で描き、本体の文字は最初から新しい値にする（最終状態は即座に正しい）。
/// 省略モード・非表示中は文字を差し替えるだけ。
/// </summary>
public static class FlipCounter
{
    private const float Duration = 0.4f;

    /// <summary>text を newText に替える（同じ文字なら何もしない）。</summary>
    public static void Flip(TMP_Text text, string newText)
    {
        if (text == null) return;
        string oldText = text.text;
        if (oldText == newText) return;

        var ui = text as TextMeshProUGUI;
        var old = ui != null ? ui.rectTransform.Find("FlipPage") : null;
        if (old != null) UIMotion.DestroySafe(old.gameObject);
        DOTween.Kill(text.gameObject);
        text.text = newText;
        if (ui == null || UIMotion.IsSkip || !text.gameObject.activeInHierarchy) return;

        var rt = ui.rectTransform;
        float baseAlpha = ui.alpha;
        Vector3 baseScale = UIMotion.BaseScaleOf(rt);

        // 古い数字の紙（本体の子＝本体より手前に描かれる）
        var page = UIMotionFx.NewText("FlipPage", rt, oldText, ui.fontSize, ui.color, Vector2.zero);
        page.font = ui.font;
        page.fontSharedMaterial = ui.fontSharedMaterial;
        page.fontStyle = ui.fontStyle;
        page.alignment = ui.alignment;
        page.enableAutoSizing = ui.enableAutoSizing;
        page.fontSizeMin = ui.fontSizeMin;
        page.fontSizeMax = ui.fontSizeMax;
        page.characterSpacing = ui.characterSpacing;
        page.richText = ui.richText;
        page.textWrappingMode = ui.textWrappingMode;
        page.margin = ui.margin;
        var prt = page.rectTransform;
        UIMotionFx.Stretch(prt);
        // 上辺を軸にしてめくる
        prt.pivot = new Vector2(0.5f, 1f);
        prt.anchoredPosition = Vector2.zero;
        UIMotionFx.IgnoreLayout(page.gameObject);

        ui.alpha = 0f;
        var seq = DOTween.Sequence().SetId(text.gameObject);
        seq.Insert(0f, prt.DOLocalRotate(new Vector3(-90f, 0f, 0f), Duration * 0.55f).SetEase(Ease.InQuad));
        seq.Insert(0f, prt.DOAnchorPosY(14f, Duration * 0.55f).SetEase(Ease.InQuad));
        seq.Insert(Duration * 0.3f, page.DOFade(0f, Duration * 0.25f));
        // 新しい数字: めくれ始めたら出て、少し跳ねて落ち着く
        seq.InsertCallback(Duration * 0.25f, () =>
        {
            ui.alpha = baseAlpha;
            rt.localScale = new Vector3(baseScale.x, baseScale.y * 0.7f, baseScale.z);
        });
        seq.Insert(Duration * 0.25f, rt.DOScaleY(baseScale.y * 1.08f, Duration * 0.35f).SetEase(Ease.OutQuad));
        seq.Insert(Duration * 0.6f, rt.DOScaleY(baseScale.y, Duration * 0.4f).SetEase(Ease.OutBack));
        seq.OnKill(() =>
        {
            if (page != null) UIMotion.DestroySafe(page.gameObject);
            if (ui == null) return;
            ui.alpha = baseAlpha;
            rt.localScale = baseScale;
        });
        UIMotion.Run(seq, text.gameObject);
    }
}

using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>#15 強調のしかた。</summary>
public enum CommentHighlightStyle
{
    /// <summary>大きなコメント: 暗い木の板＋金の縁。</summary>
    Big = 0,
    /// <summary>スパチャ（投げ銭）など: 色付きの板＋明るい縁が脈打つ。</summary>
    SuperChat = 1,
}

/// <summary>
/// #15 配信コメントの強調。流れるコメントのうち、大きなコメント・スパチャなどだけを枠付きで膨らませて目立たせる。
/// それ以外のコメントは今まで通り流れる（何も付けない）。
/// NicoCommentLayerView のコメント（使い回しの 1 行）に対して、表示した直後に <see cref="Apply"/> を呼ぶ。
/// 枠はコメントの直前の兄弟として作り、毎フレーム位置と大きさを合わせる（コメントの文字より後ろに描くため）。
/// コメントが非表示になる（使い回しで Release される）と枠も自動で消え、次に別のコメントで使われても枠は出ない。
/// 省略モード: 膨らみ・脈打ちは省き、枠だけ出す。
/// </summary>
[DisallowMultipleComponent]
public sealed class CommentHighlight : MonoBehaviour
{
    private const float PadX = 18f;
    private const float PadY = 6f;

    private RectTransform comment;
    private RectTransform frame;
    private Image fill;
    private Image border;
    private bool applied;
    private Sequence pulse;

    /// <summary>comment（流れるコメントの RectTransform）を強調する。accent は板の色（省略時は style の既定色）。</summary>
    public static void Apply(RectTransform comment, CommentHighlightStyle style = CommentHighlightStyle.Big, Color? accent = null)
    {
        if (comment == null) return;
        if (!comment.TryGetComponent<CommentHighlight>(out var h)) h = comment.gameObject.AddComponent<CommentHighlight>();
        h.enabled = true;
        h.Show(style, accent);
    }

    /// <summary>強調を外す（枠を隠す）。</summary>
    public static void Clear(RectTransform comment)
    {
        if (comment != null && comment.TryGetComponent<CommentHighlight>(out var h)) h.Hide();
    }

    private void Show(CommentHighlightStyle style, Color? accent)
    {
        comment = (RectTransform)transform;
        EnsureFrame();
        applied = true;
        frame.gameObject.SetActive(true);
        frame.SetSiblingIndex(comment.GetSiblingIndex());

        bool super = style == CommentHighlightStyle.SuperChat;
        Color baseColor = accent ?? (super ? UIMotionFx.Gold : new Color(UIMotionFx.Wood.r, UIMotionFx.Wood.g, UIMotionFx.Wood.b, 0.82f));
        if (super) baseColor.a = 0.92f;
        fill.color = baseColor;
        border.color = super ? UIMotionFx.Cream : UIMotionFx.Gold2;
        SyncNow();

        DOTween.Kill(this);
        pulse = null;
        if (UIMotion.IsSkip)
        {
            comment.localScale = Vector3.one;
            SyncNow();
            return;
        }

        // 膨らんで出る（0.6 → 1.15 → 1）
        comment.localScale = Vector3.one * 0.6f;
        var pop = DOTween.Sequence().SetTarget(this);
        pop.Append(comment.DOScale(1.15f, 0.16f).SetEase(Ease.OutQuad));
        pop.Append(comment.DOScale(1f, 0.14f).SetEase(Ease.InOutSine));
        pop.OnKill(() => { if (comment != null) comment.localScale = Vector3.one; });
        UIMotion.Run(pop, gameObject);

        if (super)
        {
            var seq = DOTween.Sequence().SetTarget(this);
            seq.Append(border.DOFade(0.35f, 0.4f).SetEase(Ease.InOutSine));
            seq.Append(border.DOFade(1f, 0.4f).SetEase(Ease.InOutSine));
            seq.SetLoops(-1);
            pulse = UIMotion.RunLoop(seq, frame.gameObject);
        }
    }

    private void Hide()
    {
        applied = false;
        DOTween.Kill(this);
        pulse = null;
        if (frame != null) frame.gameObject.SetActive(false);
    }

    private void EnsureFrame()
    {
        if (frame != null) return;
        var parent = comment.parent;
        var go = new GameObject("CommentFrame", typeof(RectTransform));
        go.layer = gameObject.layer;
        frame = (RectTransform)go.transform;
        frame.SetParent(parent, false);
        UIMotionFx.IgnoreLayout(go);
        fill = UIMotionFx.NewImage("Fill", frame, UIMotionFx.RoundSprite, Color.white, Vector2.zero);
        fill.pixelsPerUnitMultiplier = 1.6f;
        UIMotionFx.Stretch(fill.rectTransform);
        border = UIMotionFx.NewImage("Border", frame, UIMotionFx.FrameSprite, Color.white, Vector2.zero);
        border.type = Image.Type.Sliced;
        border.pixelsPerUnitMultiplier = 1.4f;
        UIMotionFx.Stretch(border.rectTransform);
    }

    /// <summary>枠の位置・大きさ・拡縮をコメントに合わせる（流れているので毎フレーム）。</summary>
    internal void SyncNow()
    {
        if (!applied || frame == null || comment == null) return;
        frame.anchorMin = comment.anchorMin;
        frame.anchorMax = comment.anchorMax;
        frame.pivot = comment.pivot;
        frame.sizeDelta = comment.sizeDelta + new Vector2(PadX * 2f, PadY * 2f);
        // 余白の分だけ左（pivot に合わせて）へずらす
        Vector2 shift = new Vector2(PadX * 2f, PadY * 2f) * (comment.pivot - new Vector2(0.5f, 0.5f));
        frame.anchoredPosition = comment.anchoredPosition + shift;
        frame.localScale = comment.localScale;
        frame.localRotation = comment.localRotation;
    }

    private void LateUpdate() => SyncNow();

    private void OnDisable() => Hide();

    private void OnDestroy()
    {
        DOTween.Kill(this);
        if (frame != null) UIMotion.DestroySafe(frame.gameObject);
    }

    /// <summary>撮影ツール用: 全部の枠を合わせる（編集モードでは LateUpdate が呼ばれないため）。</summary>
    public static void SyncAll()
    {
        foreach (var h in FindObjectsByType<CommentHighlight>(FindObjectsSortMode.None)) h.SyncNow();
    }
}

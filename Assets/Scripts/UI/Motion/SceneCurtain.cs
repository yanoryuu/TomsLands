using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// #6 幕で場面切替。左右から木の幕が閉じ（0.35 秒、少し行き過ぎて戻る）、中央に金の紋章がはまる → 裏で読み込み → 幕が開く（0.35 秒）。
/// LoadingView（Resources/LoadingOverlay.prefab）の Canvas の中に実行時に作る（プレハブは変えない）。
/// 呼び出し側（SceneLoader）は変えず、LoadingView.FadeInAsync / FadeOutAsync がこれを使う。unscaled。
/// 省略モードでは動かさずに閉じた／開いた状態にする。
/// </summary>
public sealed class SceneCurtain
{
    private const float MoveDuration = 0.35f;

    /// <summary>false にすると LoadingView は従来の黒フェードに戻る。</summary>
    public static bool Enabled { get; set; } = true;

    private readonly RectTransform root;
    private readonly RectTransform left;
    private readonly RectTransform right;
    private readonly RectTransform seal;
    private float halfWidth;

    /// <summary>幕が閉じているか（閉じかけ・開きかけを含む）。</summary>
    public bool IsClosed { get; private set; }

    private SceneCurtain(RectTransform root, RectTransform left, RectTransform right, RectTransform seal)
    {
        this.root = root;
        this.left = left;
        this.right = right;
        this.seal = seal;
    }

    /// <summary>parent（Canvas の直下を想定）の siblingIndex の位置に幕を作る。最初は開いた状態（画面外）。</summary>
    public static SceneCurtain Create(RectTransform parent, int siblingIndex)
    {
        var go = new GameObject("SceneCurtain", typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var root = (RectTransform)go.transform;
        root.SetParent(parent, false);
        root.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, parent.childCount - 1));
        UIMotionFx.Stretch(root);

        var l = CreatePanel("Left", root, leftSide: true);
        var r = CreatePanel("Right", root, leftSide: false);

        var sealImg = UIMotionFx.NewImage("Seal", root, UIMotionFx.SealSprite, Color.white, new Vector2(230f, 230f));
        sealImg.rectTransform.localScale = Vector3.zero;

        var curtain = new SceneCurtain(root, l, r, sealImg.rectTransform);
        curtain.SetClosedImmediate(false);
        return curtain;
    }

    private static RectTransform CreatePanel(string name, RectTransform parent, bool leftSide)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        // 画面の半分（＋継ぎ目を隠す数 px）
        rt.anchorMin = new Vector2(leftSide ? 0f : 0.5f, 0f);
        rt.anchorMax = new Vector2(leftSide ? 0.5f : 1f, 1f);
        rt.offsetMin = new Vector2(leftSide ? 0f : -2f, 0f);
        rt.offsetMax = new Vector2(leftSide ? 2f : 0f, 0f);
        var img = go.AddComponent<Image>();
        img.sprite = UIMotionFx.WoodCurtainSprite;
        img.raycastTarget = false;

        // 内側のふち: 影と金の縁取り
        var shade = UIMotionFx.NewImage("Shade", rt, UIMotionFx.WhiteSprite, new Color(0f, 0f, 0f, 0.35f), Vector2.zero);
        var srt = shade.rectTransform;
        srt.anchorMin = new Vector2(leftSide ? 1f : 0f, 0f);
        srt.anchorMax = new Vector2(leftSide ? 1f : 0f, 1f);
        srt.pivot = new Vector2(leftSide ? 1f : 0f, 0.5f);
        srt.sizeDelta = new Vector2(26f, 0f);
        var trim = UIMotionFx.NewImage("GoldTrim", rt, UIMotionFx.WhiteSprite, UIMotionFx.Gold, Vector2.zero);
        var trt = trim.rectTransform;
        trt.anchorMin = srt.anchorMin;
        trt.anchorMax = srt.anchorMax;
        trt.pivot = srt.pivot;
        trt.sizeDelta = new Vector2(10f, 0f);
        // 上下の金の帯
        for (int i = 0; i < 2; i++)
        {
            var band = UIMotionFx.NewImage(i == 0 ? "BandTop" : "BandBottom", rt, UIMotionFx.WhiteSprite, new Color(UIMotionFx.Gold.r, UIMotionFx.Gold.g, UIMotionFx.Gold.b, 0.85f), Vector2.zero);
            var b = band.rectTransform;
            b.anchorMin = new Vector2(0f, i == 0 ? 1f : 0f);
            b.anchorMax = new Vector2(1f, i == 0 ? 1f : 0f);
            b.pivot = new Vector2(0.5f, i == 0 ? 1f : 0f);
            b.sizeDelta = new Vector2(0f, 8f);
            b.anchoredPosition = new Vector2(0f, i == 0 ? -34f : 34f);
        }
        return rt;
    }

    private void MeasureWidth()
    {
        halfWidth = Mathf.Max(100f, root.rect.width * 0.5f + 4f);
    }

    /// <summary>動かさずに閉じた（true）／開いた（false）状態にする。</summary>
    public void SetClosedImmediate(bool closed)
    {
        Kill();
        MeasureWidth();
        IsClosed = closed;
        root.gameObject.SetActive(closed);
        left.anchoredPosition = new Vector2(closed ? 0f : -halfWidth, 0f);
        right.anchoredPosition = new Vector2(closed ? 0f : halfWidth, 0f);
        seal.localScale = closed ? Vector3.one : Vector3.zero;
        seal.localRotation = Quaternion.identity;
    }

    /// <summary>幕を閉じて、中央に紋章をはめる。完了まで待つ。</summary>
    public UniTask CloseAsync(CancellationToken ct) => WaitAsync(BeginClose(), ct);

    /// <summary>紋章を引っ込めて幕を開く。完了まで待つ。</summary>
    public UniTask OpenAsync(CancellationToken ct) => WaitAsync(BeginOpen(), ct);

    /// <summary>閉じ始める（Tween を返す。撮影ツールは待たずにこちらを使う）。</summary>
    public Tween BeginClose()
    {
        Kill();
        MeasureWidth();
        IsClosed = true;
        root.gameObject.SetActive(true);
        left.anchoredPosition = new Vector2(-halfWidth, 0f);
        right.anchoredPosition = new Vector2(halfWidth, 0f);
        seal.localScale = Vector3.zero;
        seal.localRotation = Quaternion.Euler(0f, 0f, -120f);

        float bounce = halfWidth * 0.04f; // 合わさった反動で少し戻る
        var seq = DOTween.Sequence().SetId(this);
        seq.Insert(0f, left.DOAnchorPosX(0f, MoveDuration * 0.8f).SetEase(Ease.InQuad));
        seq.Insert(0f, right.DOAnchorPosX(0f, MoveDuration * 0.8f).SetEase(Ease.InQuad));
        seq.Insert(MoveDuration * 0.8f, left.DOAnchorPosX(-bounce, MoveDuration * 0.1f).SetEase(Ease.OutQuad));
        seq.Insert(MoveDuration * 0.8f, right.DOAnchorPosX(bounce, MoveDuration * 0.1f).SetEase(Ease.OutQuad));
        seq.Insert(MoveDuration * 0.9f, left.DOAnchorPosX(0f, MoveDuration * 0.1f).SetEase(Ease.InQuad));
        seq.Insert(MoveDuration * 0.9f, right.DOAnchorPosX(0f, MoveDuration * 0.1f).SetEase(Ease.InQuad));
        seq.Insert(MoveDuration * 0.8f, seal.DOScale(1.15f, 0.16f).SetEase(Ease.OutQuad));
        seq.Insert(MoveDuration * 0.8f, seal.DOLocalRotate(Vector3.zero, 0.16f).SetEase(Ease.OutQuad));
        seq.Insert(MoveDuration * 0.8f + 0.16f, seal.DOScale(1f, 0.08f));
        return UIMotion.Run(seq, root.gameObject, unscaled: true);
    }

    /// <summary>開き始める（Tween を返す）。</summary>
    public Tween BeginOpen()
    {
        Kill();
        MeasureWidth();
        root.gameObject.SetActive(true);
        var seq = DOTween.Sequence().SetId(this);
        seq.Insert(0f, seal.DOScale(0f, 0.14f).SetEase(Ease.InBack));
        seq.Insert(0.08f, left.DOAnchorPosX(-halfWidth, MoveDuration).SetEase(Ease.InOutCubic));
        seq.Insert(0.08f, right.DOAnchorPosX(halfWidth, MoveDuration).SetEase(Ease.InOutCubic));
        seq.OnComplete(() =>
        {
            IsClosed = false;
            if (root != null) root.gameObject.SetActive(false);
        });
        var tween = UIMotion.Run(seq, root.gameObject, unscaled: true);
        if (tween == null || !tween.IsActive()) IsClosed = false;
        return tween;
    }

    public void Kill() => DOTween.Kill(this);

    private static UniTask WaitAsync(Tween tween, CancellationToken ct) =>
        tween == null ? UniTask.CompletedTask : UniTask.WaitWhile(() => tween.IsActive() && tween.IsPlaying(), cancellationToken: ct);
}

using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// #8 レアなアイコンに時々光が走る（斜めの光の帯が左→右へ 0.5 秒、既定 3 秒ごと）＋ 角に小さなきらめき。
/// アイコンの矩形で切り抜く子（IconGlint）を足すだけで、アイコンの絵・色は触らない。省略モードでは出さない。
/// </summary>
[DisallowMultipleComponent]
public sealed class IconGlint : MonoBehaviour
{
    private float interval = 3f;
    private RectTransform maskRoot;
    private RectTransform bar;
    private RectTransform sparkle;
    private Sequence loop;

    /// <summary>icon（レアの商品・遺物のアイコンなど）に光を走らせる。</summary>
    public static IconGlint Attach(RectTransform icon, float interval = 3f)
    {
        if (icon == null) return null;
        if (!icon.TryGetComponent<IconGlint>(out var glint)) glint = icon.gameObject.AddComponent<IconGlint>();
        glint.interval = Mathf.Max(1f, interval);
        glint.enabled = true;
        glint.Restart();
        return glint;
    }

    /// <summary>光を止めて部品を外す。</summary>
    public static void Detach(RectTransform icon)
    {
        if (icon == null || !icon.TryGetComponent<IconGlint>(out var glint)) return;
        glint.Stop();
        if (glint.maskRoot != null) UIMotion.DestroySafe(glint.maskRoot.gameObject);
        UIMotion.DestroySafe(glint);
    }

    private void OnEnable()
    {
        if (loop == null) Begin();
    }

    private void OnDisable() => Stop();

    private void OnDestroy() => Stop();

    private void Restart()
    {
        Stop();
        Begin();
    }

    private void Build()
    {
        if (maskRoot != null) return;
        var rt = (RectTransform)transform;
        var go = new GameObject("IconGlint", typeof(RectTransform));
        go.layer = gameObject.layer;
        maskRoot = (RectTransform)go.transform;
        maskRoot.SetParent(rt, false);
        UIMotionFx.Stretch(maskRoot);
        go.AddComponent<RectMask2D>();
        UIMotionFx.IgnoreLayout(go);
        float h = Mathf.Max(rt.rect.height, 40f) * 1.8f;
        var barImg = UIMotionFx.NewImage("Bar", maskRoot, UIMotionFx.SoftBarSprite, new Color(1f, 1f, 0.9f, 0.6f), new Vector2(Mathf.Max(rt.rect.width, 40f) * 0.35f, h));
        bar = barImg.rectTransform;
        bar.localRotation = Quaternion.Euler(0f, 0f, -24f);

        var sp = UIMotionFx.NewImage("Sparkle", rt, UIMotionFx.SparkleSprite, Color.white, new Vector2(34f, 34f));
        sparkle = sp.rectTransform;
        sparkle.anchorMin = sparkle.anchorMax = new Vector2(0.85f, 0.85f);
        sparkle.localScale = Vector3.zero;
        UIMotionFx.IgnoreLayout(sp.gameObject);
        sparkle.SetParent(maskRoot.parent, true);
        sparkle.SetSiblingIndex(maskRoot.GetSiblingIndex() + 1);
    }

    private void Begin()
    {
        if (UIMotion.IsSkip) return;
        Build();
        maskRoot.gameObject.SetActive(true);
        float w = Mathf.Max(((RectTransform)transform).rect.width, 40f);
        bar.anchoredPosition = new Vector2(-w, 0f);
        sparkle.localScale = Vector3.zero;
        float phase = Mathf.Repeat(GetInstanceID() * 0.37f, 1f) * interval;

        var seq = DOTween.Sequence().SetTarget(this);
        seq.AppendInterval(interval * 0.7f);
        seq.Append(bar.DOAnchorPosX(w, 0.5f).SetEase(Ease.InOutSine));
        seq.Insert(interval * 0.7f + 0.3f, sparkle.DOScale(1f, 0.12f).SetEase(Ease.OutBack));
        seq.Insert(interval * 0.7f + 0.3f, sparkle.DOLocalRotate(new Vector3(0f, 0f, 90f), 0.4f, RotateMode.LocalAxisAdd));
        seq.Insert(interval * 0.7f + 0.45f, sparkle.DOScale(0f, 0.2f).SetEase(Ease.InQuad));
        seq.AppendInterval(interval * 0.3f - 0.5f > 0f ? interval * 0.3f - 0.5f : 0.05f);
        seq.SetLoops(-1);
        loop = UIMotion.RunLoop(seq, gameObject);
        loop?.Goto(phase, true);
    }

    private void Stop()
    {
        loop?.Kill();
        loop = null;
        if (maskRoot != null) maskRoot.gameObject.SetActive(false);
        if (sparkle != null) sparkle.localScale = Vector3.zero;
    }
}

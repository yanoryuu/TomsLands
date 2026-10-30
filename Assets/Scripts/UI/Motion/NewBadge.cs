using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// #8 NEW 札: 対象の右上に赤い札を付け、2 秒ごとに「ぷるっ」と揺らす（-12°→10°→-6°→0°、少し大きく）。
/// 札は対象の子（NewBadge）として足し、レイアウトには入らない。Detach で外す。省略モードでは揺らさない（札は出す）。
/// </summary>
[DisallowMultipleComponent]
public sealed class NewBadge : MonoBehaviour
{
    private RectTransform badge;
    private TextMeshProUGUI label;
    private Sequence loop;

    /// <summary>target の右上に札を付ける（既に付いていれば文字だけ変える）。offset は右上の角からのずれ。</summary>
    public static NewBadge Attach(RectTransform target, string text = "NEW", Vector2 offset = default)
    {
        if (target == null) return null;
        if (!target.TryGetComponent<NewBadge>(out var nb)) nb = target.gameObject.AddComponent<NewBadge>();
        nb.Build(text, offset);
        nb.enabled = true;
        nb.Restart();
        return nb;
    }

    /// <summary>札を外す。</summary>
    public static void Detach(RectTransform target)
    {
        if (target == null || !target.TryGetComponent<NewBadge>(out var nb)) return;
        nb.Stop();
        if (nb.badge != null) UIMotion.DestroySafe(nb.badge.gameObject);
        UIMotion.DestroySafe(nb);
    }

    private void Build(string text, Vector2 offset)
    {
        if (badge == null)
        {
            var img = UIMotionFx.NewImage("NewBadge", transform, UIMotionFx.RoundSprite, UIMotionFx.Red, new Vector2(78f, 34f));
            img.pixelsPerUnitMultiplier = 2.5f;
            UIMotionFx.IgnoreLayout(img.gameObject);
            badge = img.rectTransform;
            badge.anchorMin = badge.anchorMax = Vector2.one;
            badge.pivot = new Vector2(0.5f, 0.5f);
            label = UIMotionFx.NewText("Label", badge, text, 22f, UIMotionFx.Cream, new Vector2(78f, 34f));
            label.fontStyle = FontStyles.Bold;
            label.fontSharedMaterial = UIMotionFx.OutlinedMaterial(label.font);
        }
        label.text = text;
        float w = Mathf.Max(64f, label.GetPreferredValues(text).x + 22f);
        badge.sizeDelta = new Vector2(w, 34f);
        label.rectTransform.sizeDelta = badge.sizeDelta;
        badge.anchoredPosition = new Vector2(-w * 0.3f, -6f) + offset;
        badge.localRotation = Quaternion.Euler(0f, 0f, 8f);
        badge.SetAsLastSibling();
    }

    private void OnEnable()
    {
        if (loop == null && badge != null) Begin();
    }

    private void OnDisable() => Stop();

    private void OnDestroy() => Stop();

    private void Restart()
    {
        Stop();
        Begin();
    }

    private void Begin()
    {
        if (badge == null || UIMotion.IsSkip) return;
        float phase = Mathf.Repeat(GetInstanceID() * 0.41f, 1f) * 2f;
        var seq = DOTween.Sequence().SetTarget(this);
        seq.AppendInterval(1.2f);
        seq.Append(badge.DOLocalRotate(new Vector3(0f, 0f, -12f), 0.12f));
        seq.Join(badge.DOScale(1.15f, 0.12f));
        seq.Append(badge.DOLocalRotate(new Vector3(0f, 0f, 10f), 0.12f));
        seq.Append(badge.DOLocalRotate(new Vector3(0f, 0f, -6f), 0.12f));
        seq.Append(badge.DOLocalRotate(new Vector3(0f, 0f, 8f), 0.12f));
        seq.Join(badge.DOScale(1f, 0.12f));
        seq.AppendInterval(0.32f);
        seq.SetLoops(-1);
        loop = UIMotion.RunLoop(seq, badge.gameObject);
        loop?.Goto(phase, true);
    }

    private void Stop()
    {
        loop?.Kill();
        loop = null;
        if (badge == null) return;
        badge.localRotation = Quaternion.Euler(0f, 0f, 8f);
        badge.localScale = Vector3.one;
    }
}

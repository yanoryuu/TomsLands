using DG.Tweening;
using UnityEngine;

/// <summary>
/// #8 村のショートカットの帯で「拡張できる」施設に付く金の「！」を、2 秒ごとにぷるっと揺らす（NewBadge と同じ揺れ方）。
/// 既にある「！」（Badge）に付けるだけで、絵・位置・表示は変えない。表示中だけ動き、隠れたら傾き・大きさを元に戻す。
/// 省略モード（AutoPlay・人格マラソン）では揺らさない。
/// </summary>
[DisallowMultipleComponent]
public sealed class VillageBadgeWobble : MonoBehaviour
{
    private Tween loop;
    private Quaternion baseRotation;
    private Vector3 baseScale;
    private bool captured;

    /// <summary>badge に付ける（既に付いていれば何もしない）。</summary>
    public static void Ensure(Transform badge)
    {
        if (badge == null || badge.GetComponent<VillageBadgeWobble>() != null) return;
        badge.gameObject.AddComponent<VillageBadgeWobble>();
    }

    private void OnEnable()
    {
        if (!captured)
        {
            baseRotation = transform.localRotation;
            baseScale = transform.localScale;
            captured = true;
        }
        Begin();
    }

    private void OnDisable() => Stop();

    private void OnDestroy() => Stop();

    private void Begin()
    {
        Stop();
        if (UIMotion.IsSkip) return;
        var t = transform;
        Vector3 r = baseRotation.eulerAngles;
        float phase = Mathf.Repeat(GetInstanceID() * 0.37f, 1f) * 2f;
        var seq = DOTween.Sequence().SetTarget(t);
        seq.AppendInterval(1.2f);
        seq.Append(t.DOLocalRotate(r + new Vector3(0f, 0f, -14f), 0.1f));
        seq.Join(t.DOScale(baseScale * 1.2f, 0.1f));
        seq.Append(t.DOLocalRotate(r + new Vector3(0f, 0f, 12f), 0.1f));
        seq.Append(t.DOLocalRotate(r + new Vector3(0f, 0f, -7f), 0.1f));
        seq.Append(t.DOLocalRotate(r, 0.1f));
        seq.Join(t.DOScale(baseScale, 0.1f));
        seq.AppendInterval(0.4f);
        seq.SetLoops(-1);
        loop = UIMotion.RunLoop(seq, gameObject);
        loop?.Goto(phase, true);
    }

    private void Stop()
    {
        loop?.Kill();
        loop = null;
        if (!captured) return;
        transform.localRotation = baseRotation;
        transform.localScale = baseScale;
    }
}

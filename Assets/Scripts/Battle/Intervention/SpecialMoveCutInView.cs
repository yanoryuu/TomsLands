using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 赤スパ必殺技のカットイン（勇者立ち絵＋赤帯＋集中線）。約0.95秒。
/// 入力は塞がない（売り場操作を邪魔しない）。時間は unscaled で進める。
/// 未配線の参照があっても例外を出さない（その要素を飛ばすだけ）。
/// </summary>
public class SpecialMoveCutInView : MonoBehaviour, ISpecialMoveCutIn
{
    [SerializeField] private CanvasGroup root;
    [SerializeField] private Image dim;
    [SerializeField] private RectTransform speedLines;
    [Tooltip("赤帯（Mask を持ち、子の立ち絵を帯の形で切り抜く）")]
    [SerializeField] private RectTransform band;
    [SerializeField] private RectTransform portrait;
    [SerializeField] private Image flash;

    [Header("タイミング")]
    [SerializeField] private float bandIn = 0.16f;
    [SerializeField] private float hold = 0.5f;
    [SerializeField] private float bandOut = 0.2f;
    [SerializeField] private float dimAlpha = 0.7f;

    [Header("位置")]
    [SerializeField] private float bandTravel = 2200f;
    [SerializeField] private Vector2 portraitRestPos = new(430f, -600f);
    [SerializeField] private float portraitEnterOffset = 700f;
    [SerializeField] private float portraitDrift = 70f;

    private Sequence _seq;
    private Vector2 _bandHome;
    private bool _homeCached;

    public float Duration => bandIn + hold + bandOut + 0.08f;

    private void Awake()
    {
        CacheHome();
        HideImmediate();
    }

    private void CacheHome()
    {
        if (_homeCached) return;
        _homeCached = true;
        if (band != null) _bandHome = band.anchoredPosition;
    }

    public async UniTask PlayAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!gameObject.activeInHierarchy) gameObject.SetActive(true);
        if (!gameObject.activeInHierarchy) return; // 親が非表示なら演出を飛ばす
        CacheHome();

        _seq?.Kill();
        PrepareStart();
        var tcs = new UniTaskCompletionSource();
        _seq = BuildSequence();
        _seq.OnComplete(() => tcs.TrySetResult());
        _seq.OnKill(() => tcs.TrySetResult());

        using (ct.Register(() => _seq?.Kill()))
        {
            await tcs.Task;
        }

        HideImmediate();
        ct.ThrowIfCancellationRequested();
    }

    private void PrepareStart()
    {
        if (root != null)
        {
            root.alpha = 1f;
            root.blocksRaycasts = false;
            root.interactable = false;
        }
        if (dim != null) SetAlpha(dim, 0f);
        if (flash != null) SetAlpha(flash, 0f);
        if (band != null)
        {
            band.gameObject.SetActive(true);
            band.anchoredPosition = _bandHome + new Vector2(-bandTravel, 0f);
            band.localScale = new Vector3(1f, 0.2f, 1f);
        }
        if (portrait != null) portrait.anchoredPosition = portraitRestPos + new Vector2(portraitEnterOffset, 0f);
        if (speedLines != null)
        {
            speedLines.gameObject.SetActive(true);
            speedLines.localScale = Vector3.one * 1.25f;
            speedLines.localRotation = Quaternion.identity;
            var g = speedLines.GetComponent<Graphic>();
            if (g != null) SetAlpha(g, 0f);
        }
    }

    private Sequence BuildSequence()
    {
        var s = DOTween.Sequence();
        float tHoldEnd = bandIn + hold;

        if (dim != null)
        {
            s.Insert(0f, Fade(dim, 0f, dimAlpha, 0.1f));
            s.Insert(tHoldEnd, Fade(dim, dimAlpha, 0f, bandOut + 0.05f));
        }

        if (flash != null)
        {
            s.Insert(bandIn * 0.6f, Fade(flash, 0f, 0.85f, 0.04f));
            s.Insert(bandIn * 0.6f + 0.04f, Fade(flash, 0.85f, 0f, 0.2f).SetEase(Ease.OutQuad));
        }

        if (band != null)
        {
            s.Insert(0f, band.DOAnchorPos(_bandHome, bandIn).SetEase(Ease.OutCubic));
            s.Insert(0f, band.DOScaleY(1f, bandIn).SetEase(Ease.OutBack));
            s.Insert(tHoldEnd, band.DOAnchorPos(_bandHome + new Vector2(bandTravel, 0f), bandOut).SetEase(Ease.InCubic));
            s.Insert(tHoldEnd, band.DOScaleY(0.3f, bandOut).SetEase(Ease.InQuad));
        }

        if (portrait != null)
        {
            s.Insert(0.04f, portrait.DOAnchorPos(portraitRestPos, bandIn + 0.06f).SetEase(Ease.OutCubic));
            s.Insert(0.04f + bandIn + 0.06f,
                portrait.DOAnchorPos(portraitRestPos + new Vector2(-portraitDrift, 0f), hold - 0.06f).SetEase(Ease.Linear));
        }

        if (speedLines != null)
        {
            var g = speedLines.GetComponent<Graphic>();
            if (g != null)
            {
                s.Insert(0.02f, Fade(g, 0f, 0.9f, 0.1f));
                s.Insert(tHoldEnd, Fade(g, 0.9f, 0f, bandOut));
            }
            s.Insert(0f, speedLines.DOScale(1f, bandIn + hold).SetEase(Ease.OutQuad));
            s.Insert(0f, speedLines.DOLocalRotate(new Vector3(0f, 0f, 6f), bandIn + hold + bandOut).SetEase(Ease.Linear));
        }

        s.AppendInterval(0.02f);
        s.SetUpdate(true).SetLink(gameObject);
        return s;
    }

    private void HideImmediate()
    {
        if (root != null)
        {
            root.alpha = 0f;
            root.blocksRaycasts = false;
            root.interactable = false;
        }
        if (band != null)
        {
            band.anchoredPosition = _bandHome;
            band.localScale = Vector3.one;
        }
    }

    /// <summary>開始値を明示したフェード（Sequence 内で同じ対象を何度もフェードしても値が混ざらない）。</summary>
    private static Tween Fade(Graphic g, float from, float to, float duration) =>
        DOVirtual.Float(from, to, duration, a => SetAlpha(g, a)); // 親 Sequence 側で SetLink 済み

    private static void SetAlpha(Graphic g, float a)
    {
        var c = g.color;
        c.a = a;
        g.color = c;
    }

    private void OnDisable()
    {
        _seq?.Kill();
        _seq = null;
    }

#if UNITY_EDITOR
    [ContextMenu("Debug/Play CutIn")]
    private void DebugPlay()
    {
        if (!Application.isPlaying) return;
        PlayAsync(this.GetCancellationTokenOnDestroy()).Forget(e =>
        {
            if (e is not OperationCanceledException) Debug.LogException(e);
        });
    }
#endif
}

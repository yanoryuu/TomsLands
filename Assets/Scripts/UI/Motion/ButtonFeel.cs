using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// #4 ボタンの手ざわり。全ボタンに自動で付く（<see cref="ButtonFeelInstaller"/>。ButtonSoundHandler と同じ方式）。
/// ・押すと 0.12 秒へこんで戻る（pointerDown で 0.92 倍 → pointerUp で 1.03 倍 → 等倍）
/// ・押せない（interactable=false）ボタンを押すと 0.25 秒首振り
/// ・主ボタンだけ <see cref="SetPrimary"/> で 1.6 秒周期の脈打ち（1→1.04）と時々走る光
/// 触るのは Transform の拡縮と、主ボタンに足す光の子（ButtonFeelShine）だけ。色・スプライト・CanvasGroup は触らないので
/// ButtonHoverSprite / ColorTint / ControllerHighlight とはケンカしない。省略モードでは何もしない。
/// 付けたくないボタンは <see cref="Disable"/>（宴のボタン・Animator 付きのボタンには最初から付かない）。
/// </summary>
[DisallowMultipleComponent]
public sealed class ButtonFeel : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    private const float PressScale = 0.92f;
    private const float PulsePeriod = 1.6f;
    private const float PulseScale = 1.04f;
    private const float ShineInterval = 3.2f;

    private Button button;
    private Vector3 baseScale = Vector3.one;
    private bool initialized;
    private bool pressed;
    private bool primary;
    private Tween pulse;
    private Sequence shine;
    private RectTransform shineRoot;

    /// <summary>主ボタン（その画面で次に押してほしいボタン）にする／やめる。付いていなければ付ける。</summary>
    public static ButtonFeel SetPrimary(Button target, bool on = true)
    {
        if (target == null) return null;
        if (!target.TryGetComponent<ButtonFeel>(out var feel)) feel = target.gameObject.AddComponent<ButtonFeel>();
        feel.Init();
        feel.primary = on;
        if (on) feel.StartIdle();
        else feel.StopIdle();
        return feel;
    }

    /// <summary>このボタンには手ざわりを付けない（自動付与も止まる）。</summary>
    public static void Disable(Button target)
    {
        if (target == null) return;
        if (!target.TryGetComponent<ButtonFeel>(out var feel)) feel = target.gameObject.AddComponent<ButtonFeel>();
        feel.StopIdle();
        feel.enabled = false;
    }

    /// <summary>これより大きいボタン（キャラの立ち絵など）は押しても縮ませない。</summary>
    private const float MaxFeelSize = 360f;

    /// <summary>付いてよいボタンか（宴の自前ボタン・Animator で動かすボタン・立ち絵のような大きなボタンは除く）。</summary>
    public static bool IsEligible(Button b) =>
        b != null && b.transition != Selectable.Transition.Animation && b.GetComponent<Animator>() == null &&
        b.GetComponent<Utage.UguiButtonSe>() == null && !IsLarge(b);

    private static bool IsLarge(Button b)
    {
        if (b.transform is not RectTransform rt) return false;
        var size = rt.rect.size;
        return size.x > MaxFeelSize || size.y > MaxFeelSize;
    }

    private void Init()
    {
        if (initialized) return;
        initialized = true;
        button = GetComponent<Button>();
        baseScale = transform.localScale;
    }

    private void Awake() => Init();

    private void OnEnable()
    {
        Init();
        if (primary) StartIdle();
    }

    private void OnDisable()
    {
        StopIdle();
        if (pressed || DOTween.IsTweening(this))
        {
            DOTween.Kill(this);
            transform.localScale = baseScale;
        }
        pressed = false;
    }

    private bool Interactable => button == null || button.IsInteractable();

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isActiveAndEnabled && Application.isPlaying) return;
        Init();
        if (UIMotion.IsSkip || !Interactable) return;
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
        if (!DOTween.IsTweening(this) && !pressed) baseScale = CurrentRestScale();
        pressed = true;
        StopPulse();
        DOTween.Kill(this);
        UIMotion.Run(transform.DOScale(baseScale * PressScale, 0.05f).SetEase(Ease.OutQuad).SetId(this), gameObject, unscaled: true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!pressed) return;
        pressed = false;
        DOTween.Kill(this);
        var seq = DOTween.Sequence().SetId(this);
        seq.Append(transform.DOScale(baseScale * 1.03f, 0.04f).SetEase(Ease.OutQuad));
        seq.Append(transform.DOScale(baseScale, 0.07f).SetEase(Ease.OutSine));
        seq.OnComplete(() => { if (primary) StartPulse(); });
        UIMotion.Run(seq, gameObject, unscaled: true);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isActiveAndEnabled && Application.isPlaying) return;
        if (Interactable) return;
        // 押せないボタンを押した: 首振りで「今は押せない」を伝える
        UIMotion.Shake((RectTransform)transform, 6f, 0.25f, unscaled: true);
    }

    // ---------------------------------------------------------------- 主ボタン（脈打ち＋光）

    private void StartIdle()
    {
        StartPulse();
        StartShine();
    }

    private void StopIdle()
    {
        StopPulse();
        if (shine != null) shine.Kill();
        shine = null;
        if (shineRoot != null) shineRoot.gameObject.SetActive(false);
    }

    private void StartPulse()
    {
        if (!primary || pressed || UIMotion.IsSkip || (pulse != null && pulse.IsActive())) return;
        if (Application.isPlaying && !isActiveAndEnabled) return;
        if (!DOTween.IsTweening(this)) baseScale = CurrentRestScale();
        var b = baseScale;
        // 0→1→0 を sin で回す（1 周 1.6 秒）。他の演出（UIFx.Pop など）に止められたら少し待って再開する
        pulse = UIMotion.RunLoop(DOVirtual.Float(0f, 1f, PulsePeriod, t =>
            {
                float k = (1f - Mathf.Cos(t * Mathf.PI * 2f)) * 0.5f;
                transform.localScale = b * Mathf.Lerp(1f, PulseScale, k);
            })
            .SetEase(Ease.Linear).SetLoops(-1).SetTarget(transform)
            .OnKill(OnPulseKilled), gameObject);
    }

    private void OnPulseKilled()
    {
        pulse = null;
        if (this == null || !primary || pressed) return;
        if (Application.isPlaying && !isActiveAndEnabled) return;
        UIMotion.Delay(0.5f, () => { if (this != null) StartPulse(); }, gameObject, unscaled: true);
    }

    private void StopPulse()
    {
        if (pulse == null) return;
        var p = pulse;
        pulse = null;
        p.OnKill(null);
        p.Kill();
        transform.localScale = baseScale;
    }

    /// <summary>脈打ちの途中なら等倍に直した大きさ。</summary>
    private Vector3 CurrentRestScale() => pulse != null && pulse.IsActive() ? baseScale : transform.localScale;

    private void StartShine()
    {
        if (UIMotion.IsSkip || (shine != null && shine.IsActive())) return;
        var rt = (RectTransform)transform;
        if (shineRoot == null)
        {
            // ボタンの矩形で切り抜く子を作り、その中を斜めの光の帯が左から右へ走る
            var go = new GameObject("ButtonFeelShine", typeof(RectTransform));
            go.layer = gameObject.layer;
            shineRoot = (RectTransform)go.transform;
            shineRoot.SetParent(rt, false);
            UIMotionFx.Stretch(shineRoot);
            shineRoot.offsetMin = new Vector2(6f, 6f);
            shineRoot.offsetMax = new Vector2(-6f, -6f);
            go.AddComponent<RectMask2D>();
            UIMotionFx.IgnoreLayout(go);
            var bar = UIMotionFx.NewImage("Bar", shineRoot, UIMotionFx.SoftBarSprite, new Color(1f, 0.98f, 0.85f, 0.55f), new Vector2(46f, 400f));
            bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -20f);
        }
        shineRoot.gameObject.SetActive(true);
        var barRt = (RectTransform)shineRoot.GetChild(0);
        float w = Mathf.Max(80f, rt.rect.width);
        barRt.anchoredPosition = new Vector2(-w, 0f);
        var seq = DOTween.Sequence();
        seq.AppendInterval(ShineInterval * 0.55f);
        seq.Append(barRt.DOAnchorPosX(w, ShineInterval * 0.2f).SetEase(Ease.InOutSine));
        seq.AppendInterval(ShineInterval * 0.25f);
        seq.SetLoops(-1);
        shine = UIMotion.RunLoop(seq, shineRoot.gameObject);
    }

    private void OnDestroy()
    {
        DOTween.Kill(this);
        if (pulse != null) pulse.OnKill(null);
        pulse?.Kill();
        shine?.Kill();
    }
}

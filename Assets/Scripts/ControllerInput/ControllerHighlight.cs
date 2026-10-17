using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// パッド/キー使用中に、選択中の要素へ金色の選択枠をかぶせる共通オーバーレイ。
/// 各ボタンの Transition 設定に依存せず、選択要素の画面上の矩形へ自動追従する。
/// ControllerOverlay プレハブ（常駐 Overlay Canvas）の一部。
/// </summary>
public sealed class ControllerHighlight : MonoBehaviour
{
    [SerializeField] private RectTransform frame;
    [SerializeField] private CanvasGroup frameGroup;
    [Tooltip("枠の外側への余白（1920x1080 基準 px）")]
    [SerializeField] private Vector2 defaultPadding = new(8f, 8f);
    [Tooltip("パルスの振れ幅（px）")]
    [SerializeField] private float pulseAmplitude = 4f;
    [SerializeField] private float pulseSpeed = 5f;
    [Tooltip("選択移動時の追従の速さ")]
    [SerializeField] private float followSharpness = 24f;
    [SerializeField] private float fadeSpeed = 12f;

    private readonly Vector3[] corners = new Vector3[4];
    private GameObject currentTarget;
    private bool visible;
    private Vector2 pos;
    private Vector2 size;
    private float alpha;

    private void Awake()
    {
        if (frameGroup != null) frameGroup.alpha = 0f;
        if (frame != null) frame.gameObject.SetActive(true);
    }

    private void LateUpdate()
    {
        if (frame == null) return;

        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        bool want = ControllerInput.IsNavigating.CurrentValue && sel != null && sel.activeInHierarchy;

        UIFocusHighlightOverride ov = null;
        RectTransform targetRect = null;
        float targetAlpha = 0f;
        Vector2 targetPos = pos, targetSize = size;

        if (want)
        {
            ov = sel.GetComponent<UIFocusHighlightOverride>();
            if (ov != null && ov.hide) want = false;
            else
            {
                targetRect = ov != null && ov.target != null ? ov.target : sel.transform as RectTransform;
                want = targetRect != null && TryGetRect(targetRect, ov != null ? ov.padding : defaultPadding, out targetPos, out targetSize);
                if (want) targetAlpha = EffectiveAlpha(targetRect);
            }
        }

        float dt = Time.unscaledDeltaTime;
        if (want)
        {
            if (!visible || alpha < 0.02f)
            {
                // 新しく出現: その場にスナップして軽くポップ
                pos = targetPos;
                size = targetSize;
                frame.DOKill();
                frame.localScale = Vector3.one * 1.08f;
                frame.DOScale(1f, 0.18f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(frame.gameObject);
            }
            else if (sel != currentTarget)
            {
                frame.DOKill();
                frame.localScale = Vector3.one;
            }
            float k = 1f - Mathf.Exp(-followSharpness * dt);
            pos = Vector2.Lerp(pos, targetPos, k);
            size = Vector2.Lerp(size, targetSize, k);
            currentTarget = sel;
            visible = true;
        }
        else
        {
            visible = false;
            currentTarget = null;
            targetAlpha = 0f;
        }

        alpha = Mathf.MoveTowards(alpha, targetAlpha, fadeSpeed * dt);
        if (frameGroup != null) frameGroup.alpha = alpha;

        float pulse = (Mathf.Sin(Time.unscaledTime * pulseSpeed) * 0.5f + 0.5f) * pulseAmplitude;
        frame.anchoredPosition = pos;
        frame.sizeDelta = size + new Vector2(pulse, pulse) * 2f;
    }

    private bool TryGetRect(RectTransform target, Vector2 padding, out Vector2 center, out Vector2 rectSize)
    {
        center = default;
        rectSize = default;
        var parent = frame.parent as RectTransform;
        if (parent == null) return false;

        var canvas = target.GetComponentInParent<Canvas>();
        if (canvas == null) return false;
        var root = canvas.rootCanvas;
        Camera cam = null;
        if (root.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = root.worldCamera != null ? root.worldCamera : Camera.main;

        target.GetWorldCorners(corners);
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        for (int i = 0; i < 4; i++)
        {
            var sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, sp, null, out var lp)) return false;
            min = Vector2.Min(min, lp);
            max = Vector2.Max(max, lp);
        }
        var s = max - min;
        if (s.x < 2f || s.y < 2f) return false;
        center = (min + max) * 0.5f;
        rectSize = s + padding * 2f;
        return true;
    }

    private static float EffectiveAlpha(Transform t)
    {
        float a = 1f;
        for (var p = t; p != null; p = p.parent)
        {
            var cg = p.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                a *= cg.alpha;
                if (cg.ignoreParentGroups) break;
            }
        }
        return a;
    }
}

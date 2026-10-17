using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ScrollRect に付けるだけで、パッド/キーで選択がスクロール内の要素に移ったとき
/// その要素が見える位置まで自動スクロールする。マウス操作中は何もしない。
/// </summary>
[RequireComponent(typeof(ScrollRect))]
public sealed class ScrollRectAutoFollow : MonoBehaviour
{
    [Tooltip("要素の上下左右に確保する余白（px）")]
    [SerializeField] private float margin = 16f;
    [SerializeField] private float duration = 0.15f;

    private ScrollRect scroll;
    private GameObject lastSelected;
    private Tween tween;

    private void Awake() => scroll = GetComponent<ScrollRect>();

    private void LateUpdate()
    {
        if (scroll == null || scroll.content == null) return;
        if (!ControllerInput.IsNavigating.CurrentValue) { lastSelected = null; return; }

        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        if (sel == lastSelected) return;
        lastSelected = sel;
        if (sel == null || !sel.transform.IsChildOf(scroll.content)) return;

        ScrollTo(sel.transform as RectTransform);
    }

    /// <summary>指定要素が見えるようにスクロールする（コードからも呼べる）。</summary>
    public void ScrollTo(RectTransform target, bool instant = false)
    {
        if (scroll == null) scroll = GetComponent<ScrollRect>();
        if (scroll == null || target == null || scroll.content == null) return;

        var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        var content = scroll.content;
        Canvas.ForceUpdateCanvases();

        var vr = viewport.rect;
        var tb = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, target);
        var cb = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, content);

        Vector2 delta = Vector2.zero;
        if (scroll.vertical)
        {
            if (tb.max.y + margin > vr.yMax) delta.y = vr.yMax - (tb.max.y + margin);
            else if (tb.min.y - margin < vr.yMin) delta.y = vr.yMin - (tb.min.y - margin);
            // コンテンツの端を超えない
            if (cb.size.y > vr.height)
                delta.y = Mathf.Clamp(delta.y, vr.yMax - cb.max.y, vr.yMin - cb.min.y);
            else delta.y = 0f;
        }
        if (scroll.horizontal)
        {
            if (tb.max.x + margin > vr.xMax) delta.x = vr.xMax - (tb.max.x + margin);
            else if (tb.min.x - margin < vr.xMin) delta.x = vr.xMin - (tb.min.x - margin);
            if (cb.size.x > vr.width)
                delta.x = Mathf.Clamp(delta.x, vr.xMax - cb.max.x, vr.xMin - cb.min.x);
            else delta.x = 0f;
        }
        if (delta.sqrMagnitude < 0.25f) return;

        // viewport 空間の移動量 → content の親空間
        var parent = content.parent;
        Vector3 worldDelta = viewport.TransformVector(delta);
        Vector2 localDelta = parent != null ? (Vector2)parent.InverseTransformVector(worldDelta) : (Vector2)worldDelta;
        var to = content.anchoredPosition + localDelta;

        scroll.StopMovement();
        tween?.Kill();
        if (instant || duration <= 0f)
            content.anchoredPosition = to;
        else
            tween = content.DOAnchorPos(to, duration).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
    }

    private void OnDisable()
    {
        tween?.Kill();
        lastSelected = null;
    }
}

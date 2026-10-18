using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// パッド/キーで選択されたこと（＝マウスのホバー相当）を通知する小さな中継。
/// Button / Dropdown など、Pointer 系ではなく「選択」をホバー説明・行選択の代わりに使いたい要素に付ける。
/// マウス操作中の選択（クリックで選ばれた等）では発火しない。
/// EventTrigger と違いドラッグ/スクロールを横取りしないので、ScrollRect 内の行にも付けられる。
/// </summary>
[DisallowMultipleComponent]
public sealed class ControllerSelectRelay : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    public event Action Selected;
    public event Action Deselected;

    private bool active;

    public static ControllerSelectRelay GetOrAdd(Component target)
    {
        if (target == null) return null;
        return target.TryGetComponent<ControllerSelectRelay>(out var relay)
            ? relay
            : target.gameObject.AddComponent<ControllerSelectRelay>();
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (!ControllerInput.IsNavigating.CurrentValue) return;
        active = true;
        Selected?.Invoke();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        if (!active) return;
        active = false;
        Deselected?.Invoke();
    }

    private void OnDisable()
    {
        if (!active) return;
        active = false;
        Deselected?.Invoke();
    }

    private void OnDestroy()
    {
        Selected = null;
        Deselected = null;
    }
}

/// <summary>
/// 「動的に作り直す一覧の先頭へフォーカスを合わせたい」を View の LateUpdate で処理する小物。
/// Request の 1 フレーム後（Destroy した旧要素が消えてから）に対象を探し、見つかるまで数フレーム再試行する。
/// 対象は常に scope.FirstSelected にし、force のとき（開いた直後・選択中の要素を作り直したとき）は選択も移す。
/// </summary>
public sealed class PendingFocus
{
    private int frames;
    private int skip;
    private bool force;

    public void Request(bool forceSelect, int retryFrames = 30)
    {
        frames = retryFrames;
        skip = 1;
        force |= forceSelect;
    }

    public void Tick(UIFocusScope scope, Func<Selectable> getTarget)
    {
        if (frames <= 0) return;
        if (skip > 0) { skip--; return; }
        frames--;
        var target = getTarget?.Invoke();
        if (target == null)
        {
            if (frames <= 0) force = false;
            return;
        }
        frames = 0;
        if (scope != null)
        {
            scope.FirstSelected = target;
            if (force || ControllerFocusUtil.IsSelectionLost()) scope.Select(target);
        }
        force = false;
    }
}

/// <summary>画面 View からパッドのフォーカスを扱うための小物。</summary>
public static class ControllerFocusUtil
{
    /// <summary>root 以下で最初の「パッドで選べる」Selectable（Scrollbar・Navigation=None・操作不可は除く）。</summary>
    public static Selectable FirstUsable(Transform root)
    {
        if (root == null || !root.gameObject.activeInHierarchy) return null;
        foreach (var s in root.GetComponentsInChildren<Selectable>(false))
        {
            if (s == null || s is Scrollbar) continue;
            if (s.navigation.mode == Navigation.Mode.None) continue;
            if (!UIFocusManager.IsUsable(s.gameObject)) continue;
            return s;
        }
        return null;
    }

    /// <summary>今の選択が root の子孫か。</summary>
    public static bool IsSelectionInside(Transform root)
    {
        if (root == null) return false;
        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        return sel != null && sel.transform.IsChildOf(root);
    }

    /// <summary>何も選択されていない／選択が使えない要素か。</summary>
    public static bool IsSelectionLost()
    {
        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        return sel == null || !UIFocusManager.IsUsable(sel);
    }
}

using System;
using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// UIFocusScope のスタック管理と、パッド/キー操作時の選択の維持（静的サービス）。
///
/// ・パッド/キー使用中に「何も選択されていない／選択要素が非表示・操作不可になった／
///   モーダルスコープの外に出た」場合、最前面スコープの既定要素を選び直す。
/// ・スコープが 1 つも無い画面では、直近に表示されたパネル（NotifyPanelShown）内、
///   無ければ画面全体から「最前面 Canvas の左上の Selectable」を選ぶ（フォールバック）。
///   PlayerInput が有効なフィールド画面（村）ではフォールバックしない。
/// ・マウスに切り替わったら選択を外す（ハイライト残り防止）。
/// ・Cancel / LB / RB / X / Y / Start を最前面スコープに配送する。
/// </summary>
public static class UIFocusManager
{
    private const float RetryInterval = 0.1f;

    private static readonly List<UIFocusScope> scopes = new();
    private static int orderCounter;
    private static UIFocusScope lastTop;
    private static bool focusDirty = true;
    private static GameObject panelHint;
    private static float nextFallbackTime;
    private static int inputBlockCount;
    // 十字キー限定ナビ（UIFocusScope.dpadOnlyNavigation）で無効化中の Navigate アクション
    private static InputAction dpadOnlyAction;
    private static readonly List<int> dpadOnlyDisabledBindings = new();

    /// <summary>スコープが無い画面でも適当な要素を自動選択するか。</summary>
    public static bool FallbackSelectionEnabled { get; set; } = true;

    /// <summary>最前面のスコープ（無ければ null）。</summary>
    public static UIFocusScope Top
    {
        get
        {
            UIFocusScope best = null;
            for (int i = scopes.Count - 1; i >= 0; i--)
            {
                var s = scopes[i];
                if (s == null) { scopes.RemoveAt(i); continue; }
                if (!s.isActiveAndEnabled) continue;
                if (best == null || s.Priority > best.Priority || (s.Priority == best.Priority && s.order > best.order))
                    best = s;
            }
            return best;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        scopes.Clear();
        orderCounter = 0;
        lastTop = null;
        focusDirty = true;
        panelHint = null;
        inputBlockCount = 0;
        FallbackSelectionEnabled = true;
        dpadOnlyAction = null;
        dpadOnlyDisabledBindings.Clear();
    }

    // ------------------------------------------------------------------ 公開 API

    /// <summary>
    /// パネルを表示した直後に呼ぶ（GamePanelManager 等）。
    /// パネル内に UIFocusScope があればそれが最前面になる。無ければこのパネル内を優先してフォールバック選択する。
    /// UIFx.PanelOpen のフェード中でも選択は入る（alpha=0 の間は次フレーム以降に再試行）。
    /// </summary>
    public static void NotifyPanelShown(GameObject panel)
    {
        if (panel == null) return;
        panelHint = panel;
        focusDirty = true;
        nextFallbackTime = 0f;
    }

    /// <summary>次の更新で最前面スコープの既定要素へフォーカスし直す。</summary>
    public static void RequestRefocus()
    {
        focusDirty = true;
        nextFallbackTime = 0f;
        var top = Top;
        if (top != null) top.nextFocusRetryTime = 0f;
    }

    /// <summary>EventSystem の選択を変更する（null 可）。</summary>
    public static void Select(GameObject go)
    {
        var es = EventSystem.current;
        if (es == null || es.alreadySelecting) return;
        es.SetSelectedGameObject(go);
    }

    /// <summary>
    /// 会話（Utage）など別系統の入力が前面にある間、Cancel/LB/RB/X/Y/Start のスコープ配送を止める。
    /// 戻り値を Dispose で解除。
    /// </summary>
    public static IDisposable BlockScopeInput()
    {
        inputBlockCount++;
        return Disposable.Create(() => inputBlockCount = Mathf.Max(0, inputBlockCount - 1));
    }

    public static bool IsScopeInputBlocked => inputBlockCount > 0;

    /// <summary>
    /// Additive ロードしたポップアップシーンに、コードからモーダルスコープを付ける（シーンを編集せずに対応するため）。
    /// Canvas を持つ最初のルートに付与。既に UIFocusScope があればそれを優先しつつ Cancel だけ設定する。
    /// </summary>
    public static UIFocusScope AttachSceneScope(string sceneName, Action onCancel, int priority = 100)
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded) return null;

        GameObject host = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var existing = root.GetComponentInChildren<UIFocusScope>(true);
            if (existing != null) { host = existing.gameObject; break; }
            if (host == null && root.GetComponentInChildren<Canvas>(true) != null) host = root;
        }
        if (host == null) return null;

        var scope = UIFocusScope.GetOrAdd(host);
        scope.Modal = true;
        scope.Priority = Mathf.Max(scope.Priority, priority);
        if (onCancel != null)
        {
            // B/Esc で閉じるときも「戻る」音を鳴らす（閉じるボタンを押したときと揃える）
            scope.SetCancelAction(() =>
            {
                UISound.PlayBack();
                onCancel();
            });
        }
        RequestRefocus();
        return scope;
    }

    /// <summary>選択対象として使えるか（有効・表示中・操作可能）。</summary>
    public static bool IsUsable(GameObject go)
    {
        if (go == null || !go.activeInHierarchy) return false;
        if (!go.TryGetComponent<Selectable>(out var s)) return true;
        return s.isActiveAndEnabled && s.IsInteractable();
    }

    // ------------------------------------------------------------------ 内部

    internal static void Register(UIFocusScope scope)
    {
        if (scope == null) return;
        scopes.Remove(scope);
        scope.order = ++orderCounter;
        scope.nextFocusRetryTime = 0f;
        scopes.Add(scope);
        focusDirty = true;
    }

    internal static void Unregister(UIFocusScope scope)
    {
        scopes.Remove(scope);
        focusDirty = true;
    }

    internal static void OnDeviceChanged(InputDeviceKind kind)
    {
        if (kind == InputDeviceKind.Mouse)
        {
            var es = EventSystem.current;
            if (es == null) return;
            var sel = es.currentSelectedGameObject;
            if (sel == null) return;
            // 入力欄の入力中は外さない
            var tmp = sel.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused) return;
            var legacy = sel.GetComponent<InputField>();
            if (legacy != null && legacy.isFocused) return;
            Select(null);
        }
        else
        {
            focusDirty = true;
            nextFallbackTime = 0f;
        }
    }

    /// <summary>Update で呼ぶ: Cancel 配送。</summary>
    internal static void HandleCancelInput()
    {
        if (!ControllerInput.CancelPressedThisFrame()) return;
        if (IsScopeInputBlocked || ControllerInput.IsTextInputFocused()) return;
        if (IsDropdownExpanded()) return;
        var top = Top;
        if (top != null) top.HandleCancel();
    }

    /// <summary>LB/RB/X/Y/Start を最前面スコープへ。</summary>
    internal static void HandleButton(ControllerButton button)
    {
        if (button == ControllerButton.A || button == ControllerButton.B) return;
        if (IsScopeInputBlocked) return;
        var top = Top;
        if (top != null) top.HandleButton(button);
    }

    /// <summary>LateUpdate で呼ぶ: 選択の維持。</summary>
    internal static void Tick()
    {
        var es = EventSystem.current;
        if (es == null) return;

        var top = Top;
        if (top != lastTop)
        {
            lastTop = top;
            focusDirty = true;
        }
        UpdateDpadOnlyNavigation(es, top);

        var sel = es.currentSelectedGameObject;
        if (top != null && sel != null && top.RememberLastSelected && top.Contains(sel) && IsUsable(sel))
            top.lastSelected = sel;

        if (!ControllerInput.IsNavigating.CurrentValue) return;

        bool valid = IsUsable(sel);
        if (valid && top != null && top.Modal && !top.Contains(sel)) valid = false;
        if (!valid) focusDirty = true;
        if (!focusDirty) return;

        float now = Time.unscaledTime;

        if (top != null)
        {
            if (!top.AutoSelect)
            {
                if (!valid && sel != null) Select(null);
                focusDirty = false;
                return;
            }
            if (valid && top.Contains(sel))
            {
                focusDirty = false;
                return;
            }
            if (now < top.nextFocusRetryTime) return;
            var target = top.GetFocusTarget();
            if (target != null)
            {
                Select(target);
                focusDirty = false;
            }
            else
            {
                top.nextFocusRetryTime = now + RetryInterval;
            }
            return;
        }

        // スコープ無し
        bool hintAlive = panelHint != null && panelHint.activeInHierarchy;
        if (valid && (!hintAlive || sel.transform.IsChildOf(panelHint.transform)))
        {
            focusDirty = false;
            return;
        }
        if (!FallbackSelectionEnabled || PlayerInput.all.Count > 0)
        {
            focusDirty = false;
            return;
        }
        if (now < nextFallbackTime) return;
        var fb = FindFallback(hintAlive ? panelHint.transform : null);
        if (fb == null && hintAlive) fb = FindFallback(null);
        if (fb != null)
        {
            Select(fb);
            focusDirty = false;
        }
        else
        {
            nextFallbackTime = now + RetryInterval;
        }
    }

    /// <summary>
    /// 最前面スコープが dpadOnlyNavigation の間、EventSystem の Navigate からスティックと WASD のバインドを外す
    /// （空パスのオーバーライド＝無効）。十字キー・矢印キーだけが UI の選択移動に効き、スティック/WASD はキャラ移動に回せる。
    /// スコープが最前面でなくなったら元に戻す。
    /// </summary>
    private static void UpdateDpadOnlyNavigation(EventSystem es, UIFocusScope top)
    {
        bool want = top != null && top.DpadOnlyNavigation;
        InputAction action = null;
        if (want)
        {
            var module = es.currentInputModule as InputSystemUIInputModule;
            if (module == null) module = es.GetComponent<InputSystemUIInputModule>();
            action = module != null && module.move != null ? module.move.action : null;
        }
        if (action == dpadOnlyAction) return;

        RestoreDpadOnlyNavigation();
        if (action == null) return;

        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            if (b.isComposite) continue;
            if (!IsStickOrWasd(b.path)) continue;
            action.ApplyBindingOverride(i, string.Empty);
            dpadOnlyDisabledBindings.Add(i);
        }
        dpadOnlyAction = action;
        Application.quitting -= RestoreDpadOnlyNavigation;
        Application.quitting += RestoreDpadOnlyNavigation;
    }

    private static void RestoreDpadOnlyNavigation()
    {
        if (dpadOnlyAction != null)
        {
            foreach (var i in dpadOnlyDisabledBindings)
                if (i < dpadOnlyAction.bindings.Count)
                    dpadOnlyAction.RemoveBindingOverride(i);
        }
        dpadOnlyAction = null;
        dpadOnlyDisabledBindings.Clear();
    }

    private static bool IsStickOrWasd(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.IndexOf("stick", StringComparison.OrdinalIgnoreCase) >= 0) return true; // 左右スティック・Joystick のスティック
        return path == "<Keyboard>/w" || path == "<Keyboard>/a" || path == "<Keyboard>/s" || path == "<Keyboard>/d";
    }

    private static bool IsDropdownExpanded()
    {
        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        if (sel == null) return false;
        var tmp = sel.GetComponentInParent<TMP_Dropdown>();
        if (tmp != null && tmp.IsExpanded) return true;
        // 展開中のリスト（"Dropdown List"）内の項目が選択されているケース
        return sel.transform.parent != null && sel.GetComponentInParent<Toggle>() != null
            && FindNamedAncestor(sel.transform, "Dropdown List") != null;
    }

    private static Transform FindNamedAncestor(Transform t, string name)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name == name) return p;
        return null;
    }

    private static GameObject FindFallback(Transform under)
    {
        var all = Selectable.allSelectablesArray;
        Selectable best = null;
        int bestOrder = int.MinValue;
        Vector2 bestPos = default;

        foreach (var s in all)
        {
            if (s == null || s is Scrollbar) continue;
            if (s.navigation.mode == Navigation.Mode.None) continue;
            if (!IsUsable(s.gameObject)) continue;
            if (under != null && !s.transform.IsChildOf(under)) continue;
            if (EffectiveAlpha(s.transform) < 0.05f) continue;

            var canvas = s.GetComponentInParent<Canvas>();
            if (canvas == null) continue;
            var root = canvas.rootCanvas;
            int order = root.sortingOrder + (root.renderMode == RenderMode.ScreenSpaceOverlay ? 100000 : 0);
            Vector2 pos = ScreenCenter(s.transform as RectTransform, root);

            bool better;
            if (best == null || order > bestOrder) better = true;
            else if (order < bestOrder) better = false;
            else if (Mathf.Abs(pos.y - bestPos.y) > 24f) better = pos.y > bestPos.y; // 上にある方
            else better = pos.x < bestPos.x;                                          // 左にある方

            if (better)
            {
                best = s;
                bestOrder = order;
                bestPos = pos;
            }
        }
        return best != null ? best.gameObject : null;
    }

    private static float EffectiveAlpha(Transform t)
    {
        float a = 1f;
        for (var p = t; p != null; p = p.parent)
        {
            if (p.TryGetComponent<CanvasGroup>(out var cg))
            {
                a *= cg.alpha;
                if (cg.ignoreParentGroups) break;
            }
        }
        return a;
    }

    private static Vector2 ScreenCenter(RectTransform rt, Canvas root)
    {
        if (rt == null) return Vector2.zero;
        var cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : (root.worldCamera != null ? root.worldCamera : Camera.main);
        return RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
    }
}

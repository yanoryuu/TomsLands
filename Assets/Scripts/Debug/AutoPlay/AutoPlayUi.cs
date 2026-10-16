#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

/// <summary>
/// オートプレイ用の UI 操作・DI 解決・リフレクションのヘルパー（読み取りとボタン押下のみ。既存コードは書き換えない）。
/// </summary>
public static class AutoPlayUi
{
    // =====================================================
    // ボタン押下
    // =====================================================

    /// <summary>実際に押せる状態か（表示中・interactable・親 CanvasGroup がブロックしていない）。</summary>
    public static bool IsClickable(Selectable selectable)
    {
        if (selectable == null) return false;
        if (!selectable.gameObject.activeInHierarchy || !selectable.IsActive() || !selectable.IsInteractable()) return false;

        // 会話チュートリアル中のロック（blocksRaycasts=false）も「押せない」とみなす
        var t = selectable.transform;
        while (t != null)
        {
            if (t.TryGetComponent<CanvasGroup>(out var group) && group.enabled)
            {
                if (!group.blocksRaycasts || !group.interactable) return false;
                if (group.ignoreParentGroups) break;
            }
            t = t.parent;
        }

        var canvas = selectable.GetComponentInParent<Canvas>();
        return canvas == null || canvas.enabled;
    }

    /// <summary>
    /// ボタンを「押す」。押し込み表示（pointerDown → 待ち → pointerUp）を見せてから onClick.Invoke する。
    /// 押せない状態なら何もしないで false。
    /// </summary>
    public static async UniTask<bool> PressAsync(Button button, float holdSeconds, CancellationToken ct)
    {
        if (!IsClickable(button)) return false;

        var es = EventSystem.current;
        PointerEventData ped = null;
        if (es != null && holdSeconds > 0f)
        {
            ped = new PointerEventData(es)
            {
                button = PointerEventData.InputButton.Left,
                position = ScreenPositionOf(button.transform as RectTransform),
            };
            ExecuteEvents.Execute(button.gameObject, ped, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(button.gameObject, ped, ExecuteEvents.pointerDownHandler);
            await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            if (button == null) return false;
        }

        // 押し込み中に状態が変わった（画面が閉じた等）なら押さない
        bool stillClickable = IsClickable(button);
        if (ped != null)
        {
            ExecuteEvents.Execute(button.gameObject, ped, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, ped, ExecuteEvents.pointerExitHandler);
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == button.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }
        if (!stillClickable) return false;

        button.onClick.Invoke();
        return true;
    }

    /// <summary>
    /// Button ではない UI（IPointerClickHandler を実装したスロット等）をクリックする。
    /// pointerEnter → down → 待ち → up → click → exit の順にイベントを送る。
    /// </summary>
    public static async UniTask<bool> PointerClickAsync(GameObject target, float holdSeconds, CancellationToken ct)
    {
        if (target == null || !target.activeInHierarchy) return false;
        var es = EventSystem.current;
        if (es == null) return false;

        var ped = new PointerEventData(es)
        {
            button = PointerEventData.InputButton.Left,
            position = ScreenPositionOf(target.transform as RectTransform),
            clickCount = 1,
        };
        ped.pressPosition = ped.position;
        ped.pointerPress = target;
        ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerDownHandler);
        if (holdSeconds > 0f)
            await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        if (target == null || !target.activeInHierarchy) return false;
        ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerExitHandler);
        return true;
    }

    /// <summary>
    /// ドラッグ＆ドロップを再現する（beginDrag → drag を数回 → 相手に drop → endDrag）。
    /// ゴーストアイコン等の見た目は対象側の実装（ItemSlotView など）がそのまま出す。
    /// </summary>
    public static async UniTask<bool> DragAsync(GameObject from, GameObject to, float seconds, CancellationToken ct)
    {
        if (from == null || to == null || !from.activeInHierarchy || !to.activeInHierarchy) return false;
        var es = EventSystem.current;
        if (es == null) return false;

        Vector2 start = ScreenPositionOf(from.transform as RectTransform);
        Vector2 end = ScreenPositionOf(to.transform as RectTransform);
        var ped = new PointerEventData(es)
        {
            button = PointerEventData.InputButton.Left,
            position = start,
            pressPosition = start,
            pointerPress = from,
            pointerDrag = from,
            dragging = true,
        };

        ExecuteEvents.Execute(from, ped, ExecuteEvents.beginDragHandler);
        const int steps = 12;
        float stepWait = Mathf.Max(0.01f, seconds / steps);
        try
        {
            for (int i = 1; i <= steps; i++)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(stepWait), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                if (from == null) return false;
                ped.position = Vector2.Lerp(start, end, Mathf.SmoothStep(0f, 1f, (float)i / steps));
                ExecuteEvents.Execute(from, ped, ExecuteEvents.dragHandler);
            }
            if (to != null && to.activeInHierarchy)
                ExecuteEvents.Execute(to, ped, ExecuteEvents.dropHandler);
        }
        finally
        {
            if (from != null)
            {
                ped.dragging = false;
                ExecuteEvents.Execute(from, ped, ExecuteEvents.endDragHandler);
            }
        }
        return true;
    }

    private static Vector2 ScreenPositionOf(RectTransform rt)
    {
        if (rt == null) return Vector2.zero;
        var canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
    }

    /// <summary>ボタンの表示ラベル（TMP / uGUI Text）。無ければ空。</summary>
    public static string LabelOf(Button button)
    {
        if (button == null) return "";
        var tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return tmp.text;
        var text = button.GetComponentInChildren<Text>(true);
        return text != null ? text.text ?? "" : "";
    }

    // =====================================================
    // 詰まり復帰用の汎用「閉じる / 次へ」
    // =====================================================

    private static readonly string[][] GenericKeywordsByPriority =
    {
        new[] { "閉じる", "とじる", "close", "×" },
        new[] { "次へ", "進む", "next", "続ける", "スキップ", "skip" },
        new[] { "ok", "確認", "決定", "confirm", "了解" },
        new[] { "戻る", "back", "return" },
    };

    /// <summary>押すと取り返しがつかない／ランが終わる系は汎用復帰では押さない。</summary>
    private static readonly string[] GenericDenyKeywords =
    {
        "タイトル", "title", "削除", "delete", "上書き", "quit", "終了", "retry", "リトライ", "もう一度",
        "購入", "buy", "売", "sell", "補充", "投資", "拡張", "建設", "debug", "デバッグ", "支払", "pay",
    };

    /// <summary>
    /// 画面上の押せるボタンから「閉じる/次へ/OK/戻る」らしいものを1つ選ぶ（最前面のキャンバス優先）。無ければ null。
    /// </summary>
    public static Button FindGenericAdvanceButton(out string label)
    {
        label = null;
        Button best = null;
        int bestPriority = int.MaxValue;
        int bestOrder = int.MinValue;

        var buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var b in buttons)
        {
            if (!IsClickable(b)) continue;
            string text = (b.gameObject.name + " " + LabelOf(b)).ToLowerInvariant();
            if (ContainsAny(text, GenericDenyKeywords)) continue;

            int priority = -1;
            for (int p = 0; p < GenericKeywordsByPriority.Length; p++)
            {
                if (ContainsAny(text, GenericKeywordsByPriority[p])) { priority = p; break; }
            }
            if (priority < 0) continue;

            var canvas = b.GetComponentInParent<Canvas>();
            int order = canvas != null ? canvas.rootCanvas.sortingOrder : 0;
            if (priority < bestPriority || (priority == bestPriority && order > bestOrder))
            {
                best = b;
                bestPriority = priority;
                bestOrder = order;
            }
        }

        if (best != null) label = $"{best.gameObject.name}「{LabelOf(best)}」";
        return best;
    }

    private static bool ContainsAny(string text, string[] keywords)
    {
        foreach (var k in keywords)
        {
            if (text.Contains(k.ToLowerInvariant())) return true;
        }
        return false;
    }

    // =====================================================
    // DI（各シーンの LifetimeScope から解決。DebugMenu と同じく無ければ null）
    // =====================================================

    private static readonly Dictionary<Type, object> ResolveCache = new();
    private static readonly Dictionary<Type, float> ResolveMissUntil = new();
    private const float ResolveMissRetrySeconds = 0.5f;
    private static int _cacheSceneHandle = -1;
    private static int _cacheSceneCount = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ResolveCache.Clear();
        ResolveMissUntil.Clear();
        _cacheSceneHandle = -1;
        _cacheSceneCount = -1;
    }

    /// <summary>
    /// ロード済みの LifetimeScope から T を探す（シーンが変わるまでキャッシュ。見つからなければ毎回探し直す）。
    /// Singleton 登録の Model / Manager / View 向け。Transient 登録の型には使わないこと（新規生成されるため）。
    /// </summary>
    public static T Resolve<T>() where T : class
    {
        var active = SceneManager.GetActiveScene();
        if (active.handle != _cacheSceneHandle || SceneManager.sceneCount != _cacheSceneCount)
        {
            ResolveCache.Clear();
            ResolveMissUntil.Clear();
            _cacheSceneHandle = active.handle;
            _cacheSceneCount = SceneManager.sceneCount;
        }

        if (ResolveCache.TryGetValue(typeof(T), out var cached))
        {
            if (cached is UnityEngine.Object uo) { if (uo != null) return (T)cached; }
            else if (cached != null) return (T)cached;
            ResolveCache.Remove(typeof(T));
        }

        // 見つからなかった型は少し間を置いてから探し直す（毎フレームの全スコープ走査を避ける）
        if (ResolveMissUntil.TryGetValue(typeof(T), out var retryAt) && Time.unscaledTime < retryAt) return null;

        var scopes = UnityEngine.Object.FindObjectsByType<LifetimeScope>(FindObjectsSortMode.None);
        foreach (var scope in scopes)
        {
            if (scope == null || scope.Container == null) continue;
            try
            {
                if (scope.Container.TryResolve<T>(out var value) && value != null)
                {
                    ResolveCache[typeof(T)] = value;
                    return value;
                }
            }
            catch (Exception)
            {
                // 解決途中の例外（未初期化の依存など）は「無い」扱い
            }
        }
        ResolveMissUntil[typeof(T)] = Time.unscaledTime + ResolveMissRetrySeconds;
        return null;
    }

    // =====================================================
    // リフレクション（既存 View の SerializeField を読むだけ）
    // =====================================================

    private static readonly Dictionary<(Type, string), FieldInfo> FieldCache = new();
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>インスタンスフィールドを読む（参照型）。無い・破棄済みなら null。</summary>
    public static T GetField<T>(object target, string name) where T : class
    {
        var value = GetFieldValue(target, name) as T;
        return IsAlive(value) ? value : null;
    }

    /// <summary>インスタンスフィールドを読む（値型も可）。無ければ null。</summary>
    public static object GetFieldValue(object target, string name)
    {
        if (target == null) return null;
        if (target is UnityEngine.Object uo && uo == null) return null;

        var type = target.GetType();
        var key = (type, name);
        if (!FieldCache.TryGetValue(key, out var field))
        {
            for (var t = type; t != null && field == null; t = t.BaseType)
                field = t.GetField(name, AnyInstance | BindingFlags.DeclaredOnly);
            FieldCache[key] = field;
        }
        return field?.GetValue(target);
    }

    private static bool IsAlive(object o) => o != null && !(o is UnityEngine.Object uo && uo == null);

    /// <summary>GameObject 型フィールドが表示中か。</summary>
    public static bool IsActive(object target, string gameObjectField)
    {
        var go = GetField<GameObject>(target, gameObjectField);
        return go != null && go.activeInHierarchy;
    }
}
#endif

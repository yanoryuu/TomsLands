using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// IPointerEnter/Exit/Click しか持たない部品（鍛冶屋スロット・ホバー説明など）をパッドでも動かす橋渡し。
///
/// ・パッド/キーで選択された → PointerEnter を転送（ホバー説明が出る）
/// ・選択が外れた           → PointerExit を転送
/// ・決定(A/Enter)         → PointerDown → PointerUp → PointerClick を転送
///
/// マウス操作中は何もしない（本物のポインタイベントと二重にならないように）。
/// Selectable が無ければ Awake で自動付与する（Transition=None, Navigation=Automatic）。
/// 転送先は targets（未指定なら自分）。転送先の GameObject 上のハンドラだけを呼ぶ。
/// </summary>
[DisallowMultipleComponent]
public sealed class PointerEventBridge : MonoBehaviour, ISelectHandler, IDeselectHandler, ISubmitHandler
{
    [Tooltip("転送先。空なら自分自身。")]
    [SerializeField] private List<GameObject> targets = new();
    [SerializeField] private bool forwardEnterExit = true;
    [SerializeField] private bool forwardClick = true;
    [Tooltip("ON: クリック時に PointerDown/Up も送る（押下で反応する部品向け）")]
    [SerializeField] private bool forwardDownUp = true;

    private bool entered;

    private void Awake()
    {
        if (GetComponent<Selectable>() == null)
        {
            var s = gameObject.AddComponent<Selectable>();
            s.transition = Selectable.Transition.None;
            var nav = s.navigation;
            nav.mode = Navigation.Mode.Automatic;
            s.navigation = nav;
        }
    }

    private void OnDisable()
    {
        if (entered) Forward(ExecuteEvents.pointerExitHandler);
        entered = false;
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (!forwardEnterExit || !ControllerInput.IsNavigating.CurrentValue) return;
        entered = true;
        Forward(ExecuteEvents.pointerEnterHandler);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        if (!entered) return;
        entered = false;
        Forward(ExecuteEvents.pointerExitHandler);
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (!forwardClick) return;
        var sel = GetComponent<Selectable>();
        if (sel != null && !sel.IsInteractable()) return;
        if (forwardDownUp) Forward(ExecuteEvents.pointerDownHandler);
        if (forwardDownUp) Forward(ExecuteEvents.pointerUpHandler);
        Forward(ExecuteEvents.pointerClickHandler);
    }

    private void Forward<T>(ExecuteEvents.EventFunction<T> func) where T : IEventSystemHandler
    {
        var es = EventSystem.current;
        if (es == null) return;
        var data = CreatePointerData(es);
        if (targets == null || targets.Count == 0)
        {
            ExecuteOn(gameObject, data, func);
            return;
        }
        foreach (var t in targets)
            if (t != null) ExecuteOn(t, data, func);
    }

    private void ExecuteOn<T>(GameObject target, PointerEventData data, ExecuteEvents.EventFunction<T> func) where T : IEventSystemHandler
    {
        // 自分に Button/Toggle 等の Submit 処理が既にあるなら、クリック系は二重発火になるので送らない
        if (target == gameObject && typeof(T) != typeof(IPointerEnterHandler) && typeof(T) != typeof(IPointerExitHandler)
            && HasOtherSubmitHandler())
            return;
        data.pointerEnter = target;
        data.pointerPress = target;
        data.rawPointerPress = target;
        data.pointerClick = target;
        ExecuteEvents.Execute(target, data, func);
    }

    private bool HasOtherSubmitHandler()
    {
        foreach (var h in GetComponents<ISubmitHandler>())
            if (!ReferenceEquals(h, this)) return true;
        return false;
    }

    private PointerEventData CreatePointerData(EventSystem es)
    {
        var data = new PointerEventData(es)
        {
            button = PointerEventData.InputButton.Left,
            clickCount = 1,
            clickTime = Time.unscaledTime,
            eligibleForClick = true,
        };
        var rt = transform as RectTransform;
        if (rt != null)
        {
            var canvas = GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.rootCanvas.worldCamera;
            var sp = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
            data.position = sp;
            data.pressPosition = sp;
            var rr = new RaycastResult { gameObject = gameObject, screenPosition = sp, module = null };
            data.pointerCurrentRaycast = rr;
            data.pointerPressRaycast = rr;
        }
        return data;
    }
}

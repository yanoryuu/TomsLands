using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ホバー中（マウス）とパッド/キーで選択中だけ、ボタンの絵を「-1」版（明るさ・色の違う差分素材）に差し替える。
/// 押下は Button の ColorTint（色が少し暗くなる）に任せ、絵の差し替えはしない（押したときに大きさが変わって見えないように）。
/// RectTransform は動かさないので、パッドの選択枠（ControllerHighlight）とずれない。
/// マウスでクリックした後に残る「選択中」では差し替えない（ホバーが外れたら元の絵に戻る）。
/// コードが元の絵を差し替えた場合は、それを新しい元の絵として扱う。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public class ButtonHoverSprite : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Tooltip("差し替える Image（空なら Selectable の targetGraphic）")]
    [SerializeField] private Image target;
    [Tooltip("ホバー・選択中の絵")]
    [SerializeField] private Sprite hoverSprite;

    private Selectable selectable;
    private Sprite normalSprite;
    private bool hovering;
    private bool selected;
    private bool showingHover;

    public Sprite HoverSprite { get => hoverSprite; set { hoverSprite = value; Apply(); } }

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
        if (target == null && selectable != null) target = selectable.targetGraphic as Image;
        if (target != null) normalSprite = target.sprite;
    }

    private void OnEnable()
    {
        hovering = false;
        var es = EventSystem.current;
        selected = es != null && es.currentSelectedGameObject == gameObject;
        Apply();
    }

    private void OnDisable()
    {
        hovering = false;
        selected = false;
        Apply();
    }

    public void OnPointerEnter(PointerEventData eventData) { hovering = true; Apply(); }
    public void OnPointerExit(PointerEventData eventData) { hovering = false; Apply(); }
    public void OnSelect(BaseEventData eventData) { selected = true; Apply(); }
    public void OnDeselect(BaseEventData eventData) { selected = false; Apply(); }

    private void LateUpdate()
    {
        // 押せなくなった・入力機器が変わった（パッド→マウス）ときに戻す
        if (showingHover != ShouldShowHover()) Apply();
    }

    private bool ShouldShowHover()
    {
        if (hoverSprite == null || target == null || !isActiveAndEnabled) return false;
        if (selectable != null && !selectable.IsInteractable()) return false;
        return hovering || (selected && ControllerInput.IsNavigating.CurrentValue);
    }

    private void Apply()
    {
        if (target == null) return;
        // コード側が絵を替えていたら（ホバー絵以外）、それを元の絵として覚え直す
        if (target.sprite != hoverSprite) normalSprite = target.sprite;

        bool show = ShouldShowHover();
        showingHover = show;
        var sprite = show ? hoverSprite : normalSprite;
        if (sprite != null && target.sprite != sprite) target.sprite = sprite;
    }
}

using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 右パネルのアイテムスロット。ドラッグ可能にする。
/// </summary>
[RequireComponent(typeof(CanvasGroup), typeof(RectTransform))]
public class DraggableItemSlot : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private UnityEngine.UI.Image icon;
    public string ItemId { get; private set; }

    private CanvasGroup _canvasGroup;
    private RectTransform _rectTransform;
    private Transform _originalParent;

    /// <summary>ItemId とアイコンを設定。</summary>
    public void Initialize(string itemId, Sprite sprite)
    {
        ItemId = itemId;
        icon.sprite = sprite;
    }

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _rectTransform = GetComponent<RectTransform>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _originalParent = transform.parent;
        transform.SetParent(transform.root); 
        _canvasGroup.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        _rectTransform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        transform.SetParent(_originalParent);
        _canvasGroup.blocksRaycasts = true;
    }
}
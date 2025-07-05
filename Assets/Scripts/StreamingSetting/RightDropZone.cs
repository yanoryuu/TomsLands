using UnityEngine;
using UnityEngine.EventSystems;
using System;

/// <summary>
/// 左パネルのドロップエリア。DraggableItemSlot のドロップを受け付ける。
/// </summary>
public class RightDropZone : MonoBehaviour, IDropHandler
{
    /// <summary>アイテムID がドロップされたとき発火</summary>
    public event Action<string> OnItemDropped;

    public void OnDrop(PointerEventData eventData)
    {
        var slot = eventData.pointerDrag?.GetComponent<DraggableItemSlot>();
        if (slot != null)
        {
            OnItemDropped?.Invoke(slot.ItemId);
        }
    }
}
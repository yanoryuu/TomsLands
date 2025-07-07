using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using TMPro;
public class AvailableItemSlot : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    public string ItemId { get; private set; }

    public event Action<string> OnItemSelected;

    /// <summary>アイテムID、アイコン、表示名を設定。</summary>
    public void Initialize(string itemId, Sprite sprite, string displayName)
    {
        ItemId = itemId;
        icon.sprite = sprite;
        if (nameText != null) nameText.text = displayName;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            OnItemSelected?.Invoke(ItemId);
    }
}
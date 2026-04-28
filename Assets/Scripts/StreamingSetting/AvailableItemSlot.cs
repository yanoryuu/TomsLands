using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using TMPro;

public class AvailableItemSlot : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private TextMeshProUGUI attributeText;

    public string ItemId { get; private set; }

    public event Action<string> OnItemSelected;

    /// <summary>アイテムID、アイコン、表示名、在庫数、属性を設定。</summary>
    public void Initialize(string itemId, Sprite sprite, string displayName, int stock, ItemTypeData.ItemAttribute attribute)
    {
        ItemId = itemId;
        icon.sprite = sprite;
        if (nameText != null) nameText.text = displayName;
        if (stockText != null) stockText.text = $"在庫: {stock}";
        if (attributeText != null) attributeText.text = AttributeToJapanese(attribute);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            OnItemSelected?.Invoke(ItemId);
    }

    private static string AttributeToJapanese(ItemTypeData.ItemAttribute attribute) => attribute switch
    {
        ItemTypeData.ItemAttribute.Fire  => "炎",
        ItemTypeData.ItemAttribute.Water => "水",
        ItemTypeData.ItemAttribute.Earth => "土",
        ItemTypeData.ItemAttribute.Wind  => "風",
        ItemTypeData.ItemAttribute.Light => "光",
        ItemTypeData.ItemAttribute.Dark  => "闇",
        _ => "—"
    };
}
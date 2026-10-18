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

    private void Awake()
    {
        // パッド/キーで選べるようにする（A で左クリックと同じ OnPointerClick を転送。マウス時は何もしない）
        if (!TryGetComponent<PointerEventBridge>(out _)) gameObject.AddComponent<PointerEventBridge>();
        // 選択枠はカードの見た目（BackGround）に合わせる（ルートの矩形はレイアウト用で見た目とずれている）
        var background = transform.Find("BackGround") as RectTransform;
        if (background != null && GetComponent<UIFocusHighlightOverride>() == null)
            gameObject.AddComponent<UIFocusHighlightOverride>().target = background;
    }

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
        ItemTypeData.ItemAttribute.Fire  => "火",
        ItemTypeData.ItemAttribute.Water => "水",
        ItemTypeData.ItemAttribute.Wood  => "木",
        ItemTypeData.ItemAttribute.Light => "光",
        ItemTypeData.ItemAttribute.Dark  => "闇",
        _ => "—"
    };
}
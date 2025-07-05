using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using R3;
using TMPro;

/// <summary>
/// 選択済みアイテムスロット。
/// スライダーで数量を変更し、右クリックで解除を通知します。
/// </summary>
public class SelectedItemSlot : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI amountText;
    [SerializeField] private Slider qtySlider;

    private string _itemId;

    /// <summary>数量変更時：(itemId, quantity)</summary>
    public event Action<string, int> OnQuantityChanged;
    /// <summary>右クリック解除時：itemId</summary>
    public event Action<string> OnItemDeselected;

    /// <summary>
    /// 初期化。アイコン、表示名、初期数量、在庫上限を設定。
    /// </summary>
    public void Initialize(string itemId, Sprite sprite, string displayName, int initialQty, int maxQty)
    {
        _itemId = itemId;
        icon.sprite = sprite;
        if (nameText != null) nameText.text = displayName;

        // スライダー設定
        qtySlider.wholeNumbers = true;
        qtySlider.minValue = 1;
        qtySlider.maxValue = maxQty;
        qtySlider.value = Mathf.Clamp(initialQty, 1, maxQty);

        // 初期表示
        if (amountText != null)
            amountText.text = qtySlider.value.ToString();

        // 値変更時のイベント登録
        qtySlider.onValueChanged.AddListener(v =>
        {
            int qty = Mathf.FloorToInt(v);
            if (amountText != null)
                amountText.text = qty.ToString();
            OnQuantityChanged?.Invoke(_itemId, qty);
        });
    }

    /// <summary>
    /// 右クリックでこのスロットを解除（リストから削除）します。
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
            OnItemDeselected?.Invoke(_itemId);
    }
}
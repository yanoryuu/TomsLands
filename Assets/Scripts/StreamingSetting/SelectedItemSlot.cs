using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using R3;
using TMPro;

public class SelectedItemSlot : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI amountText;
    [SerializeField] private Slider qtySlider;

    [Header("Step Buttons")]
    [SerializeField] private Button plusButton;
    [SerializeField] private Button minusButton;

    private string _itemId;
    private bool _suppress;

    /// <summary>数量変更時：(itemId, quantity)</summary>
    public event Action<string, int> OnQuantityChanged;
    /// <summary>右クリック解除時：itemId</summary>
    public event Action<string> OnItemDeselected;

    /// <summary>
    /// 初期化。アイコン、表示名、初期数量、在庫上限を設定。
    /// </summary>
    public void Initialize(string itemId, Sprite sprite, Sprite background, string displayName, int initialQty, int maxQty)
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

        // スライダー値変更時のイベント登録
        qtySlider.onValueChanged.AddListener(v =>
        {
            if (_suppress) return;
            int qty = Mathf.FloorToInt(v);
            if (amountText != null)
                amountText.text = qty.ToString();
            OnQuantityChanged?.Invoke(_itemId, qty);
            UpdateButtonsInteractable();
        });

        // +/-ボタン
        if (plusButton) plusButton.onClick.AddListener(() => Step(+1));
        if (minusButton) minusButton.onClick.AddListener(() => Step(-1));

        UpdateButtonsInteractable();
    }

    private void Step(int delta)
    {
        int next = Mathf.Clamp(Mathf.RoundToInt(qtySlider.value) + delta, (int)qtySlider.minValue, (int)qtySlider.maxValue);
        _suppress = true;
        qtySlider.value = next;
        _suppress = false;
        if (amountText != null) amountText.text = next.ToString();
        OnQuantityChanged?.Invoke(_itemId, next);
        UpdateButtonsInteractable();
    }

    private void UpdateButtonsInteractable()
    {
        int current = Mathf.RoundToInt(qtySlider.value);
        if (plusButton) plusButton.interactable = current < (int)qtySlider.maxValue;
        if (minusButton) minusButton.interactable = current > (int)qtySlider.minValue;
    }

    /// <summary>
    /// 右クリックでこのスロットを解除（リストから削除）します。
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
            OnItemDeselected?.Invoke(_itemId);
    }

    private void OnDestroy()
    {
        qtySlider?.onValueChanged.RemoveAllListeners();
        plusButton?.onClick.RemoveAllListeners();
        minusButton?.onClick.RemoveAllListeners();
    }
}
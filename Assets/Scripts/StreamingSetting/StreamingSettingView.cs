using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using R3;

public class StreamingSettingView : MonoBehaviour
{
    [SerializeField] private Transform availablePanel;
    [SerializeField] private Transform selectedPanel;
    [SerializeField] private GameObject availableSlotPrefab;
    [SerializeField] private GameObject selectedSlotPrefab;

    [Header("確定ボタン")]
    [SerializeField] private Button confirmButton;

    public Subject<string> OnItemSelected     { get; } = new Subject<string>();
    public Subject<string> OnItemDeselected   { get; } = new Subject<string>();
    public Subject<(string id, int qty)> OnQuantityChanged { get; } = new Subject<(string, int)>();
    public Subject<Unit> OnConfirmClicked     { get; } = new Subject<Unit>();

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(() => OnConfirmClicked.OnNext(Unit.Default));
        }
    }

    public void PopulateAvailable(IEnumerable<RuntimeItemData> items)
    {
        foreach (Transform t in availablePanel) Destroy(t.gameObject);
        foreach (var item in items)
        {
            // 所持数が0以下のアイテムは表示しない
            if (item.Stock.Value <= 0) continue;

            var go   = Instantiate(availableSlotPrefab, availablePanel);
            var slot = go.GetComponent<AvailableItemSlot>();
            slot.Initialize(item.ItemId, item.ItemIcon, item.ItemName, item.Stock.Value, item.ItemAttribute);
            slot.OnItemSelected += id => OnItemSelected.OnNext(id);
        }
    }

    public void PopulateSelected(IEnumerable<KeyValuePair<string, int>> selected, ItemModel model)
    {
        foreach (Transform t in selectedPanel) Destroy(t.gameObject);

        foreach (var kv in selected)
        {
            var id     = kv.Key;
            var qty    = kv.Value;
            var master = model.GetMasterItem(id);
            var runtime= model.GetRuntimeItem(id);
            if (master == null || runtime == null) 
                continue;

            // ここで在庫上限を取得
            int maxQty = runtime.Stock.Value;

            var go   = Instantiate(selectedSlotPrefab, selectedPanel);
            var slot = go.GetComponent<SelectedItemSlot>();
            slot.Initialize(
                id,
                master.itemIcon,
                runtime.ItemBackground,
                master.itemName,
                qty,
                maxQty
            );
            slot.OnQuantityChanged += (itemId, newQty) => 
                OnQuantityChanged.OnNext((itemId, newQty));
            slot.OnItemDeselected += itemId => 
                OnItemDeselected.OnNext(itemId);
        }
    }

    /// <summary>
    /// 個別追加用。（必要であればこちらも同様に maxQty を渡す）
    /// </summary>
    public void AddSelectedItem(string id, Sprite icon, Sprite background, string name, int qty, int maxQty)
    {
        var go   = Instantiate(selectedSlotPrefab, selectedPanel);
        var slot = go.GetComponent<SelectedItemSlot>();
        slot.Initialize(id, icon, background, name, qty, maxQty);
        slot.OnQuantityChanged += (itemId, newQty) => 
            OnQuantityChanged.OnNext((itemId, newQty));
        slot.OnItemDeselected += itemId => 
            OnItemDeselected.OnNext(itemId);
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}

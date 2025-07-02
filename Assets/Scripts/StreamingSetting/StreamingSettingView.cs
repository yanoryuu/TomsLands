using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using R3;

/// <summary>
/// 配信設定画面の View。右パネルに利用可能アイテム、左パネルに選択済みアイテムを表示。
/// </summary>
public class StreamingSettingView : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private Transform rightPanel;       // 利用可能アイテム
    [SerializeField] private LeftDropZone leftDropZone;  // ドロップエリア
    [SerializeField] private Transform leftPanel;        // 選択済みアイテム
    [SerializeField] private Button confirmButton;

    [Header("Prefabs")]
    [SerializeField] private GameObject draggableSlotPrefab;
    [SerializeField] private GameObject selectedSlotPrefab;

    public Subject<string> OnItemDropped { get; } = new Subject<string>();
    public Subject<(string id, int qty)> OnQuantityChanged { get; } = new Subject<(string, int)>();
    public Subject<string> OnItemRemoved { get; } = new Subject<string>();
    
    public Subject<Unit> OnConfirmClicked { get; } = new Subject<Unit>();

    private void Awake()
    {
        leftDropZone.OnItemDropped += id => OnItemDropped.OnNext(id);
        confirmButton.onClick.AddListener(() => OnConfirmClicked.OnNext(Unit.Default));
    }

    /// <summary>右パネルのアイテムを全件表示</summary>
    public void PopulateAvailableItems(IEnumerable<RuntimeItemData> items)
    {
        foreach (Transform t in rightPanel) Destroy(t.gameObject);
        foreach (var item in items)
        {
            var go = Instantiate(draggableSlotPrefab, rightPanel);
            var slot = go.GetComponent<DraggableItemSlot>();
            slot.Initialize(item.ItemId, item.ItemIcon);
        }
    }

    /// <summary>左パネルに選択スロットを追加</summary>
    public void AddSelectedItem(string id, Sprite icon, string name)
    {
        var go = Instantiate(selectedSlotPrefab, leftPanel);
        var slot = go.GetComponent<SelectedItemSlot>();
        slot.Initialize(id, icon, name);
        slot.OnQuantityChanged += (itemId, qty) => OnQuantityChanged.OnNext((itemId, qty));
        slot.OnRemoved         += itemId => OnItemRemoved.OnNext(itemId);
    }
}
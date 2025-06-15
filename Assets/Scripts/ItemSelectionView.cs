using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using R3;

public class ItemSelectionView : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform itemListParent;                   // スロットを並べる親
    [SerializeField] private GameObject itemSelectionSlotPrefab;         // スロットプレハブ
    [SerializeField] private Button confirmButton;                       // 確定ボタン
    [SerializeField] private Button closeButton;

    public Subject<Dictionary<string, int>> OnConfirmSelection { get; } = new();
    public Subject<Unit> OnCloseRequested { get; } = new();

    private readonly Dictionary<string, int> selectedItems = new();
    private readonly List<GameObject> activeSlots = new();

    private void Awake()
    {
        confirmButton.onClick.AddListener(OnConfirmButtonClicked);
        
        closeButton.onClick.AddListener(() =>OnCloseRequested.OnNext(Unit.Default));
    }

    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 商品リストを表示
    /// </summary>
    public void PopulateItemList(List<RuntimeItemData> runtimeItems)
    {
        // 既存スロットを削除
        foreach (var slotObj in activeSlots)
        {
            Destroy(slotObj);
        }
        activeSlots.Clear();
        selectedItems.Clear();

        foreach (var item in runtimeItems)
        {
            var slotObj = Instantiate(itemSelectionSlotPrefab, itemListParent);
            var slot = slotObj.GetComponent<ItemSelectionSlot>();

            slot.SetItem(
                item.ItemId,
                item.ItemIcon,
                item.ItemId,  // ItemName があればそちらでもOK
                item.CurrentPrice.Value,
                item.Stock.Value
            );

            // Toggle変更
            slot.OnToggleChanged
                .Subscribe(isOn =>
                {
                    if (isOn)
                    {
                        if (!selectedItems.ContainsKey(item.ItemId))
                        {
                            selectedItems[item.ItemId] = 1; // デフォルト1
                        }
                    }
                    else
                    {
                        selectedItems.Remove(item.ItemId);
                    }
                })
                .AddTo(this);

            // 数量変更
            slot.OnQuantityChanged
                .Subscribe(quantity =>
                {
                    if (selectedItems.ContainsKey(item.ItemId))
                    {
                        selectedItems[item.ItemId] = quantity;
                    }
                })
                .AddTo(this);

            activeSlots.Add(slotObj);
        }
    }

    private void OnConfirmButtonClicked()
    {
        if (selectedItems.Count > 0)
        {
            OnConfirmSelection.OnNext(new Dictionary<string, int>(selectedItems));
        }
        else
        {
            Debug.LogWarning("商品が選択されていません！");
        }
    }
}

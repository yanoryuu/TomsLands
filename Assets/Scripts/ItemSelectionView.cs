using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using R3;

public class ItemSelectionView : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform itemListParent;
    [SerializeField] private GameObject itemSelectionSlotPrefab;
    [SerializeField] private Button confirmButton;

    // イベント（Presenterに通知）
    public Subject<Dictionary<string, int>> OnConfirmSelection { get; } = new();

    // 内部データ
    private readonly Dictionary<string, int> selectedItems = new();

    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    // 商品リストを表示する
    public void SelectionItemList(List<RuntimeItemData> runtimeItems)
    {
        // 既存リスト削除
        foreach (Transform child in itemListParent)
        {
            Destroy(child.gameObject);
        }

        // 商品を生成
        foreach (var item in runtimeItems)
        {
            GameObject slotObj = Instantiate(itemSelectionSlotPrefab, itemListParent);
            var slot = slotObj.GetComponent<ItemSelectionSlot>();

            slot.SetItem(
                item.ItemId,
                item.ItemIcon,
                item.ItemId,  // ここはitemNameでもOK
                item.CurrentPrice.Value,
                item.Stock.Value
            );

            // Toggleの変化を購読
            slot.OnToggleChanged
                .Subscribe(isOn =>
                {
                    if (!isOn)
                    {
                        selectedItems.Remove(item.ItemId);
                    }
                })
                .AddTo(this);

            // Quantity変更を購読
            slot.OnQuantityChanged
                .Subscribe(quantity =>
                {
                    if (selectedItems.ContainsKey(item.ItemId))
                    {
                        selectedItems[item.ItemId] = quantity;
                    }
                })
                .AddTo(this);

            // Toggle初期化（選択状態なら追加）
            slot.OnToggleChanged
                .Where(isOn => isOn)
                .Subscribe(_ =>
                {
                    if (!selectedItems.ContainsKey(item.ItemId))
                    {
                        selectedItems[item.ItemId] = 1; // デフォルト1
                    }
                })
                .AddTo(this);
        }
    }

    private void Awake()
    {
        confirmButton.onClick.AddListener(OnConfirmButtonClicked);
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

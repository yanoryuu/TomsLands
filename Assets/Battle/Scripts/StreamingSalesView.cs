using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class StreamingSalesView : MonoBehaviour
{
    [SerializeField] private TMP_Text totalSalesText;
    [SerializeField] private List<ItemSlotView> itemSlots;

    public void UpdateTotalSales(int amount)
    {
        if (totalSalesText != null)
            totalSalesText.text = $"{amount} G";
    }

    /// <summary>
    /// 売り場の商品を初期表示
    /// </summary>
    public void DisplayItems(List<RuntimeItemData> items)
    {

        Debug.Log($"【StreamingSalesView】DisplayItemsが呼ばれました。現在商品数: {items.Count}");
        for (int i = 0; i < itemSlots.Count; i++)
        {
            if (i < items.Count)
            {
                var item = items[i];
                itemSlots[i].SetItem(item);
            }
            else
            {
                itemSlots[i].SetItem(null);
            }
        }
    }

    /// <summary>
    /// 特定のスロットの在庫表示だけを更新
    /// </summary>
    public void UpdateItemStock(int slotIndex, int newStock)
    {
        if (slotIndex < 0 || slotIndex >= itemSlots.Count) return;
        itemSlots[slotIndex].UpdateStock(newStock);
    }

    public int GetSlotIndex(ItemSlotView slot)
    {
        return itemSlots.IndexOf(slot);
    }

    /// <summary>
    /// 特定のスロットで、売れたアニメーションを再生
    /// </summary>
    public void PlaySoldAnimation(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= itemSlots.Count) return;
        itemSlots[slotIndex].PlaySoldAnimation();
    }
}

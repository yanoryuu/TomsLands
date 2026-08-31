using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ShopDeskDisplay : MonoBehaviour
{
    // 机スプライトの見た目上の上限。店レベルの陳列上限(最大12銘柄)より大きいので通常は間引かれない。
    [SerializeField] private int maxSlots = 16;

    [SerializeField] private SpriteRenderer[] itemSlots;

    public void RefreshDisplay(List<RuntimeItemData> runtimeItems)
    {
        var displayItems = runtimeItems
            .Where(r => r.IsDisplay.Value && r.Stock.Value > 0)
            .ToList();

        if (displayItems.Count > maxSlots)
        {
            displayItems = displayItems
                .OrderBy(_ => Random.value)
                .Take(maxSlots)
                .ToList();
        }

        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] == null) continue;

            if (i < displayItems.Count)
            {
                itemSlots[i].sprite = displayItems[i].ItemIcon;
                itemSlots[i].gameObject.SetActive(true);
            }
            else
            {
                itemSlots[i].gameObject.SetActive(false);
            }
        }
    }

    public void ClearDisplay()
    {
        foreach (var slot in itemSlots)
        {
            if (slot == null) continue;
            slot.gameObject.SetActive(false);
        }
    }
}

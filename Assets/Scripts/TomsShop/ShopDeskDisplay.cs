using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ShopDeskDisplay : MonoBehaviour
{
    private const int MaxSlots = 16;

    [SerializeField] private SpriteRenderer[] itemSlots;

    public void RefreshDisplay(List<RuntimeItemData> runtimeItems)
    {
        var displayItems = runtimeItems
            .Where(r => r.IsDisplay.Value && r.Stock.Value > 0)
            .ToList();

        if (displayItems.Count > MaxSlots)
        {
            displayItems = displayItems
                .OrderBy(_ => Random.value)
                .Take(MaxSlots)
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

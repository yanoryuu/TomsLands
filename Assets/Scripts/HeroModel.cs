using System.Collections.Generic;

public class HeroModel
{
    public List<string> EquippedItemIds { get; private set; } = new List<string>();

    public void EquipItem(string itemId)
    {
        if (!EquippedItemIds.Contains(itemId))
        {
            EquippedItemIds.Add(itemId);
        }
    }

    public void UnequipItem(string itemId)
    {
        EquippedItemIds.Remove(itemId);
    }

    public void ClearEquippedItems()
    {
        EquippedItemIds.Clear();
    }
}
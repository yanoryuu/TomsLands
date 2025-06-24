using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "Item Data")]
public class ItemData : ScriptableObject
{
    public string itemId;
    public string itemName;
    public int basePrice;
    public string description;
    public Sprite itemIcon;
    public int initialStock;
    public int maxStock = 100;
    public ItemTypeData.ItemType itemType;
<<<<<<< HEAD
    public ItemTypeData.ItemAttribute itemAttribute;
=======

>>>>>>> 5d29d51ac28a5e3e8a1e56ccbca708930499309a
}
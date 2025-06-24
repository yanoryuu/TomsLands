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
    public ItemTypeData.ItemType itemType;
    public ItemTypeData.ItemAttribute itemAttribute;
}
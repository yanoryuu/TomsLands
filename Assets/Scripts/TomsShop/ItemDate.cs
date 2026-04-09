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
    public int initialDisplayStock;
    public ItemTypeData.ItemType itemType;
    public ItemTypeData.ItemAttribute itemAttribute;
    public int requiredLevel = 1;

    [Header("売れやすさ")]
    [Tooltip("アイテム固有の売れやすさ倍率。1.0が標準。高いほど1ターンで多く売れる")]
    [Range(0.1f, 5.0f)]
    public float salesRate = 1.0f;
}
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
    public int maxStock = 99;
    public int initialDisplayStock;
    public ItemTypeData.ItemType itemType;
    public ItemTypeData.ItemAttribute itemAttribute;
    public int requiredLevel = 1;

    [Header("売れやすさ")]
    [Tooltip("アイテム固有の売れやすさ倍率。1.0が標準。高いほど1ターンで多く売れる")]
    [Range(0.1f, 5.0f)]
    public float salesRate = 1.0f;

    [Header("配当（配当付き武器）")]
    [Tooltip("在庫として1個保有するごとに毎ターン入る配当金。0で配当なし。" +
             "「売って儲ける」か「持ち続けて配当を得る」かのジレンマを作る。")]
    public int dividendPerTurn = 0;
}

using System;

/// <summary>
/// スプレッドシートで管理するアイテムマスターの上書き値（itemId 単位）。
/// Sprite 等のビジュアルは対象外（既存 ItemData(SO) のまま）。
/// enum はシート可読性のため文字列で持ち、適用時に Enum.TryParse する。
/// </summary>
[Serializable]
public class ItemOverride
{
    public string itemId;
    public string itemName;        // 空なら master 維持
    public int basePrice;
    public int initialStock;
    public int maxStock;
    public int initialDisplayStock;
    public string itemType;        // "Weapon" / "Armor" / "Tool"
    public string itemAttribute;   // "Fire" / "Water" / "Earth" / "Wind" / "Light" / "Dark"
    public int requiredLevel;
    public float salesRate;
    public string description;      // 空なら master 維持
    public int dividendPerTurn;     // 在庫1個あたりの毎ターン配当（0=配当なし）

    /// <summary>master のクローンへ上書きを反映する。Sprite 等は触らない。</summary>
    public void ApplyTo(ItemData target)
    {
        if (target == null) return;

        if (!string.IsNullOrEmpty(itemName)) target.itemName = itemName;
        target.basePrice = basePrice;
        target.initialStock = initialStock;
        target.maxStock = maxStock;
        target.initialDisplayStock = initialDisplayStock;
        if (Enum.TryParse<ItemTypeData.ItemType>(itemType, true, out var t)) target.itemType = t;
        if (Enum.TryParse<ItemTypeData.ItemAttribute>(itemAttribute, true, out var a)) target.itemAttribute = a;
        target.requiredLevel = requiredLevel;
        target.salesRate = salesRate;
        target.dividendPerTurn = dividendPerTurn;
        if (!string.IsNullOrEmpty(description)) target.description = description;
    }
}

/// <summary>
/// アイテムマスター配信のエンベロープ。version/schemaVersion 付きで items 配列を包む。
/// </summary>
[Serializable]
public class ItemMasterEnvelope
{
    public int version;
    public int schemaVersion;
    public string updatedAt;
    public ItemOverride[] items;
}

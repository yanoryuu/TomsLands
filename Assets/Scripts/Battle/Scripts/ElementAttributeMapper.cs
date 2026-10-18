/// <summary>
/// EnemyData の ElementType と RuntimeItemData の ItemAttribute を相互変換するユーティリティ。
/// また、属性間の有利/不利判定を提供する。
/// アイテム属性は敵と同じ5属性（水・火・木・光・闇）で 1:1 に対応する。
/// 相性: 水>火、火>木、木>水（3すくみ）、光⇄闇（相互に有利）。
/// </summary>
public static class ElementAttributeMapper
{
    /// <summary>
    /// EnemyData.ElementType → ItemTypeData.ItemAttribute に変換する。
    /// </summary>
    public static ItemTypeData.ItemAttribute? ToItemAttribute(ElementType elementType)
    {
        return elementType switch
        {
            ElementType.Fire  => ItemTypeData.ItemAttribute.Fire,
            ElementType.Water => ItemTypeData.ItemAttribute.Water,
            ElementType.Wood  => ItemTypeData.ItemAttribute.Wood,
            ElementType.Light => ItemTypeData.ItemAttribute.Light,
            ElementType.Dark  => ItemTypeData.ItemAttribute.Dark,
            _ => null // None は属性なし
        };
    }

    /// <summary>
    /// アイテム属性が敵属性に対して有利かどうかを判定する。
    /// </summary>
    public static bool IsEffective(ItemTypeData.ItemAttribute itemAttr, ElementType enemyElement)
    {
        return (ItemTypeData.NormalizeAttribute(itemAttr), enemyElement) switch
        {
            // 水 → 火に有利
            (ItemTypeData.ItemAttribute.Water, ElementType.Fire)  => true,
            // 火 → 木に有利
            (ItemTypeData.ItemAttribute.Fire, ElementType.Wood)   => true,
            // 木 → 水に有利
            (ItemTypeData.ItemAttribute.Wood, ElementType.Water)  => true,
            // 光 → 闇に有利
            (ItemTypeData.ItemAttribute.Light, ElementType.Dark)  => true,
            // 闇 → 光に有利
            (ItemTypeData.ItemAttribute.Dark, ElementType.Light)  => true,
            _ => false
        };
    }

    /// <summary>
    /// アイテム属性が敵属性に対して不利かどうかを判定する（3すくみの逆向き）。
    /// 光・闇は互いに有利なだけで不利はない。
    /// </summary>
    public static bool IsWeak(ItemTypeData.ItemAttribute itemAttr, ElementType enemyElement)
    {
        return (ItemTypeData.NormalizeAttribute(itemAttr), enemyElement) switch
        {
            // 火 → 水に不利
            (ItemTypeData.ItemAttribute.Fire, ElementType.Water)  => true,
            // 木 → 火に不利
            (ItemTypeData.ItemAttribute.Wood, ElementType.Fire)   => true,
            // 水 → 木に不利
            (ItemTypeData.ItemAttribute.Water, ElementType.Wood)  => true,
            _ => false
        };
    }
}

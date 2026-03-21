/// <summary>
/// EnemyData の ElementType と RuntimeItemData の ItemAttribute を相互変換するユーティリティ。
/// また、属性間の有利/不利判定を提供する。
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
            ElementType.Wood  => ItemTypeData.ItemAttribute.Earth,   // Wood → Earth にマッピング
            ElementType.Light => ItemTypeData.ItemAttribute.Light,
            ElementType.Dark  => ItemTypeData.ItemAttribute.Dark,
            ElementType.None  => null,                                // None は属性なし
            _ => null
        };
    }

    /// <summary>
    /// アイテム属性が敵属性に対して有利かどうかを判定する。
    /// </summary>
    public static bool IsEffective(ItemTypeData.ItemAttribute itemAttr, ElementType enemyElement)
    {
        return (itemAttr, enemyElement) switch
        {
            // 火 → 木（Earth/Wood）に有利
            (ItemTypeData.ItemAttribute.Fire, ElementType.Wood)   => true,
            // 水 → 火に有利
            (ItemTypeData.ItemAttribute.Water, ElementType.Fire)  => true,
            // 土/木 → 水に有利
            (ItemTypeData.ItemAttribute.Earth, ElementType.Water) => true,
            // 風 → 土/木に有利
            (ItemTypeData.ItemAttribute.Wind, ElementType.Wood)   => true,
            // 光 → 闇に有利
            (ItemTypeData.ItemAttribute.Light, ElementType.Dark)  => true,
            // 闇 → 光に有利
            (ItemTypeData.ItemAttribute.Dark, ElementType.Light)  => true,
            _ => false
        };
    }

    /// <summary>
    /// アイテム属性が敵属性に対して不利かどうかを判定する。
    /// </summary>
    public static bool IsWeak(ItemTypeData.ItemAttribute itemAttr, ElementType enemyElement)
    {
        return (itemAttr, enemyElement) switch
        {
            // 火 → 水に不利
            (ItemTypeData.ItemAttribute.Fire, ElementType.Water)  => true,
            // 水 → 木に不利
            (ItemTypeData.ItemAttribute.Water, ElementType.Wood)  => true,
            // 土 → 火に不利
            (ItemTypeData.ItemAttribute.Earth, ElementType.Fire)  => true,
            // 風 → 火に不利
            (ItemTypeData.ItemAttribute.Wind, ElementType.Fire)   => true,
            // 光 → 光に不利（同属性不利）ではなく、闇の敵に不利ではない
            (ItemTypeData.ItemAttribute.Light, ElementType.Light) => false,
            // 闇 → 闇に不利ではない
            (ItemTypeData.ItemAttribute.Dark, ElementType.Dark)   => false,
            _ => false
        };
    }
}


/// <summary>
/// 属性の日本語表記（1文字）。地図の名札など、弱点を表示する画面で共有する。
/// （BlackSmithPresenter.AttributeToJapanese と同じ対応）
/// </summary>
public static class ItemAttributeText
{
    public static string ToJapanese(ItemTypeData.ItemAttribute attr) => attr switch
    {
        ItemTypeData.ItemAttribute.Fire  => "火",
        ItemTypeData.ItemAttribute.Water => "水",
        ItemTypeData.ItemAttribute.Earth => "土",
        ItemTypeData.ItemAttribute.Wind  => "風",
        ItemTypeData.ItemAttribute.Light => "光",
        ItemTypeData.ItemAttribute.Dark  => "闇",
        _ => attr.ToString()
    };
}

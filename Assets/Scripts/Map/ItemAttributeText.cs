/// <summary>
/// 属性の日本語表記（1文字）。属性・弱点を表示する画面はここを通す（英語の enum 名を画面に出さない）。
/// 文字の対応は ItemTypeData.AttributeLabel が正（旧 Wind などの寄せ替えもそちらで行う）。
/// </summary>
public static class ItemAttributeText
{
    /// <summary>「火」「水」…。未定義の値は「？」。</summary>
    public static string ToJapanese(ItemTypeData.ItemAttribute attr) => ItemTypeData.AttributeLabel(attr);

    /// <summary>「火属性」。</summary>
    public static string WithSuffix(ItemTypeData.ItemAttribute attr) => $"{ToJapanese(attr)}属性";

    /// <summary>弱点の欄（地図の名札と同じ書き方）。知らないときは「弱点:？」（不明は全角の「？」1字にそろえる）。</summary>
    public static string Weakness(ItemTypeData.ItemAttribute attr, bool known)
        => known ? $"弱点:{ToJapanese(attr)}" : "弱点:？";
}

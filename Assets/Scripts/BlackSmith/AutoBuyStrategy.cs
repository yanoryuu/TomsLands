/// <summary>
/// お任せ仕入れの方針プリセット（予算ポップアップのドロップダウン index と一致）。
/// </summary>
public enum AutoBuyStrategy
{
    /// <summary>おすすめ順（期待収益＋次ダンジョン属性ボーナス）。従来挙動。</summary>
    Recommend = 0,
    /// <summary>次ダンジョンの弱点属性に一致する武具を最優先で買う。</summary>
    DungeonFocus = 1,
    /// <summary>割安買い（現在価格／基準価格が低い順。売却遅延・値上がり狙い向け）。</summary>
    Bargain = 2,
    /// <summary>配当重視（配当利回り = 配当／現在価格 が高い順）。</summary>
    Dividend = 3,
}

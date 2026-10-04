using System.Collections.Generic;

/// <summary>
/// 記事1本分のマスター。NewsArticles.csv（タブ区切り）1行 = 1記事。
///
/// <b>本文は「原因」だけを書き、効果も対象銘柄も書かない。</b>
/// 「氷結種の大量発生」→ 氷の敵には火が有効 → 火の武器が売れる、という推論を
/// プレイヤーにさせるのがこの機能の本体（Docs/News_Spec.md §1 D1・§5 因果の3法則）。
/// 機械が効果を適用するために必要な情報だけを、本文とは別の列で持つ。
/// </summary>
public class NewsArticleData
{
    public string id;
    public string category;       // dungeon / hero / rival / supply / culture / economy / village / correction / shop
    public string page;           // front / second / market / rumor
    public string sourceClarity;  // confirmed / presumed / anonymous（誤報を見抜く無料の手がかり）

    public string headline;
    public string lead;           // 紙面に常時出す2行
    public string body;           // クリックで開く。推論のヒントはここに置く
    public string byline;         // 空 = 無署名（最も危険）

    public string crossRefGroup;  // 同一事象を指す記事群。真の事象は複数社が報じ、誤報は1本だけ
    public string truth;          // true / exaggerated / false

    // --- 効果の対象。空欄は「絞り込まない」。すべての条件を満たす銘柄に効く ---
    public string targetAttribute;  // Fire / Water / Earth / Wind / Light / Dark
    public string targetType;       // Weapon / Armor / Tool
    public string targetItemId;

    // --- 効果量 ---
    public float trendDelta;      // -1.0〜+1.0。遅効・持続。適正値を動かす本体
    public float demandKick;      // 0〜0.15。発効ターンに1回だけ需要へ直撃
    public float hypeRate;        // 1.00〜1.08。掲載ターンの価格倍率。真偽に関わらず起きる
    public int durationTurns;

    /// <summary>
    /// 決着後に出す続報の記事ID。カンマ区切りで複数書ける（結果記事と訂正記事を両方用意しておく）。
    /// 実際に出す方は掲載時の真偽で決まる（本物 → 結果記事 / 誤報 → 訂正記事）。
    /// 向きは「親 → 続報」の一方向。続報側の followUpId は空にする。
    /// </summary>
    public string followUpId;
    public string condition;      // 出現条件（nextDungeon=X / heroLevel>=N / turn>=N）
    public int weight;
    public string summaryEn;      // Jev 検証専用。製品には出さない

    public bool IsFalse => truth == "false";
    public bool IsExaggerated => truth == "exaggerated";

    /// <summary>訂正記事。単独では抽選されず、誤報の親記事の続報としてのみ載る。</summary>
    public bool IsCorrection => category == "correction";

    /// <summary>効果の対象（属性・種別・銘柄のいずれか）が書かれているか。</summary>
    public bool HasTarget =>
        !string.IsNullOrEmpty(targetAttribute)
        || !string.IsNullOrEmpty(targetType)
        || !string.IsNullOrEmpty(targetItemId);

    /// <summary>followUpId を分解した続報ID の列。</summary>
    public IEnumerable<string> FollowUpIds
    {
        get
        {
            if (string.IsNullOrEmpty(followUpId)) yield break;
            foreach (var part in followUpId.Split(','))
            {
                var t = part.Trim();
                if (t.Length > 0) yield return t;
            }
        }
    }

    /// <summary>この記事の効果が指定の銘柄に及ぶか。空欄の条件は無視する。</summary>
    public bool Matches(RuntimeItemData item)
    {
        if (item == null) return false;

        if (!string.IsNullOrEmpty(targetItemId))
            return item.ItemId == targetItemId;

        if (!string.IsNullOrEmpty(targetAttribute)
            && item.ItemAttribute.ToString() != targetAttribute) return false;

        if (!string.IsNullOrEmpty(targetType)
            && item.ItemType.ToString() != targetType) return false;

        // 3つとも空の記事は誰にも効かない（フレーバー記事）
        return !string.IsNullOrEmpty(targetAttribute)
            || !string.IsNullOrEmpty(targetType);
    }

    /// <summary>需要を動かす効果を持つか。持たない記事は純粋なフレーバー。</summary>
    public bool HasEffect => (trendDelta != 0f || demandKick != 0f) && HasTarget;

    public override string ToString() => $"[{id}] {headline} ({category}/{page}/{truth})";
}

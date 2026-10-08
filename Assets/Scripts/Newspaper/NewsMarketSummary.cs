using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 相場欄（右下ペイン）の1行分の集計。種別（武器・防具・道具）ごとに銘柄をまとめ、
/// 値動きの指数・変動率・<see cref="MarketHeat"/> を求める（Docs/News_Spec.md §10.2）。
///
/// <b>過去の価格履歴（ShopPriceHistory）だけを使う</b>。未来情報は含まない。
/// 記事から銘柄へのリンクは作らない（§9.3）ので、ここも種別単位の粗い集計に留める。
/// </summary>
public static class NewsMarketSummary
{
    /// <summary>スパークラインに描くターン数。</summary>
    public const int SparkWindow = 12;

    /// <summary>変動率を測る間隔（ターン）。</summary>
    public const int ChangeWindow = 5;

    public struct Row
    {
        public List<int> spark;    // 指数 ×1000（PriceChartView が int を取るため）
        public float changeRate;   // 0.18 = +18%
        public float heat;         // 0〜1
        public string heatLabel;   // 凪 / 静か / 平常 / 活況 / 荒れ
        public int heatLevel;      // 0〜4
        public bool hasData;
    }

    /// <summary>指定の種別の行を作る。filter が null なら全銘柄。</summary>
    public static Row Compute(IEnumerable<RuntimeItemData> items, ItemTypeData.ItemType? filter)
    {
        var histories = new List<List<int>>();
        if (items != null)
        {
            foreach (var it in items)
            {
                if (it == null || it.ShopPriceHistory == null || it.ShopPriceHistory.Count == 0) continue;
                if (filter.HasValue && it.ItemType != filter.Value) continue;
                histories.Add(it.ShopPriceHistory);
            }
        }

        var row = new Row { spark = new List<int>(), heat = MarketHeat.Neutral };
        if (histories.Count == 0)
        {
            row.heatLabel = MarketHeat.Describe(row.heat);
            row.heatLevel = MarketHeat.ToLevel(row.heat);
            return row;
        }

        // 末尾（最新）を揃え、各銘柄を窓の先頭値で正規化して平均する（＝等ウェイトの指数）
        int len = 0;
        foreach (var h in histories) len = Mathf.Max(len, Mathf.Min(SparkWindow, h.Count));

        var index = new float[len];
        var counts = new int[len];
        foreach (var h in histories)
        {
            int n = Mathf.Min(len, h.Count);
            int start = h.Count - n;
            float baseValue = h[start];
            if (baseValue <= 0f) continue;
            for (int i = 0; i < n; i++)
            {
                int slot = len - n + i;
                index[slot] += h[start + i] / baseValue;
                counts[slot]++;
            }
        }
        for (int i = 0; i < len; i++)
        {
            float v = counts[i] > 0 ? index[i] / counts[i] : 1f;
            index[i] = v;
            row.spark.Add(Mathf.RoundToInt(v * 1000f));
        }

        if (len >= 2)
        {
            int back = Mathf.Min(ChangeWindow, len - 1);
            float from = index[len - 1 - back];
            row.changeRate = from > 0f ? index[len - 1] / from - 1f : 0f;
        }

        float heatSum = 0f;
        foreach (var h in histories) heatSum += MarketHeat.Compute(h);
        row.heat = heatSum / histories.Count;
        row.heatLabel = MarketHeat.Describe(row.heat);
        row.heatLevel = HeatLevel(row.heat);
        row.hasData = len >= 2;
        return row;
    }

    /// <summary>Describe() の境界に揃えた5段階（凪0 / 静か1 / 平常2 / 活況3 / 荒れ4）。</summary>
    public static int HeatLevel(float heat)
    {
        if (heat < 0.25f) return 0;
        if (heat < 0.42f) return 1;
        if (heat < 0.58f) return 2;
        if (heat < 0.75f) return 3;
        return 4;
    }
}

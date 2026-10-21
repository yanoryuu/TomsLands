using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 価格・需要の時系列を折れ線で描く自作UIグラフ。
/// 外部ライブラリ不要・自己完結（OnPopulateMesh で線分を四角形メッシュ化）。
/// 価格系列を主、需要系列(0〜1)を任意の副系列として、それぞれ独立スケールで描画する。
///
/// 目安の線（G-12）: SetGuides で基準価格を渡すと、縦軸を基準価格の 0.5〜1.8 倍に固定し
/// （はみ出したら広げる）、基準価格の点線と平均取得単価の線、出来事の印（配信・貼った記事）を描く。
/// 基準価格を渡さないとき（金融の基準価額・新聞の指数など）は従来どおり表示範囲いっぱいに引き伸ばす。
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class PriceChartView : MaskableGraphic
{
    /// <summary>折れ線の上に立てる出来事の印。Index は価格系列の何点目か。</summary>
    public readonly struct Marker
    {
        public readonly int Index;
        public readonly Color Color;
        public Marker(int index, Color color) { Index = index; Color = color; }
    }

    [Header("描画設定")]
    [Tooltip("線の太さ（ピクセル）")]
    [SerializeField] private float lineThickness = 3f;
    [Tooltip("グラフ外周の余白（ピクセル）")]
    [SerializeField] private float padding = 8f;
    [Tooltip("価格線の色")]
    [SerializeField] private Color priceColor = new Color(1f, 0.78f, 0.25f, 1f);
    [Tooltip("需要線の色")]
    [SerializeField] private Color demandColor = new Color(0.35f, 0.8f, 1f, 0.9f);
    [Tooltip("需要線を描画するか")]
    [SerializeField] private bool drawDemand = true;

    [Header("目安の線（SetGuides）")]
    [SerializeField] private Color baseLineColor = new Color(1f, 1f, 1f, 0.45f);
    [SerializeField] private Color costLineColor = new Color(0.55f, 0.95f, 0.55f, 0.9f);
    [Tooltip("縦軸の固定範囲（基準価格の倍率）")]
    [SerializeField] private float rangeMinRate = 0.5f;
    [SerializeField] private float rangeMaxRate = 1.8f;
    [SerializeField] private float guideThickness = 2f;
    [SerializeField] private float dashLength = 8f;
    [SerializeField] private float markerSize = 9f;

    private readonly List<float> priceValues = new();
    private readonly List<float> demandValues = new();
    private readonly List<Marker> markers = new();
    private float? basePrice;
    private float? costPrice;

    /// <summary>価格系列のみを設定する。</summary>
    public void SetData(IReadOnlyList<int> prices)
    {
        priceValues.Clear();
        demandValues.Clear();
        ClearGuidesInternal();
        if (prices != null)
            foreach (var p in prices) priceValues.Add(p);
        SetVerticesDirty();
    }

    /// <summary>価格系列＋需要系列(0〜1)を設定する。</summary>
    public void SetData(IReadOnlyList<int> prices, IReadOnlyList<float> demands)
    {
        priceValues.Clear();
        demandValues.Clear();
        ClearGuidesInternal();
        if (prices != null)
            foreach (var p in prices) priceValues.Add(p);
        if (demands != null)
            foreach (var d in demands) demandValues.Add(d);
        SetVerticesDirty();
    }

    /// <summary>
    /// 目安の線と印を設定する（SetData の後に呼ぶ）。basePrice が 0 以下なら縦軸は従来の自動スケール。
    /// costPrice は平均取得単価（不明なら null）。
    /// </summary>
    public void SetGuides(int basePriceValue, int? costPriceValue, IReadOnlyList<Marker> eventMarkers)
    {
        basePrice = basePriceValue > 0 ? basePriceValue : (float?)null;
        costPrice = costPriceValue.HasValue && costPriceValue.Value >= 0 ? costPriceValue.Value : (float?)null;
        markers.Clear();
        if (eventMarkers != null) markers.AddRange(eventMarkers);
        SetVerticesDirty();
    }

    public void Clear()
    {
        priceValues.Clear();
        demandValues.Clear();
        ClearGuidesInternal();
        SetVerticesDirty();
    }

    private void ClearGuidesInternal()
    {
        basePrice = null;
        costPrice = null;
        markers.Clear();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = GetPixelAdjustedRect();
        float x0 = r.xMin + padding;
        float x1 = r.xMax - padding;
        float y0 = r.yMin + padding;
        float y1 = r.yMax - padding;
        if (x1 <= x0 || y1 <= y0) return;

        // 価格の縦軸: 基準価格があれば 0.5〜1.8 倍に固定（小さな揺れを大相場に見せない）、無ければ自動
        GetPriceRange(out float min, out float max);
        float range = max - min;
        float Y(float v) => range <= Mathf.Epsilon ? (y0 + y1) * 0.5f : Mathf.Lerp(y0, y1, (v - min) / range);

        // 目安の線（折れ線の下に描く）
        if (basePrice.HasValue) AddDashedLine(vh, x0, x1, Y(basePrice.Value), baseLineColor);
        if (costPrice.HasValue) AddLine(vh, new Vector2(x0, Y(costPrice.Value)), new Vector2(x1, Y(costPrice.Value)), guideThickness, costLineColor);

        // 需要は常に 0〜1 の固定スケールで描く
        AddPolyline(vh, priceValues, x0, x1, y0, y1, priceColor, min, max);
        if (drawDemand)
            AddPolyline(vh, demandValues, x0, x1, y0, y1, demandColor, 0f, 1f);

        // 出来事の印（下端に小さな三角。上端は凡例と重なるので避ける）
        int n = priceValues.Count;
        if (n >= 2)
        {
            foreach (var m in markers)
            {
                if (m.Index < 0 || m.Index >= n) continue;
                float px = Mathf.Lerp(x0, x1, (float)m.Index / (n - 1));
                AddTriangle(vh, new Vector2(px, y0 + markerSize), markerSize, m.Color);
            }
        }
    }

    private void GetPriceRange(out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        foreach (var v in priceValues)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }
        if (!basePrice.HasValue)
        {
            if (priceValues.Count == 0) { min = 0f; max = 1f; }
            return;
        }
        float lo = basePrice.Value * rangeMinRate;
        float hi = basePrice.Value * rangeMaxRate;
        if (costPrice.HasValue) { lo = Mathf.Min(lo, costPrice.Value); hi = Mathf.Max(hi, costPrice.Value); }
        if (priceValues.Count == 0) { min = lo; max = hi; return; }
        // 実際の値が固定範囲を超えたら軸を広げ、線が上端・下端に張り付いて切れて見えないよう余白も足す
        float margin = (Mathf.Max(max, hi) - Mathf.Min(min, lo)) * 0.08f;
        float dataMax = max, dataMin = min;
        max = dataMax > hi ? dataMax + margin : hi;
        min = dataMin < lo ? Mathf.Max(0f, dataMin - margin) : lo;
    }

    private void AddPolyline(VertexHelper vh, List<float> values,
        float x0, float x1, float y0, float y1, Color color, float min, float max)
    {
        int n = values.Count;
        if (n < 2) return;

        float range = max - min;
        float midY = (y0 + y1) * 0.5f;

        // x は等間隔、y は min〜max を高さにマップ。range==0 のときは中央に水平線。
        Vector2 Point(int i)
        {
            float tx = (n == 1) ? 0f : (float)i / (n - 1);
            float px = Mathf.Lerp(x0, x1, tx);
            float py = (range <= Mathf.Epsilon)
                ? midY
                : Mathf.Lerp(y0, y1, (values[i] - min) / range);
            return new Vector2(px, py);
        }

        for (int i = 0; i < n - 1; i++)
            AddLine(vh, Point(i), Point(i + 1), lineThickness, color);
    }

    private void AddDashedLine(VertexHelper vh, float x0, float x1, float y, Color color)
    {
        float step = Mathf.Max(2f, dashLength);
        for (float x = x0; x < x1; x += step * 2f)
            AddLine(vh, new Vector2(x, y), new Vector2(Mathf.Min(x + step, x1), y), guideThickness, color);
    }

    private void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color color)
    {
        Vector2 dir = b - a;
        if (dir.sqrMagnitude < Mathf.Epsilon) return;
        dir.Normalize();
        Vector2 normal = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);

        int idx = vh.currentVertCount;
        AddVert(vh, a - normal, color);
        AddVert(vh, a + normal, color);
        AddVert(vh, b + normal, color);
        AddVert(vh, b - normal, color);
        vh.AddTriangle(idx + 0, idx + 1, idx + 2);
        vh.AddTriangle(idx + 2, idx + 3, idx + 0);
    }

    /// <summary>上向きの三角（頂点が tip、底辺は tip の size 下）。</summary>
    private void AddTriangle(VertexHelper vh, Vector2 tip, float size, Color color)
    {
        int idx = vh.currentVertCount;
        AddVert(vh, tip, color);
        AddVert(vh, tip + new Vector2(size * 0.6f, -size), color);
        AddVert(vh, tip + new Vector2(-size * 0.6f, -size), color);
        vh.AddTriangle(idx, idx + 1, idx + 2);
    }

    private void AddVert(VertexHelper vh, Vector2 pos, Color c)
    {
        var v = UIVertex.simpleVert;
        v.position = pos;
        v.color = c;
        vh.AddVert(v);
    }
}

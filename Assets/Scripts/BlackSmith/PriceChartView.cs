using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 価格・需要の時系列を折れ線で描く自作UIグラフ。
/// 外部ライブラリ不要・自己完結（OnPopulateMesh で線分を四角形メッシュ化）。
/// 価格系列を主、需要系列(0〜1)を任意の副系列として、それぞれ独立スケールで描画する。
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class PriceChartView : MaskableGraphic
{
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

    private readonly List<float> priceValues = new();
    private readonly List<float> demandValues = new();

    /// <summary>価格系列のみを設定する。</summary>
    public void SetData(IReadOnlyList<int> prices)
    {
        priceValues.Clear();
        demandValues.Clear();
        if (prices != null)
            foreach (var p in prices) priceValues.Add(p);
        SetVerticesDirty();
    }

    /// <summary>価格系列＋需要系列(0〜1)を設定する。</summary>
    public void SetData(IReadOnlyList<int> prices, IReadOnlyList<float> demands)
    {
        priceValues.Clear();
        demandValues.Clear();
        if (prices != null)
            foreach (var p in prices) priceValues.Add(p);
        if (demands != null)
            foreach (var d in demands) demandValues.Add(d);
        SetVerticesDirty();
    }

    public void Clear()
    {
        priceValues.Clear();
        demandValues.Clear();
        SetVerticesDirty();
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

        // 需要は常に 0〜1 の固定スケールで描く
        AddPolyline(vh, priceValues, x0, x1, y0, y1, priceColor, autoScale: true);
        if (drawDemand)
            AddPolyline(vh, demandValues, x0, x1, y0, y1, demandColor, autoScale: false, fixedMin: 0f, fixedMax: 1f);
    }

    private void AddPolyline(VertexHelper vh, List<float> values,
        float x0, float x1, float y0, float y1, Color color,
        bool autoScale, float fixedMin = 0f, float fixedMax = 1f)
    {
        int n = values.Count;
        if (n < 2) return;

        float min, max;
        if (autoScale)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (var v in values)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }
        else
        {
            min = fixedMin;
            max = fixedMax;
        }

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

        float half = lineThickness * 0.5f;
        for (int i = 0; i < n - 1; i++)
        {
            Vector2 a = Point(i);
            Vector2 b = Point(i + 1);
            Vector2 dir = (b - a);
            if (dir.sqrMagnitude < Mathf.Epsilon) continue;
            dir.Normalize();
            Vector2 normal = new Vector2(-dir.y, dir.x) * half;

            int idx = vh.currentVertCount;
            AddVert(vh, a - normal, color);
            AddVert(vh, a + normal, color);
            AddVert(vh, b + normal, color);
            AddVert(vh, b - normal, color);
            vh.AddTriangle(idx + 0, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx + 0);
        }
    }

    private void AddVert(VertexHelper vh, Vector2 pos, Color c)
    {
        var v = UIVertex.simpleVert;
        v.position = pos;
        v.color = c;
        vh.AddVert(v);
    }
}

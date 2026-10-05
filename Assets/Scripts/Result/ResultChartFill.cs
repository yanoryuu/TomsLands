using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 所持金グラフ（PriceChartView）の線の下を塗る面。PriceChartView と同じ座標系
/// （外周 padding・等間隔のx・min〜max の自動スケール）で描くので、同じ RectTransform サイズで重ねて使う。
/// 上端 topColor → 下端 bottomColor の縦グラデーション。
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class ResultChartFill : MaskableGraphic
{
    [SerializeField] private float padding = 8f;
    [SerializeField] private Color topColor = new(0.910f, 0.639f, 0.239f, 0.45f);
    [SerializeField] private Color bottomColor = new(0.910f, 0.639f, 0.239f, 0.0f);

    private readonly List<float> _values = new();

    public float Padding => padding;
    public int PointCount => _values.Count;

    public void SetData(IReadOnlyList<int> values)
    {
        _values.Clear();
        if (values != null)
            foreach (var v in values) _values.Add(v);
        SetVerticesDirty();
    }

    /// <summary>i 番目の点の、この Graphic のローカル座標（PriceChartView と同じ配置）。</summary>
    public bool TryGetPoint(int index, out Vector2 point)
    {
        point = default;
        int n = _values.Count;
        if (n < 2 || index < 0 || index >= n) return false;
        Rect r = GetPixelAdjustedRect();
        point = Map(index, r, Min(), Max());
        return true;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        int n = _values.Count;
        if (n < 2) return;

        Rect r = GetPixelAdjustedRect();
        float min = Min(), max = Max();
        float yBottom = r.yMin + padding;
        float yTop = r.yMax - padding;

        for (int i = 0; i < n - 1; i++)
        {
            Vector2 a = Map(i, r, min, max);
            Vector2 b = Map(i + 1, r, min, max);
            int idx = vh.currentVertCount;
            AddVert(vh, new Vector2(a.x, yBottom), bottomColor);
            AddVert(vh, a, Color.Lerp(bottomColor, topColor, Mathf.InverseLerp(yBottom, yTop, a.y)));
            AddVert(vh, b, Color.Lerp(bottomColor, topColor, Mathf.InverseLerp(yBottom, yTop, b.y)));
            AddVert(vh, new Vector2(b.x, yBottom), bottomColor);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }
    }

    private float Min()
    {
        float m = float.MaxValue;
        foreach (var v in _values) if (v < m) m = v;
        return m;
    }

    private float Max()
    {
        float m = float.MinValue;
        foreach (var v in _values) if (v > m) m = v;
        return m;
    }

    private Vector2 Map(int i, Rect r, float min, float max)
    {
        int n = _values.Count;
        float x0 = r.xMin + padding, x1 = r.xMax - padding;
        float y0 = r.yMin + padding, y1 = r.yMax - padding;
        float range = max - min;
        float px = Mathf.Lerp(x0, x1, n <= 1 ? 0f : (float)i / (n - 1));
        float py = range <= Mathf.Epsilon ? (y0 + y1) * 0.5f : Mathf.Lerp(y0, y1, (_values[i] - min) / range);
        return new Vector2(px, py);
    }

    private static void AddVert(VertexHelper vh, Vector2 pos, Color c)
    {
        var v = UIVertex.simpleVert;
        v.position = pos;
        v.color = c;
        vh.AddVert(v);
    }
}

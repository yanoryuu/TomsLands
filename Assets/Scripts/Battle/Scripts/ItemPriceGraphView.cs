using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// バトル中のアイテム価格を折れ線グラフで表示するビュー。
/// アイテムアイコンをクリックすると表示される。
/// 全ターンの価格推移と各ターンの増減額・現在価格を可視化する。
/// </summary>
public class ItemPriceGraphView : MonoBehaviour
{
    [Header("パネル")]
    [SerializeField] private GameObject graphPanel;
    [SerializeField] private Button closeButton;

    [Header("アイテム情報")]
    [Tooltip("アイテムアイコンを表示する Image")]
    [SerializeField] private Image itemIconImage;
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private TMP_Text currentPriceText;
    [SerializeField] private TMP_Text totalChangeText;

    [Header("グラフエリア")]
    [Tooltip("折れ線グラフを描画する RectTransform。Pivot=(0,0)、Anchor=左下 推奨。")]
    [SerializeField] private RectTransform graphContainer;
    [Tooltip("データ点間の水平間隔（px）")]
    [SerializeField] private float pointSpacing = 40f;
    [Tooltip("グラフ描画エリアの高さ（px）。graphContainer の高さと合わせる。")]
    [SerializeField] private float graphHeight = 150f;
    [Tooltip("グラフ上下の余白（px）")]
    [SerializeField] private float graphPaddingY = 16f;
    [Tooltip("折れ線の太さ（px）")]
    [SerializeField] private float lineThickness = 2.5f;
    [Tooltip("データ点の直径（px）")]
    [SerializeField] private float pointSize = 8f;
    [Tooltip("ターン・差分ラベルのグラフ下余白（px）")]
    [SerializeField] private float labelBottomOffset = 28f;
    [Tooltip("点を丸くするスプライト（null の場合は四角）")]
    [SerializeField] private Sprite pointSprite;

    [Header("小さく出す（商品詳細パネル内）")]
    [Tooltip("ON: graphContainer の大きさに合わせて横幅・高さを自動で決め、ラベル文字を出さない。値の文字は詳細パネル側に出す")]
    [SerializeField] private bool compactMode = false;
    [Tooltip("compactMode で表示する直近の点数（多いと潰れるため）")]
    [SerializeField] private int compactMaxPoints = 12;
    [Tooltip("compactMode の左右の余白（px）")]
    [SerializeField] private float compactPaddingX = 10f;

    [Header("カラー")]
    [SerializeField] private Color lineColorUp   = new Color(0.25f, 0.85f, 0.35f);
    [SerializeField] private Color lineColorDown = new Color(0.90f, 0.25f, 0.25f);
    [SerializeField] private Color lineColorFlat = new Color(0.85f, 0.85f, 0.25f);
    [SerializeField] private Color axisColor     = new Color(0.6f,  0.6f,  0.6f,  0.6f);
    [SerializeField] private Color labelColor    = Color.white;

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);
        Hide();
    }

    // ─────────────────────────────────────────
    //  公開 API
    // ─────────────────────────────────────────

    /// <summary>指定アイテムの価格グラフを表示する。</summary>
    public void Show(RuntimeItemData item)
    {
        if (item == null) return;

        if (itemIconImage != null)
        {
            itemIconImage.sprite  = item.ItemIcon;
            itemIconImage.enabled = item.ItemIcon != null;
        }

        if (itemNameText != null)
            itemNameText.text = item.ItemName;

        var history = item.BattlePriceHistory;
        if (compactMode)
        {
            // 詳細パネルには「現在価格」「前ターン比」の見出しが既にあるので、値だけを書く
            if (currentPriceText != null)
                currentPriceText.text = $"{item.CurrentPrice.Value:N0}G";
            int prev = history.Count >= 2 ? history[history.Count - 2] : item.CurrentPrice.Value;
            int diff = item.CurrentPrice.Value - prev;
            if (totalChangeText != null)
            {
                totalChangeText.text = diff > 0 ? $"+{diff:N0}G" : diff < 0 ? $"-{-diff:N0}G" : "±0G";
                totalChangeText.color = diff > 0 ? lineColorUp : diff < 0 ? lineColorDown : Color.white;
            }
        }
        else
        {
            if (currentPriceText != null)
                currentPriceText.text = $"現在価格: {item.CurrentPrice.Value} G";

            int totalChange = history.Count > 0 ? item.CurrentPrice.Value - history[0] : 0;
            if (totalChangeText != null)
            {
                string sign = totalChange >= 0 ? "+" : "";
                totalChangeText.text = $"累計変動: {sign}{totalChange} G";
                totalChangeText.color = totalChange > 0 ? lineColorUp
                                      : totalChange < 0 ? lineColorDown
                                      : lineColorFlat;
            }
        }

        BuildLineGraph(history);

        if (graphPanel != null)
            graphPanel.SetActive(true);
    }

    public void Hide()
    {
        if (graphPanel != null)
            graphPanel.SetActive(false);
    }

    public bool IsVisible => graphPanel != null && graphPanel.activeSelf;

    // ─────────────────────────────────────────
    //  グラフ描画
    // ─────────────────────────────────────────

    private void BuildLineGraph(List<int> history)
    {
        if (graphContainer == null) return;

        foreach (Transform child in graphContainer)
            Destroy(child.gameObject);

        if (history == null || history.Count == 0) return;

        if (compactMode)
        {
            BuildCompactGraph(history);
            return;
        }

        int maxP = history.Max();
        int minP = history.Min();
        int range = Mathf.Max(1, maxP - minP);

        float drawBottom = graphPaddingY;
        float drawTop    = graphHeight - graphPaddingY;
        float drawRange  = drawTop - drawBottom;

        // 各データ点のスクリーン座標を計算
        var points = new Vector2[history.Count];
        for (int i = 0; i < history.Count; i++)
        {
            float nx = i * pointSpacing;
            float ny = drawBottom + drawRange * (float)(history[i] - minP) / range;
            points[i] = new Vector2(nx, ny);
        }

        // 水平軸（装飾）
        DrawRect("Axis", graphContainer,
            pos:  new Vector2(points[0].x - pointSize, drawBottom - 1f),
            size: new Vector2((history.Count - 1) * pointSpacing + pointSize * 2f, 1.5f),
            pivot: new Vector2(0f, 0.5f),
            color: axisColor,
            rotation: 0f);

        // 折れ線セグメントとデータ点
        for (int i = 0; i < history.Count; i++)
        {
            // 折れ線（i-1 → i）
            if (i > 0)
            {
                Color segColor = history[i] > history[i - 1] ? lineColorUp
                               : history[i] < history[i - 1] ? lineColorDown
                               : lineColorFlat;
                DrawSegment(graphContainer, points[i - 1], points[i], segColor);
            }

            // データ点（折れ線より手前に描画したいので後から追加）
            Color ptColor = i == 0 ? lineColorFlat
                          : history[i] > history[i - 1] ? lineColorUp
                          : history[i] < history[i - 1] ? lineColorDown
                          : lineColorFlat;
            DrawPoint(graphContainer, points[i], ptColor);

            // 価格ラベル（点の上）
            string priceLabel = $"{history[i]:N0}G";
            CreateLabel(graphContainer, priceLabel, points[i] + new Vector2(0f, pointSize * 0.5f + 4f),
                fontSize: 8f, color: ptColor, pivot: new Vector2(0.5f, 0f));

            // ターン＋差分ラベル（グラフ下）
            int    delta = i == 0 ? 0 : history[i] - history[i - 1];
            string sign  = delta > 0 ? "+" : "";
            string label = i == 0 ? $"開始\n±0G" : $"T{i}\n{sign}{delta:N0}G";
            Color  lc    = i == 0 ? lineColorFlat
                         : delta > 0 ? lineColorUp
                         : delta < 0 ? lineColorDown
                         : lineColorFlat;
            CreateLabel(graphContainer, label,
                new Vector2(points[i].x, drawBottom - labelBottomOffset),
                fontSize: 7f, color: lc, pivot: new Vector2(0.5f, 1f));
        }
    }

    /// <summary>
    /// 小さい領域向け: 直近の点だけを、領域の幅・高さいっぱいに描く。文字ラベルは出さない。
    /// </summary>
    private void BuildCompactGraph(List<int> history)
    {
        int count = Mathf.Clamp(compactMaxPoints, 2, 64);
        int start = Mathf.Max(0, history.Count - count);
        var data = history.GetRange(start, history.Count - start);
        int n = data.Count;

        var rect = graphContainer.rect;
        float w = Mathf.Max(10f, rect.width - compactPaddingX * 2f);
        float h = Mathf.Max(10f, rect.height);
        float padY = Mathf.Max(pointSize, h * 0.16f);
        float bottom = padY, top = h - padY;

        int maxP = data.Max();
        int minP = data.Min();
        int range = Mathf.Max(1, maxP - minP);
        // 2点の差が小さいときに線が上下端へ張り付かないよう、値幅に下限を設ける
        float minRange = Mathf.Max(1f, maxP * 0.04f);
        float mid = (maxP + minP) * 0.5f;
        float half = Mathf.Max(range, minRange) * 0.5f;

        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float x = compactPaddingX + (n == 1 ? w * 0.5f : w * i / (n - 1));
            float t = (data[i] - (mid - half)) / (half * 2f);
            pts[i] = new Vector2(x, Mathf.Lerp(bottom, top, Mathf.Clamp01(t)));
        }

        // 開始価格の目安線（薄く）
        DrawRect("Base", graphContainer,
            pos: new Vector2(compactPaddingX, pts[0].y),
            size: new Vector2(w, 1.5f),
            pivot: new Vector2(0f, 0.5f),
            color: axisColor,
            rotation: 0f);

        for (int i = 1; i < n; i++)
        {
            Color c = data[i] > data[i - 1] ? lineColorUp : data[i] < data[i - 1] ? lineColorDown : lineColorFlat;
            DrawSegment(graphContainer, pts[i - 1], pts[i], c);
        }
        for (int i = 0; i < n; i++)
        {
            Color c = i == 0 ? lineColorFlat
                    : data[i] > data[i - 1] ? lineColorUp
                    : data[i] < data[i - 1] ? lineColorDown
                    : lineColorFlat;
            DrawPoint(graphContainer, pts[i], c);
        }
        // 最新の点を少し大きく
        var last = graphContainer.GetChild(graphContainer.childCount - 1) as RectTransform;
        if (last != null) last.sizeDelta = Vector2.one * pointSize * 1.6f;
    }

    // ─────────────────────────────────────────
    //  プリミティブ描画ヘルパー
    // ─────────────────────────────────────────

    /// <summary>2点間を繋ぐ折れ線セグメントを描画する。</summary>
    private void DrawSegment(Transform parent, Vector2 from, Vector2 to, Color color)
    {
        float  length = Vector2.Distance(from, to);
        float  angle  = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
        Vector2 mid   = (from + to) * 0.5f;

        DrawRect($"Seg_{from.x:F0}_{to.x:F0}", parent,
            pos:      mid,
            size:     new Vector2(length, lineThickness),
            pivot:    new Vector2(0.5f, 0.5f),
            color:    color,
            rotation: angle);
    }

    /// <summary>データ点（丸または四角）を描画する。</summary>
    private void DrawPoint(Transform parent, Vector2 pos, Color color)
    {
        var go  = new GameObject("Point");
        go.transform.SetParent(parent, false);

        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin       = Vector2.zero;
        rt.anchorMax       = Vector2.zero;
        rt.pivot           = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta       = new Vector2(pointSize, pointSize);

        var img = go.AddComponent<Image>();
        img.color  = color;
        if (pointSprite != null) img.sprite = pointSprite;
    }

    /// <summary>任意の矩形 Image を描画する。</summary>
    private void DrawRect(string name, Transform parent, Vector2 pos, Vector2 size,
                          Vector2 pivot, Color color, float rotation)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin          = Vector2.zero;
        rt.anchorMax          = Vector2.zero;
        rt.pivot              = pivot;
        rt.anchoredPosition   = pos;
        rt.sizeDelta          = size;
        rt.localEulerAngles   = new Vector3(0f, 0f, rotation);

        go.AddComponent<Image>().color = color;
    }

    /// <summary>テキストラベルを描画する。</summary>
    private void CreateLabel(Transform parent, string text, Vector2 pos,
                             float fontSize, Color color, Vector2 pivot)
    {
        var go  = new GameObject("Label");
        go.transform.SetParent(parent, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = color;

        var rt = tmp.rectTransform;
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.zero;
        rt.pivot            = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta        = new Vector2(pointSpacing, 32f);
    }
}

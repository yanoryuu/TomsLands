using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 紙面の記事1本分のセル。
/// 見出しとリード文は常時表示し、<b>本文はクリックで開く</b>。
/// 推論のヒントは本文にあるので、読んだ人が得をする形にしている
/// （Docs/News_Spec.md §10.4）。
/// </summary>
public class NewsArticleCellUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI headlineText;
    [SerializeField] private TextMeshProUGUI leadText;
    [SerializeField] private TextMeshProUGUI bylineText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private GameObject bodyRoot;      // 本文のまとまり。既定は非表示
    [SerializeField] private GameObject newBadge;      // 未読マーク
    [SerializeField] private Button rootButton;
    [Tooltip("裏取り（酒場 Lv5）の結果の小さなハンコ（任意）")]
    [SerializeField] private Image vetStamp;

    public string ArticleId { get; private set; }
    public event Action<string> OnClicked;

    private void Awake()
    {
        if (rootButton != null)
            rootButton.onClick.AddListener(() => OnClicked?.Invoke(ArticleId));
        if (bodyRoot != null) bodyRoot.SetActive(false);
    }

    public void SetData(string articleId, string companyName, string headline, string lead, string byline, bool isRead)
    {
        ArticleId = articleId;
        if (headlineText != null) headlineText.text = headline;
        if (leadText != null) leadText.text = lead;
        if (bylineText != null)
            bylineText.text = string.IsNullOrEmpty(byline)
                ? $"{companyName}／無署名"   // 無署名は最も危険、という手がかりをそのまま出す
                : $"{companyName}／記者：{byline}";
        if (newBadge != null) newBadge.SetActive(!isRead);
        if (bodyRoot != null) bodyRoot.SetActive(false);
    }

    /// <summary>3ペイン版の小記事。署名は呼び出し側で整形済み（社の紙面なら社名を省く）。</summary>
    public void SetData(NewspaperView.ArticleData a)
    {
        ArticleId = a.entryKey;
        if (headlineText != null) headlineText.text = a.headline;
        if (leadText != null) leadText.text = a.lead;
        if (bylineText != null) bylineText.text = a.byline;
        if (newBadge != null) newBadge.SetActive(!a.isRead);
        if (bodyRoot != null) bodyRoot.SetActive(false);
    }

    /// <summary>裏取りの結果（null = まだ・ハンコを隠す）。style はスクラップカードのプレハブ（○△× の素材の見本）。</summary>
    public void SetVetStamp(ScrapState? stamp, NewsScrapCardUI style)
    {
        if (vetStamp == null) return;
        bool show = stamp.HasValue && style != null;
        vetStamp.gameObject.SetActive(show);
        ShiftHeadlineForStamp(show);
        if (!show) return;
        // ハンコは左上、未読マークは右上なので重ならない（未読マークはそのまま出す）
        style.PaintBadge(vetStamp, stamp.Value);
    }

    private Vector4? headlineMargin;

    /// <summary>ハンコは見出しの左に押すので、押してある間は見出しをハンコの幅だけ右へずらす。</summary>
    private void ShiftHeadlineForStamp(bool stamped)
    {
        if (headlineText == null) return;
        headlineMargin ??= headlineText.margin;
        var m = headlineMargin.Value;
        if (stamped) m.x += StampInset();
        headlineText.margin = m;
    }

    /// <summary>見出しの左端からハンコの右端までの幅（ハンコが見出しより左にあれば 0）。</summary>
    private float StampInset()
    {
        var cell = (RectTransform)transform;
        var stampRect = vetStamp.rectTransform;
        var headRect = headlineText.rectTransform;
        // セル座標でのハンコの右端と見出しの左端
        Vector3[] c = new Vector3[4];
        stampRect.GetWorldCorners(c);
        float stampRight = cell.InverseTransformPoint(c[2]).x;
        headRect.GetWorldCorners(c);
        float headLeft = cell.InverseTransformPoint(c[0]).x;
        return Mathf.Max(0f, stampRight - headLeft + 4f);
    }

    public void ShowBody(string body)
    {
        if (bodyText != null) bodyText.text = body;
        if (bodyRoot != null) bodyRoot.SetActive(true);
        if (newBadge != null) newBadge.SetActive(false);
    }
}

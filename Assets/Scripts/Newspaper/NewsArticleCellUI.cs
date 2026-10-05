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

    public void ShowBody(string body)
    {
        if (bodyText != null) bodyText.text = body;
        if (bodyRoot != null) bodyRoot.SetActive(true);
        if (newBadge != null) newBadge.SetActive(false);
    }
}

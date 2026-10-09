using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 紙面上段の大記事（一面トップ）。見出し・リード・本文・署名・「スクラップする」。
/// 下の小記事をクリックするとここへ差し替わって本文が開く。
/// 本文はクリックで開閉する（推論のヒントは本文にある。Docs/News_Spec.md §10.4）。
/// </summary>
public class NewsLeadArticleUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI headlineText;
    [SerializeField] private TextMeshProUGUI leadText;
    [SerializeField] private GameObject bodyRoot;
    [SerializeField] private TextMeshProUGUI bodyText;
    [Tooltip("本文を開いていないときに出す写真枠（任意）。本文と同じ場所を使う")]
    [SerializeField] private GameObject photoRoot;
    [SerializeField] private TextMeshProUGUI bylineText;
    [SerializeField] private GameObject newBadge;
    [SerializeField] private Button rootButton;
    [SerializeField] private Button scrapButton;   // 18スクラップする

    public string EntryKey { get; private set; }
    /// <summary>スクラップへ飛ぶ演出の出発点（「スクラップする」ボタン。無ければ記事そのもの）。</summary>
    public RectTransform ScrapSource => scrapButton != null ? (RectTransform)scrapButton.transform : (RectTransform)transform;
    public event Action<string> OnClicked;
    public event Action<string> OnScrapClicked;

    private void Awake()
    {
        if (rootButton != null) rootButton.onClick.AddListener(() => OnClicked?.Invoke(EntryKey));
        if (scrapButton != null) scrapButton.onClick.AddListener(() => OnScrapClicked?.Invoke(EntryKey));
    }

    public void SetData(NewspaperView.ArticleData a)
    {
        bool changed = EntryKey != a.entryKey;
        EntryKey = a.entryKey;

        if (headlineText != null) headlineText.text = a.headline;
        if (leadText != null) leadText.text = a.lead;
        if (bodyText != null) bodyText.text = a.body;
        if (bodyRoot != null) bodyRoot.SetActive(a.bodyOpen);
        if (photoRoot != null) photoRoot.SetActive(!a.bodyOpen);
        if (bylineText != null) bylineText.text = a.byline;
        if (newBadge != null) newBadge.SetActive(!a.isRead);

        if (scrapButton != null)
        {
            scrapButton.gameObject.SetActive(a.canScrapKind);
            scrapButton.interactable = a.canScrap;
        }

        if (changed) UIFx.Pop(transform, 0.98f, 0.14f);
    }
}

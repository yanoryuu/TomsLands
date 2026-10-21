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
    [Tooltip("酒場 Lv5「裏を取る」（任意）。スクラップするの左に置く")]
    [SerializeField] private Button vetButton;
    [Tooltip("裏取りの結果のハンコ（任意）。○△× はスクラップカードの素材で塗る")]
    [SerializeField] private Image vetStamp;

    public string EntryKey { get; private set; }
    /// <summary>スクラップへ飛ぶ演出の出発点（「スクラップする」ボタン。無ければ記事そのもの）。</summary>
    public RectTransform ScrapSource => scrapButton != null ? (RectTransform)scrapButton.transform : (RectTransform)transform;
    public event Action<string> OnClicked;
    public event Action<string> OnScrapClicked;
    public event Action<string> OnVetClicked;

    private void Awake()
    {
        if (rootButton != null) rootButton.onClick.AddListener(() => OnClicked?.Invoke(EntryKey));
        if (scrapButton != null) scrapButton.onClick.AddListener(() => OnScrapClicked?.Invoke(EntryKey));
        if (vetButton != null) vetButton.onClick.AddListener(() => OnVetClicked?.Invoke(EntryKey));
        if (vetStamp != null) vetStamp.gameObject.SetActive(false);
        // パッド/キー: 記事の内側にある「スクラップする」へ右で移れるようにする
        if (rootButton != null && scrapButton != null) InnerSelectableNavigation.Attach(rootButton, scrapButton);
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

    /// <summary>
    /// 裏取り（酒場 Lv5）。showButton = ボタンを出すか（Lv5 でまだ答えの出ていない報道）、canVet = 押せるか（この配信の間にまだ使っていない）。
    /// stamp = 裏取りで分かった真偽（null = まだ）。style はスクラップカードのプレハブ（○△× の素材の見本）。reveal = いま裏を取った（ハンコを押す演出）。
    /// </summary>
    public void SetVet(bool showButton, bool canVet, ScrapState? stamp, NewsScrapCardUI style, bool reveal = false)
    {
        if (vetButton != null)
        {
            vetButton.gameObject.SetActive(showButton && !stamp.HasValue);
            vetButton.interactable = canVet;
        }
        if (vetStamp == null) return;
        bool show = stamp.HasValue && style != null;
        vetStamp.gameObject.SetActive(show);
        if (!show) return;
        if (newBadge != null) newBadge.SetActive(false); // ハンコは未読マークと同じ右上に押すので重ねない
        style.PaintBadge(vetStamp, stamp.Value);
        if (reveal) UIFx.Pop(vetStamp.transform, 1.8f, 0.22f);
    }
}

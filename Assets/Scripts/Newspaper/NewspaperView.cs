using System;
using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 朝刊画面のビュー（フェーズ1の最小版）。
/// 題字・第N号・所持金・記事一覧・閉じるだけを持つ。
/// 3ペイン（購読／紙面／スクラップ・相場）への拡張は Docs/News_Spec.md §10。
/// </summary>
public class NewspaperView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI mastheadText;   // 社名（題字）
    [SerializeField] private TextMeshProUGUI issueText;      // 第N号
    [SerializeField] private TextMeshProUGUI moneyText;      // 所持金
    [SerializeField] private Transform articleParent;        // 記事セルの親
    [SerializeField] private NewsArticleCellUI articleCellPrefab;
    [SerializeField] private Button closeButton;

    private readonly Subject<Unit> onCloseClicked = new();
    private readonly Subject<string> onArticleClicked = new();

    public Observable<Unit> OnCloseClicked => onCloseClicked;
    public Observable<string> OnArticleClicked => onArticleClicked;

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(() => onCloseClicked.OnNext(Unit.Default));
    }

    public void ShowHeader(string issueLabel, int money)
    {
        if (issueText != null) issueText.text = issueLabel;
        if (moneyText != null) moneyText.text = $"{money:N0}G";
    }

    /// <summary>紙面を描き直す。1行 = 1記事。</summary>
    public void ShowArticles(IReadOnlyList<(string articleId, string companyName, string headline, string lead, string byline, bool isRead)> rows)
    {
        if (articleParent == null || articleCellPrefab == null) return;

        for (int i = articleParent.childCount - 1; i >= 0; i--)
            Destroy(articleParent.GetChild(i).gameObject);

        // 題字は最初の記事の社名を出しておく（購読・社切替はフェーズ4）
        if (mastheadText != null)
            mastheadText.text = rows.Count > 0 ? rows[0].companyName : "朝刊";

        foreach (var r in rows)
        {
            var cell = Instantiate(articleCellPrefab, articleParent);
            cell.SetData(r.articleId, r.companyName, r.headline, r.lead, r.byline, r.isRead);
            cell.OnClicked += id => onArticleClicked.OnNext(id);
        }
    }

    /// <summary>記事の本文を開く（フェーズ1は該当セルを展開するだけ）。</summary>
    public void ExpandArticle(string articleId, string body)
    {
        foreach (Transform child in articleParent)
        {
            var cell = child.GetComponent<NewsArticleCellUI>();
            if (cell != null && cell.ArticleId == articleId) cell.ShowBody(body);
        }
    }

    private void OnDestroy()
    {
        onCloseClicked.Dispose();
        onArticleClicked.Dispose();
    }
}

using System;
using System.Collections.Generic;
using R3;
using VContainer.Unity;

/// <summary>
/// 朝刊画面のプレゼンター（フェーズ1の最小版）。
/// そのターンの紙面を出し、記事をクリックしたら本文を開いて既読にする。
/// </summary>
public class NewspaperPresenter : IStartable, IDisposable
{
    private readonly NewspaperView view;
    private readonly NewsModel newsModel;
    private readonly TomsModel tomsModel;
    private readonly StateManager stateManager;
    private readonly CompositeDisposable disposables = new();

    /// <summary>朝刊を開く直前のフェーズ。閉じたらここへ戻す。</summary>
    /// <remarks>ターン頭から開けば Shop、仕入れ画面から開けば BlackSmith に戻る。</remarks>
    private TomsShopGamePhase returnPhase = TomsShopGamePhase.Shop;

    public NewspaperPresenter(
        NewspaperView view,
        NewsModel newsModel,
        TomsModel tomsModel,
        StateManager stateManager)
    {
        this.view = view;
        this.newsModel = newsModel;
        this.tomsModel = tomsModel;
        this.stateManager = stateManager;

        stateManager.RegisterOnEnter(TomsShopGamePhase.Newspaper, Entry);
    }

    public void Start()
    {
        // 朝刊以外のフェーズを通るたびに戻り先を覚えておく
        stateManager.CurrentTomsShopPhase
            .Subscribe(p => { if (p != TomsShopGamePhase.Newspaper) returnPhase = p; })
            .AddTo(disposables);

        view.OnCloseClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(returnPhase))
            .AddTo(disposables);

        view.OnArticleClicked
            .Subscribe(OpenArticle)
            .AddTo(disposables);
    }

    private void Entry()
    {
        // 周のシードからカレンダーを組む（GameFlowManager 側でも呼ぶので通常は何もしない）
        newsModel.Build(tomsModel.FlowSeed);

        int turn = tomsModel.CurrentTurn.Value;
        view.ShowHeader($"第{turn}号", tomsModel.PlayerMoney.Value);

        var rows = new List<(string, string, string, string, string, bool)>();
        foreach (var e in SortForPaper(newsModel.IssueOf(turn)))
        {
            var a = e.Article;
            var c = e.Company;
            if (a == null) continue;
            rows.Add((a.id, c?.companyName ?? "", a.headline, a.lead, a.byline, newsModel.IsRead(a.id)));
        }
        view.ShowArticles(rows);
    }

    /// <summary>
    /// 紙面の並び順。通常記事を面の順（一面→二面→市況→うわさ）に並べ、
    /// その後ろに結果記事、<b>訂正記事は最後（隅）</b>に置く。
    /// 訂正は小さく目立たない場所に出すのが仕様（Docs/News_Spec.md §10.4）。見落としは自己責任。
    /// </summary>
    private static List<NewsIssueEntry> SortForPaper(List<NewsIssueEntry> entries)
    {
        var sorted = new List<NewsIssueEntry>(entries);
        // List.Sort は安定でないので、元の並び（カレンダー順）を最後のキーにする
        var order = new Dictionary<NewsIssueEntry, int>();
        for (int i = 0; i < entries.Count; i++) order[entries[i]] = i;

        sorted.Sort((x, y) =>
        {
            int c = RankOf(x).CompareTo(RankOf(y));
            return c != 0 ? c : order[x].CompareTo(order[y]);
        });
        return sorted;
    }

    private static int RankOf(NewsIssueEntry e)
    {
        switch (e.kind)
        {
            case NewsEntryKind.Result: return 10;
            case NewsEntryKind.Correction: return 20;
        }
        return (e.Article?.page) switch
        {
            "front" => 0,
            "second" => 1,
            "market" => 2,
            "rumor" => 3,
            _ => 4,
        };
    }

    private void OpenArticle(string articleId)
    {
        var a = NewsMasterLoader.FindArticle(articleId);
        if (a == null) return;

        newsModel.MarkRead(articleId);
        view.ExpandArticle(articleId, a.body);
    }

    public void Dispose() => disposables.Dispose();
}

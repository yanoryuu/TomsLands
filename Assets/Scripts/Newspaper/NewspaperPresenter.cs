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
        foreach (var e in newsModel.IssueOf(turn))
        {
            var a = e.Article;
            var c = e.Company;
            if (a == null) continue;
            rows.Add((a.id, c?.companyName ?? "", a.headline, a.lead, a.byline, newsModel.IsRead(a.id)));
        }
        view.ShowArticles(rows);
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

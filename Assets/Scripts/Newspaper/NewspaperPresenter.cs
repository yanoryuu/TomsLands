using System;
using System.Collections.Generic;
using R3;
using VContainer.Unity;

/// <summary>
/// 朝刊画面（情報ターミナル・C案 3ペイン）のプレゼンター。Docs/News_Spec.md §8〜§10。
///
///   左   … 購読（契約・購読枠・契約更新までの残り）。購読中カードのクリックで読む社を切り替える
///   中央 … 選んだ社のその日の号。面タブはその社が持つ面＋自店。未購読なら壁新聞（一面に1〜2本）
///   右上 … スクラップ（手動ピン・枠3）。決着後に確認 1,500G / 3件まとめて 4,000G
///   右下 … 相場（種別ごとの値動き・変動率・MarketHeat）
///
/// 状態を変えたら必ず <see cref="Save"/> を呼ぶ（所持金も同時に保存する）。
/// </summary>
public class NewspaperPresenter : IStartable, IDisposable
{
    private const string FrontPage = "front";
    private static readonly string[] PageOrder = { "front", "second", "market", "rumor", NewsPageTabUI.ShopPage };

    private readonly NewspaperView view;
    private readonly NewsModel newsModel;
    private readonly TomsModel tomsModel;
    private readonly StateManager stateManager;
    private readonly NewspaperSubscriptionModel subscriptions;
    private readonly ScrapbookModel scrapbook;
    private readonly ItemModel itemModel;
    private readonly GameFlowManager gameFlowManager;
    private readonly MorningReportModel morningReport;
    private readonly CompositeDisposable disposables = new();

    /// <summary>朝刊を開く直前のフェーズ。閉じたらここへ戻す。</summary>
    /// <remarks>ターン頭から開けば Shop、仕入れ画面から開けば BlackSmith に戻る。</remarks>
    private TomsShopGamePhase returnPhase = TomsShopGamePhase.Shop;

    private int loadedSeed;
    private bool loaded;

    // 画面内の選択状態（保存しない）
    private string selectedCompanyId;   // null = 壁新聞
    private string selectedPage = FrontPage;
    private string leadKey;             // 一面トップに出している掲載
    private readonly HashSet<string> openedBodies = new();

    // TomsModel.CurrentTurn はラン終了時にしか同期されないので、進行中のターンは GameFlowManager から取る
    private int Turn => gameFlowManager != null ? gameFlowManager.CurrentTurn.Value : tomsModel.CurrentTurn.Value;

    public NewspaperPresenter(
        NewspaperView view,
        NewsModel newsModel,
        TomsModel tomsModel,
        StateManager stateManager,
        NewspaperSubscriptionModel subscriptions,
        ScrapbookModel scrapbook,
        ItemModel itemModel,
        GameFlowManager gameFlowManager,
        MorningReportModel morningReport)
    {
        this.view = view;
        this.newsModel = newsModel;
        this.tomsModel = tomsModel;
        this.stateManager = stateManager;
        this.subscriptions = subscriptions;
        this.scrapbook = scrapbook;
        this.itemModel = itemModel;
        this.gameFlowManager = gameFlowManager;
        this.morningReport = morningReport;

        stateManager.RegisterOnEnter(TomsShopGamePhase.Newspaper, Entry);
    }

    public void Start()
    {
        // 朝刊以外のフェーズを通るたびに戻り先を覚えておく
        stateManager.CurrentTomsShopPhase
            .Subscribe(p => { if (p != TomsShopGamePhase.Newspaper) returnPhase = p; })
            .AddTo(disposables);

        view.OnCloseClicked.Subscribe(_ => stateManager.ChangeTomsShopPhase(returnPhase)).AddTo(disposables);
        view.OnArticleClicked.Subscribe(OpenArticle).AddTo(disposables);
        view.OnScrapClicked.Subscribe(PinArticle).AddTo(disposables);
        view.OnCompanySelected.Subscribe(SelectCompany).AddTo(disposables);
        view.OnSubscribeClicked.Subscribe(Subscribe).AddTo(disposables);
        view.OnPageSelected.Subscribe(SelectPage).AddTo(disposables);
        view.OnPageStep.Subscribe(StepPage).AddTo(disposables);
        view.OnConfirmClicked.Subscribe(ConfirmOne).AddTo(disposables);
        view.OnBatchConfirmClicked.Subscribe(_ => ConfirmBatch()).AddTo(disposables);
    }

    // =====================================================================
    // 入口
    // =====================================================================

    private void Entry()
    {
        int seed = tomsModel.FlowSeed;

        // 周のシードからカレンダーを組む（GameFlowManager 側でも呼ぶので通常は何もしない）
        newsModel.Build(seed);
        // 連載の分岐の取りこぼし（ロード直後など）をここでも決める。通常は GameFlowManager が日送りで決めている
        newsModel.ResolveArcs(Turn, ev => itemModel != null && itemModel.RuntimeItems.Exists(i => i.Stock.Value > 0 && ev.Matches(i)));
        EnsureLoaded(seed);
        // カレンダーの形式が変わる（旧形式 → フェーズ3）と、旧セーブのスクラップは掲載を引けなくなる。
        // 引けないまま枠を占有し続けないよう、ここで捨てる
        if (scrapbook.PruneMissing(key => newsModel.FindEntry(key) != null) > 0) Save();

        // 読む社: 前回の社がまだ購読中ならそれ、無ければ最初の購読社、無ければ壁新聞
        if (selectedCompanyId == null || !subscriptions.IsSubscribed(selectedCompanyId, Turn))
            selectedCompanyId = FirstSubscribed();
        selectedPage = FirstPageWithContent();
        leadKey = null;
        openedBodies.Clear();

        RenderAll();  // パネルのフェードは GamePanelManager が行う
        view.PlayIntro();
    }

    private void EnsureLoaded(int seed)
    {
        if (loaded && loadedSeed == seed) return;
        NewspaperSaveStore.Load(seed, subscriptions, scrapbook, newsModel);
        loadedSeed = seed;
        loaded = true;
    }

    private void Save()
    {
        NewspaperSaveStore.Save(tomsModel.FlowSeed, subscriptions, scrapbook, newsModel);
    }

    // =====================================================================
    // 描画
    // =====================================================================

    private void RenderAll()
    {
        RenderHeader();
        RenderCompanies();
        RenderPaper();
        RenderScraps();
        RenderMarket();
    }

    private void RenderHeader()
    {
        int untilBattle = gameFlowManager != null ? gameFlowManager.GetTurnsUntilNextBattle() : -1;
        view.ShowHeader($"第{Turn}号", tomsModel.PlayerMoney.Value, untilBattle);
    }

    private int Slots => NewsTuning.SubscriptionSlotsFor(tomsModel.ShopLevel.Value);

    private void RenderCompanies()
    {
        int turn = Turn;
        int money = tomsModel.PlayerMoney.Value;
        int slots = Slots;

        var list = new List<NewspaperCompanyCardUI.Data>();
        foreach (var c in NewsMasterLoader.LoadCompanies())
        {
            bool subscribed = subscriptions.IsSubscribed(c.companyId, turn);
            list.Add(new NewspaperCompanyCardUI.Data
            {
                companyId = c.companyId,
                companyName = c.companyName,
                emblem = view.EmblemOf(c.companyId),
                price = c.price,
                contractTurns = c.contractTurns,
                trust = c.trustRating,
                speed = c.speedRating,
                coverage = c.pages.Count,   // 網羅バー = 保有する面の数（§4.1）
                measuredAccuracy = scrapbook.MeasuredAccuracy(c.companyId),
                subscribed = subscribed,
                selected = subscribed && c.companyId == selectedCompanyId,
                canSubscribe = subscriptions.CanSubscribe(c, turn, slots, money),
            });
        }
        view.ShowCompanies(list);
        view.ShowSlots(subscriptions.UsedSlots(turn), slots, subscriptions.TurnsUntilRenewal(turn));
    }

    private void RenderPaper()
    {
        var company = selectedCompanyId != null ? NewsMasterLoader.FindCompany(selectedCompanyId) : null;
        var pages = VisiblePages(company);
        if (!pages.Contains(selectedPage)) selectedPage = FrontPage;

        var paper = new NewspaperView.PaperData
        {
            masthead = company != null ? company.companyName : "壁新聞",
            emblem = company != null ? view.EmblemOf(company.companyId) : null,
        };

        if (selectedPage == NewsPageTabUI.ShopPage)
        {
            paper.isShopPage = true;
            if (morningReport != null) paper.shopLines.AddRange(morningReport.Peek());
        }
        else
        {
            var onPage = new List<NewsIssueEntry>();
            foreach (var e in TodaysEntries(company))
            {
                if (e.kind == NewsEntryKind.Correction)
                {
                    // 訂正は社の面に関係なく、一面の隅の「お詫びと訂正」枠へ（§10.4）
                    if (selectedPage == FrontPage && e.Article != null) paper.corrections.Add(e.Article.lead);
                    continue;
                }
                // 一面にはその社の記事をすべて載せる。二面以下はその面に割り当てた記事だけ
                if (selectedPage == FrontPage || PageOf(e, company) == selectedPage) onPage.Add(e);
            }
            onPage = SortForFront(onPage);

            // 一面トップ: 小記事から選ばれたものがあればそれ、無ければ面の先頭
            NewsIssueEntry lead = null;
            if (leadKey != null) lead = onPage.Find(e => e.Key == leadKey);
            if (lead == null && onPage.Count > 0) lead = onPage[0];
            leadKey = lead?.Key;

            if (lead != null)
            {
                paper.hasLead = true;
                paper.lead = ToArticle(lead, company == null);
                paper.lead.bodyOpen = true;   // 一面トップは挿絵を置かず本文で埋める
            }
            foreach (var e in onPage)
                if (e != lead) paper.grid.Add(ToArticle(e, company == null));
        }

        view.ShowPaper(paper);

        int idx = Array.IndexOf(PageOrder, selectedPage);
        bool prev = false, next = false;
        for (int i = 0; i < PageOrder.Length; i++)
        {
            if (!pages.Contains(PageOrder[i])) continue;
            if (i < idx) prev = true;
            if (i > idx) next = true;
        }
        view.ShowTabs(pages, selectedPage, prev, next);
    }

    private NewspaperView.ArticleData ToArticle(NewsIssueEntry e, bool withCompany)
    {
        var a = e.Article;
        string key = e.Key;
        return new NewspaperView.ArticleData
        {
            entryKey = key,
            headline = a?.headline ?? "",
            lead = a?.lead ?? "",
            body = a?.body ?? "",
            byline = FormatByline(a?.byline, withCompany ? e.Company?.companyName : null),
            isRead = a != null && newsModel.IsRead(a.id),
            bodyOpen = openedBodies.Contains(key),
            canScrapKind = ScrapbookModel.CanPinKind(e),
            canScrap = scrapbook.CanPin(e),
        };
    }

    /// <summary>署名。無署名は最も危険、という手がかりをそのまま出す。壁新聞では社名も添える。</summary>
    private static string FormatByline(string byline, string companyName)
    {
        // 部署名（王立広報局など）はそのまま、個人名は「記者：」を付ける
        string who = string.IsNullOrEmpty(byline) ? "無署名"
            : byline.EndsWith("局") || byline.EndsWith("部") || byline.EndsWith("課") ? byline
            : $"記者：{byline}";
        return string.IsNullOrEmpty(companyName) ? who : $"{companyName}／{who}";
    }

    private void RenderScraps()
    {
        int turn = Turn;
        int money = tomsModel.PlayerMoney.Value;

        var list = new List<NewspaperView.ScrapData>();
        int unconfirmed = 0;
        foreach (var s in scrapbook.Pinned)
        {
            var entry = newsModel.FindEntry(s.entryKey);
            var state = ScrapbookModel.StateOf(s, entry, turn);
            if (state == ScrapState.Unconfirmed) unconfirmed++;
            list.Add(ToScrap(s, entry, state, money >= NewsTuning.ConfirmCost));
        }
        // 今日確認したものは、空いた枠にそのまま答えを見せておく（翌日には名鑑へ消える）
        foreach (var s in scrapbook.ConfirmedOn(turn))
        {
            if (list.Count >= scrapbook.Capacity) break;
            list.Add(ToScrap(s, newsModel.FindEntry(s.entryKey), ScrapbookModel.StateOf(s, null, turn), false));
        }

        bool canBatch = unconfirmed >= NewsTuning.ConfirmBatchCount && money >= NewsTuning.ConfirmBatchCost;
        view.ShowScraps(list, scrapbook.Pinned.Count, scrapbook.Capacity, canBatch,
            NewsTuning.ConfirmCost, NewsTuning.ConfirmBatchCost);
    }

    private static NewspaperView.ScrapData ToScrap(ScrapbookModel.Scrap s, NewsIssueEntry entry, ScrapState state, bool canAfford)
    {
        // フェーズ3の記事は事象×テンプレートで組み立てて掲載に持たせているので、マスター検索では引けない。掲載から引く
        var a = entry?.Article ?? NewsMasterLoader.FindArticle(s.articleId);
        var company = string.IsNullOrEmpty(s.companyId) ? null : NewsMasterLoader.FindCompany(s.companyId);
        return new NewspaperView.ScrapData
        {
            entryKey = s.entryKey,
            headline = a?.headline ?? "",
            lead = a?.lead ?? "",
            body = a?.body ?? "",
            byline = $"第{s.publishTurn}号／{FormatByline(a?.byline, company?.companyName)}",
            state = state,
            canAfford = canAfford,
        };
    }

    private void RenderMarket()
    {
        var rows = view.MarketRows;
        var items = itemModel?.RuntimeItems;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] == null) continue;
            view.ShowMarketRow(i, NewsMarketSummary.Compute(items, rows[i].Filter));
        }
    }

    // =====================================================================
    // 紙面の中身
    // =====================================================================

    private string FirstSubscribed()
    {
        foreach (var c in NewsMasterLoader.LoadCompanies())
            if (subscriptions.IsSubscribed(c.companyId, Turn)) return c.companyId;
        return null;
    }

    /// <summary>開いたときの面。一面にその社の全記事が載るので、常に一面から。</summary>
    private string FirstPageWithContent() => FrontPage;

    /// <summary>
    /// 面タブ。一面と自店は常設。二面・市況・うわさは<b>その日その面に記事があるときだけ</b>出す
    /// （社が保有する面のうち、記事が割り当てられた面）。壁新聞は一面＋自店。
    /// </summary>
    private HashSet<string> VisiblePages(NewspaperCompanyData company)
    {
        var set = new HashSet<string> { FrontPage, NewsPageTabUI.ShopPage };
        if (company == null) return set;
        foreach (var e in TodaysEntries(company))
        {
            if (e.kind == NewsEntryKind.Correction) continue;
            var p = PageOf(e, company);
            if (p != FrontPage) set.Add(p);
        }
        return set;
    }

    /// <summary>
    /// 紙面の並び。効果のある報道を上（規模の大きい順）、次に効果のない報道・結果記事、埋め草は下段。
    /// </summary>
    private static List<NewsIssueEntry> SortForFront(List<NewsIssueEntry> entries)
    {
        var order = new Dictionary<NewsIssueEntry, int>();
        for (int i = 0; i < entries.Count; i++) order[entries[i]] = i;
        int Rank(NewsIssueEntry e)
        {
            switch (e.kind)
            {
                case NewsEntryKind.Report:
                    var ev = e.eventInstance;
                    bool effect = ev != null && ev.HasTarget && ev.trendDelta != 0f;
                    return (effect ? 0 : 10) - (ev != null ? ev.scaleRank : 0);
                case NewsEntryKind.Result: return 20;
                case NewsEntryKind.Filler: return 30;
                default: return 40;
            }
        }
        var sorted = new List<NewsIssueEntry>(entries);
        sorted.Sort((x, y) =>
        {
            int c = Rank(x).CompareTo(Rank(y));
            return c != 0 ? c : order[x].CompareTo(order[y]);
        });
        return sorted;
    }

    /// <summary>その社の面に載る位置。社が持たない面の記事（結果記事など）は一面へ寄せる。</summary>
    private static string PageOf(NewsIssueEntry e, NewspaperCompanyData company)
    {
        string page = e.Article?.page;
        if (string.IsNullOrEmpty(page)) return FrontPage;
        if (company == null) return FrontPage;
        return company.HasPage(page) ? page : FrontPage;
    }

    /// <summary>
    /// 今日の紙面に載る掲載。社を選んでいればその社の号、壁新聞なら全社の一面記事から
    /// <see cref="NewsTuning.WallPaperArticles"/> 本だけ（§8 未購読時）。
    /// </summary>
    private List<NewsIssueEntry> TodaysEntries(NewspaperCompanyData company)
    {
        var sorted = SortForPaper(newsModel.IssueOf(Turn));
        var list = new List<NewsIssueEntry>();
        if (company != null)
        {
            foreach (var e in sorted)
                if (e.companyId == company.companyId) list.Add(e);
            return list;
        }

        foreach (var e in sorted)
        {
            if (list.Count >= NewsTuning.WallPaperArticles) break;
            if (e.kind != NewsEntryKind.Report) continue;
            if (e.Article?.page != FrontPage) continue;
            list.Add(e);
        }
        // 一面記事が無い日は、どの面でも構わないので1本は出す（画面を空にしない）。報道が無ければ埋め草
        if (list.Count == 0)
            foreach (var e in sorted)
                if (e.kind == NewsEntryKind.Report) { list.Add(e); break; }
        if (list.Count == 0)
            foreach (var e in sorted)
                if (e.kind == NewsEntryKind.Filler) { list.Add(e); break; }
        return list;
    }

    /// <summary>
    /// 紙面の並び順。通常記事を面の順（一面→二面→市況→うわさ）に並べ、
    /// その後ろに結果記事、<b>訂正記事は最後（隅）</b>に置く（Docs/News_Spec.md §10.4）。
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

    // =====================================================================
    // 操作
    // =====================================================================

    /// <summary>記事のクリック。小記事なら一面トップへ差し替える（トップは常に本文まで出す）。既読にする。</summary>
    private void OpenArticle(string entryKey)
    {
        var entry = newsModel.FindEntry(entryKey);
        var a = entry?.Article;
        if (a == null) return;

        bool changed = leadKey != entryKey;
        leadKey = entryKey;

        if (!newsModel.IsRead(a.id))
        {
            newsModel.MarkRead(a.id);
            Save();
            changed = true;
        }
        if (changed) RenderPaper();
    }

    private void PinArticle(string entryKey)
    {
        var entry = newsModel.FindEntry(entryKey);
        if (entry == null || !scrapbook.Pin(entry, Turn)) return;
        Save();
        RenderPaper();
        RenderScraps();
        view.FlyToScrap(entryKey);
    }

    private void SelectCompany(string companyId)
    {
        if (!subscriptions.IsSubscribed(companyId, Turn) || companyId == selectedCompanyId) return;
        selectedCompanyId = companyId;
        selectedPage = FirstPageWithContent();
        leadKey = null;
        RenderCompanies();
        RenderPaper();
        view.AnimatePaper(0);
    }

    private void Subscribe(string companyId)
    {
        var company = NewsMasterLoader.FindCompany(companyId);
        int turn = Turn;
        if (!subscriptions.CanSubscribe(company, turn, Slots, tomsModel.PlayerMoney.Value)) return;

        tomsModel.PurchaseItem(company.price);
        subscriptions.Subscribe(company, turn);
        tomsModel.SavePlayerMoney();
        Save();

        // 契約したらその社の号を開く
        selectedCompanyId = companyId;
        selectedPage = FirstPageWithContent();
        leadKey = null;
        RenderAll();
        view.PopCompany(companyId);
        view.AnimatePaper(0);
    }

    private void SelectPage(string page)
    {
        if (page == selectedPage) return;
        var company = selectedCompanyId != null ? NewsMasterLoader.FindCompany(selectedCompanyId) : null;
        if (!VisiblePages(company).Contains(page)) return;
        int dir = Math.Sign(Array.IndexOf(PageOrder, page) - Array.IndexOf(PageOrder, selectedPage));
        selectedPage = page;
        leadKey = null;
        RenderPaper();
        view.AnimatePaper(dir);
    }

    private void StepPage(int dir)
    {
        var company = selectedCompanyId != null ? NewsMasterLoader.FindCompany(selectedCompanyId) : null;
        var pages = VisiblePages(company);
        int idx = Array.IndexOf(PageOrder, selectedPage);
        for (int i = idx + dir; i >= 0 && i < PageOrder.Length; i += dir)
        {
            if (!pages.Contains(PageOrder[i])) continue;
            SelectPage(PageOrder[i]);
            return;
        }
    }

    private void ConfirmOne(string entryKey)
    {
        if (tomsModel.PlayerMoney.Value < NewsTuning.ConfirmCost) return;
        var entry = newsModel.FindEntry(entryKey);
        if (entry == null || !scrapbook.Confirm(entryKey, entry, Turn)) return;

        tomsModel.PurchaseItem(NewsTuning.ConfirmCost);
        tomsModel.SavePlayerMoney();
        Save();
        AfterConfirm();
    }

    /// <summary>決着・未確認が3件そろっているときだけ、まとめて 4,000G で確認する。</summary>
    private void ConfirmBatch()
    {
        if (tomsModel.PlayerMoney.Value < NewsTuning.ConfirmBatchCost) return;

        int turn = Turn;
        var targets = new List<(string key, NewsIssueEntry entry)>();
        foreach (var s in scrapbook.Pinned)
        {
            var entry = newsModel.FindEntry(s.entryKey);
            if (ScrapbookModel.StateOf(s, entry, turn) == ScrapState.Unconfirmed) targets.Add((s.entryKey, entry));
            if (targets.Count >= NewsTuning.ConfirmBatchCount) break;
        }
        if (targets.Count < NewsTuning.ConfirmBatchCount) return;

        foreach (var (key, entry) in targets) scrapbook.Confirm(key, entry, turn);
        tomsModel.PurchaseItem(NewsTuning.ConfirmBatchCost);
        tomsModel.SavePlayerMoney();
        Save();
        AfterConfirm();
    }

    private void AfterConfirm()
    {
        RenderHeader();
        RenderCompanies();  // 実測バー・購読ボタンの可否（所持金）が変わる
        RenderPaper();      // スクラップ枠が空いたのでボタンの可否が変わる
        RenderScraps();
    }

    public void Dispose() => disposables.Dispose();
}

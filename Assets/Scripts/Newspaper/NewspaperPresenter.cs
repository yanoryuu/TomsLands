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


    // 画面内の選択状態（保存しない）
    private string selectedCompanyId;   // null = 壁新聞
    private int issueOffset;            // 0 = 今日の号 / -1〜-BackIssueDays = 前の号（I-8）
    private string selectedPage = FrontPage;
    private string leadKey;             // 一面トップに出している掲載
    private readonly HashSet<string> openedBodies = new();

    // TomsModel.CurrentTurn はラン終了時にしか同期されないので、進行中のターンは GameFlowManager から取る
    private int Turn => gameFlowManager != null ? gameFlowManager.CurrentTurn.Value : tomsModel.CurrentTurn.Value;

    /// <summary>紙面に出している号（前の号を開いているときは過去のターン）。</summary>
    private int IssueTurn => Math.Max(1, Turn + issueOffset);

    /// <summary>前の号を何号さかのぼれるか（I-8）。</summary>
    public const int BackIssueDays = 3;

    /// <summary>左ペインの壁新聞カードの社ID（購読中でも壁新聞を開けるようにする。I-1）。</summary>
    public const string WallPaperId = "";

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
        view.OnIssueStep.Subscribe(StepIssue).AddTo(disposables);
        view.OnPredictionChanged.Subscribe(p => SetPrediction(p.entryKey, p.value)).AddTo(disposables);
        view.OnVetClicked.Subscribe(VetArticle).AddTo(disposables);
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
        // 続報（結果記事・訂正記事）が出たスクラップは、購読していなくても届いて無料で名鑑へ移る（I-3）
        if (scrapbook.DeliverFollowUps(newsModel, Turn) > 0) Save();

        // 読む社: 前回の社がまだ購読中ならそれ、無ければ最初の購読社、無ければ壁新聞
        if (selectedCompanyId == null || !subscriptions.IsSubscribed(selectedCompanyId, Turn))
            selectedCompanyId = FirstSubscribed();
        selectedPage = FirstPageWithContent();
        leadKey = null;
        issueOffset = 0;
        openedBodies.Clear();

        RenderAll();  // パネルのフェードは GamePanelManager が行う
        view.PlayIntro();
    }

    private void EnsureLoaded(int seed) => NewspaperSaveStore.EnsureLoaded(seed, subscriptions, scrapbook, newsModel);

    /// <summary>
    /// 今日の号でまだ読んでいない報道の本数（店ホームの新聞ボタンのバッジ。I-7）。購読中の社の号と壁新聞の報道を数える。
    /// </summary>
    public static int CountUnread(NewsModel news, NewspaperSubscriptionModel subscriptions, int turn)
    {
        if (news == null || !news.IsBuilt) return 0;
        var issue = news.IssueOf(turn);
        var seen = new HashSet<string>();
        int n = 0;
        void Count(NewsIssueEntry e)
        {
            if (e.kind != NewsEntryKind.Report || !seen.Add(e.Key) || news.IsRead(e.Key)) return;
            n++;
        }
        foreach (var e in issue)
            if (subscriptions != null && subscriptions.IsSubscribed(e.companyId, turn)) Count(e);
        foreach (var e in WallPaperEntries(issue)) Count(e);
        return n;
    }

    private void Save() => NewspaperSaveStore.Save(tomsModel.FlowSeed, subscriptions, scrapbook, newsModel);

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
        // 表示用の日数（今日 0・明日 1）。GetTurnsUntilNextBattle は前日に 0 になるので使わない
        int untilBattle = gameFlowManager != null ? gameFlowManager.GetDaysUntilNextStream() : -1;
        view.ShowHeader($"第{IssueTurn}号", tomsModel.PlayerMoney.Value, untilBattle);
        view.ShowIssueStep(CanStepIssue(-1), CanStepIssue(1), issueOffset < 0);
    }

    private int Slots => NewsTuning.SubscriptionSlotsFor(tomsModel.ShopLevel.Value);

    private void RenderCompanies()
    {
        int turn = Turn;
        int money = tomsModel.PlayerMoney.Value;
        int slots = Slots;

        // 壁新聞（無料）。購読中でもここを押せば壁新聞を読める（I-1）
        view.ShowWallPaperCard(selectedCompanyId == null);

        var list = new List<NewspaperCompanyCardUI.Data>();
        foreach (var c in NewsMasterLoader.LoadCompanies())
        {
            bool subscribed = subscriptions.IsSubscribed(c.companyId, turn);
            list.Add(new NewspaperCompanyCardUI.Data
            {
                companyId = c.companyId,
                companyName = c.companyName,
                emblem = view.EmblemOf(c.companyId),
                price = NewspaperSubscriptionModel.PriceOf(c),
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
                paper.lead = ToArticle(lead, company);
                paper.lead.bodyOpen = true;   // 一面トップは挿絵を置かず本文で埋める
            }
            foreach (var e in onPage)
                if (e != lead) paper.grid.Add(ToArticle(e, company));
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

    private NewspaperView.ArticleData ToArticle(NewsIssueEntry e, NewspaperCompanyData company)
    {
        var a = e.Article;
        string key = e.Key;
        // 載った面（うわさ欄など）を署名の前に添える。誤報は一面以外に回ることがあるので、これが手がかりになる（I-9）。
        // 壁新聞は社名を伏せる（I-1）
        string pageLabel = company != null ? PageLabel(PageOf(e, company)) : null;
        string byline = FormatByline(a?.byline, null);
        return new NewspaperView.ArticleData
        {
            entryKey = key,
            headline = a?.headline ?? "",
            lead = a?.lead ?? "",
            body = a?.body ?? "",
            byline = string.IsNullOrEmpty(pageLabel) ? byline : $"{pageLabel}／{byline}",
            isRead = a != null && newsModel.IsRead(key),
            bodyOpen = openedBodies.Contains(key),
            canScrapKind = ScrapbookModel.CanPinKind(e),
            canScrap = scrapbook.CanPin(e),
            showVet = CanVetArticles && scrapbook.IsVettable(e),
            canVet = CanVetArticles && scrapbook.CanVet(e, VetWindow),
            vetState = scrapbook.VetStateOf(key),
            vetReveal = key == justVettedKey,
        };
    }

    /// <summary>一面以外の面の呼び名（一面は空）。</summary>
    private static string PageLabel(string page) => page switch
    {
        "second" => "二面",
        "market" => "市況欄",
        "rumor" => "うわさ欄",
        _ => null,
    };

    /// <summary>署名。無署名は最も危険、という手がかりをそのまま出す。</summary>
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
        // 今日確認したもの・続報が届いたものは、空いた枠にそのまま答えを見せておく（翌日には名鑑へ消える）
        foreach (var s in scrapbook.ConfirmedOn(turn))
        {
            if (list.Count >= scrapbook.Capacity) break;
            list.Add(ToScrap(s, newsModel.FindEntry(s.entryKey), ScrapbookModel.StateOf(s, null, turn), false));
        }

        bool canBatch = unconfirmed >= NewsTuning.ConfirmBatchCount && money >= NewsTuning.ConfirmBatchCost;
        view.ShowScraps(list, scrapbook.Pinned.Count, scrapbook.Capacity, canBatch,
            NewsTuning.ConfirmCost, NewsTuning.ConfirmBatchCost);
    }

    private NewspaperView.ScrapData ToScrap(ScrapbookModel.Scrap s, NewsIssueEntry entry, ScrapState state, bool canAfford)
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
            followUp = FollowUpText(s),
            answer = AnswerText(s, entry, state),
            prediction = s.prediction ?? string.Empty,
            vetState = scrapbook.VetStateOf(s.entryKey),
        };
    }

    /// <summary>届いた続報の見出しとリード（無ければ空）。</summary>
    private string FollowUpText(ScrapbookModel.Scrap s)
    {
        if (string.IsNullOrEmpty(s.followUpKey)) return string.Empty;
        var a = newsModel.FindEntry(s.followUpKey)?.Article;
        if (a == null) return string.Empty;
        return string.IsNullOrEmpty(a.lead) ? a.headline : $"{a.headline}\n{a.lead}";
    }

    /// <summary>
    /// 答え合わせ（I-4）: 確認した・続報が届いたスクラップに「何に効いたか」と掲載の日からの値動きを出す。
    /// 答えが出る前は出さない（記事から銘柄へのリンクを作らない約束。News_Spec §9.3）。予想を書いていれば当たり外れも。
    /// </summary>
    private string AnswerText(ScrapbookModel.Scrap s, NewsIssueEntry entry, ScrapState state)
    {
        var ev = entry?.eventInstance;
        bool resolved = state == ScrapState.ConfirmedHit || state == ScrapState.ConfirmedMiss || state == ScrapState.ConfirmedPartial;
        if (ev == null || !resolved) return string.Empty;
        string target = ScrapPrediction.TargetLabel(ev);
        if (string.IsNullOrEmpty(target)) return string.Empty;
        var lines = new List<string>();
        float? move = PriceMoveSince(ev, s.publishTurn);
        string moveText = move.HasValue ? $"（第{s.publishTurn}号の日から {move.Value * 100f:+0;-0}%）" : string.Empty;
        lines.Add(state == ScrapState.ConfirmedMiss ? $"記事が言っていた効き先: {target}{moveText}" : $"効き先: {target}{moveText}");
        if (!string.IsNullOrEmpty(s.prediction))
        {
            bool ok = state != ScrapState.ConfirmedMiss && ScrapPrediction.IsCorrect(s.prediction, ev);
            lines.Add($"予想: {ScrapPrediction.Label(s.prediction)} → {(ok ? "的中" : "外れ")}");
        }
        return string.Join("\n", lines);
    }

    /// <summary>効き先の品（解放済み）の、掲載の日から今日までの値動きの平均。品が無ければ null。</summary>
    private float? PriceMoveSince(NewsEventInstance ev, int publishTurn)
    {
        var items = itemModel?.RuntimeItems;
        if (items == null) return null;
        int back = Math.Max(0, Turn - publishTurn);
        int level = tomsModel.BlacksmithLevel.Value;
        double sum = 0; int n = 0;
        foreach (var r in items)
        {
            if (r.RequiredLevel.Value > level || !ev.Matches(r)) continue;
            var h = r.ShopPriceHistory;
            if (h == null || h.Count <= back) continue;
            int then = h[h.Count - 1 - back];
            if (then <= 0) continue;
            sum += Math.Log(r.CurrentPrice.Value / (double)then);
            n++;
        }
        return n > 0 ? (float)(Math.Exp(sum / n) - 1.0) : (float?)null;
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
    private static List<NewsIssueEntry> SortForFront(List<NewsIssueEntry> entries) => StableSorted(entries, FrontRankOf);

    private static int FrontRankOf(NewsIssueEntry e)
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
        var sorted = SortForPaper(newsModel.IssueOf(IssueTurn));
        var list = new List<NewsIssueEntry>();
        if (company != null)
        {
            foreach (var e in sorted)
                if (e.companyId == company.companyId) list.Add(e);
            return list;
        }
        return WallPaperEntries(sorted);
    }

    /// <summary>
    /// 壁新聞（無料）の中身（I-1）: <b>かわら版の一面の報道1本（社名は伏せる）＋埋め草</b>だけ。
    /// 以前は全社の一面から2本を出していたので、王立官報・ギルド報の有料の一面がただで読めた。
    /// AutoPlay のヘッドレス（AutoPlayHeadlessGame.Snapshot）も同じ規則を使う。
    /// </summary>
    public static List<NewsIssueEntry> WallPaperEntries(List<NewsIssueEntry> sortedIssue)
    {
        var list = new List<NewsIssueEntry>();
        foreach (var e in sortedIssue)
        {
            if (e.kind != NewsEntryKind.Report || e.companyId != NewsTuning.WallPaperCompany || e.Article?.page != FrontPage) continue;
            list.Add(e);
            break;
        }
        var fillers = new HashSet<string>();
        foreach (var e in sortedIssue)
        {
            if (list.Count >= NewsTuning.WallPaperArticles + 1) break;
            if (e.kind != NewsEntryKind.Filler || !fillers.Add(e.articleId)) continue;
            list.Add(e);
        }
        return list;
    }

    /// <summary>
    /// 紙面の並び順。通常記事を面の順（一面→二面→市況→うわさ）に並べ、
    /// その後ろに結果記事、<b>訂正記事は最後（隅）</b>に置く（Docs/News_Spec.md §10.4）。
    /// </summary>
    private static List<NewsIssueEntry> SortForPaper(List<NewsIssueEntry> entries) => StableSorted(entries, RankOf);

    /// <summary>rank の昇順に並べた新しいリスト。</summary>
    private static List<NewsIssueEntry> StableSorted(List<NewsIssueEntry> entries, Func<NewsIssueEntry, int> rank)
    {
        // List.Sort は安定でないので、元の並び（カレンダー順）を最後のキーにする
        var order = new Dictionary<NewsIssueEntry, int>();
        for (int i = 0; i < entries.Count; i++) order[entries[i]] = i;

        var sorted = new List<NewsIssueEntry>(entries);
        sorted.Sort((x, y) =>
        {
            int c = rank(x).CompareTo(rank(y));
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

        // 既読は掲載ごとのキー（号:社:記事）で覚える。テンプレート ID だと、同じ系統の事象や埋め草が
        // 後日また載ったとき（文面は別の新しい記事）に最初から既読になり NEW が付かない
        if (!newsModel.IsRead(entryKey))
        {
            newsModel.MarkRead(entryKey);
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
        // 壁新聞カード（I-1）
        if (string.IsNullOrEmpty(companyId))
        {
            if (selectedCompanyId == null) return;
            selectedCompanyId = null;
            selectedPage = FirstPageWithContent();
            leadKey = null;
            RenderHeader();
            RenderCompanies();
            RenderPaper();
            view.AnimatePaper(0);
            return;
        }
        if (!subscriptions.IsSubscribed(companyId, IssueTurn) || companyId == selectedCompanyId) return;
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

        tomsModel.PurchaseItem(NewspaperSubscriptionModel.PriceOf(company));
        subscriptions.Subscribe(company, turn);
        tomsModel.SavePlayerMoney();
        Save();

        // 契約したらその社の今日の号を開く
        selectedCompanyId = companyId;
        selectedPage = FirstPageWithContent();
        leadKey = null;
        issueOffset = 0;
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

    // ---------------- 前の号（I-8） ----------------

    /// <summary>dir 号ぶん動けるか。前の号は BackIssueDays まで。社の号は、その日に購読していた号だけ読める。</summary>
    private bool CanStepIssue(int dir)
    {
        int next = issueOffset + dir;
        if (next > 0 || next < -BackIssueDays || Turn + next < 1) return false;
        return selectedCompanyId == null || subscriptions.IsSubscribed(selectedCompanyId, Turn + next);
    }

    private void StepIssue(int dir)
    {
        if (!CanStepIssue(dir)) return;
        issueOffset += dir;
        selectedPage = FirstPageWithContent();
        leadKey = null;
        RenderHeader();
        RenderPaper();
        view.AnimatePaper(dir);
    }

    // ---------------- 予想（I-6） ----------------

    private void SetPrediction(string entryKey, string value)
    {
        if (!scrapbook.SetPrediction(entryKey, value)) return;
        Save();
        RenderScraps();
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

    // ---------------- 裏取り（酒場 Lv5。News_Spec §9.4） ----------------

    private string justVettedKey;

    private static bool CanVetArticles => VillageEffects.Current.CanVetArticles;

    /// <summary>今の「配信の間」（次の配信がこのランの何回目か。配信が残っていなければ 0）。裏取りは1つの間に1回。</summary>
    private int VetWindow
    {
        get
        {
            if (gameFlowManager == null) return 0;
            gameFlowManager.GetStreamProgress(out int upcoming, out _);
            return upcoming;
        }
    }

    /// <summary>
    /// 一面トップの記事の裏を取る（無料）。真偽（本物／誇張／誤報）だけが分かり、ハンコが付く。
    /// 効き先・名鑑・社の実測的中率・スクラップの枠は変えない（答え合わせは確認・続報のときだけ）。
    /// </summary>
    private void VetArticle(string entryKey)
    {
        if (!CanVetArticles) return;
        var entry = newsModel.FindEntry(entryKey);
        if (entry == null || !scrapbook.VetArticle(entry, VetWindow, Turn)) return;
        Save();
        justVettedKey = entryKey;
        RenderPaper();
        RenderScraps();
        justVettedKey = null;
    }

    public void Dispose() => disposables.Dispose();
}

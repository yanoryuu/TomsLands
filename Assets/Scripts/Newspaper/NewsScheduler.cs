using System;
using System.Collections.Generic;

/// <summary>紙面上の記事の種類。</summary>
public enum NewsEntryKind
{
    Report,      // 通常の記事（原因の報道）。効果を持ちうる
    Result,      // 結果記事。本物の記事が決着したことの報道。効果なし
    Correction,  // 訂正記事。誤報だったことの報道。効果なし。紙面の最後（隅）に置く
}

/// <summary>紙面に載った記事1本。どの社が、何ターン目に載せ、いつ効き始めるか。</summary>
public class NewsIssueEntry
{
    public int publishTurn;   // 掲載ターン
    public int effectTurn;    // 効果の発現ターン（publishTurn + 社の leadTurns）
    public string companyId;
    public string articleId;

    public NewsEntryKind kind = NewsEntryKind.Report;

    /// <summary>続報（結果・訂正）のとき、元になった記事のID。通常記事は空。</summary>
    public string parentArticleId;

    /// <summary>
    /// この掲載が誤報か。<b>真偽は記事ではなく掲載ごとに持つ</b>。
    /// 現状は記事の truth をそのまま写すが、フェーズ3で社の falseRate により
    /// 同じ記事が社によって誤報になる形へ広げるため、効果の判定はこちらを見る。
    /// </summary>
    public bool isFalseReport;

    /// <summary>2段目（trendDelta / demandKick）に掛ける倍率。本物 1 / 誇張 0.5 / 誤報 0。</summary>
    public float effectScale = 1f;

    // 経済計算で品目数 × カレンダー本数ぶん引かれるので、マスターの線形検索を1回で済ませる
    private NewsArticleData _article;
    public NewsArticleData Article => _article ??= NewsMasterLoader.FindArticle(articleId);
    public NewspaperCompanyData Company => NewsMasterLoader.FindCompany(companyId);

    /// <summary>決着ターン（発効 + duration）。この日に効果が切れる。</summary>
    public int SettleTurn
    {
        get
        {
            var a = Article;
            return effectTurn + Math.Max(1, a != null ? a.durationTurns : 1);
        }
    }

    /// <summary>2段目（実需）が来る掲載か。誤報・続報・効果なしの記事は来ない。</summary>
    public bool HasRealEffect
    {
        get
        {
            if (kind != NewsEntryKind.Report || isFalseReport || effectScale <= 0f) return false;
            var a = Article;
            return a != null && a.HasEffect;
        }
    }
}

/// <summary>
/// 周の開始時に「発行カレンダー」を一括生成する。純粋関数（同じ入力なら必ず同じ結果）。
///
/// 毎ターン乱数で引くと「何も起きない周」が出てしまうため、先にまとめて組む。
/// シードだけ保存すれば再現できるので、記事本文はセーブ対象にならない
/// （Docs/News_Spec.md §6.1）。
/// </summary>
public static class NewsScheduler
{
    /// <summary>
    /// カレンダーを組む。
    /// </summary>
    /// <param name="seed">周のシード（TomsModel.FlowSeed を想定）</param>
    /// <param name="maxTurn">生成する最終ターン</param>
    /// <param name="companies">新聞社一覧</param>
    /// <param name="articles">記事一覧</param>
    public static List<NewsIssueEntry> Build(
        int seed, int maxTurn,
        IReadOnlyList<NewspaperCompanyData> companies,
        IReadOnlyList<NewsArticleData> articles)
    {
        var result = new List<NewsIssueEntry>();
        if (companies == null || companies.Count == 0) return result;
        if (articles == null || articles.Count == 0) return result;

        var rng = new System.Random(seed);

        // 記事プール。使い切ったら切り直す（同じ記事が近いターンに固まらないように）
        // 続報（結果・訂正）は単独では抽選しない。親記事の決着に合わせて後から差し込む。
        var followUps = CollectFollowUpIds(articles);
        var pool = new List<NewsArticleData>();
        foreach (var a in articles)
            if (!a.IsCorrection && !followUps.Contains(a.id)) pool.Add(a);
        if (pool.Count == 0) return result;
        Shuffle(pool, rng);
        int cursor = 0;

        // 1ターンに載る本数は、社の articlesPerIssue の合計を上限としつつ、
        // フェーズ1では記事の総数が少ないので控えめに出す。
        int perTurn = Math.Max(1, Math.Min(3, pool.Count / 3));

        for (int turn = 1; turn <= maxTurn; turn++)
        {
            for (int i = 0; i < perTurn; i++)
            {
                if (cursor >= pool.Count)
                {
                    Shuffle(pool, rng);
                    cursor = 0;
                }
                var article = pool[cursor++];

                var company = PickCompany(companies, article, rng);
                if (company == null) continue;

                result.Add(new NewsIssueEntry
                {
                    publishTurn = turn,
                    effectTurn = turn + Math.Max(0, company.leadTurns),
                    companyId = company.companyId,
                    articleId = article.id,
                    kind = NewsEntryKind.Report,
                    isFalseReport = article.IsFalse,
                    effectScale = article.IsFalse ? 0f
                                : article.IsExaggerated ? NewsTuning.ExaggeratedScale
                                : 1f,
                });
            }
        }

        // 続報は乱数を使わずに差し込む。本編の抽選系列を変えないので、
        // 続報の有無で同じシードの紙面が変わることはない。
        AppendFollowUps(result, maxTurn, articles);

        return result;
    }

    /// <summary>
    /// 決着した記事の続報を、決着ターンの紙面へ差し込む。
    /// 本物（誇張を含む）は結果記事、誤報は訂正記事。<b>同じ社が自分の記事の続報を出す</b>
    /// （訂正は誤った社自身が出す。社の当たり外れを紙面で学べるように）。
    /// 用意されていない側は何も出さない（決着は値動きだけで察するしかない）。
    /// </summary>
    private static void AppendFollowUps(
        List<NewsIssueEntry> entries, int maxTurn, IReadOnlyList<NewsArticleData> articles)
    {
        var byId = new Dictionary<string, NewsArticleData>();
        foreach (var x in articles) byId[x.id] = x;

        // 同じ記事が一巡して再掲されると、同じ日に同じ続報が重なりうる。1本にまとめる
        var placed = new HashSet<(int, string)>();

        int count = entries.Count;
        for (int i = 0; i < count; i++)
        {
            var parent = entries[i];
            if (!byId.TryGetValue(parent.articleId, out var a)) continue;
            if (string.IsNullOrEmpty(a.followUpId)) continue;

            var followUp = PickFollowUp(a, parent.isFalseReport, byId);
            if (followUp == null) continue;

            int turn = parent.effectTurn + Math.Max(1, a.durationTurns)
                + (parent.isFalseReport ? NewsTuning.CorrectionDelay : NewsTuning.ResultDelay);
            if (turn > maxTurn) continue;
            if (!placed.Add((turn, followUp.id))) continue;

            entries.Add(new NewsIssueEntry
            {
                publishTurn = turn,
                effectTurn = turn,
                companyId = parent.companyId,
                articleId = followUp.id,
                kind = parent.isFalseReport ? NewsEntryKind.Correction : NewsEntryKind.Result,
                parentArticleId = a.id,
                isFalseReport = false,
                effectScale = 0f,   // 続報は値を動かさない。答え合わせだけ
            });
        }
    }

    /// <summary>親の followUpId から、真偽に合う続報を選ぶ（誤報 → 訂正記事 / 本物 → それ以外）。</summary>
    private static NewsArticleData PickFollowUp(
        NewsArticleData parent, bool isFalseReport, Dictionary<string, NewsArticleData> byId)
    {
        foreach (var id in parent.FollowUpIds)
        {
            if (!byId.TryGetValue(id, out var f)) continue;
            if (f.IsCorrection == isFalseReport) return f;
        }
        return null;
    }

    private static HashSet<string> CollectFollowUpIds(IReadOnlyList<NewsArticleData> articles)
    {
        var set = new HashSet<string>();
        foreach (var a in articles)
            foreach (var id in a.FollowUpIds) set.Add(id);
        return set;
    }

    /// <summary>
    /// 記事のカテゴリと面を扱える社から1つ選ぶ。該当が無ければどこかの社に載せる。
    /// lead は社が持つので、どの社が載せたかで「何ターン前に知れるか」が変わる。
    /// </summary>
    private static NewspaperCompanyData PickCompany(
        IReadOnlyList<NewspaperCompanyData> companies, NewsArticleData article, System.Random rng)
    {
        var fit = new List<NewspaperCompanyData>();
        foreach (var c in companies)
            if (c.HandlesCategory(article.category) && c.HasPage(article.page)) fit.Add(c);

        if (fit.Count == 0)
            foreach (var c in companies)
                if (c.HasPage(article.page)) fit.Add(c);

        if (fit.Count == 0) return companies[rng.Next(companies.Count)];
        return fit[rng.Next(fit.Count)];
    }

    private static void Shuffle<T>(IList<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

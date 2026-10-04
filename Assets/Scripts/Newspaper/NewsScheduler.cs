using System;
using System.Collections.Generic;

/// <summary>紙面に載った記事1本。どの社が、何ターン目に載せ、いつ効き始めるか。</summary>
public class NewsIssueEntry
{
    public int publishTurn;   // 掲載ターン
    public int effectTurn;    // 効果の発現ターン（publishTurn + 社の leadTurns）
    public string companyId;
    public string articleId;

    public NewsArticleData Article => NewsMasterLoader.FindArticle(articleId);
    public NewspaperCompanyData Company => NewsMasterLoader.FindCompany(companyId);
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
        var pool = new List<NewsArticleData>(articles);
        Shuffle(pool, rng);
        int cursor = 0;

        // 1ターンに載る本数は、社の articlesPerIssue の合計を上限としつつ、
        // フェーズ1では記事の総数が少ないので控えめに出す。
        int perTurn = Math.Max(1, Math.Min(3, articles.Count / 3));

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
                });
            }
        }

        return result;
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

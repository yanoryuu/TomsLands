using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 発行カレンダーと既読状態を持つ。記事本文はマスター側にあるので、
/// このモデルが保存する必要があるのは<b>シードと既読フラグだけ</b>。
/// </summary>
public class NewsModel
{
    /// <summary>カレンダーを生成しておく最大ターン。Long モードでも十分足りる長さ。</summary>
    private const int CalendarTurns = 200;

    private readonly List<NewsIssueEntry> calendar = new();
    private readonly HashSet<string> readArticleIds = new();

    public int Seed { get; private set; }
    public bool IsBuilt => calendar.Count > 0;

    /// <summary>周のシードからカレンダーを組む。既に同じシードで組んであれば何もしない。</summary>
    public void Build(int seed)
    {
        if (IsBuilt && Seed == seed) return;

        Seed = seed;
        calendar.Clear();
        calendar.AddRange(NewsScheduler.Build(
            seed, CalendarTurns,
            NewsMasterLoader.LoadCompanies(),
            NewsMasterLoader.LoadArticles()));

        Debug.Log($"[NewsModel] seed={seed} でカレンダーを生成しました（{calendar.Count} 本）。");
    }

    /// <summary>そのターンの紙面（掲載された記事）。</summary>
    public List<NewsIssueEntry> IssueOf(int turn)
    {
        var list = new List<NewsIssueEntry>();
        foreach (var e in calendar)
            if (e.publishTurn == turn) list.Add(e);
        return list;
    }

    /// <summary>
    /// そのターンに<b>効果が生きている</b>記事。
    /// 誤報は効果を持たない（1段目の値動きだけ起きて、実体が来ないのが誤報の正体）。
    /// </summary>
    public List<NewsIssueEntry> ActiveEffectsOn(int turn)
    {
        var list = new List<NewsIssueEntry>();
        foreach (var e in calendar)
        {
            var a = e.Article;
            if (a == null || a.IsFalse || !a.HasEffect) continue;

            int duration = Mathf.Max(1, a.durationTurns);
            if (turn >= e.effectTurn && turn < e.effectTurn + duration) list.Add(e);
        }
        return list;
    }

    /// <summary>そのターンに発効したばかりの記事（需要への直撃は1回だけ）。</summary>
    public List<NewsIssueEntry> JustTriggeredOn(int turn)
    {
        var list = new List<NewsIssueEntry>();
        foreach (var e in calendar)
        {
            var a = e.Article;
            if (a == null || a.IsFalse || !a.HasEffect) continue;
            if (e.effectTurn == turn) list.Add(e);
        }
        return list;
    }

    public bool IsRead(string articleId) => readArticleIds.Contains(articleId);
    public void MarkRead(string articleId) => readArticleIds.Add(articleId);

    // --- セーブ/ロード ---

    public List<string> ReadIdsToPlain() => new(readArticleIds);

    public void ReadIdsFromPlain(IEnumerable<string> ids)
    {
        readArticleIds.Clear();
        if (ids == null) return;
        foreach (var id in ids) readArticleIds.Add(id);
    }
}

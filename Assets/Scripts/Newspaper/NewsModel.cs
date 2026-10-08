using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 発行カレンダーと既読状態を持つ。記事本文はマスター側にあるので、
/// このモデルが保存する必要があるのは<b>シードと既読フラグだけ</b>。
///
/// フェーズ3（Docs/News_Phase3_Spec.md）からは、カレンダーは「事象」と「報道（社 × ターン）」の2つ。
/// <b>効果は事象が持つ</b>ので、同じ事象を何社が報じても効果は1回になる。
/// 事象マスター（NewsEvents.csv）が空なら、旧形式（NewsArticles.csv）のスケジューラで組む。
/// </summary>
public class NewsModel
{
    /// <summary>カレンダーを生成しておく最大ターン。Long モードでも十分足りる長さ。</summary>
    public const int CalendarTurns = 200;

    private readonly List<NewsIssueEntry> calendar = new();
    private readonly List<NewsEventInstance> events = new();
    private readonly HashSet<string> readArticleIds = new();

    private NewsWorld world;
    private string builtWorldPrint;

    public int Seed { get; private set; }
    public bool IsBuilt => calendar.Count > 0;
    /// <summary>フェーズ3の事象＋テンプレートで組んだか（false なら旧形式）。</summary>
    public bool IsPhase3 { get; private set; }

    public IReadOnlyList<NewsEventInstance> Events => events;
    public IReadOnlyList<NewsIssueEntry> Calendar => calendar;

    /// <summary>
    /// ダンジョンの名前・弱点と戦闘の予定を渡す（GameFlowManager がフローを組んだ後に呼ぶ）。
    /// 渡し直すと、次の Build で組み直す。
    /// </summary>
    public void SetWorld(NewsWorld w)
    {
        world = w;
    }

    /// <summary>周のシードからカレンダーを組む。同じシード・同じ世界で組んであれば何もしない。</summary>
    public void Build(int seed)
    {
        string print = world != null ? world.Fingerprint() : string.Empty;
        if (IsBuilt && Seed == seed && builtWorldPrint == print) return;

        Seed = seed;
        builtWorldPrint = print;
        calendar.Clear();
        events.Clear();

        var companies = NewsMasterLoader.LoadCompanies();
        var evMaster = NewsMasterLoader.LoadEvents();
        var templates = NewsMasterLoader.LoadTemplates();

        if (evMaster.Count > 0 && templates.Count > 0)
        {
            IsPhase3 = true;
            var r = NewsCalendarBuilder.Build(seed, CalendarTurns, companies, evMaster, templates,
                NewsMasterLoader.LoadFillers(), world);
            events.AddRange(r.events);
            calendar.AddRange(r.entries);
        }
        else
        {
            IsPhase3 = false;
            calendar.AddRange(NewsScheduler.Build(seed, CalendarTurns, companies, NewsMasterLoader.LoadArticles()));
            WrapLegacyEvents();
        }

        Debug.Log($"[NewsModel] seed={seed} でカレンダーを生成しました（{(IsPhase3 ? "フェーズ3" : "旧形式")}・事象 {events.Count} 件・掲載 {calendar.Count} 本・世界={(world != null ? "あり" : "なし")}）。");
    }

    /// <summary>
    /// 旧形式は掲載1件 = 事象1件として扱う（効果の計算を事象側に一本化するため）。
    /// 掲載ターンに跳ね、発効ターンから効く。従来の挙動と一致する。
    /// </summary>
    private void WrapLegacyEvents()
    {
        foreach (var e in calendar)
        {
            if (e.kind != NewsEntryKind.Report) continue;
            var a = e.Article;
            if (a == null) continue;
            var ev = new NewsEventInstance
            {
                key = e.Key,
                eventId = a.id,
                category = a.category,
                effectTurn = e.effectTurn,
                durationTurns = Mathf.Max(1, a.durationTurns),
                truth = e.isFalseReport ? "false" : a.IsExaggerated ? "exaggerated" : "true",
                effectScale = e.effectScale,
                trendDelta = a.HasEffect ? a.trendDelta : 0f,
                demandKick = a.HasEffect ? a.demandKick : 0f,
                targetAttribute = a.targetAttribute,
                targetType = a.targetType,
                targetItemId = a.targetItemId,
            };
            // 旧形式は記事に hypeRate を直接書けるので、その値を使う（面の既定値で埋め済み）
            if (a.HasTarget && a.hypeRate > 0f) ev.AddHype(e.publishTurn, a.hypeRate);
            e.eventInstance = ev;
            events.Add(ev);
        }
    }

    /// <summary>そのターンの紙面（全社分）。</summary>
    public List<NewsIssueEntry> IssueOf(int turn)
    {
        var list = new List<NewsIssueEntry>();
        foreach (var e in calendar)
            if (e.publishTurn == turn) list.Add(e);
        return list;
    }

    /// <summary>そのターンの、ある社の号。</summary>
    public List<NewsIssueEntry> IssueOf(int turn, string companyId)
    {
        var list = new List<NewsIssueEntry>();
        foreach (var e in calendar)
            if (e.publishTurn == turn && e.companyId == companyId) list.Add(e);
        return list;
    }

    /// <summary>
    /// そのターンに<b>効果が生きている</b>事象（2段目）。
    /// 誤報は効果を持たない（1段目の値動きだけ起きて、実体が来ないのが誤報の正体）。
    /// </summary>
    public List<NewsEventInstance> ActiveEffectsOn(int turn)
    {
        var list = new List<NewsEventInstance>();
        foreach (var e in events)
        {
            if (!e.HasRealEffect) continue;
            if (turn >= e.effectTurn && turn < e.SettleTurn) list.Add(e);
        }
        return list;
    }

    /// <summary>そのターンに発効したばかりの事象（需要への直撃は1回だけ）。</summary>
    public List<NewsEventInstance> JustTriggeredOn(int turn)
    {
        var list = new List<NewsEventInstance>();
        foreach (var e in events)
            if (e.HasRealEffect && e.effectTurn == turn) list.Add(e);
        return list;
    }

    /// <summary>
    /// 価格の跳ね（1段目）に関わる事象。報じられた日に跳ね、剥がれ始めから
    /// NewsTuning.HypeUnwindTurns かけて剥がれる。<b>真偽を問わない</b>。
    /// </summary>
    public List<NewsEventInstance> HypeSourcesOn(int turn)
    {
        var list = new List<NewsEventInstance>();
        foreach (var e in events)
        {
            if (e.hypeByDay.Count == 0) continue;
            if (e.hypeByDay.ContainsKey(turn)) { list.Add(e); continue; }
            int start = e.UnwindStart;
            if (turn >= start && turn < start + NewsTuning.HypeUnwindTurns) list.Add(e);
        }
        return list;
    }

    /// <summary>掲載キー（NewsIssueEntry.Key）から掲載を引く。カレンダー外なら null。</summary>
    public NewsIssueEntry FindEntry(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var e in calendar)
            if (e.Key == key) return e;
        return null;
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

using System;
using System.Collections.Generic;

/// <summary>スクラップ1件の状態（Docs/News_Spec.md §9.1）。</summary>
public enum ScrapState
{
    Pending,       // 砂時計: 発効 + duration がまだ来ていない
    Unconfirmed,   // ?: 決着したが真偽は不明（確認できる）
    ConfirmedHit,  // ○: 確認済み・本物（誇張を含む）
    ConfirmedMiss, // ×: 確認済み・誤報
}

/// <summary>
/// スクラップ（手動ピン留め）と確認済みの名鑑、社の実測的中率（§9）。
///
/// ・貼れるのは通常記事だけ（結果記事・訂正記事は答え合わせなので貼らない）
/// ・枠は <see cref="NewsTuning.ScrapCapacity"/>。<b>空けるには確認するしかない</b>（剥がす手段はない）
/// ・確認は決着後のみ。確認すると名鑑へ移り、その社の実測的中率に積まれる
/// 真偽そのものは保存しない。発行カレンダーはシードから再現できるので、掲載キーから引き直す。
/// </summary>
public class ScrapbookModel
{
    [Serializable]
    public class Scrap
    {
        public string entryKey;     // NewsIssueEntry.Key
        public string articleId;
        public string companyId;
        public int publishTurn;
        public int pinnedTurn;
        public bool confirmed;
        public bool hit;            // 確認済みのときだけ意味を持つ
        public int confirmedTurn;
    }

    [Serializable]
    public class CompanyRecord
    {
        public string companyId;
        public int confirmed;
        public int hits;
    }

    private readonly List<Scrap> pinned = new();   // 未確認（枠を占有している）
    private readonly List<Scrap> archive = new();  // 確認済み（名鑑）
    private readonly List<CompanyRecord> records = new();

    public IReadOnlyList<Scrap> Pinned => pinned;
    public IReadOnlyList<Scrap> Archive => archive;

    public int Capacity => NewsTuning.ScrapCapacity;
    public bool IsFull => pinned.Count >= Capacity;

    public bool IsPinned(string entryKey)
    {
        foreach (var s in pinned) if (s.entryKey == entryKey) return true;
        foreach (var s in archive) if (s.entryKey == entryKey) return true;
        return false;
    }

    public static bool CanPinKind(NewsIssueEntry entry) =>
        entry != null && entry.kind == NewsEntryKind.Report;

    public bool CanPin(NewsIssueEntry entry) =>
        CanPinKind(entry) && !IsFull && !IsPinned(entry.Key);

    public bool Pin(NewsIssueEntry entry, int turn)
    {
        if (!CanPin(entry)) return false;
        pinned.Add(new Scrap
        {
            entryKey = entry.Key,
            articleId = entry.articleId,
            companyId = entry.companyId,
            publishTurn = entry.publishTurn,
            pinnedTurn = turn,
        });
        return true;
    }

    /// <summary>状態。掲載がカレンダーから引けないときは保留扱い（確認させない）。</summary>
    public static ScrapState StateOf(Scrap scrap, NewsIssueEntry entry, int turn)
    {
        if (scrap == null) return ScrapState.Pending;
        if (scrap.confirmed) return scrap.hit ? ScrapState.ConfirmedHit : ScrapState.ConfirmedMiss;
        if (entry == null) return ScrapState.Pending;
        return turn >= entry.SettleTurn ? ScrapState.Unconfirmed : ScrapState.Pending;
    }

    /// <summary>
    /// 1件を確認済みにして名鑑へ移す。支払いは呼び出し側。
    /// 本物・誇張は的中、誤報は外れ（誇張は「事件そのものは本当」なので的中に数える）。
    /// </summary>
    public bool Confirm(string entryKey, NewsIssueEntry entry, int turn)
    {
        var scrap = pinned.Find(s => s.entryKey == entryKey);
        if (scrap == null || StateOf(scrap, entry, turn) != ScrapState.Unconfirmed) return false;

        scrap.confirmed = true;
        scrap.hit = !entry.isFalseReport;
        scrap.confirmedTurn = turn;
        pinned.Remove(scrap);
        archive.Add(scrap);

        var rec = RecordOf(scrap.companyId, create: true);
        rec.confirmed++;
        if (scrap.hit) rec.hits++;
        return true;
    }

    /// <summary>その日に確認した名鑑の記事（確認した直後もカードを表示しておくため）。</summary>
    public List<Scrap> ConfirmedOn(int turn)
    {
        var list = new List<Scrap>();
        foreach (var s in archive) if (s.confirmedTurn == turn) list.Add(s);
        return list;
    }

    /// <summary>社の実測的中率。確認した記事が無ければ -1。</summary>
    public float MeasuredAccuracy(string companyId)
    {
        var rec = RecordOf(companyId, create: false);
        if (rec == null || rec.confirmed <= 0) return -1f;
        return (float)rec.hits / rec.confirmed;
    }

    private CompanyRecord RecordOf(string companyId, bool create)
    {
        var rec = records.Find(r => r.companyId == companyId);
        if (rec == null && create)
        {
            rec = new CompanyRecord { companyId = companyId };
            records.Add(rec);
        }
        return rec;
    }

    /// <summary>
    /// 掲載が引けなくなった未確認のスクラップを捨てる（カレンダーの形式が変わった旧セーブ用）。
    /// 引けないままだと決着せず、枠を占有し続けるため。捨てた件数を返す。
    /// </summary>
    public int PruneMissing(Func<string, bool> exists)
    {
        if (exists == null) return 0;
        return pinned.RemoveAll(s => !exists(s.entryKey));
    }

    public void Clear()
    {
        pinned.Clear();
        archive.Clear();
        records.Clear();
    }

    // --- セーブ/ロード ---

    public List<Scrap> PinnedToPlain() => new(pinned);
    public List<Scrap> ArchiveToPlain() => new(archive);
    public List<CompanyRecord> RecordsToPlain() => new(records);

    public void FromPlain(IEnumerable<Scrap> pinnedPlain, IEnumerable<Scrap> archivePlain,
        IEnumerable<CompanyRecord> recordPlain)
    {
        Clear();
        if (pinnedPlain != null)
            foreach (var s in pinnedPlain)
                if (s != null && !string.IsNullOrEmpty(s.entryKey) && !s.confirmed) pinned.Add(s);
        if (archivePlain != null)
            foreach (var s in archivePlain)
                if (s != null && !string.IsNullOrEmpty(s.entryKey)) archive.Add(s);
        if (recordPlain != null)
            foreach (var r in recordPlain)
                if (r != null && !string.IsNullOrEmpty(r.companyId)) records.Add(r);

        // 壊れたセーブで枠を超えていたら古い方から残す
        while (pinned.Count > Capacity) pinned.RemoveAt(pinned.Count - 1);
    }
}

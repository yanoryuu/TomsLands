using System;
using System.Collections.Generic;
using R3;

/// <summary>スクラップ1件の状態（Docs/News_Spec.md §9.1）。</summary>
public enum ScrapState
{
    Pending,       // 砂時計: 発効がまだ来ていない
    Unconfirmed,   // ?: 発効した（効き始めた）が真偽は不明（確認できる）
    ConfirmedHit,  // ○: 本物
    ConfirmedMiss, // ×: 誤報
    ConfirmedPartial, // △: 誇張（事件は本当だが紙面ほどではない）。末尾に足した（2026-10 I-15）
}

/// <summary>
/// スクラップ（手動ピン留め）と確認済みの名鑑、社の実測的中率（§9）。
///
/// ・貼れるのは通常記事だけ（結果記事・訂正記事は答え合わせなので貼らない）
/// ・枠は <see cref="NewsTuning.ScrapCapacity"/>。空くのは「確認したとき」と「続報が届いたとき」（剥がす手段はない）
/// ・確認は<b>発効した日から</b>（有料）。続報（結果記事・訂正記事）は購読が切れていても届き、そのとき無料で名鑑へ移る（I-3）
/// ・確認・続報で名鑑へ移ると、その社の実測的中率に積まれる（誇張は 0.5）
/// ・スクラップに「この品が上がる」の予想（属性×種別 / 高額帯）を書ける。仕入れ画面の付箋に出る（I-6。D5 を変更）
/// 真偽そのものは保存しない（確認済みの結果だけ持つ）。発行カレンダーはシードから再現できるので、掲載キーから引き直す。
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
        public bool hit;            // 確認済みのときだけ意味を持つ（誇張も true）
        public bool partial;        // 確認済みで誇張だった（△）
        public int confirmedTurn;
        /// <summary>届いた続報の掲載キー（無料で答えが分かったとき）。空 = 有料で確認した / まだ。</summary>
        public string followUpKey;
        /// <summary>予想（<see cref="ScrapPrediction"/> の値）。空 = 予想なし。</summary>
        public string prediction;
    }

    [Serializable]
    public class CompanyRecord
    {
        public string companyId;
        public int confirmed;
        public int hits;      // 誇張を含む
        public int partials;  // うち誇張
    }

    private readonly List<Scrap> pinned = new();   // 未確認（枠を占有している）
    private readonly List<Scrap> archive = new();  // 確認済み（名鑑）
    private readonly List<CompanyRecord> records = new();

    private readonly Subject<(NewsIssueEntry entry, int turn)> pinnedSubject = new();
    private readonly Subject<(Scrap scrap, NewsIssueEntry entry)> confirmedSubject = new();
    /// <summary>記事をスクラップした（ロード時は出ない）。実績の判定用。</summary>
    public Observable<(NewsIssueEntry entry, int turn)> OnPinned => pinnedSubject;
    /// <summary>スクラップを有料で確認して真偽が分かった（続報で分かったときは出ない。ロード時も出ない）。実績の判定用。</summary>
    public Observable<(Scrap scrap, NewsIssueEntry entry)> OnConfirmed => confirmedSubject;

    public IReadOnlyList<Scrap> Pinned => pinned;
    public IReadOnlyList<Scrap> Archive => archive;

    public int Capacity => NewsTuning.ScrapCapacity;

    /// <summary>セーブを読み込んだランのシード（NewspaperSaveStore.EnsureLoaded が使う。保存しない）。</summary>
    public int LoadedRunSeed { get; set; } = int.MinValue;
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
        pinnedSubject.OnNext((entry, turn));
        return true;
    }

    /// <summary>
    /// 状態。掲載がカレンダーから引けないときは保留扱い（確認させない）。
    /// 確認できるのは<b>発効した日から</b>（跳ねが剥がれ始め、本物なら実需が来る日。売るか持つかを決める日に答えを買える）。
    /// </summary>
    public static ScrapState StateOf(Scrap scrap, NewsIssueEntry entry, int turn)
    {
        if (scrap == null) return ScrapState.Pending;
        if (scrap.confirmed)
            return !scrap.hit ? ScrapState.ConfirmedMiss : scrap.partial ? ScrapState.ConfirmedPartial : ScrapState.ConfirmedHit;
        if (entry == null) return ScrapState.Pending;
        return turn >= ConfirmableTurn(entry) ? ScrapState.Unconfirmed : ScrapState.Pending;
    }

    /// <summary>確認できるようになる日（発効日。発効日が無い旧データは決着日）。</summary>
    public static int ConfirmableTurn(NewsIssueEntry entry)
    {
        if (entry == null) return int.MaxValue;
        int eff = entry.eventInstance != null ? entry.eventInstance.effectTurn : entry.effectTurn;
        return eff > 0 ? eff : entry.SettleTurn;
    }

    private static bool IsExaggerated(NewsIssueEntry entry) =>
        entry != null && !entry.isFalseReport && entry.eventInstance != null && entry.eventInstance.truth == "exaggerated";

    /// <summary>
    /// 1件を確認済みにして名鑑へ移す。支払いは呼び出し側。
    /// 本物は ○、誇張は △（的中に数え、実測的中率では 0.5）、誤報は ×。
    /// </summary>
    public bool Confirm(string entryKey, NewsIssueEntry entry, int turn)
    {
        var scrap = pinned.Find(s => s.entryKey == entryKey);
        if (scrap == null || StateOf(scrap, entry, turn) != ScrapState.Unconfirmed) return false;
        Resolve(scrap, entry, turn, null);
        return true;
    }

    private void Resolve(Scrap scrap, NewsIssueEntry entry, int turn, string followUpKey)
    {
        scrap.confirmed = true;
        scrap.hit = !entry.isFalseReport;
        scrap.partial = IsExaggerated(entry);
        scrap.confirmedTurn = turn;
        scrap.followUpKey = followUpKey;
        pinned.Remove(scrap);
        archive.Add(scrap);

        var rec = RecordOf(scrap.companyId, create: true);
        rec.confirmed++;
        if (scrap.hit) rec.hits++;
        if (scrap.partial) rec.partials++;
        // 実績（誤報を確かめた）は有料で確認したときだけ。続報で分かったときは出さない（実績の意味を変えないため）
        if (string.IsNullOrEmpty(followUpKey)) confirmedSubject.OnNext((scrap, entry));
    }

    /// <summary>
    /// 続報（結果記事・訂正記事）が今日までに出たスクラップを、無料で名鑑へ移す（購読していなくても届く。I-3）。
    /// 移した件数を返す（呼び出し側で保存する）。
    /// </summary>
    public int DeliverFollowUps(NewsModel news, int turn)
    {
        if (news == null || pinned.Count == 0) return 0;
        int n = 0;
        foreach (var s in pinned.ToArray())
        {
            var entry = news.FindEntry(s.entryKey);
            var follow = FollowUpOf(news, entry, turn);
            if (follow == null) continue;
            Resolve(s, entry, turn, follow.Key);
            n++;
        }
        return n;
    }

    /// <summary>その報道の続報のうち、今日までに出たもの（同じ事象・報じた社自身）。無ければ null。</summary>
    public static NewsIssueEntry FollowUpOf(NewsModel news, NewsIssueEntry entry, int turn)
    {
        if (news == null || entry?.eventInstance == null) return null;
        foreach (var e in news.Calendar)
        {
            if (e.publishTurn > turn) continue;
            if (e.kind != NewsEntryKind.Result && e.kind != NewsEntryKind.Correction) continue;
            if (e.eventInstance != entry.eventInstance || e.companyId != entry.companyId) continue;
            return e;
        }
        return null;
    }

    /// <summary>予想を書く（空で消す）。未確認・確認済みどちらでも書ける。</summary>
    public bool SetPrediction(string entryKey, string prediction)
    {
        var scrap = pinned.Find(s => s.entryKey == entryKey) ?? archive.Find(s => s.entryKey == entryKey);
        if (scrap == null) return false;
        scrap.prediction = prediction ?? string.Empty;
        return true;
    }

    public Scrap Find(string entryKey) =>
        pinned.Find(s => s.entryKey == entryKey) ?? archive.Find(s => s.entryKey == entryKey);

    /// <summary>その日に名鑑へ移った記事（確認した直後・続報が届いた日も、カードを表示しておくため）。</summary>
    public List<Scrap> ConfirmedOn(int turn)
    {
        var list = new List<Scrap>();
        foreach (var s in archive) if (s.confirmedTurn == turn) list.Add(s);
        return list;
    }

    /// <summary>社の実測的中率（誇張は 0.5 として数える）。確認した記事が無ければ -1。</summary>
    public float MeasuredAccuracy(string companyId)
    {
        var rec = RecordOf(companyId, create: false);
        if (rec == null || rec.confirmed <= 0) return -1f;
        return (rec.hits - 0.5f * rec.partials) / rec.confirmed;
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
        vetted.Clear();
    }

    // --- 裏取り（酒場 Lv5。Docs/News_Spec.md §9.4） ---

    /// <summary>
    /// 裏取りで分かった記事1本の真偽。スクラップとは別に持ち、社の実測的中率には数えない
    /// （実測は「確認・続報で答えが出た記事」だけの数字のまま。裏取りは選んだ1本なので偏る）。
    /// 効き先（何の品に効くか）は出さない。答え合わせは確認・続報のときだけ（記事から銘柄へのリンクを作らない約束。§9.3）。
    /// </summary>
    [Serializable]
    public class Vet
    {
        public string entryKey;
        /// <summary>使った「配信の間」（次の配信がこのランの何回目か。GameFlowManager.GetStreamProgress の upcoming）。</summary>
        public int window;
        public int turn;
        public bool hit;      // 本物か誇張（誤報でない）
        public bool partial;  // 誇張
    }

    private readonly List<Vet> vetted = new();

    public IReadOnlyList<Vet> Vetted => vetted;

    public Vet FindVet(string entryKey) => vetted.Find(v => v.entryKey == entryKey);

    /// <summary>その「配信の間」にもう裏取りを使ったか（配信の間に1回）。</summary>
    public bool HasVetIn(int window) => vetted.Exists(v => v.window == window);

    /// <summary>裏を取れる種類の記事か（通常の報道だけ。結果記事・訂正記事・埋め草は取らない）。</summary>
    public static bool CanVetKind(NewsIssueEntry entry) => CanPinKind(entry);

    /// <summary>まだ答えが出ていない（裏取りしていない・確認や続報で名鑑へ移っていない）報道か。</summary>
    public bool IsVettable(NewsIssueEntry entry)
    {
        if (!CanVetKind(entry) || FindVet(entry.Key) != null) return false;
        var scrap = archive.Find(s => s.entryKey == entry.Key);
        return scrap == null;
    }

    public bool CanVet(NewsIssueEntry entry, int window) => IsVettable(entry) && !HasVetIn(window);

    /// <summary>裏を取る（無料・配信の間に1回）。名鑑・実測的中率・スクラップの枠は変えない。</summary>
    public bool VetArticle(NewsIssueEntry entry, int window, int turn)
    {
        if (!CanVet(entry, window)) return false;
        vetted.Add(new Vet
        {
            entryKey = entry.Key,
            window = window,
            turn = turn,
            hit = !entry.isFalseReport,
            partial = IsExaggerated(entry),
        });
        return true;
    }

    /// <summary>裏取りの結果を ○△× の状態で（裏取りしていなければ null）。</summary>
    public ScrapState? VetStateOf(string entryKey)
    {
        var v = FindVet(entryKey);
        if (v == null) return null;
        return !v.hit ? ScrapState.ConfirmedMiss : v.partial ? ScrapState.ConfirmedPartial : ScrapState.ConfirmedHit;
    }

    public List<Vet> VettedToPlain() => new(vetted);

    public void VettedFromPlain(IEnumerable<Vet> plain)
    {
        vetted.Clear();
        if (plain == null) return;
        foreach (var v in plain)
            if (v != null && !string.IsNullOrEmpty(v.entryKey)) vetted.Add(v);
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

/// <summary>
/// スクラップの予想（「この記事でこの品が上がる」）。値は "Fire,Weapon" のような「属性,種別」か、高額帯の "L3"。
/// 記事から銘柄へ自動で飛ぶリンクは作らない（紐付けはプレイヤーが自分で書く。News_Spec §9.3）。
/// </summary>
public static class ScrapPrediction
{
    public const string HighTier = "L3";

    private static readonly (string id, string ja)[] Attributes =
        { ("Fire", "火"), ("Water", "水"), ("Wood", "木"), ("Light", "光"), ("Dark", "闇") };
    private static readonly (string id, string ja)[] Types =
        { ("Weapon", "武器"), ("Armor", "防具"), ("Tool", "道具") };

    /// <summary>選べる予想の一覧（先頭は「メモなし」。画面では「予想メモ」と呼ぶ）。</summary>
    public static List<(string value, string label)> Options()
    {
        var list = new List<(string, string)> { (string.Empty, "メモなし") };
        foreach (var a in Attributes)
            foreach (var t in Types)
                list.Add(($"{a.id},{t.id}", $"{a.ja}の{t.ja}"));
        list.Add((HighTier, "高額の品"));
        return list;
    }

    public static string Label(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        foreach (var o in Options()) if (o.value == value) return o.label;
        return value;
    }

    /// <summary>その品が予想に当てはまるか。</summary>
    public static bool Matches(string value, RuntimeItemData item)
    {
        if (string.IsNullOrEmpty(value) || item == null) return false;
        if (value == HighTier) return item.RequiredLevel != null && item.RequiredLevel.Value >= NewsTuning.L3MinRequiredLevel;
        var p = value.Split(',');
        if (p.Length != 2) return false;
        return ItemTypeData.AttributeMatches(item.ItemAttribute, p[0]) && item.ItemType.ToString() == p[1];
    }

    /// <summary>事象の効き先を、予想と同じ書き方の言葉にする（答え合わせの表示用）。</summary>
    public static string TargetLabel(NewsEventInstance ev)
    {
        if (ev == null || !ev.HasTarget) return string.Empty;
        if (ev.minRequiredLevel > 0 && string.IsNullOrEmpty(ev.targetAttribute) && string.IsNullOrEmpty(ev.targetType)) return "高額の品";
        string attr = string.Empty, type = string.Empty;
        foreach (var a in Attributes) if (a.id == ev.targetAttribute) attr = a.ja;
        foreach (var t in Types) if (t.id == ev.targetType) type = t.ja;
        if (attr.Length > 0 && type.Length > 0) return $"{attr}の{type}";
        if (attr.Length > 0) return $"{attr}の品";
        if (type.Length > 0) return $"{type}全般";
        return "特定の品";
    }

    /// <summary>予想が事象の効き先と合っているか（属性と種別が両方合えば的中。高額帯は高額帯同士）。</summary>
    public static bool IsCorrect(string value, NewsEventInstance ev)
    {
        if (string.IsNullOrEmpty(value) || ev == null || !ev.HasTarget) return false;
        if (value == HighTier) return ev.minRequiredLevel > 0;
        var p = value.Split(',');
        if (p.Length != 2) return false;
        bool attrOk = string.IsNullOrEmpty(ev.targetAttribute) || ev.targetAttribute == p[0];
        bool typeOk = string.IsNullOrEmpty(ev.targetType) || ev.targetType == p[1];
        return attrOk && typeOk && (!string.IsNullOrEmpty(ev.targetAttribute) || !string.IsNullOrEmpty(ev.targetType));
    }
}

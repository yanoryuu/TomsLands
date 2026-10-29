using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 朝レポート。日送り（GameFlowManager.NextTurn）中に発生した入金・生成イベントを
/// 溜め、ホーム画面の Entry で消費型表示する
/// （売り注文の持ち越し精算 / 配当 / 債券償還 / マシンの設備収入・生成アイテム / レリック）。
/// 朝はターン演出・バズ演出・イベント・借金パネルで表示が渋滞するため、1つの通知に統合する。
/// 入金は「出どころ（アイコン・名前・台数・額）」として、それ以外（生成アイテム・取り立て等）は文の行として持つ。
/// 永続化しない（お金自体は適用済みで、レポートは表示のみのため）。
/// </summary>
public class MorningReportModel
{
    private readonly List<string> lines = new();
    private readonly List<MorningIncomeEntry> incomes = new();

    public bool HasLines => lines.Count > 0 || incomes.Count > 0;

    // ---- 今朝の入金合計（F-4。日送りの前後の所持金の差で数える） ----
    private int moneyBefore;
    private int moneyAfter;
    private bool moneyMarked;

    /// <summary>日送りの朝の処理（持ち越し精算・配当・債券・設備・レリック）の前の所持金を記録する。</summary>
    public void MarkMoneyBefore(int money) { moneyBefore = money; moneyAfter = money; moneyMarked = true; }

    /// <summary>朝の処理を終えた後の所持金を記録する。</summary>
    public void MarkMoneyAfter(int money) { if (moneyMarked) moneyAfter = money; }

    /// <summary>今朝の入金の合計（朝の処理で増えた所持金）。記録が無ければ 0。</summary>
    public int MorningIncome => moneyMarked ? moneyAfter - moneyBefore : 0;

    /// <summary>前の朝に見せた総資産（このシーンの間だけ覚える。続きからの最初の朝は昨日比を出さない）。</summary>
    public int? LastNetWorth { get; set; }

    /// <summary>朝の通知（イベント・税金・朝レポート・レリック）を順に出している最中か。会話のあとの自動移動を待たせる。</summary>
    public bool IsShowingNotices { get; set; }

    public void Add(string line)
    {
        if (!string.IsNullOrEmpty(line)) lines.Add(line);
    }

    public void AddRange(IEnumerable<string> newLines)
    {
        if (newLines == null) return;
        foreach (var line in newLines) Add(line);
    }

    /// <summary>
    /// 入金の出どころを1件足す。同じ key（同じマシン・同じレリック）は1行にまとめ、台数と額を積む
    /// （小さな貯金ゴーレムを2台置いていれば「×2 +1,600G」）。額が 0 以下なら何もしない。
    /// </summary>
    public void AddIncome(string key, string displayName, Sprite icon, int amount)
    {
        if (amount <= 0) return;
        if (string.IsNullOrEmpty(key)) key = displayName ?? string.Empty;
        foreach (var e in incomes)
        {
            if (e.Key != key) continue;
            e.Count++;
            e.Amount += amount;
            return;
        }
        incomes.Add(new MorningIncomeEntry(key, displayName, icon, amount));
    }

    /// <summary>溜まった入金の出どころ（足した順）。</summary>
    public IReadOnlyList<MorningIncomeEntry> Incomes => incomes;

    /// <summary>
    /// 溜まった行を消費せずに覗く（朝刊の「自店」面が使う。ホーム画面の消費より先に開くため）。
    /// 入金の出どころも文の行にして先頭に並べる。
    /// </summary>
    public IReadOnlyList<string> Peek() => BuildTextLines();

    /// <summary>溜まった行を取り出してクリアする（消費型。入金の出どころも文の行にして含める）。</summary>
    public List<string> Consume()
    {
        var result = BuildTextLines();
        Clear();
        return result;
    }

    /// <summary>
    /// ホーム画面の朝レポート用: 入金の出どころと、それ以外の行を分けて取り出してクリアする（消費型）。
    /// </summary>
    public MorningReportSnapshot ConsumeReport()
    {
        var snapshot = new MorningReportSnapshot(MorningIncome, new List<MorningIncomeEntry>(incomes), new List<string>(lines));
        Clear();
        return snapshot;
    }

    private List<string> BuildTextLines()
    {
        var result = new List<string>(incomes.Count + lines.Count);
        foreach (var e in incomes) result.Add(e.ToLine());
        result.AddRange(lines);
        return result;
    }

    private void Clear()
    {
        lines.Clear();
        incomes.Clear();
        moneyMarked = false;
    }

    /// <summary>
    /// 表示する行を組む: 額の大きい順に maxRows 行まで。溢れた分は最後の1行「ほか n件」にまとめる
    /// （朝の通知を画面に収めるため）。
    /// </summary>
    public static List<MorningIncomeEntry> BuildRows(IReadOnlyList<MorningIncomeEntry> entries, int maxRows)
    {
        // 同額は足した順（OrderByDescending は安定ソート）
        var sorted = entries == null
            ? new List<MorningIncomeEntry>()
            : entries.Where(e => e != null && e.Amount > 0).OrderByDescending(e => e.Amount).ToList();

        if (maxRows <= 0 || sorted.Count <= maxRows) return sorted;

        var rows = sorted.GetRange(0, maxRows - 1);
        int rest = 0, restCount = 0;
        for (int i = maxRows - 1; i < sorted.Count; i++) { rest += sorted[i].Amount; restCount++; }
        rows.Add(new MorningIncomeEntry(OtherKey, $"ほか{restCount}件", null, rest));
        return rows;
    }

    /// <summary>「ほか n件」の行の key。</summary>
    public const string OtherKey = "__other";

    /// <summary>行の額の合計。</summary>
    public static int SumAmount(IReadOnlyList<MorningIncomeEntry> entries)
    {
        long total = 0;
        if (entries != null)
            foreach (var e in entries) if (e != null) total += e.Amount;
        return (int)System.Math.Min(int.MaxValue, total);
    }
}

/// <summary>朝レポートの入金の出どころ1行（同じ出どころは台数と額をまとめる）。</summary>
public class MorningIncomeEntry
{
    public string Key { get; }
    public string DisplayName { get; }
    /// <summary>出どころのアイコン（レリック・マシン）。null なら表示側が金貨で代用する。</summary>
    public Sprite Icon { get; }
    /// <summary>まとめた件数（マシンの台数）。</summary>
    public int Count { get; set; }
    public int Amount { get; set; }

    public MorningIncomeEntry(string key, string displayName, Sprite icon, int amount, int count = 1)
    {
        Key = key; DisplayName = displayName ?? string.Empty; Icon = icon; Amount = amount; Count = count;
    }

    /// <summary>台数の表記（2台以上のときだけ「×2」）。</summary>
    public string CountLabel => Count >= 2 ? $"×{Count}" : string.Empty;

    public string AmountLabel => $"+{Amount:N0}G";

    /// <summary>文の1行（朝刊の自店面・自動プレイのメモ用）: 「小さな貯金ゴーレム ×2 +1,600G」。</summary>
    public string ToLine() => Count >= 2 ? $"{DisplayName} {CountLabel} {AmountLabel}" : $"{DisplayName} {AmountLabel}";
}

/// <summary>ホーム画面の朝レポートに渡す中身（消費時点の写し）。</summary>
public class MorningReportSnapshot
{
    /// <summary>朝の処理で増えた所持金（取り立てなどの差し引き込み）。</summary>
    public int MoneyDelta { get; }
    public List<MorningIncomeEntry> Incomes { get; }
    /// <summary>入金以外の行（生成アイテム・取り立て・前倒し入金など）。</summary>
    public List<string> Notes { get; }

    public MorningReportSnapshot(int moneyDelta, List<MorningIncomeEntry> incomes, List<string> notes)
    {
        MoneyDelta = moneyDelta; Incomes = incomes ?? new(); Notes = notes ?? new();
    }

    public bool IsEmpty => Incomes.Count == 0 && Notes.Count == 0;
}

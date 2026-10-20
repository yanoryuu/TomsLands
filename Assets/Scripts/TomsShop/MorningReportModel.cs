using System.Collections.Generic;

/// <summary>
/// 朝レポート。日送り（GameFlowManager.NextTurn）中に発生した入金・生成イベントを
/// 行単位で溜め、ホーム画面の Entry で消費型表示する
/// （売り注文の持ち越し精算 / 配当 / 債券償還 / マシンの設備収入・生成アイテム）。
/// 朝はターン演出・バズ演出・イベント・借金パネルで表示が渋滞するため、1つの通知に統合する。
/// 永続化しない（お金自体は適用済みで、レポートは表示のみのため）。
/// </summary>
public class MorningReportModel
{
    private readonly List<string> lines = new();

    public bool HasLines => lines.Count > 0;

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

    /// <summary>溜まった行を消費せずに覗く（朝刊の「自店」面が使う。ホーム画面の消費より先に開くため）。</summary>
    public IReadOnlyList<string> Peek() => lines;

    /// <summary>溜まった行を取り出してクリアする（消費型）。</summary>
    public List<string> Consume()
    {
        var result = new List<string>(lines);
        lines.Clear();
        moneyMarked = false;
        return result;
    }
}

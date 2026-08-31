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

    public void Add(string line)
    {
        if (!string.IsNullOrEmpty(line)) lines.Add(line);
    }

    public void AddRange(IEnumerable<string> newLines)
    {
        if (newLines == null) return;
        foreach (var line in newLines) Add(line);
    }

    /// <summary>溜まった行を取り出してクリアする（消費型）。</summary>
    public List<string> Consume()
    {
        var result = new List<string>(lines);
        lines.Clear();
        return result;
    }
}

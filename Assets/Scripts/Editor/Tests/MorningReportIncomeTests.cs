using NUnit.Framework;

/// <summary>
/// 朝レポートの入金の行: 同じ出どころ（同じマシン・同じレリック）は1行にまとめて台数と額を積み、
/// 額の大きい順に上限まで並べ、溢れた分は「ほか n件」にまとめる。
/// </summary>
public class MorningReportIncomeTests
{
    [Test]
    public void AddIncome_SameKeyIsMergedWithCount()
    {
        var model = new MorningReportModel();
        model.AddIncome("machine:machine_money_s", "小さな貯金ゴーレム", null, 800);
        model.AddIncome("relic:relic_piggy", "子豚の貯金箱", null, 3000);
        model.AddIncome("machine:machine_money_s", "小さな貯金ゴーレム", null, 800);

        Assert.AreEqual(2, model.Incomes.Count);
        var golem = model.Incomes[0];
        Assert.AreEqual(2, golem.Count);
        Assert.AreEqual(1600, golem.Amount);
        Assert.AreEqual("×2", golem.CountLabel);
        Assert.AreEqual("小さな貯金ゴーレム ×2 +1,600G", golem.ToLine());
        Assert.AreEqual("子豚の貯金箱 +3,000G", model.Incomes[1].ToLine(), "1件だけなら台数は出さない");
    }

    [Test]
    public void AddIncome_IgnoresZeroOrNegative()
    {
        var model = new MorningReportModel();
        model.AddIncome("dividend", "配当", null, 0);
        model.AddIncome("dividend", "配当", null, -10);
        Assert.IsFalse(model.HasLines);
    }

    [Test]
    public void ConsumeReport_SeparatesIncomesAndNotes_AndClears()
    {
        var model = new MorningReportModel();
        model.MarkMoneyBefore(1000);
        model.AddIncome("relic:relic_piggy", "子豚の貯金箱", null, 3000);
        model.Add("魔導炉心の工房: 鉄の剣 ×1");
        model.MarkMoneyAfter(4000);

        var snap = model.ConsumeReport();
        Assert.AreEqual(3000, snap.MoneyDelta);
        Assert.AreEqual(1, snap.Incomes.Count);
        CollectionAssert.AreEqual(new[] { "魔導炉心の工房: 鉄の剣 ×1" }, snap.Notes);
        Assert.IsFalse(model.HasLines);
        Assert.AreEqual(0, model.MorningIncome);
    }

    [Test]
    public void Consume_IncludesIncomeLinesFirst()
    {
        // 朝刊の自店面と自動プレイのメモは文の行で読むので、入金も行にして先頭に並べる
        var model = new MorningReportModel();
        model.Add("呪われた帳簿: 外れた配信の取り立て -500G");
        model.AddIncome("machine:machine_money_s", "小さな貯金ゴーレム", null, 800);

        CollectionAssert.AreEqual(
            new[] { "小さな貯金ゴーレム +800G", "呪われた帳簿: 外れた配信の取り立て -500G" },
            model.Peek());
        CollectionAssert.AreEqual(
            new[] { "小さな貯金ゴーレム +800G", "呪われた帳簿: 外れた配信の取り立て -500G" },
            model.Consume());
        Assert.IsFalse(model.HasLines);
    }

    [Test]
    public void BuildRows_SortsByAmountAndKeepsOrderOnTie()
    {
        var model = new MorningReportModel();
        model.AddIncome("a", "A", null, 500);
        model.AddIncome("b", "B", null, 3000);
        model.AddIncome("c", "C", null, 500);

        var rows = MorningReportModel.BuildRows(model.Incomes, 5);
        CollectionAssert.AreEqual(new[] { "B", "A", "C" }, rows.ConvertAll(r => r.DisplayName));
        Assert.AreEqual(4000, MorningReportModel.SumAmount(rows));
    }

    [Test]
    public void BuildRows_OverflowIsFoldedIntoOtherRow()
    {
        var model = new MorningReportModel();
        for (int i = 1; i <= 7; i++) model.AddIncome($"k{i}", $"出どころ{i}", null, i * 100);

        var rows = MorningReportModel.BuildRows(model.Incomes, 5);
        Assert.AreEqual(5, rows.Count);
        Assert.AreEqual("出どころ7", rows[0].DisplayName);
        var other = rows[4];
        Assert.AreEqual(MorningReportModel.OtherKey, other.Key);
        Assert.AreEqual("ほか3件", other.DisplayName);
        Assert.AreEqual(100 + 200 + 300, other.Amount);
        Assert.AreEqual(2800, MorningReportModel.SumAmount(rows), "まとめても合計は変わらない");
    }
}

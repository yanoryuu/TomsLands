using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 村へ入る額の内訳（MetaProgressModel.CalculateVillageFundsBreakdown）。
/// Total が以前の式（内訳を足す前の CalculateVillageFunds）と同じになること、行の足し算が Total と合うことを確かめる。
/// </summary>
public class VillageFundsBreakdownTests
{
    private static readonly VillageEffects NoFacilities = VillageEffects.ForLevels(0, 0, 0, 0, 0, 0);
    private static readonly VillageEffects HallLv4 = VillageEffects.ForLevels(4, 0, 0, 0, 0, 0);

    /// <summary>内訳を入れる前の CalculateVillageFunds の式（そのまま写したもの）。</summary>
    private static int OldFormula(bool cleared, int profit, int carried, GameModeId mode, int dueDebtCycles, VillageEffects effects)
    {
        var settings = GameConst.Village;
        var table = settings.difficultyConversionMultipliers;
        int i = (int)mode;
        float diff = table != null && i >= 0 && i < table.Length ? Mathf.Max(0f, table[i]) : 1f;
        float rate = Mathf.Max(0f, settings.conversionRate) * diff;
        carried = Mathf.Max(0, carried);
        if (!cleared)
        {
            float bankruptRate = Mathf.Max(0f, settings.bankruptcyConversionRate);
            int fromProfit = Mathf.FloorToInt(Mathf.Max(0, profit) * rate * bankruptRate);
            long relief = Mathf.Max(0, settings.bankruptcyBaseFunds)
                          + (long)(DebtCalculator.GetScheduledTotal(Mathf.Max(0, dueDebtCycles)) * Mathf.Max(0f, settings.bankruptcyPaidDebtRate));
            return (int)System.Math.Min(System.Math.Max(fromProfit, relief), int.MaxValue);
        }
        long total = Mathf.Max(0, carried + Mathf.Min(0, profit))
                     + (long)effects.ApplyClearBonus(MetaProgressModel.ClearBonusBase(dueDebtCycles))
                     + (long)Mathf.FloorToInt(Mathf.Max(0, profit) * rate);
        return (int)System.Math.Min(total, int.MaxValue);
    }

    private static readonly object[] Cases =
    {
        // cleared, profit, carried, mode, dueDebtCycles, hall
        new object[] { true, 0, 0, GameModeId.Short, 0, false },
        new object[] { true, 12000, 0, GameModeId.Medium, 1, false },
        new object[] { true, 12000, 0, GameModeId.Medium, 1, true },
        new object[] { true, 250000, 10000, GameModeId.Long, 4, true },
        new object[] { true, -5000, 0, GameModeId.Medium, 2, false },
        new object[] { true, -5000, 20000, GameModeId.Medium, 2, true },
        new object[] { true, -50000, 20000, GameModeId.Short, 3, false },
        new object[] { true, 1234567, 0, GameModeId.Long, 5, true },
        new object[] { false, 0, 0, GameModeId.Short, 0, false },
        new object[] { false, 40000, 10000, GameModeId.Medium, 2, false },
        new object[] { false, -3000, 0, GameModeId.Medium, 1, true },
        new object[] { false, 100_000_000, 0, GameModeId.Short, 1, false },
        new object[] { false, 100_000_000, 0, GameModeId.Long, 3, true },
    };

    [TestCaseSource(nameof(Cases))]
    public void Total_MatchesOldFormula(bool cleared, int profit, int carried, GameModeId mode, int cycles, bool hall)
    {
        var effects = hall ? HallLv4 : NoFacilities;
        var b = MetaProgressModel.CalculateVillageFundsBreakdown(cleared, profit, carried, mode, cycles, effects);
        Assert.AreEqual(OldFormula(cleared, profit, carried, mode, cycles, effects), b.Total);
        Assert.AreEqual(b.Total, MetaProgressModel.CalculateVillageFunds(cleared, profit, carried, mode, cycles, effects));
    }

    [TestCaseSource(nameof(Cases))]
    public void Parts_AddUpToTotal(bool cleared, int profit, int carried, GameModeId mode, int cycles, bool hall)
    {
        var b = MetaProgressModel.CalculateVillageFundsBreakdown(cleared, profit, carried, mode, cycles, hall ? HallLv4 : NoFacilities);
        long sum = cleared
            ? (long)b.CarriedReturn + b.ClearBonusFlat + b.ClearBonusDebt + b.HallBonus + b.ProfitFunds
            : b.UsedProfit ? b.BankruptFromProfit : (long)b.ReliefBase + b.ReliefDebt;
        Assert.AreEqual(b.Total, sum);
        Assert.IsNotEmpty(b.BuildLines());
    }

    [Test]
    public void MediumCleared_LinesExplainBonus()
    {
        var b = MetaProgressModel.CalculateVillageFundsBreakdown(true, 12000, 0, GameModeId.Medium, 1, HallLv4);
        string text = b.BuildText();
        StringAssert.Contains("完走ボーナス", text);
        StringAssert.Contains("納付金のぶん", text);
        StringAssert.Contains("領主館 +15%", text);
        StringAssert.Contains("儲け 12,000G", text);
        StringAssert.Contains("+15,000G", text); // 12,000 × 1.25
        StringAssert.DoesNotContain("持ち込み", text);
    }

    [Test]
    public void Bankrupt_ReliefLines()
    {
        var b = MetaProgressModel.CalculateVillageFundsBreakdown(false, 0, 0, GameModeId.Medium, 2, NoFacilities);
        Assert.IsFalse(b.UsedProfit);
        string text = b.BuildText();
        StringAssert.Contains("救済", text);
        StringAssert.Contains("納付した額", text);
        StringAssert.DoesNotContain("完走ボーナス", text);
    }

    [Test]
    public void Bankrupt_ProfitLineWhenLarger()
    {
        var b = MetaProgressModel.CalculateVillageFundsBreakdown(false, 100_000_000, 0, GameModeId.Short, 1, NoFacilities);
        Assert.IsTrue(b.UsedProfit);
        StringAssert.Contains("最高の儲け", b.BuildText());
        StringAssert.DoesNotContain("救済", b.BuildText());
    }

    [Test]
    public void Lines_AvoidBannedPhrases()
    {
        foreach (var c in Cases)
        {
            var a = (object[])c;
            var b = MetaProgressModel.CalculateVillageFundsBreakdown((bool)a[0], (int)a[1], (int)a[2], (GameModeId)a[3], (int)a[4],
                (bool)a[5] ? HallLv4 : NoFacilities);
            string text = b.BuildText() + b.BuildInlineText();
            foreach (var banned in new[] { "効いて", "効いた", "回目" })
                StringAssert.DoesNotContain(banned, text);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 人格プレイの判断の直し（2026-10-30）: 村資金の貯め込み防止（PersonaStrategy.HoardTopUp）、
/// 魔王軍支援の期待値（PersonaSupportEv / PersonaSupportRates）、周の終わりの設備（AutoPlayMachineRules.ShouldPlace）。
/// </summary>
public class PersonaVillageSupportTests
{
    private static PersonaFacilityView Fac(string id, int cost, bool canInvest = true, int level = 0) =>
        new() { Id = id, Name = id, Level = level, MaxLevel = 5, NextCost = cost, CanInvest = canInvest };

    private static PersonaVillageSnapshot Village(int funds, params PersonaFacilityView[] facs)
    {
        var v = new PersonaVillageSnapshot { Funds = funds, MaxVillageLevel = 30 };
        v.Facilities.AddRange(facs);
        return v;
    }

    // ---- 村資金の貯め込み防止 ----

    [Test]
    public void HoardTopUp_MetanNewSave_BuildsHallWhenFundsIdle()
    {
        // 2026-10-30 10:18 めたんの新しいセーブ: 村資金 827,139G・残す 206,785G で領主館 250,000G を「元が取れない」で見送っていた
        var v = Village(827139, Fac(VillageModel.HallId, 250000), Fac("bank", 400000, canInvest: false));
        var added = PersonaStrategy.HoardTopUp(v, 206785, new HashSet<string>());
        CollectionAssert.AreEqual(new[] { VillageModel.HallId }, added);
    }

    [Test]
    public void HoardTopUp_KeepsReserve_AndNeedsTwiceTheCost()
    {
        // 残す額を除いて 2 回は買えないなら足さない（ふつうの額のときは人格の判断のまま）
        var v = Village(431000, Fac("bank", 200000));
        CollectionAssert.IsEmpty(PersonaStrategy.HoardTopUp(v, 107750, new HashSet<string>()));
        // 予定にある施設の額は先に引く
        var v2 = Village(1000000, Fac("guild", 160000), Fac("tavern", 270000));
        CollectionAssert.AreEqual(new[] { "tavern" }, PersonaStrategy.HoardTopUp(v2, 250000, new HashSet<string> { "guild" }));
        CollectionAssert.IsEmpty(PersonaStrategy.HoardTopUp(Village(0, Fac("bank", 1)), 0, new HashSet<string>()));
    }

    [Test]
    public void HoardTopUp_ZundamonRich_BuildsEverythingAffordable_HallFirst()
    {
        // ずんだもん 1,674 万G・総合Lv 28 で「気分じゃない」と何も建てなかった
        var v = Village(16743110, Fac("tavern", 600000, level: 4), Fac(VillageModel.HallId, 2000000, level: 4), Fac("plaza", 0, level: 5));
        var added = PersonaStrategy.HoardTopUp(v, 0, new HashSet<string>());
        CollectionAssert.AreEqual(new[] { VillageModel.HallId, "tavern" }, added);
    }

    // ---- 村の施設は元が取れそうか（村資金の単位で比べる） ----

    [Test]
    public void PaybackPlausible_ComparesWithVillageFundsReceivedLastLap()
    {
        // 村資金 1,000 万・残す 0 で、5% (50 万) を超える 210 万の施設
        // 前の周に村へ 60 万入った → 4 周で 240 万まで → 元が取れる
        Assert.IsTrue(PersonaChooser.PaybackPlausible(2100000, 10000000, 0, 600000));
        // 前の周に村へ 50 万 → 4 周で 200 万まで → 210 万は取れない
        Assert.IsFalse(PersonaChooser.PaybackPlausible(2100000, 10000000, 0, 500000));
        // ちょうど PaybackLaps 倍は取れる
        Assert.IsTrue(PersonaChooser.PaybackPlausible(PersonaChooser.PaybackLaps * 500000, 10000000, 0, 500000));
    }

    [Test]
    public void PaybackPlausible_NoPreviousLap_OnlyIdleFundsRule()
    {
        // 前の周が無い（入った額 0）: 村資金の 5% 以下なら買う、それを超えれば見送る
        Assert.IsTrue(PersonaChooser.PaybackPlausible(500000, 10000000, 0, 0));
        Assert.IsFalse(PersonaChooser.PaybackPlausible(500001, 10000000, 0, 0));
        Assert.IsFalse(PersonaChooser.PaybackPlausible(2100000, 10000000, 0, -1));
    }

    [Test]
    public void PaybackPlausible_RespectsReserveAndCost()
    {
        // 残す額を割るなら、入った額がいくら大きくても買わない
        Assert.IsFalse(PersonaChooser.PaybackPlausible(900000, 1000000, 200000, 100000000));
        Assert.IsFalse(PersonaChooser.PaybackPlausible(0, 1000000, 0, 1000000));
        // 大きな額でも桁あふれしない
        Assert.IsTrue(PersonaChooser.PaybackPlausible(2000000000, int.MaxValue, 0, 600000000));
    }

    // ---- 魔王軍支援の期待値 ----

    private static PersonaSupportRates Rates(string dungeon, params (int lv, int n, int losses)[] cells)
    {
        var r = new PersonaSupportRates();
        var by = new Dictionary<string, PersonaSupportRates.Cell>();
        foreach (var c in cells) by[c.lv.ToString()] = new PersonaSupportRates.Cell { N = c.n, Losses = c.losses };
        r.Rates[dungeon] = by;
        return r;
    }

    // Lv0 は防衛報酬なし（DungeonLevels.Unfunded）。1 段 1 万G、報酬は Lv × 3 万G
    private static int Cost(int lv) => 10000;
    private static int Reward(int lv) => lv <= 0 ? 0 : 30000 * lv;

    [Test]
    public void SupportEv_EasyDungeon_PaysNothing_HardDungeon_Pays()
    {
        var none = new PersonaSupportRates();
        // 深緑の獣林（勇者 97% 勝ち）: 出資は丸損
        var easy = PersonaSupportEv.Choose(none, "DeepGreenBeastForest", 0, 5, Cost, Reward, 1000000);
        Assert.AreEqual(0, easy.Levels, easy.Detail);
        // 魔王城（勇者 17% 勝ち）: 上げるほど得
        var hard = PersonaSupportEv.Choose(none, "DemonKingCastle", 0, 5, Cost, Reward, 1000000);
        Assert.AreEqual(5, hard.Levels, hard.Detail);
        Assert.Greater(hard.Ev, hard.EvNone);
        // 予算を超える段数は見ない
        Assert.AreEqual(2, PersonaSupportEv.Choose(none, "DemonKingCastle", 0, 5, Cost, Reward, 25000).Levels);
        // 上限のダンジョンは上げない
        Assert.AreEqual(0, PersonaSupportEv.Choose(none, "DemonKingCastle", 5, 5, Cost, Reward, 1000000).Levels);
    }

    [Test]
    public void SupportRates_UseMeasuredSteps_AndNeverDropWithLevel()
    {
        // 氷霧の洞窟: Lv1 では勇者がほぼ勝つが、Lv2 から負けが増える（数えた表）
        var r = Rates("IceMistCave", (1, 20, 1), (2, 20, 16));
        Assert.Less(r.LossRate("IceMistCave", 1), 0.2f);
        Assert.Greater(r.LossRate("IceMistCave", 2), 0.6f);
        Assert.GreaterOrEqual(r.LossRate("IceMistCave", 4), r.LossRate("IceMistCave", 2), "数えていない高いレベルは下のレベルを下回らない");
        var c = PersonaSupportEv.Choose(r, "IceMistCave", 0, 2, Cost, Reward, 1000000);
        Assert.AreEqual(2, c.Levels, c.Detail);
        // 件数が無いダンジョンは既定値（分析の率）
        Assert.AreEqual(PersonaSupportRates.PriorLossRate["DemonKingCastle"], new PersonaSupportRates().LossRate("DemonKingCastle", 1), 1e-4);
        Assert.AreEqual(PersonaSupportRates.UnknownDungeonLossRate, new PersonaSupportRates().LossRate("Nowhere", 1), 1e-4);
        Assert.IsNotNull(PersonaSupportRates.Load("Z:/no/such/file.json"));
    }

    // ---- 周の終わりの設備 ----

    [Test]
    public void MachinePlace_LateInLap_IsSkipped()
    {
        Assert.IsTrue(AutoPlayMachineRules.ShouldPlace(4000, 1000, 10), "4 日で元が取れる");
        Assert.IsFalse(AutoPlayMachineRules.ShouldPlace(4000, 1000, 3), "残り 3 日では元が取れない");
        Assert.IsTrue(AutoPlayMachineRules.ShouldPlace(4000, 0, AutoPlayMachineRules.LatePlaceMornings + 1));
        Assert.IsFalse(AutoPlayMachineRules.ShouldPlace(4000, 0, AutoPlayMachineRules.LatePlaceMornings), "価値の測れない設備は残り 4 日以内なら置かない");
        Assert.IsTrue(AutoPlayMachineRules.ShouldPlace(4000, 0, -1), "残りが分からなければ止めない");
    }
}

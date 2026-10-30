using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 魔王軍支援の配当の元本保証（streamTuning.supportPayoutFloorToStake。2026-10-30 ユーザー決定）。
/// 読みが当たった（勇者が負けた）ら、配当は出資の合計（今のLvまでの魔王軍支援の費用の合計）を下回らない。勇者が勝てば 0 のまま。
/// 式は SupportOdds.Payout の 1 か所で、実際の支払い・配当通知書・実績・AI の見込みが同じ額になること。
/// </summary>
public class SupportPayoutFloorTests
{
    private readonly List<Object> _created = new();

    [TearDown]
    public void TearDown()
    {
        GameConst.ResetToDefault();
        RemoteBalance.ResetToDefault();
        foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
        _created.Clear();
    }

    // 配信中の表（シート dungeonLevels、2026-10-30 の balance.json version 10。Balance_Change_Log の 2026-10-30 の行）
    private static readonly int[] LiveCosts = { 10000, 30000, 40000, 70000, 0 };
    private static readonly int[] LiveRewards = { 30000, 60000, 150000, 250000, 450000 };
    // 出資の合計（Lv0→1 と Lv1→2 はどちらも Lv1 の行の費用）
    private static readonly int[] LiveStakes = { 0, 10000, 20000, 50000, 90000, 160000 };

    // 同梱のアセットの表（dungeonLevels が無い配信のとき）
    private static readonly int[] AssetCosts = { 10000, 20000, 40000, 150000, 0 };
    private static readonly int[] AssetRewards = { 15000, 30000, 45000, 70000, 120000 };

    /// <summary>シートに入れる予定の境目とオッズ（75 / 55 / 45 / 15、2.0 / 1.5 / 1.0 / 0.45 / 0.3）＋元本保証。</summary>
    private static string PlannedJson(bool floor) =>
        "{\"clearOutlook\":{\"easyMin\":75,\"favorableMin\":55,\"evenMin\":45,\"unfavorableMin\":15}," +
        "\"streamTuning\":{\"supportOddsEasy\":2,\"supportOddsFavorable\":1.5,\"supportOddsEven\":1," +
        "\"supportOddsUnfavorable\":0.45,\"supportOddsHopeless\":0.3,\"supportPayoutFloorToStake\":" + (floor ? "true" : "false") + "}}";

    private const float HopelessPct = 5f;
    // 予定の境目での 不利 / 互角 / 有利 / 楽勝
    private static readonly float[] BetterPcts = { 25f, 50f, 60f, 90f };

    private DungeonInfoScriptableObj MakeAssetDungeon()
    {
        var so = ScriptableObject.CreateInstance<DungeonInfoScriptableObj>();
        _created.Add(so);
        so.key = DungeonName.DemonKingCastle;
        so.dungeonName = "魔王城";
        so.initDungeonLevel = 0;
        so.levelDataList = new DungeonLevelData[5];
        for (int i = 0; i < 5; i++)
            so.levelDataList[i] = new DungeonLevelData { levelUpCost = AssetCosts[i], rewardGold = AssetRewards[i], bossName = "boss" + i };
        return so;
    }

    /// <summary>今の表を dungeonLevels の配信で当てたダンジョン（実際のゲームと同じ経路: ApplyRemoteBalance）。</summary>
    private DungeonInfoScriptableObj MakeLiveDungeon()
    {
        var so = MakeAssetDungeon();
        var rows = new List<string>();
        for (int i = 0; i < 5; i++)
            rows.Add($"{{\"dungeonId\":\"*\",\"level\":{i + 1},\"levelUpCost\":{LiveCosts[i]},\"rewardGold\":{LiveRewards[i]}}}");
        Assert.AreEqual(10, RemoteBalance.OverrideFromBundle(
            "{\"version\":10,\"schemaVersion\":1,\"dungeonLevels\":[" + string.Join(",", rows) + "]}"));
        var applied = DungeonInfoScriptableObj.ApplyRemoteBalance(new List<DungeonInfoScriptableObj> { so })[0];
        if (applied != so) _created.Add(applied);
        return applied;
    }

    // ---- 出資の合計（1 つの helper） ----

    [Test]
    public void InvestedTotal_LiveTable_MatchesSupportPaid()
    {
        var so = MakeLiveDungeon();
        var data = new DungeonData(so);
        for (int lv = 0; lv <= 5; lv++)
        {
            Assert.AreEqual(LiveStakes[lv], so.GetInvestedTotal(lv), $"Lv{lv}");
            Assert.AreEqual(LiveStakes[lv], data.GetInvestedTotal(lv), $"Lv{lv}（マップ・実績・AI の DungeonData も同じ）");
        }

        // 魔王軍支援の画面で 1 段ずつ払う額（DungeonData.levelUpCost）を足すと同じ
        int paid = 0;
        for (int lv = 0; lv < 5; lv++)
        {
            data.currentDungeonLevel = lv;
            paid += data.levelUpCost;
            Assert.AreEqual(paid, data.GetInvestedTotal(lv + 1), $"Lv{lv + 1}");
        }
    }

    [Test]
    public void InvestedTotal_AssetTable()
    {
        var so = MakeAssetDungeon();
        int[] expected = { 0, 10000, 20000, 40000, 80000, 230000 };
        for (int lv = 0; lv <= 5; lv++) Assert.AreEqual(expected[lv], so.GetInvestedTotal(lv), $"Lv{lv}");
    }

    // ---- 既定（キー off）は今の動き ----

    [Test]
    public void Default_KeyOff_ReproducesCurrentBehavior()
    {
        Assert.IsFalse(new StreamTuningData().supportPayoutFloorToStake);
        GameConst.ResetToDefault();
        Assert.IsFalse(SupportOdds.FloorsToStake());

        // 同梱の表（Lv4・Lv5 は配当 < 出資）でも、オッズを入れても、キーが off なら下限はかからない
        var so = MakeAssetDungeon();
        foreach (float relic in new[] { 1f, 1.2f })
            for (int lv = 1; lv <= 5; lv++)
            {
                int legacy = Mathf.RoundToInt(so.GetDefenseReward(lv) * relic);
                var p = SupportOdds.Payout(so.GetDefenseReward(lv), relic, HopelessPct, so.GetInvestedTotal(lv));
                Assert.AreEqual(legacy, p.Total, $"Lv{lv}");
                Assert.IsFalse(p.FloorApplied);
                Assert.IsFalse(DemonKingPayoutData.From("魔王城", lv, so.GetInvestedTotal(lv), p).HasOddsLine);
            }

        GameConst.OverrideFromJson(PlannedJson(false));
        var hopeless = SupportOdds.Payout(150000, 1f, HopelessPct, 50000);
        Assert.AreEqual(45000, hopeless.Total, "キー off なら 絶望 ×0.3 はそのまま");
        Assert.IsFalse(hopeless.FloorApplied);
    }

    /// <summary>今の表とオッズ 1 では、キーを入れても全レベル・全段・レリックありで配当は変わらない（配当 ≥ 出資の 2.8 倍）。</summary>
    [Test]
    public void PayoutFloor_LiveTable_OddsOne_IsUnchanged()
    {
        var so = MakeLiveDungeon();
        var on = new StreamTuningData { supportPayoutFloorToStake = true };
        var off = new StreamTuningData();
        foreach (float relic in new[] { 1f, 1.1f, 1.2f, 1.35f })
            foreach (float pct in new[] { -1f, 0f, 5f, 14.9f, 15f, 25f, 34.9f, 35f, 50f, 63f, 75f, 100f })
                for (int lv = 0; lv <= 5; lv++)
                {
                    int baseReward = so.GetDefenseReward(lv);
                    int stake = so.GetInvestedTotal(lv);
                    int legacy = Mathf.RoundToInt(baseReward * relic);
                    var withFloor = SupportOdds.Payout(baseReward, relic, pct, stake, on);
                    Assert.AreEqual(legacy, withFloor.Total, $"Lv{lv} {pct}% ×{relic}");
                    Assert.AreEqual(SupportOdds.Payout(baseReward, relic, pct, stake, off).Total, withFloor.Total);
                    Assert.IsFalse(withFloor.FloorApplied);
                    Assert.AreEqual(legacy, SupportOdds.Expected(legacy, () => pct, stake, on));
                }
    }

    // ---- キー on・予定のオッズ（GameConst 経由＝実際の支払いと同じ呼び方） ----

    [Test]
    public void PayoutFloor_Hopeless_LiftsToStake_EveryLevel()
    {
        var so = MakeLiveDungeon();
        GameConst.OverrideFromJson(PlannedJson(true));
        Assert.IsTrue(SupportOdds.FloorsToStake());
        Assert.AreEqual(SupportOddsTier.Hopeless, SupportOdds.FromClearPct(HopelessPct));

        for (int lv = 1; lv <= 5; lv++)
        {
            int baseReward = so.GetDefenseReward(lv);
            int stake = so.GetInvestedTotal(lv);
            var paid = SupportOdds.Payout(baseReward, 1f, HopelessPct, stake); // BattleSceneStarter.CalculateDefeatReward と同じ呼び出し
            Assert.Less(SupportOdds.Apply(baseReward, 0.3f), stake, $"Lv{lv}: 絶望 ×0.3 は出資を下回る");
            Assert.IsTrue(paid.FloorApplied, $"Lv{lv}");
            Assert.AreEqual(stake, paid.Total, $"Lv{lv}: 元本まで引き上げる");
            Assert.AreEqual(stake, paid.Stake);
            AssertEveryoneSeesPaid(so, lv, 1f, HopelessPct, paid);
        }
    }

    [Test]
    public void PayoutFloor_UnfavorableOrBetter_NotApplied_LiveTable()
    {
        var so = MakeLiveDungeon();
        GameConst.OverrideFromJson(PlannedJson(true));
        foreach (float pct in BetterPcts)
            foreach (float relic in new[] { 1f, 1.2f })
                for (int lv = 1; lv <= 5; lv++)
                {
                    int baseReward = so.GetDefenseReward(lv);
                    int relicReward = Mathf.RoundToInt(baseReward * relic);
                    var paid = SupportOdds.Payout(baseReward, relic, pct, so.GetInvestedTotal(lv));
                    Assert.IsFalse(paid.FloorApplied, $"Lv{lv} {pct}% ×{relic}");
                    Assert.AreEqual(SupportOdds.Apply(relicReward, paid.OddsMultiplier), paid.Total);
                    Assert.GreaterOrEqual(paid.Total, paid.Stake);
                    AssertEveryoneSeesPaid(so, lv, relic, pct, paid);
                }
    }

    [Test]
    public void PayoutFloor_OddsOne_AssetTable_LiftsLv4AndLv5()
    {
        var so = MakeAssetDungeon();
        var d = new StreamTuningData { supportPayoutFloorToStake = true };
        var lv4 = SupportOdds.Payout(so.GetDefenseReward(4), 1f, 50f, so.GetInvestedTotal(4), d);
        Assert.AreEqual(80000, lv4.Total);
        Assert.IsTrue(lv4.FloorApplied);
        var notice = DemonKingPayoutData.From("魔王城", 4, so.GetInvestedTotal(4), lv4);
        Assert.IsTrue(notice.HasOddsLine, "オッズ 1 でも引き上げた分は行に出す");
        Assert.AreEqual("元本保証", notice.OddsLabel);
        Assert.AreEqual("+10,000", notice.OddsValueNumber);
        Assert.AreEqual(notice.FinalReward, notice.BaseReward + notice.RelicBonus + notice.OddsBonus);
        Assert.AreEqual(230000, SupportOdds.Payout(so.GetDefenseReward(5), 1f, 50f, so.GetInvestedTotal(5), d).Total);
        Assert.AreEqual(45000, SupportOdds.Payout(so.GetDefenseReward(3), 1f, 50f, so.GetInvestedTotal(3), d).Total, "Lv3 は配当 > 出資なのでそのまま");
    }

    // ---- 勇者が勝ったら 0 ----

    [Test]
    public void HeroWins_StaysZero()
    {
        GameConst.OverrideFromJson(PlannedJson(true));
        Assert.AreEqual(0, SupportPayout.None.Total, "勝ちは CalculateDefeatReward が None を返す");
        Assert.IsFalse(SupportPayout.None.FloorApplied);
        // 防衛報酬が無い（勝ち・未出資）なら出資があっても下限をかけない
        var p = SupportOdds.Payout(0, 1.2f, HopelessPct, 160000);
        Assert.AreEqual(0, p.Total);
        Assert.IsFalse(p.FloorApplied);
        Assert.AreEqual(0, SupportOdds.Expected(0, () => HopelessPct, 160000));
        Assert.AreEqual(0, AchievementTracker.ComputeDefenseReward(0, 1.2f, HopelessPct, 160000));
        var rewardAt = PersonaSupportEv.RewardWithOdds(lv => 0, lv => HopelessPct, lv => 160000);
        Assert.AreEqual(0, rewardAt(5));
        Assert.AreEqual(0, DemonKingPayoutData.From("魔王城", 5, 160000, SupportPayout.None).FinalReward);
    }

    // ---- 通知書の見た目の約束 ----

    [Test]
    public void Notice_FloorFoldsIntoOddsRow_StampNeverBelowOne()
    {
        var so = MakeLiveDungeon();
        GameConst.OverrideFromJson(PlannedJson(true));
        var paid = SupportOdds.Payout(so.GetDefenseReward(3), 1f, HopelessPct, so.GetInvestedTotal(3));
        var notice = DemonKingPayoutData.From("魔王城", 3, so.GetInvestedTotal(3), paid, 0);
        Assert.IsTrue(notice.PrincipalGuaranteed);
        Assert.IsTrue(notice.HasOddsLine);
        StringAssert.Contains("見込み 絶望", notice.OddsLabel);
        StringAssert.Contains("×0.3", notice.OddsLabel);
        StringAssert.Contains("（元本保証）", notice.OddsLabel);
        Assert.AreEqual("-100,000", notice.OddsValueNumber, "150,000 → 50,000（×0.3 の 45,000 を元本まで）");
        Assert.AreEqual(1f, notice.ReturnRate, 1e-6f, "初回の防衛成功で回収倍率はちょうど 1 倍");
        Assert.IsTrue(notice.JustRecoveredPrincipal, "ハンコは「元本回収」（0.9倍 は出ない）");

        // 2 回目以降も累計 ÷ 支援額は 1 倍以上
        var second = DemonKingPayoutData.From("魔王城", 3, so.GetInvestedTotal(3), paid, notice.CumulativeReward);
        Assert.GreaterOrEqual(second.ReturnRate, 1f);
    }

    /// <summary>通知書・実績・AI の見込みが、実際の支払い paid と同じ額を見ること。</summary>
    private static void AssertEveryoneSeesPaid(DungeonInfoScriptableObj so, int lv, float relic, float pct, SupportPayout paid)
    {
        int baseReward = so.GetDefenseReward(lv);
        int stake = so.GetInvestedTotal(lv);
        var data = new DungeonData(so) { currentDungeonLevel = lv };
        Assert.AreEqual(stake, data.GetInvestedTotal(lv));

        // 配当通知書（支援額の行も同じ出資の合計）
        var notice = DemonKingPayoutData.From("魔王城", lv, stake, paid, 0);
        Assert.AreEqual(paid.Total, notice.FinalReward);
        Assert.AreEqual(notice.FinalReward, notice.BaseReward + notice.RelicBonus + notice.OddsBonus, "行を足すと受取額");
        Assert.AreEqual(paid.FloorApplied, notice.PrincipalGuaranteed);
        Assert.GreaterOrEqual(notice.ReturnRate, 1f, "防衛成功で回収倍率が 1 倍を下回らない");

        // 実績（AchievementTracker.DefenseReward と同じ: DungeonData の GetDefenseReward・GetInvestedTotal）
        Assert.AreEqual(paid.Total, AchievementTracker.ComputeDefenseReward(data.GetDefenseReward(lv), relic, pct, data.GetInvestedTotal(lv)));

        // AI の見込み（オートプレイ: レリック込みの額に Expected、支援の期待値: RewardWithOdds）
        Assert.AreEqual(paid.Total, SupportOdds.Expected(paid.RelicReward, () => pct, stake));
        if (Mathf.Approximately(relic, 1f))
        {
            var rewardAt = PersonaSupportEv.RewardWithOdds(data.GetDefenseReward, _ => pct, data.GetInvestedTotal);
            Assert.AreEqual(paid.Total, rewardAt(lv));
        }
        Assert.AreEqual(baseReward, paid.BaseReward);
    }
}

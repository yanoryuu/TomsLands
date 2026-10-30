using NUnit.Framework;

/// <summary>
/// 配信の調整キー（gameconst の streamTuning.* と village.rankBase*。2026-10-30）。
/// 既定値（配信に無いとき）は今の動きそのまま、キーを入れたらその値が効くこと。
/// </summary>
public class StreamTuningTests
{
    [TearDown]
    public void TearDown()
    {
        GameConst.ResetToDefault();
        RemoteBalance.ResetToDefault();
        BalanceLog.TestSink = null;
    }

    // ---- 既定値 ----

    [Test]
    public void Defaults_ReproduceCurrentBehavior()
    {
        var d = new StreamTuningData();
        Assert.AreEqual(1f, d.supportOddsEasy);
        Assert.AreEqual(1f, d.supportOddsFavorable);
        Assert.AreEqual(1f, d.supportOddsEvenHero);
        Assert.AreEqual(1f, d.supportOddsEvenDemon);
        Assert.AreEqual(1f, d.supportOddsUnfavorable);
        Assert.AreEqual(1f, d.supportOddsHopeless);
        Assert.IsFalse(d.plazaBonusAfterRefund);
        Assert.IsFalse(d.paymentMorningLossFloor);
        Assert.AreEqual(ItemModel.HeroWinPriceMultiplier, d.heroWinPriceMultiplier);
        Assert.AreEqual(ItemModel.HeroWinBossPriceMultiplier, d.heroWinBossPriceMultiplier);
        Assert.AreEqual(1f, d.restockRefundRate);

        var v = new VillageSettingsData();
        Assert.AreEqual(0, v.rankBasePerVillageLevel);
        Assert.AreEqual(0, v.rankBaseProfitShort);
        Assert.AreEqual(0, v.rankBaseProfitMedium);
        Assert.AreEqual(0, v.rankBaseProfitLong);
    }

    [Test]
    public void BakedGameConst_HasDefaultTuning()
    {
        GameConst.ResetToDefault();
        Assert.IsNotNull(GameConst.Data.streamTuning, "ベイク済みアセットに区画が無くても既定値で作られる");
        Assert.IsNotNull(GameConst.Data.bossReaction);
        Assert.AreEqual(1f, SupportOdds.Multiplier(SupportOddsTier.Easy));
        Assert.AreEqual(2.2f, ItemModel.HeroWinMultiplier(1), 1e-6f);
        Assert.AreEqual(1.8f, ItemModel.HeroWinMultiplier(0), 1e-6f);
    }

    [Test]
    public void Clone_CopiesTuningDeeply()
    {
        var a = new GameConstData();
        a.streamTuning.supportOddsEasy = 2f;
        var b = a.Clone();
        b.streamTuning.supportOddsEasy = 3f;
        Assert.AreEqual(2f, a.streamTuning.supportOddsEasy, "複製を書き換えても元は変わらない");
        Assert.AreNotSame(a.bossReaction, b.bossReaction);
    }

    // ---- 1. オッズ ----

    [TestCase(90f, SupportOddsTier.Easy)]
    [TestCase(69f, SupportOddsTier.Favorable)]
    [TestCase(59f, SupportOddsTier.EvenHero)]
    [TestCase(40f, SupportOddsTier.EvenDemon)]
    [TestCase(25f, SupportOddsTier.Unfavorable)]
    [TestCase(5f, SupportOddsTier.Hopeless)]
    [TestCase(-1f, SupportOddsTier.Unknown)]
    public void SupportOdds_TierFromClearPct(float pct, SupportOddsTier expected)
    {
        Assert.AreEqual(expected, SupportOdds.FromClearPct(pct));
    }

    [Test]
    public void SupportOdds_DefaultIsOneForEveryTier_RewardUnchanged()
    {
        var d = new StreamTuningData();
        foreach (SupportOddsTier t in System.Enum.GetValues(typeof(SupportOddsTier)))
        {
            Assert.AreEqual(1f, SupportOdds.Multiplier(t, d), t.ToString());
            Assert.AreEqual(45000, SupportOdds.Apply(45000, SupportOdds.Multiplier(t, d)));
        }
    }

    [Test]
    public void SupportOdds_PlannedValues_Applied()
    {
        var d = new StreamTuningData
        {
            supportOddsEasy = 2f, supportOddsFavorable = 1.5f, supportOddsEvenHero = 1.4f,
            supportOddsEvenDemon = 0.6f, supportOddsUnfavorable = 0.45f, supportOddsHopeless = 0.3f,
        };
        Assert.AreEqual(200000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.Easy, d)));
        Assert.AreEqual(150000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.Favorable, d)));
        Assert.AreEqual(140000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.EvenHero, d)));
        Assert.AreEqual(60000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.EvenDemon, d)));
        Assert.AreEqual(45000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.Unfavorable, d)));
        Assert.AreEqual(30000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.Hopeless, d)));
        Assert.AreEqual(100000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.Unknown, d)), "見込みが分からなければ 1");
    }

    [Test]
    public void SupportOdds_FromRemoteGameConst()
    {
        GameConst.OverrideFromJson("{\"streamTuning\":{\"supportOddsEvenDemon\":0.6}}");
        Assert.AreEqual(0.6f, SupportOdds.Multiplier(SupportOddsTier.EvenDemon), 1e-6f);
        Assert.AreEqual(1f, SupportOdds.Multiplier(SupportOddsTier.EvenHero), "書いていないキーは既定のまま");
    }

    // ---- 1b. オッズの配当を、通知書・実績・AI の見込みが同じ額で見ること ----

    private static StreamTuningData PlannedOdds() => new()
    {
        supportOddsEasy = 2f, supportOddsFavorable = 1.5f, supportOddsEvenHero = 1.4f,
        supportOddsEvenDemon = 0.6f, supportOddsUnfavorable = 0.45f, supportOddsHopeless = 0.3f,
    };

    [TestCase(90f, 1f)]
    [TestCase(59f, 1.2f)]
    [TestCase(25f, 1.35f)]
    [TestCase(-1f, 1.2f)]
    public void Payout_Default_IsRelicOnly_Unchanged(float pct, float relicMul)
    {
        var d = new StreamTuningData();
        const int baseReward = 45001;
        int legacy = UnityEngine.Mathf.RoundToInt(baseReward * relicMul); // 2026-10-30 までの式
        var p = SupportOdds.Payout(baseReward, relicMul, pct, d);
        Assert.IsFalse(SupportOdds.IsActive(d));
        Assert.AreEqual(legacy, p.Total);
        Assert.AreEqual(legacy, p.RelicReward);
        Assert.AreEqual(1f, p.OddsMultiplier);

        // 通知書: オッズの行は出ず、レリック補正の行は今までと同じ
        var notice = DemonKingPayoutData.From("魔王城", 3, 30000, p, 0);
        var old = new DemonKingPayoutData("魔王城", 3, 30000, baseReward, relicMul, legacy, 0);
        Assert.IsFalse(notice.HasOddsLine);
        Assert.AreEqual(old.HasRelicBonus, notice.HasRelicBonus);
        Assert.AreEqual(old.RelicBonus, notice.RelicBonus);
        Assert.AreEqual(old.FinalReward, notice.FinalReward);
        Assert.IsFalse(old.HasOddsLine, "オッズを渡さない古い作り方も行を出さない");

        // 実績
        Assert.AreEqual(legacy, AchievementTracker.ComputeDefenseReward(baseReward, relicMul, pct, d));

        // AI の見込み: オッズが全部 1 なら見込みのドライランを呼ばず、額もそのまま
        bool called = false;
        Assert.AreEqual(legacy, SupportOdds.Expected(legacy, () => { called = true; return pct; }, d));
        System.Func<int, int> rewardAt = lv => baseReward * lv;
        Assert.AreSame(rewardAt, PersonaSupportEv.RewardWithOdds(rewardAt, lv => { called = true; return pct; }, d));
        Assert.IsFalse(called);
    }

    [Test]
    public void Payout_DefaultFromBakedGameConst_IsInactive()
    {
        GameConst.ResetToDefault();
        Assert.IsFalse(SupportOdds.IsActive());
        Assert.AreEqual(60000, AchievementTracker.ComputeDefenseReward(50000, 1.2f, 25f));
    }

    [TestCase(90f, 2f)]
    [TestCase(69f, 1.5f)]
    [TestCase(59f, 1.4f)]
    [TestCase(40f, 0.6f)]
    [TestCase(25f, 0.45f)]
    [TestCase(5f, 0.3f)]
    public void Payout_WithOdds_NoticeAchievementAndAiMatchPaid(float pct, float odds)
    {
        var d = PlannedOdds();
        const int baseReward = 50000;
        const float relicMul = 1.2f;
        var paid = SupportOdds.Payout(baseReward, relicMul, pct, d); // BattleSceneStarter.CalculateDefeatReward と同じ呼び出し
        Assert.IsTrue(SupportOdds.IsActive(d));
        Assert.AreEqual(60000, paid.RelicReward);
        Assert.AreEqual(odds, paid.OddsMultiplier, 1e-6f);
        Assert.AreEqual((int)System.Math.Round(60000 * (double)odds, System.MidpointRounding.AwayFromZero), paid.Total);

        // 通知書: オッズの行（段の名前と倍率）を出し、差はレリック補正に混ぜない。行を足すと受取額になる
        var notice = DemonKingPayoutData.From("魔王城", 3, 30000, paid, 0);
        Assert.IsTrue(notice.HasOddsLine);
        StringAssert.Contains(SupportOdds.Label(SupportOdds.FromClearPct(pct)), notice.OddsLabel);
        StringAssert.Contains($"×{odds:0.0#}", notice.OddsLabel);
        Assert.AreEqual(10000, notice.RelicBonus, "レリック補正の行は基本額 × レリックの分だけ");
        Assert.IsTrue(notice.HasRelicBonus);
        Assert.AreEqual(paid.Total, notice.FinalReward);
        Assert.AreEqual(notice.FinalReward, notice.BaseReward + notice.RelicBonus + notice.OddsBonus);
        StringAssert.StartsWith(odds >= 1f ? "+" : "−", notice.OddsValueNumber);

        // 実績の「配当」
        Assert.AreEqual(paid.Total, AchievementTracker.ComputeDefenseReward(baseReward, relicMul, pct, d));

        // AI の見込み: レリック込みの額にオッズを掛けると実際の支払いと同じ
        Assert.AreEqual(paid.Total, SupportOdds.Expected(paid.RelicReward, () => pct, d));
        // 支援の期待値（レリックを掛けない rewardAt）: 同じ見込みならレリック 1 の支払いと同じ
        var rewardAt = PersonaSupportEv.RewardWithOdds(lv => baseReward, lv => pct, d);
        Assert.AreEqual(SupportOdds.Payout(baseReward, 1f, pct, d).Total, rewardAt(3));
    }

    [Test]
    public void Payout_WithOdds_UnknownEstimate_NoLine()
    {
        var paid = SupportOdds.Payout(50000, 1f, -1f, PlannedOdds());
        Assert.AreEqual(50000, paid.Total);
        Assert.IsFalse(DemonKingPayoutData.From("魔王城", 1, 0, paid, 0).HasOddsLine);
    }

    [Test]
    public void Payout_WithOdds_FromRemote_AchievementUsesSameValue()
    {
        GameConst.OverrideFromJson("{\"streamTuning\":{\"supportOddsUnfavorable\":0.45}}");
        Assert.IsTrue(SupportOdds.IsActive());
        var paid = SupportOdds.Payout(50000, 1.2f, 25f);
        Assert.AreEqual(27000, paid.Total);
        Assert.AreEqual(27000, AchievementTracker.ComputeDefenseReward(50000, 1.2f, 25f));
        Assert.AreEqual(60000, AchievementTracker.ComputeDefenseReward(50000, 1.2f, 59f), "1 のままの段は変わらない");
    }

    [Test]
    public void SupportEv_UsesOddsAdjustedReward()
    {
        var d = PlannedOdds();
        var rates = new PersonaSupportRates();
        // 上限 = 今のLv なので、支援しないときの期待値（負け率 × 防衛報酬）だけを見る
        var plain = PersonaSupportEv.Choose(rates, "DemonKingCastle", 3, 3, lv => 10000, lv => 100000, 1000000);
        var withOdds = PersonaSupportEv.Choose(rates, "DemonKingCastle", 3, 3, lv => 10000,
            PersonaSupportEv.RewardWithOdds(lv => 100000, lv => 25f, d), 1000000);
        float p = rates.LossRate("DemonKingCastle", 3);
        Assert.AreEqual(p * 100000, plain.EvNone, 1f);
        Assert.AreEqual(p * SupportOdds.Payout(100000, 1f, 25f, d).Total, withOdds.EvNone, 1f);
    }

    // ---- 2. 広場の上乗せの土台 ----

    [Test]
    public void PlazaBase_DefaultIsSalesBeforeRefund()
    {
        Assert.AreEqual(100000, StreamTuning.PlazaBonusBase(100000, 30000, new StreamTuningData()));
    }

    [Test]
    public void PlazaBase_AfterRefundWhenKeyOn()
    {
        var d = new StreamTuningData { plazaBonusAfterRefund = true };
        Assert.AreEqual(70000, StreamTuning.PlazaBonusBase(100000, 30000, d));
        Assert.AreEqual(0, StreamTuning.PlazaBonusBase(10000, 30000, d), "売上より返金が多くても負にしない");
    }

    // ---- 3. ランクの基準 ----

    [Test]
    public void RankBase_DefaultIsTableValue()
    {
        Assert.AreEqual(130000, StreamTuning.RankBase(130000, 0, 0, 25));
        Assert.AreEqual(GameConst.Village.rankBaseProfits[(int)GameModeId.Medium], ResultModel.RankBaseProfit(GameModeId.Medium));
    }

    [Test]
    public void RankBase_GrowsLinearlyWithVillageLevel()
    {
        Assert.AreEqual(130000 + 40000 * 10, StreamTuning.RankBase(130000, 0, 40000, 10));
        Assert.AreEqual(200000 + 40000 * 5, StreamTuning.RankBase(130000, 200000, 40000, 5), "上書きがあれば上書きを土台にする");
    }

    [Test]
    public void RankBase_OverrideFromRemote()
    {
        GameConst.OverrideFromJson("{\"village\":{\"rankBaseProfitMedium\":200000}}");
        Assert.AreEqual(200000, ResultModel.RankBaseProfit(GameModeId.Medium));
        Assert.AreEqual(GameConst.Village.rankBaseProfits[(int)GameModeId.Short], ResultModel.RankBaseProfit(GameModeId.Short));
    }

    // ---- 4. 納付日の朝の損の下限 ----

    [Test]
    public void LossFloor_ZeroFloor_IsCurrentBehavior()
    {
        Assert.AreEqual(0, StreamTuning.MoneyAfterLoss(5000, -8000, 0), "今と同じ: 0 で止める");
        Assert.AreEqual(30000, StreamTuning.MoneyAfterLoss(50000, -20000, 0));
        Assert.AreEqual(70000, StreamTuning.MoneyAfterLoss(50000, 20000, 0));
    }

    [Test]
    public void LossFloor_DoesNotTakeBelowPayment()
    {
        Assert.AreEqual(40000, StreamTuning.MoneyAfterLoss(50000, -20000, 40000), "納付額 4 万を割り込む分は取らない");
        Assert.AreEqual(30000, StreamTuning.MoneyAfterLoss(30000, -20000, 40000), "もともと納付額に届かないなら 1G も取らない");
        Assert.AreEqual(80000, StreamTuning.MoneyAfterLoss(100000, -20000, 40000), "余裕があれば全額取る");
        Assert.AreEqual(70000, StreamTuning.MoneyAfterLoss(50000, 20000, 40000), "得のイベントは変えない");
    }

    [Test]
    public void LossFloor_KeyOffOrNoPlayer_IsZero()
    {
        Assert.AreEqual(0, StreamTuning.PaymentMorningFloor(null, null, new StreamTuningData()));
        Assert.AreEqual(0, StreamTuning.PaymentMorningFloor(null, null, new StreamTuningData { paymentMorningLossFloor = true }));
    }

    // ---- 5. 勇者の装備の倍率・補充の払い戻し ----

    [Test]
    public void HeroWinBossMultiplier_FromRemote()
    {
        GameConst.OverrideFromJson("{\"streamTuning\":{\"heroWinBossPriceMultiplier\":1.6}}");
        Assert.AreEqual(1.6f, ItemModel.HeroWinMultiplier(1), 1e-6f);
        Assert.AreEqual(1.8f, ItemModel.HeroWinMultiplier(0), 1e-6f, "ボスなしの倍率は既定のまま");
    }

    [Test]
    public void HeroWinMultiplier_InvalidFallsBackToCurrent()
    {
        var d = new StreamTuningData { heroWinBossPriceMultiplier = 0f };
        Assert.AreEqual(ItemModel.HeroWinBossPriceMultiplier, StreamTuning.HeroWinMultiplier(1, d));
    }

    [Test]
    public void RestockRefund_DefaultIsFullUnitCost()
    {
        var d = new StreamTuningData();
        Assert.AreEqual(3 * 1234, StreamTuning.RestockRefund(3, 1234, d));
        Assert.AreEqual(99 * 2000000, StreamTuning.RestockRefund(99, 2000000, d), "大きな額でも整数の掛け算のまま");
        Assert.AreEqual(0, StreamTuning.RestockRefund(0, 1234, d));
    }

    [Test]
    public void RestockRefund_RateApplied()
    {
        Assert.AreEqual(1851, StreamTuning.RestockRefund(3, 1234, new StreamTuningData { restockRefundRate = 0.5f }));
        Assert.AreEqual(0, StreamTuning.RestockRefund(3, 1234, new StreamTuningData { restockRefundRate = 0f }));
        GameConst.OverrideFromJson("{\"streamTuning\":{\"restockRefundRate\":0.5}}");
        Assert.AreEqual(500, StreamTuning.RestockRefund(1, 1000));
    }
}

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

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

        // 見込みの段の境目（clearOutlook.*）は 2026-10-30 までの定数のまま
        var t = new ClearOutlookSettingsData();
        Assert.AreEqual(75f, t.easyMin);
        Assert.AreEqual(63f, t.favorableMin);
        Assert.AreEqual(35f, t.evenMin);
        Assert.AreEqual(15f, t.unfavorableMin);
        Assert.IsTrue(t.IsValid);
    }

    [Test]
    public void BakedGameConst_HasDefaultTuning()
    {
        GameConst.ResetToDefault();
        Assert.IsNotNull(GameConst.Data.streamTuning, "ベイク済みアセットに区画が無くても既定値で作られる");
        Assert.IsNotNull(GameConst.Data.bossReaction);
        Assert.IsNotNull(GameConst.Data.clearOutlook, "ベイク済みアセットに区画が無くても既定の境目で作られる");
        Assert.AreEqual(75f, ClearProbabilityCalculator.Thresholds.easyMin);
        Assert.AreEqual(63f, ClearProbabilityCalculator.Thresholds.favorableMin);
        Assert.AreEqual(35f, ClearProbabilityCalculator.Thresholds.evenMin);
        Assert.AreEqual(15f, ClearProbabilityCalculator.Thresholds.unfavorableMin);
        Assert.AreEqual(2.2f, ItemModel.HeroWinMultiplier(1), 1e-6f);
        Assert.AreEqual(1.8f, ItemModel.HeroWinMultiplier(0), 1e-6f);
    }

    [Test]
    public void Clone_CopiesTuningDeeply()
    {
        var a = new GameConstData();
        a.streamTuning.restockRefundRate = 0.5f;
        var b = a.Clone();
        b.streamTuning.restockRefundRate = 0.8f;
        Assert.AreEqual(0.5f, a.streamTuning.restockRefundRate, "複製を書き換えても元は変わらない");
        Assert.AreNotSame(a.bossReaction, b.bossReaction);
        a.clearOutlook.favorableMin = 55f;
        var c = a.Clone();
        c.clearOutlook.favorableMin = 60f;
        Assert.AreEqual(55f, a.clearOutlook.favorableMin, "境目も深くコピーする");
    }

    // ---- 1. 見込みの段の境目 ----

    /// <summary>シートで境目を変えたとき（75 / 55 / 45 / 15）。</summary>
    private const string PlannedJson =
        "{\"clearOutlook\":{\"easyMin\":75,\"favorableMin\":55,\"evenMin\":45,\"unfavorableMin\":15}}";

    // 既定の境目（75 / 63 / 35 / 15）= 2026-10-30 までの見込みの段そのまま。計測ログの段のキーも同じ境目
    [TestCase(90f, ClearOutlook.Easy, "easy")]
    [TestCase(75f, ClearOutlook.Easy, "easy")]
    [TestCase(74.9f, ClearOutlook.Favorable, "favorable")]
    [TestCase(63f, ClearOutlook.Favorable, "favorable")]
    [TestCase(62.9f, ClearOutlook.Even, "even")]
    [TestCase(40f, ClearOutlook.Even, "even")]
    [TestCase(35f, ClearOutlook.Even, "even")]
    [TestCase(34.9f, ClearOutlook.Unfavorable, "unfavorable")]
    [TestCase(15f, ClearOutlook.Unfavorable, "unfavorable")]
    [TestCase(14.9f, ClearOutlook.Hopeless, "hopeless")]
    [TestCase(5f, ClearOutlook.Hopeless, "hopeless")]
    public void ClearOutlook_DefaultThresholds_LogKeyMatches(float pct, ClearOutlook expected, string key)
    {
        GameConst.ResetToDefault();
        Assert.AreEqual(expected, ClearProbabilityCalculator.ToOutlook(pct));
        Assert.AreEqual(key, BalanceLog.OutlookKey(pct), "計測ログの段は画面の見込みと同じ");
    }

    [Test]
    public void ClearOutlook_UnknownPct_LogKeyIsUnknown()
    {
        Assert.AreEqual("unknown", BalanceLog.OutlookKey(-1f));
        Assert.AreEqual("不明", BalanceLog.OutlookLabel(-1f));
    }

    // 境目を変えたとき（clearOutlook.favorableMin 55・evenMin 45）: 楽勝 75〜 / 有利 55〜75 / 互角 45〜55 / 不利 15〜45 / 絶望 〜15
    [TestCase(90f, ClearOutlook.Easy)]
    [TestCase(74.9f, ClearOutlook.Favorable)]
    [TestCase(55f, ClearOutlook.Favorable)]
    [TestCase(54.9f, ClearOutlook.Even)]
    [TestCase(45f, ClearOutlook.Even)]
    [TestCase(44.9f, ClearOutlook.Unfavorable)]
    [TestCase(15f, ClearOutlook.Unfavorable)]
    [TestCase(14.9f, ClearOutlook.Hopeless)]
    public void ClearOutlook_RemoteThresholds(float pct, ClearOutlook expected)
    {
        GameConst.OverrideFromJson(PlannedJson);
        Assert.AreEqual(expected, ClearProbabilityCalculator.ToOutlook(pct), "画面の見込み（店・マップ・メーター）も同じ境目");
    }

    [Test]
    public void ClearOutlook_OnlyWrittenKeysChange()
    {
        GameConst.OverrideFromJson("{\"clearOutlook\":{\"evenMin\":45}}");
        var t = ClearProbabilityCalculator.Thresholds;
        Assert.AreEqual(75f, t.easyMin);
        Assert.AreEqual(63f, t.favorableMin, "書いていないキーは既定のまま");
        Assert.AreEqual(45f, t.evenMin);
        Assert.AreEqual(15f, t.unfavorableMin);
        Assert.AreEqual(ClearOutlook.Unfavorable, ClearProbabilityCalculator.ToOutlook(40f));
    }

    [Test]
    public void ClearOutlook_BrokenThresholds_FallBackToDefault()
    {
        var broken = new ClearOutlookSettingsData { favorableMin = 30f, evenMin = 45f }; // 有利の下限が互角より低い
        Assert.IsFalse(broken.IsValid);
        Assert.AreEqual(ClearOutlook.Even, ClearProbabilityCalculator.ToOutlook(40f, broken));
        Assert.AreEqual(ClearOutlook.Favorable, ClearProbabilityCalculator.ToOutlook(70f, new ClearOutlookSettingsData { easyMin = float.NaN }));
    }

    [Test]
    public void LongShotAchievement_UsesSameThresholds()
    {
        GameConst.ResetToDefault();
        Assert.IsTrue(AchievementDefinitions.IsLongShotClearPct(34.9f));
        Assert.IsFalse(AchievementDefinitions.IsLongShotClearPct(35f), "既定は今までどおり 35% 未満");
        Assert.IsFalse(AchievementDefinitions.IsLongShotClearPct(-1f), "見込みが分からなければ大穴にしない");
        GameConst.OverrideFromJson(PlannedJson);
        Assert.IsTrue(AchievementDefinitions.IsLongShotClearPct(44.9f), "境目を 45 にしたら不利は 45% 未満");
        Assert.IsFalse(AchievementDefinitions.IsLongShotClearPct(45f));
    }

    // ---- 1b. 防衛報酬（魔王軍支援の配当）= 基本額 × レリック倍率 ----

    [TestCase(1f)]
    [TestCase(1.2f)]
    [TestCase(1.35f)]
    public void Payout_IsBaseTimesRelic_NoticeMatchesPaid(float relicMul)
    {
        const int baseReward = 45001;
        int legacy = UnityEngine.Mathf.RoundToInt(baseReward * relicMul); // 2026-10-30 までの式
        var p = SupportPayout.Calculate(baseReward, relicMul); // BattleSceneStarter.CalculateDefeatReward と同じ呼び出し
        Assert.AreEqual(legacy, p.Total);
        Assert.AreEqual(baseReward, p.BaseReward);
        Assert.AreEqual(relicMul, p.Multiplier, 1e-6f);

        // 通知書: レリック補正の行は基本額との差、行を足すと受取額
        var notice = DemonKingPayoutData.From("魔王城", 3, 30000, p, 0);
        var old = new DemonKingPayoutData("魔王城", 3, 30000, baseReward, relicMul, legacy, 0);
        Assert.AreEqual(old.HasRelicBonus, notice.HasRelicBonus);
        Assert.AreEqual(old.RelicBonus, notice.RelicBonus);
        Assert.AreEqual(old.FinalReward, notice.FinalReward);
        Assert.AreEqual(notice.FinalReward, notice.BaseReward + notice.RelicBonus);
    }

    [Test]
    public void Payout_None_IsZero()
    {
        Assert.AreEqual(0, SupportPayout.None.Total);
        Assert.AreEqual(0, SupportPayout.Calculate(0, 1.2f).Total, "未出資（基本額 0）は 0");
    }

    [Test]
    public void SupportEv_UsesRewardTimesLossRate()
    {
        var rates = new PersonaSupportRates();
        // 上限 = 今のLv なので、支援しないときの期待値（負け率 × 防衛報酬）だけを見る
        var plain = PersonaSupportEv.Choose(rates, "DemonKingCastle", 3, 3, lv => 10000,
            lv => SupportPayout.Calculate(100000, 1.2f).Total, 1000000);
        float p = rates.LossRate("DemonKingCastle", 3);
        Assert.AreEqual(p * 120000, plain.EvNone, 1f);
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

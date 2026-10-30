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
        Assert.AreEqual(1f, d.supportOddsEven);
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
        a.clearOutlook.favorableMin = 55f;
        var c = a.Clone();
        c.clearOutlook.favorableMin = 60f;
        Assert.AreEqual(55f, a.clearOutlook.favorableMin, "境目も深くコピーする");
    }

    // ---- 1. オッズ ----

    // 既定の境目（75 / 63 / 35 / 15）= 2026-10-30 までの見込みの段そのまま
    [TestCase(90f, SupportOddsTier.Easy)]
    [TestCase(75f, SupportOddsTier.Easy)]
    [TestCase(74.9f, SupportOddsTier.Favorable)]
    [TestCase(63f, SupportOddsTier.Favorable)]
    [TestCase(62.9f, SupportOddsTier.Even)]
    [TestCase(59f, SupportOddsTier.Even)]
    [TestCase(40f, SupportOddsTier.Even)]
    [TestCase(35f, SupportOddsTier.Even)]
    [TestCase(34.9f, SupportOddsTier.Unfavorable)]
    [TestCase(15f, SupportOddsTier.Unfavorable)]
    [TestCase(14.9f, SupportOddsTier.Hopeless)]
    [TestCase(5f, SupportOddsTier.Hopeless)]
    [TestCase(-1f, SupportOddsTier.Unknown)]
    public void SupportOdds_TierFromClearPct_DefaultThresholds(float pct, SupportOddsTier expected)
    {
        GameConst.ResetToDefault();
        Assert.AreEqual(expected, SupportOdds.FromClearPct(pct));
        if (expected != SupportOddsTier.Unknown)
            Assert.AreEqual((int)expected, (int)ClearProbabilityCalculator.ToOutlook(pct), "画面の見込みと同じ段");
    }

    // 予定の境目（clearOutlook.favorableMin 55・evenMin 45）: 楽勝 75〜 / 有利 55〜75 / 互角 45〜55 / 不利 15〜45 / 絶望 〜15
    [TestCase(90f, SupportOddsTier.Easy)]
    [TestCase(75f, SupportOddsTier.Easy)]
    [TestCase(74.9f, SupportOddsTier.Favorable)]
    [TestCase(59f, SupportOddsTier.Favorable)]
    [TestCase(55f, SupportOddsTier.Favorable)]
    [TestCase(54.9f, SupportOddsTier.Even)]
    [TestCase(50f, SupportOddsTier.Even)]
    [TestCase(45f, SupportOddsTier.Even)]
    [TestCase(44.9f, SupportOddsTier.Unfavorable)]
    [TestCase(40f, SupportOddsTier.Unfavorable)]
    [TestCase(15f, SupportOddsTier.Unfavorable)]
    [TestCase(14.9f, SupportOddsTier.Hopeless)]
    public void SupportOdds_TierFromClearPct_RemoteThresholds(float pct, SupportOddsTier expected)
    {
        GameConst.OverrideFromJson(PlannedJson);
        Assert.AreEqual(expected, SupportOdds.FromClearPct(pct));
        Assert.AreEqual((int)expected, (int)ClearProbabilityCalculator.ToOutlook(pct), "画面の見込み（店・マップ・メーター）も同じ境目");
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
        var d = PlannedOdds();
        Assert.AreEqual(200000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.Easy, d)));
        Assert.AreEqual(150000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.Favorable, d)));
        Assert.AreEqual(100000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.Even, d)));
        Assert.AreEqual(45000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.Unfavorable, d)));
        Assert.AreEqual(30000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.Hopeless, d)));
        Assert.AreEqual(100000, SupportOdds.Apply(100000, SupportOdds.Multiplier(SupportOddsTier.Unknown, d)), "見込みが分からなければ 1");
    }

    [Test]
    public void SupportOdds_FromRemoteGameConst()
    {
        GameConst.OverrideFromJson("{\"streamTuning\":{\"supportOddsEven\":0.8}}");
        Assert.AreEqual(0.8f, SupportOdds.Multiplier(SupportOddsTier.Even), 1e-6f);
        Assert.AreEqual(1f, SupportOdds.Multiplier(SupportOddsTier.Favorable), "書いていないキーは既定のまま");

        GameConst.OverrideFromJson(PlannedJson);
        var d = StreamTuning.Data;
        var planned = PlannedOdds();
        Assert.AreEqual(planned.supportOddsEasy, d.supportOddsEasy);
        Assert.AreEqual(planned.supportOddsFavorable, d.supportOddsFavorable);
        Assert.AreEqual(planned.supportOddsEven, d.supportOddsEven);
        Assert.AreEqual(planned.supportOddsUnfavorable, d.supportOddsUnfavorable);
        Assert.AreEqual(planned.supportOddsHopeless, d.supportOddsHopeless);
    }

    // ---- 1b. オッズの配当を、通知書・実績・AI の見込みが同じ額で見ること ----

    private static StreamTuningData PlannedOdds() => new()
    {
        supportOddsEasy = 2f, supportOddsFavorable = 1.5f, supportOddsEven = 1f,
        supportOddsUnfavorable = 0.45f, supportOddsHopeless = 0.3f,
    };

    /// <summary>リセット時にシートへ入れる予定の値（境目 75 / 55 / 45 / 15、オッズ 2.0 / 1.5 / 1.0 / 0.45 / 0.3）。</summary>
    private const string PlannedJson =
        "{\"clearOutlook\":{\"easyMin\":75,\"favorableMin\":55,\"evenMin\":45,\"unfavorableMin\":15}," +
        "\"streamTuning\":{\"supportOddsEasy\":2,\"supportOddsFavorable\":1.5,\"supportOddsEven\":1," +
        "\"supportOddsUnfavorable\":0.45,\"supportOddsHopeless\":0.3}}";

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

    // シートに予定の境目とオッズを入れたとき（GameConst 経由。境目の 75 / 55 / 45 / 15 をまたぐ）
    [TestCase(90f, 2f)]
    [TestCase(75f, 2f)]
    [TestCase(74.9f, 1.5f)]
    [TestCase(55f, 1.5f)]
    [TestCase(54.9f, 1f)]
    [TestCase(45f, 1f)]
    [TestCase(44.9f, 0.45f)]
    [TestCase(15f, 0.45f)]
    [TestCase(14.9f, 0.3f)]
    [TestCase(5f, 0.3f)]
    public void Payout_WithOdds_NoticeAchievementAndAiMatchPaid(float pct, float odds)
    {
        GameConst.OverrideFromJson(PlannedJson);
        const int baseReward = 50000;
        const float relicMul = 1.2f;
        var paid = SupportOdds.Payout(baseReward, relicMul, pct); // BattleSceneStarter.CalculateDefeatReward と同じ呼び出し
        Assert.IsTrue(SupportOdds.IsActive());
        Assert.AreEqual(60000, paid.RelicReward);
        Assert.AreEqual(odds, paid.OddsMultiplier, 1e-6f);
        Assert.AreEqual((int)System.Math.Round(60000 * (double)odds, System.MidpointRounding.AwayFromZero), paid.Total);

        // 通知書: オッズの行（段の名前と倍率）を出し、差はレリック補正に混ぜない。行を足すと受取額になる。互角（×1.0）は行を出さない
        var notice = DemonKingPayoutData.From("魔王城", 3, 30000, paid, 0);
        Assert.AreEqual(!UnityEngine.Mathf.Approximately(odds, 1f), notice.HasOddsLine);
        if (notice.HasOddsLine)
        {
            StringAssert.Contains(SupportOdds.Label(SupportOdds.FromClearPct(pct)), notice.OddsLabel);
            StringAssert.Contains(ClearProbabilityCalculator.OutlookLabel(ClearProbabilityCalculator.ToOutlook(pct)), notice.OddsLabel,
                "通知書の段の名前は画面の見込みと同じ");
            StringAssert.Contains($"×{odds:0.0#}", notice.OddsLabel);
            StringAssert.StartsWith(odds >= 1f ? "+" : "-", notice.OddsValueNumber);
        }
        Assert.AreEqual(10000, notice.RelicBonus, "レリック補正の行は基本額 × レリックの分だけ");
        Assert.IsTrue(notice.HasRelicBonus);
        Assert.AreEqual(paid.Total, notice.FinalReward);
        Assert.AreEqual(notice.FinalReward, notice.BaseReward + notice.RelicBonus + notice.OddsBonus);

        // 実績の「配当」
        Assert.AreEqual(paid.Total, AchievementTracker.ComputeDefenseReward(baseReward, relicMul, pct));

        // AI の見込み: レリック込みの額にオッズを掛けると実際の支払いと同じ
        Assert.AreEqual(paid.Total, SupportOdds.Expected(paid.RelicReward, () => pct));
        // 支援の期待値（レリックを掛けない rewardAt）: 同じ見込みならレリック 1 の支払いと同じ
        var rewardAt = PersonaSupportEv.RewardWithOdds(lv => baseReward, lv => pct, StreamTuning.Data);
        Assert.AreEqual(SupportOdds.Payout(baseReward, 1f, pct).Total, rewardAt(3));
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
        Assert.AreEqual(60000, AchievementTracker.ComputeDefenseReward(50000, 1.2f, 40f), "既定の境目では 40% は互角（1 のまま）");
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

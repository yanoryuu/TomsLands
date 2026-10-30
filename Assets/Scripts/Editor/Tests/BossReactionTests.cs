using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

/// <summary>
/// ボスの反応（2026-10-30。盛り上がりと欲しさを分ける・状況の点数・反応の表 bal_bossReaction・影のログ）。
/// 表が空・キーが既定なら熱も売れ行きも今と同じで、ログだけ出ること。表を入れたらその値が効くこと。
/// </summary>
public class BossReactionTests
{
    private const float Pinch = 0.5f;
    private const float Critical = 0.25f;
    private readonly List<JObject> _logs = new();

    [SetUp]
    public void SetUp()
    {
        _logs.Clear();
        BalanceLog.TestSink = _logs.Add;
        RemoteBalance.ResetToDefault();
    }

    [TearDown]
    public void TearDown()
    {
        BalanceLog.TestSink = null;
        BalanceLog.EndStream();
        RemoteBalance.ResetToDefault();
        GameConst.ResetToDefault();
    }

    private static BossReactionTracker.Snapshot At(float meter, float hp = 1f, float heat = 50f, int phase = 4)
        => new BossReactionTracker.Snapshot(heat, meter, hp, phase, 5);

    private static void LoadTable(string rows)
        => Assert.AreEqual(7, RemoteBalance.OverrideFromBundle("{\"version\":7,\"schemaVersion\":1,\"bossReaction\":[" + rows + "]}"));

    // ---- 状況の点数 ----

    [Test]
    public void Score_MeterTiersMapToSituations()
    {
        var s = new BossReactionSettingsData();
        Assert.AreEqual(BossSituation.Winning, BossSituationScorer.Score(0.9f, 1f, false, null, s, Pinch, Critical).Situation);
        Assert.AreEqual(BossSituation.Favored, BossSituationScorer.Score(0.69f, 1f, false, null, s, Pinch, Critical).Situation);
        Assert.AreEqual(BossSituation.Close, BossSituationScorer.Score(0.59f, 1f, false, null, s, Pinch, Critical).Situation, "互角（勇者寄り）は拮抗");
        Assert.AreEqual(BossSituation.Close, BossSituationScorer.Score(0.40f, 1f, false, null, s, Pinch, Critical).Situation, "互角（魔王寄り）も拮抗");
        Assert.AreEqual(BossSituation.Behind, BossSituationScorer.Score(0.25f, 1f, false, null, s, Pinch, Critical).Situation);
        Assert.AreEqual(BossSituation.Desperate, BossSituationScorer.Score(0.05f, 1f, false, null, s, Pinch, Critical).Situation);
    }

    [Test]
    public void Score_MeterKeepsSixStepsWithOwnEvenSplit()
    {
        // 見込みの互角は 1 段だが、ボスの点数のメーター点は 6 段のまま（互角を 50% で分ける）
        var s = new BossReactionSettingsData();
        Assert.AreEqual(5f, BossSituationScorer.Score(0.90f, 1f, false, null, s, Pinch, Critical).MeterPoints);
        Assert.AreEqual(4f, BossSituationScorer.Score(0.69f, 1f, false, null, s, Pinch, Critical).MeterPoints);
        Assert.AreEqual(3f, BossSituationScorer.Score(0.59f, 1f, false, null, s, Pinch, Critical).MeterPoints);
        Assert.AreEqual(2f, BossSituationScorer.Score(0.40f, 1f, false, null, s, Pinch, Critical).MeterPoints);
        Assert.AreEqual(1f, BossSituationScorer.Score(0.25f, 1f, false, null, s, Pinch, Critical).MeterPoints);
        Assert.AreEqual(0f, BossSituationScorer.Score(0.05f, 1f, false, null, s, Pinch, Critical).MeterPoints);
        Assert.AreEqual(2.5f, BossSituationScorer.Score(float.NaN, 1f, false, null, s, Pinch, Critical).MeterPoints);
        Assert.AreEqual("evenHero", BossMeterSteps.Key(BossMeterSteps.FromMeter(0.59f)));
        Assert.AreEqual("evenDemon", BossMeterSteps.Key(BossMeterSteps.FromMeter(0.40f)));
    }

    [Test]
    public void Score_AddsHpWeakAndFlow()
    {
        var s = new BossReactionSettingsData();
        var r = BossSituationScorer.Score(0.59f, 1f, true, null, s, Pinch, Critical);
        Assert.AreEqual(4f, r.Score, 1e-5f, "互角・勇者寄り 3 + 弱点 1");
        Assert.AreEqual(BossSituation.Favored, r.Situation);

        r = BossSituationScorer.Score(0.40f, 0.2f, false, null, s, Pinch, Critical);
        Assert.AreEqual(1f, r.Score, 1e-5f, "互角・魔王寄り 2 + 大ピンチ −1");
        Assert.AreEqual(BossSituation.Behind, r.Situation);

        r = BossSituationScorer.Score(0.59f, 0.4f, false, 0.3f, s, Pinch, Critical);
        Assert.AreEqual(3f, r.Score, 1e-5f, "3 + ピンチ −0.5 + 流れ（メーターが上がった）+0.5");
        Assert.AreEqual(0.5f, r.FlowPoints);
    }

    // ---- 表が空 = 今と同じ ----

    [Test]
    public void EmptyTable_BossAddsBossAppearHeatOnly_NoMood()
    {
        Assert.IsTrue(BossReactionTable.IsEmpty);
        var t = new BossReactionTracker(new BossReactionSettingsData(), Pinch, Critical, 30f);
        var e = t.OnAppear(BossReactionTracker.KindBoss, At(0.59f), false, 40f);
        Assert.AreEqual(40f, e.Heat, "表が空なら bossAppearHeat のまま");
        Assert.IsNull(e.Row);
        Assert.AreEqual(1f, t.GetVolumeMultiplier(ItemTypeData.ItemType.Weapon));
        Assert.AreEqual(1f, t.GetVolumeMultiplier(ItemTypeData.ItemType.Armor));
    }

    [Test]
    public void EmptyTable_MidbossAndAfterEvents_AddNoHeat()
    {
        var t = new BossReactionTracker(new BossReactionSettingsData(), Pinch, Critical, 30f);
        var effects = t.RecordTurn(At(0.6f, phase: 3), 1, 1f, DungeonPhaseTypes.Midboss, false);
        Assert.AreEqual(1, effects.Count, "中ボスのウェーブに入ったら状況を出す（影）");
        Assert.AreEqual(BossReactionTracker.KindMidboss, effects[0].Kind);
        Assert.AreEqual(0f, effects[0].Heat, "表が無ければ中ボスの熱は 0");

        t.OnAppear(BossReactionTracker.KindBoss, At(0.05f), false, 40f); // 絶望で出た
        effects = t.RecordTurn(At(0.7f, phase: 4), 2, 1.02f, DungeonPhaseTypes.Boss, false);
        Assert.AreEqual(1, effects.Count);
        Assert.AreEqual(BossReactionTracker.KindComeback, effects[0].Kind);
        Assert.AreEqual(0f, effects[0].Heat, "逆転の受け口の既定は 0");
        Assert.AreEqual(1f, t.GetVolumeMultiplier(ItemTypeData.ItemType.Weapon));

        effects = t.RecordTurn(At(0.8f, phase: 4), 2, 1.02f, DungeonPhaseTypes.Boss, false);
        Assert.AreEqual(0, effects.Count, "逆転は 1 回だけ");
    }

    [Test]
    public void NoPhaseType_NoMidboss()
    {
        var t = new BossReactionTracker(new BossReactionSettingsData(), Pinch, Critical, 30f);
        Assert.AreEqual(0, t.RecordTurn(At(0.6f, phase: 0), 1, 1f, null, false).Count);
        Assert.AreEqual(0, t.RecordTurn(At(0.6f, phase: 1), 1, 1f, null, false).Count, "phaseType の無い版では中ボスは出ない");
    }

    // ---- 表を入れたとき ----

    [Test]
    public void Table_BossRow_ReplacesHeatAndSetsMoodForTurns()
    {
        LoadTable("{\"id\":\"boss_close\",\"heat\":25,\"weaponMood\":1.2,\"armorMood\":0.9,\"moodTurns\":2}");
        Assert.AreEqual(1, BossReactionTable.Count);
        var t = new BossReactionTracker(new BossReactionSettingsData(), Pinch, Critical, 30f);
        var e = t.OnAppear(BossReactionTracker.KindBoss, At(0.59f), false, 40f);
        Assert.AreEqual(BossSituation.Close, e.Situation);
        Assert.AreEqual(25f, e.Heat);
        Assert.AreEqual(1.2f, t.GetVolumeMultiplier(ItemTypeData.ItemType.Weapon), 1e-6f);
        Assert.AreEqual(0.9f, t.GetVolumeMultiplier(ItemTypeData.ItemType.Armor), 1e-6f);
        Assert.AreEqual(1f, t.GetVolumeMultiplier(ItemTypeData.ItemType.Tool), "道具は動かさない");

        t.RecordTurn(At(0.59f), 2, 1.02f, DungeonPhaseTypes.Boss, false);
        Assert.AreEqual(1.2f, t.GetVolumeMultiplier(ItemTypeData.ItemType.Weapon), 1e-6f, "1 ターン目はまだ効く");
        t.RecordTurn(At(0.59f), 2, 1.02f, DungeonPhaseTypes.Boss, false);
        Assert.AreEqual(1f, t.GetVolumeMultiplier(ItemTypeData.ItemType.Weapon), "moodTurns が過ぎたら 1 に戻る");
    }

    [Test]
    public void Table_OtherSituation_FallsBackToDefault()
    {
        LoadTable("{\"id\":\"boss_close\",\"heat\":25}");
        var t = new BossReactionTracker(new BossReactionSettingsData(), Pinch, Critical, 30f);
        var e = t.OnAppear(BossReactionTracker.KindBoss, At(0.9f), false, 40f);
        Assert.AreEqual(BossSituation.Winning, e.Situation);
        Assert.AreEqual(40f, e.Heat, "行の無い状況は bossAppearHeat");
    }

    [Test]
    public void Table_MidbossRow_JapaneseSituation()
    {
        LoadTable("{\"id\":\"m1\",\"phaseType\":\"midboss\",\"situation\":\"絶望\",\"heat\":10}");
        var t = new BossReactionTracker(new BossReactionSettingsData(), Pinch, Critical, 30f);
        var effects = t.RecordTurn(At(0.05f, phase: 3), 0, 0.97f, DungeonPhaseTypes.Midboss, false);
        Assert.AreEqual(10f, effects[0].Heat);
        Assert.AreEqual("m1", effects[0].Row.Id);
    }

    [Test]
    public void Table_ClearedOnReset()
    {
        LoadTable("{\"id\":\"boss_close\",\"heat\":25}");
        RemoteBalance.ResetToDefault();
        Assert.IsTrue(BossReactionTable.IsEmpty);
        Assert.AreEqual(7, RemoteBalance.OverrideFromBundle("{\"version\":7,\"schemaVersion\":1}"));
        Assert.IsTrue(BossReactionTable.IsEmpty, "配信に区画が無ければ空");
    }

    [Test]
    public void AfterEvents_KeysApplied()
    {
        var s = new BossReactionSettingsData { collapseHeat = 15f, collapseWeaponMood = -0.2f, afterMoodTurns = 1 };
        var t = new BossReactionTracker(s, Pinch, Critical, 30f);
        t.OnAppear(BossReactionTracker.KindBoss, At(0.9f), false, 40f); // 勝ちムードで出た
        var effects = t.RecordTurn(At(0.3f), 2, 1.02f, DungeonPhaseTypes.Boss, false);
        Assert.AreEqual(BossReactionTracker.KindCollapse, effects[0].Kind);
        Assert.AreEqual(15f, effects[0].Heat);
        Assert.AreEqual(0.8f, t.GetVolumeMultiplier(ItemTypeData.ItemType.Weapon), 1e-6f, "崩れの次のターンの販売は武器の売れ行き −0.2");
        Assert.AreEqual(1f, t.GetVolumeMultiplier(ItemTypeData.ItemType.Armor));
        t.RecordTurn(At(0.3f), 2, 1.02f, DungeonPhaseTypes.Boss, false);
        Assert.AreEqual(1f, t.GetVolumeMultiplier(ItemTypeData.ItemType.Weapon), "afterMoodTurns = 1 ターンで戻る");
    }

    [Test]
    public void NothingCarriesOver_NewTrackerStartsNeutral()
    {
        LoadTable("{\"id\":\"boss_close\",\"weaponMood\":1.5}");
        var day1 = new BossReactionTracker(new BossReactionSettingsData(), Pinch, Critical, 30f);
        day1.OnAppear(BossReactionTracker.KindBoss, At(0.59f), false, 40f);
        Assert.AreEqual(1.5f, day1.GetVolumeMultiplier(ItemTypeData.ItemType.Weapon), 1e-6f);
        var day2 = new BossReactionTracker(new BossReactionSettingsData(), Pinch, Critical, 30f);
        Assert.AreEqual(1f, day2.GetVolumeMultiplier(ItemTypeData.ItemType.Weapon), "次の配信には持ち越さない");
        Assert.AreEqual(BossSituation.None, day2.BossSituation);
    }

    // ---- 影のログ ----

    [Test]
    public void ShadowLogs_WrittenWithStreamContext()
    {
        BalanceLog.BeginStream("DemonKingCastle", 3, 12, 20, 58f);
        var t = new BossReactionTracker(new BossReactionSettingsData(), Pinch, Critical, 30f);
        t.RecordTurn(At(0.6f, heat: 35f, phase: 0), 1, 1f, DungeonPhaseTypes.Normal, false);
        var e = t.OnAppear(BossReactionTracker.KindBoss, At(0.6f), false, 40f);
        t.LogEffect(e, 35f, 75f);
        var summary = t.BuildSummary();

        Assert.AreEqual("heat_turn", (string)_logs[0]["kind"]);
        Assert.AreEqual("DemonKingCastle", (string)_logs[0]["dungeon"]);
        Assert.AreEqual("even", (string)_logs[0]["estimateTier"]);
        Assert.AreEqual("boss_situation", (string)_logs[1]["kind"]);
        Assert.AreEqual("close", (string)_logs[1]["situation"]);
        Assert.AreEqual(75.0, (double)_logs[1]["heatAfter"], 1e-6);
        Assert.AreEqual("close", (string)summary["bossSituation"]);
        Assert.AreEqual(1, (int)summary["turns"]);
    }

    [Test]
    public void ShadowTurnLog_CanBeTurnedOff()
    {
        var t = new BossReactionTracker(new BossReactionSettingsData { shadowTurnLog = false }, Pinch, Critical, 30f);
        t.RecordTurn(At(0.6f), 1, 1f, null, false);
        Assert.AreEqual(0, _logs.Count);
    }

    // ---- ウェーブの種類 ----

    [Test]
    public void PhaseType_ReadByReflection()
    {
        var phase = new DungeonPhaseData();
        var field = typeof(DungeonPhaseData).GetField("phaseType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
        if (field == null)
        {
            Assert.IsNull(DungeonPhaseTypes.Of(phase), "phaseType の無い版では null");
            return;
        }
        if (field.FieldType.IsEnum)
        {
            foreach (var name in System.Enum.GetNames(field.FieldType))
            {
                if (!string.Equals(name, "midboss", System.StringComparison.OrdinalIgnoreCase)) continue;
                field.SetValue(phase, System.Enum.Parse(field.FieldType, name));
                Assert.AreEqual(DungeonPhaseTypes.Midboss, DungeonPhaseTypes.Of(phase));
            }
        }
        else if (field.FieldType == typeof(string))
        {
            field.SetValue(phase, "midboss");
            Assert.AreEqual(DungeonPhaseTypes.Midboss, DungeonPhaseTypes.Of(phase));
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 魔王軍支援の画面の配当のオッズの見込み（SupportOddsPreview。2026-10-30 ユーザー決定: 支援する前にオッズを見せる）。
/// 出す段・倍率・配当は SupportOdds（実際の支払いと同じ式）の結果そのものであること。オッズが全部 1（今の配信）なら出さないこと。
/// </summary>
public class SupportOddsPreviewTests
{
    private const string PrefabPath = "Assets/Prefabs/Screens/DungeonSupport.prefab";

    // 同梱のアセットの表（dungeonLevels が無い配信のとき）。Lv4 は配当 70,000 < 出資 80,000 で元本保証が効く
    private static readonly int[] Costs = { 10000, 20000, 40000, 150000, 0 };
    private static readonly int[] Rewards = { 15000, 30000, 45000, 70000, 120000 };

    /// <summary>シートに入れる予定の境目とオッズ（75 / 59 / 41 / 15、2.0 / 1.5 / 1.0 / 0.45 / 0.3）＋元本保証。</summary>
    private const string PlannedJson =
        "{\"clearOutlook\":{\"easyMin\":75,\"favorableMin\":59,\"evenMin\":41,\"unfavorableMin\":15}," +
        "\"streamTuning\":{\"supportOddsEasy\":2,\"supportOddsFavorable\":1.5,\"supportOddsEven\":1," +
        "\"supportOddsUnfavorable\":0.45,\"supportOddsHopeless\":0.3,\"supportPayoutFloorToStake\":true}}";

    private readonly List<Object> _created = new();

    [TearDown]
    public void TearDown()
    {
        GameConst.ResetToDefault();
        foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
        _created.Clear();
    }

    private DungeonData MakeDungeon()
    {
        var so = ScriptableObject.CreateInstance<DungeonInfoScriptableObj>();
        _created.Add(so);
        so.key = DungeonName.DemonKingCastle;
        so.dungeonName = "魔王城";
        so.initDungeonLevel = 0;
        so.levelDataList = new DungeonLevelData[5];
        for (int i = 0; i < 5; i++)
            so.levelDataList[i] = new DungeonLevelData { levelUpCost = Costs[i], rewardGold = Rewards[i], bossName = "boss" + i };
        return new DungeonData(so) { currentDungeonLevel = 3, isShowedInfo = true };
    }

    [Test]
    public void Inactive_LiveConfig_IsHidden_AndSkipsDryRun()
    {
        GameConst.ResetToDefault();
        Assert.IsFalse(SupportOdds.IsActive(), "今の配信（同梱の既定）はオッズが全部 1");
        var d = MakeDungeon();
        bool called = false;
        var r = SupportOddsPreview.Build(3, 4, _ => { called = true; return 65f; }, d.GetDefenseReward, d.GetInvestedTotal, 1f, true);
        Assert.IsFalse(r.Visible);
        Assert.AreEqual(string.Empty, r.Text);
        Assert.IsFalse(called, "出さないときは見込みのドライランをしない");
    }

    [Test]
    public void Active_ShowsSupportOddsResults_AndNextLevelDropsTier()
    {
        GameConst.OverrideFromJson(PlannedJson);
        Assert.IsTrue(SupportOdds.IsActive());
        var d = MakeDungeon();
        const float relic = 1.2f;
        var pcts = new Dictionary<int, float> { [3] = 65f, [4] = 30f }; // Lv3 有利 → Lv4 不利
        var r = SupportOddsPreview.Build(3, 4, lv => pcts[lv], d.GetDefenseReward, d.GetInvestedTotal, relic, true);

        Assert.IsTrue(r.Visible);
        Assert.IsTrue(r.HasNext);
        foreach (var (line, lv) in new[] { (r.Current, 3), (r.Next, 4) })
        {
            var tier = SupportOdds.FromClearPct(pcts[lv]);
            var paid = SupportOdds.Payout(d.GetDefenseReward(lv), relic, pcts[lv], d.GetInvestedTotal(lv)); // BattleSceneStarter と同じ呼び出し
            Assert.AreEqual(lv, line.Level);
            Assert.AreEqual(tier, line.Tier, $"Lv{lv} 段");
            Assert.AreEqual(SupportOdds.Multiplier(tier), line.Multiplier, $"Lv{lv} 倍率");
            Assert.AreEqual(paid.Total, line.Total, $"Lv{lv} 配当");
            Assert.AreEqual(paid.FloorApplied, line.Payout.FloorApplied);
            StringAssert.Contains(SupportOdds.Label(tier), r.Text);
            StringAssert.Contains(SupportOddsPreview.FormatMultiplier(SupportOdds.Multiplier(tier)), r.Text);
            StringAssert.Contains($"{paid.Total:N0}G", r.Text);
        }

        Assert.AreEqual(SupportOddsTier.Favorable, r.Current.Tier);
        Assert.AreEqual(SupportOddsTier.Unfavorable, r.Next.Tier, "支援するほど勇者が不利になり、オッズが下がる");
        Assert.AreEqual(1.5f, r.Current.Multiplier, 1e-6f);
        Assert.AreEqual(0.45f, r.Next.Multiplier, 1e-6f);
        Assert.AreEqual(Mathf.RoundToInt(Mathf.RoundToInt(45000 * relic) * 1.5f), r.Current.Total); // 54,000 × 1.5 = 81,000
        Assert.AreEqual(80000, r.Next.Total, "Lv4: 84,000 × 0.45 = 37,800 < 出資 80,000 → 元本保証");
        Assert.IsTrue(r.Next.Payout.FloorApplied);

        StringAssert.Contains("いまの見込み: ", r.Text);
        StringAssert.Contains("×1.5", r.Text);
        StringAssert.Contains("Lv4 にすると: ", r.Text);
        StringAssert.Contains("×0.45", r.Text);
        StringAssert.Contains("元本保証", r.Text);
        StringAssert.Contains(SupportOddsPreview.FixedNote, r.Text);
    }

    [Test]
    public void Active_MaxLevel_ShowsOnlyCurrent()
    {
        GameConst.OverrideFromJson(PlannedJson);
        var d = MakeDungeon();
        var r = SupportOddsPreview.Build(5, null, _ => 90f, d.GetDefenseReward, d.GetInvestedTotal, 1f, true);
        Assert.IsTrue(r.Visible);
        Assert.IsFalse(r.HasNext);
        Assert.AreEqual(SupportOddsTier.Easy, r.Current.Tier);
        Assert.AreEqual(SupportOdds.Payout(120000, 1f, 90f, d.GetInvestedTotal(5)).Total, r.Current.Total);
        StringAssert.DoesNotContain("にすると", r.Text);
        StringAssert.Contains(SupportOddsPreview.FixedNote, r.Text);
    }

    [Test]
    public void Active_Unfunded_ShowsOddsWithoutPayout()
    {
        GameConst.OverrideFromJson(PlannedJson);
        var d = MakeDungeon();
        var r = SupportOddsPreview.Build(0, 1, _ => 50f, d.GetDefenseReward, d.GetInvestedTotal, 1f, true);
        Assert.AreEqual(0, r.Current.Total, "未出資は配当 0（SupportOdds.Payout と同じ）");
        Assert.AreEqual(SupportOdds.Payout(15000, 1f, 50f, d.GetInvestedTotal(1)).Total, r.Next.Total);
        StringAssert.Contains("配当なし", r.Text);
    }

    [Test]
    public void Active_InfoUnknown_HidesTierAndSkipsDryRun()
    {
        GameConst.OverrideFromJson(PlannedJson);
        var d = MakeDungeon();
        bool called = false;
        var r = SupportOddsPreview.Build(3, 4, _ => { called = true; return 65f; }, d.GetDefenseReward, d.GetInvestedTotal, 1f, false);
        Assert.IsTrue(r.Visible);
        Assert.IsTrue(r.Hidden);
        Assert.IsFalse(called);
        StringAssert.Contains("？", r.Text);
        StringAssert.DoesNotContain("×", r.Text);
    }

    [Test]
    public void ClearPctCache_ComputesOncePerDungeonLevelAndHeroStats()
    {
        int calls = 0;
        var cache = new ClearPctCache((_, lv) => { calls++; return lv * 10f; });
        var d = MakeDungeon();
        var stats = new ClearProbabilityCalculator.HeroStats(100, 20, 10);
        Assert.AreEqual(30f, cache.Get(d, 3, stats));
        Assert.AreEqual(30f, cache.Get(d, 3, stats));
        Assert.AreEqual(1, calls, "同じダンジョン・Lv・勇者の値なら計算し直さない");
        cache.Get(d, 4, stats);
        Assert.AreEqual(2, calls);
        cache.Get(d, 3, new ClearProbabilityCalculator.HeroStats(100, 25, 10)); // 装備を替えた
        Assert.AreEqual(3, calls, "装備（戦う値）が変われば計算し直す");
        cache.Clear();
        cache.Get(d, 3, stats);
        Assert.AreEqual(4, calls);
    }

    [Test]
    public void View_ShowsOddsWhenActive_HidesWhenInactive()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.SetActive(true);
            var view = go.GetComponentInChildren<DungeonLevelUpView>(true);
            Assert.IsNotNull(view);
            Assert.AreEqual(string.Empty, view.SupportOddsShownText, "プレハブの既定では隠れている（今の画面のまま）");

            var d = MakeDungeon();
            GameConst.ResetToDefault();
            var inactive = SupportOddsPreview.Build(3, 4, _ => 65f, d.GetDefenseReward, d.GetInvestedTotal, 1f, true);
            view.ShowDungeonDetail(new DungeonLevelUpDetailData { DungeonName = "魔王城", OddsPreview = inactive });
            Assert.AreEqual(string.Empty, view.SupportOddsShownText);

            GameConst.OverrideFromJson(PlannedJson);
            var active = SupportOddsPreview.Build(3, 4, lv => lv == 3 ? 65f : 30f, d.GetDefenseReward, d.GetInvestedTotal, 1f, true);
            view.ShowDungeonDetail(new DungeonLevelUpDetailData { DungeonName = "魔王城", OddsPreview = active });
            Assert.AreEqual(active.Text, view.SupportOddsShownText);

            view.ClearDungeonDetail();
            Assert.AreEqual(string.Empty, view.SupportOddsShownText);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}

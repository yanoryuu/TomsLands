using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// ダンジョンのレベルの表（魔王軍支援の費用 levelUpCost・防衛の配当 rewardGold）をシート dungeonLevels から配信する（2026-10-30）。
/// "*" = 全ダンジョン共通 / 個別の id が "*" より優先 / 行や項目が無ければアセットの値 / 配当の計算も配信値を使う。
/// </summary>
public class DungeonLevelRemoteTests
{
    private readonly List<Object> _created = new();

    [TearDown]
    public void TearDown()
    {
        RemoteBalance.ResetToDefault();
        foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
        _created.Clear();
    }

    // アセットと同じ表（Lv1 10000/15000 … Lv5 0/120000）
    private DungeonInfoScriptableObj MakeDungeon(DungeonName key)
    {
        var so = ScriptableObject.CreateInstance<DungeonInfoScriptableObj>();
        _created.Add(so);
        so.key = key;
        so.dungeonName = key.ToString();
        so.initDungeonLevel = 0;
        int[] costs = { 10000, 20000, 40000, 150000, 0 };
        int[] rewards = { 15000, 30000, 45000, 70000, 120000 };
        so.levelDataList = new DungeonLevelData[5];
        for (int i = 0; i < 5; i++)
            so.levelDataList[i] = new DungeonLevelData { levelUpCost = costs[i], rewardGold = rewards[i], bossName = "boss" + i };
        return so;
    }

    private static string Bundle(string rows)
        => "{\"version\":99,\"schemaVersion\":1,\"dungeonLevels\":[" + rows + "]}";

    private static string Row(string id, int level, int? cost, int? reward)
    {
        var s = $"{{\"dungeonId\":\"{id}\",\"level\":{level}";
        if (cost != null) s += $",\"levelUpCost\":{cost}";
        if (reward != null) s += $",\"rewardGold\":{reward}";
        return s + "}";
    }

    private List<DungeonInfoScriptableObj> Apply(params DungeonInfoScriptableObj[] list)
    {
        var applied = DungeonInfoScriptableObj.ApplyRemoteBalance(new List<DungeonInfoScriptableObj>(list));
        foreach (var a in applied) if (!list.Contains(a)) _created.Add(a);
        return applied;
    }

    [Test]
    public void SharedRows_ApplyToEveryDungeon()
    {
        var forest = MakeDungeon(DungeonName.DeepGreenBeastForest);
        var castle = MakeDungeon(DungeonName.DemonKingCastle);
        Assert.AreEqual(99, RemoteBalance.OverrideFromBundle(Bundle(
            Row("*", 1, 12000, 18000) + "," + Row("*", 5, 0, 200000))));

        var applied = Apply(forest, castle);
        foreach (var d in applied)
        {
            Assert.AreEqual(12000, d.GetLevelData(1).levelUpCost);
            Assert.AreEqual(18000, d.GetDefenseReward(1));
            Assert.AreEqual(200000, d.GetDefenseReward(5));
            Assert.AreEqual(30000, d.GetDefenseReward(2), "行が無いレベルはアセットの値");
            Assert.AreEqual(20000, d.GetLevelData(2).levelUpCost);
            Assert.AreEqual("boss0", d.GetLevelData(1).bossName, "ほかの項目はアセットのまま");
        }
        Assert.AreNotSame(forest, applied[0], "配信値は複製に当てる");
        Assert.AreEqual(15000, forest.GetDefenseReward(1), "元のアセットはそのまま");
    }

    [Test]
    public void DungeonRow_BeatsSharedRow_FieldByField()
    {
        var forest = MakeDungeon(DungeonName.DeepGreenBeastForest);
        var castle = MakeDungeon(DungeonName.DemonKingCastle);
        RemoteBalance.OverrideFromBundle(Bundle(
            Row("*", 3, 50000, 60000) + "," +
            Row("DemonKingCastle", 3, null, 90000)));

        var applied = Apply(forest, castle);
        Assert.AreEqual(60000, applied[0].GetDefenseReward(3));
        Assert.AreEqual(50000, applied[0].GetLevelData(3).levelUpCost);
        Assert.AreEqual(90000, applied[1].GetDefenseReward(3), "個別の行が * より優先");
        Assert.AreEqual(50000, applied[1].GetLevelData(3).levelUpCost, "個別の行で空の項目は * の値");
    }

    [Test]
    public void DungeonRowOnly_LeavesOtherDungeonsUntouched()
    {
        var forest = MakeDungeon(DungeonName.DeepGreenBeastForest);
        var castle = MakeDungeon(DungeonName.DemonKingCastle);
        RemoteBalance.OverrideFromBundle(Bundle(Row("DemonKingCastle", 1, 5000, null)));

        var applied = Apply(forest, castle);
        Assert.AreSame(forest, applied[0], "当てる行が無いダンジョンは元のアセット");
        Assert.AreEqual(5000, applied[1].GetLevelData(1).levelUpCost);
        Assert.AreEqual(15000, applied[1].GetDefenseReward(1));
    }

    [Test]
    public void MissingOrInvalidSection_UsesAssetValues()
    {
        var forest = MakeDungeon(DungeonName.DeepGreenBeastForest);

        // 区画なし（古い配信）
        RemoteBalance.OverrideFromBundle("{\"version\":1,\"schemaVersion\":1}");
        Assert.AreSame(forest, Apply(forest)[0]);

        // 不正な行（負の値・文字・level なし）は読み飛ばす
        RemoteBalance.OverrideFromBundle(Bundle(
            "{\"dungeonId\":\"*\",\"level\":1,\"levelUpCost\":-1,\"rewardGold\":\"abc\"}," +
            "{\"dungeonId\":\"*\",\"levelUpCost\":1}"));
        var applied = Apply(forest)[0];
        Assert.AreEqual(10000, applied.GetLevelData(1).levelUpCost);
        Assert.AreEqual(15000, applied.GetDefenseReward(1));

        // 配信なし
        RemoteBalance.ResetToDefault();
        Assert.AreSame(forest, Apply(forest)[0]);
    }

    [Test]
    public void Payout_And_SupportCost_UseOverride()
    {
        var castle = MakeDungeon(DungeonName.DemonKingCastle);
        RemoteBalance.OverrideFromBundle(Bundle(
            Row("*", 1, 11000, 16000) + "," + Row("DemonKingCastle", 2, 25000, 33000)));
        var applied = Apply(castle);

        // 戦闘シーンの配当（BattleSceneStarter はカタログの SO の GetDefenseReward を使う）
        var catalog = new DungeonCatalog(applied);
        var so = catalog.GetDungeon(DungeonName.DemonKingCastle);
        Assert.AreEqual(33000, so.GetDefenseReward(2));
        Assert.AreEqual(0, so.GetDefenseReward(DungeonLevels.Unfunded), "未出資は 0 のまま");

        // マップ・魔王軍支援・自動プレイ（DungeonData）
        var data = new DungeonData(so) { currentDungeonLevel = 0 };
        Assert.AreEqual(11000, data.levelUpCost, "最初の出資は Lv1 の行の費用");
        data.currentDungeonLevel = 1;
        Assert.AreEqual(16000, data.rewardGold);
        Assert.AreEqual(11000, data.levelUpCost);
        data.currentDungeonLevel = 2;
        Assert.AreEqual(33000, data.rewardGold);
        Assert.AreEqual(25000, data.levelUpCost);
    }

    [Test]
    public void SameCloneIsReused_AcrossCalls()
    {
        var castle = MakeDungeon(DungeonName.DemonKingCastle);
        RemoteBalance.OverrideFromBundle(Bundle(Row("*", 1, 11000, 16000)));
        var first = Apply(castle)[0];
        var second = Apply(castle)[0];
        Assert.AreSame(first, second, "シーンをまたいでも複製は 1 つ");
    }
}

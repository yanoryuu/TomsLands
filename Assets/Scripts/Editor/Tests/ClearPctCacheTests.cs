using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 魔王軍支援の画面の「勇者のクリア見込み」のドライランの覚え書き（ClearPctCache）。
/// 同じダンジョン・Lv・勇者の戦う値なら計算し直さないこと。
/// </summary>
public class ClearPctCacheTests
{
    private readonly List<Object> _created = new();

    [TearDown]
    public void TearDown()
    {
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
            so.levelDataList[i] = new DungeonLevelData { levelUpCost = 10000, rewardGold = 15000, bossName = "boss" + i };
        return new DungeonData(so) { currentDungeonLevel = 3, isShowedInfo = true };
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
}

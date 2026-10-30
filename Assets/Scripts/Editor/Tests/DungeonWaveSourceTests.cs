using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 敵・ダンジョン構成を実際の戦闘に届ける（2026-10-30）。
/// - bal_enemies（"enemies" 区画）が実戦闘の敵に効く（以前は Resources フォールバックだけ）
/// - bal_dungeons の enemyHpRate / enemyAtkRate / enemyDefRate がそのダンジョンの敵に掛かる（無ければ 1）
/// - 配信の dungeonPhases があればそのダンジョン×レベルはそのウェーブで戦う（normalWaveRepeat を掛けない・phaseType を持つ）
/// - 実戦闘・クリア見込み・自動プレイの戦闘の代わりが同じ構成を使う（氷のダンジョン Lv1 の食い違いの修正）
/// - 見込みの「どこまで迫れたか」を削った HP で測る（clearOutlookProgressByEnemyHp。既定 false＝今までどおり）
/// 配信に何も無いときは今までと同じ結果になること。
/// </summary>
public class DungeonWaveSourceTests
{
    private readonly List<Object> _created = new();

    [TearDown]
    public void TearDown()
    {
        RemoteBalance.ResetToDefault();
        DungeonWaveSource.EnemyCatalogOverride = null;
        DungeonWaveSource.ResetCaches();
        GameConst.ResetToDefault();
        foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
        _created.Clear();
    }

    // ─── 組み立て用 ───

    private EnemyData Enemy(string id, int hp, int atk, int def, bool boss = false)
    {
        var e = ScriptableObject.CreateInstance<EnemyData>();
        _created.Add(e);
        e.enemyId = id;
        e.enemyName = id;
        e.hp = hp;
        e.attackPower = atk;
        e.defensePower = def;
        e.isBoss = boss;
        return e;
    }

    private static DungeonPhaseData Phase(params EnemyData[] enemies) => new() { enemies = enemies.ToList() };

    private DungeonInfoScriptableObj Dungeon(DungeonName key, params DungeonLevelData[] levels)
    {
        var so = ScriptableObject.CreateInstance<DungeonInfoScriptableObj>();
        _created.Add(so);
        so.key = key;
        so.dungeonName = key.ToString();
        so.levelDataList = new DungeonLevelData[5];
        for (int i = 0; i < 5; i++) so.levelDataList[i] = i < levels.Length ? levels[i] : new DungeonLevelData();
        return so;
    }

    private static void SetRepeat(int repeat)
    {
        var data = GameConst.Data.Clone();
        data.battleTempo.normalWaveRepeat = repeat;
        GameConst.Override(data);
    }

    private static void SetProgressByHp(bool on)
    {
        var data = GameConst.Data.Clone();
        data.clearOutlookProgressByEnemyHp = on;
        GameConst.Override(data);
    }

    private static List<DungeonPhaseData> BattlePhases(DungeonInfoScriptableObj so, int level)
    {
        var ctx = new BattleContext(so, level, 0, 3);
        ctx.InitializePhases();
        return ctx.Phases.ToList();
    }

    private static string Bundle(string body) => "{\"version\":77,\"schemaVersion\":1," + body + "}";

    private static string Describe(List<DungeonPhaseData> phases)
        => string.Join(" | ", phases.Select(p => p.phaseType + ":" + string.Join(",", p.enemies.Select(e => e == null ? "null" : $"{e.enemyId}({e.hp}/{e.attackPower}/{e.defensePower})"))));

    private static void AssertSameComposition(List<DungeonPhaseData> expected, List<DungeonPhaseData> actual, string label)
    {
        Assert.AreEqual(Describe(expected), Describe(actual), label);
        Assert.AreEqual(expected.Count, actual.Count, label);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.AreEqual(expected[i].enemies.Count, actual[i].enemies.Count, $"{label} wave {i + 1}");
            for (int j = 0; j < expected[i].enemies.Count; j++)
                Assert.AreSame(expected[i].enemies[j], actual[i].enemies[j], $"{label} wave {i + 1} enemy {j + 1}");
        }
    }

    // ─── 配信が無いとき＝今までどおり ───

    [Test]
    public void NoRemote_SameAsBefore_RepeatNormalWaves_BossLast_AssetEnemies()
    {
        SetRepeat(4);
        var n1 = Enemy("n1", 10, 5, 1);
        var n2 = Enemy("n2", 12, 6, 2);
        var boss = Enemy("boss", 100, 20, 5, boss: true);
        var so = Dungeon(DungeonName.DemonKingCastle, new DungeonLevelData { phases = { Phase(n1, n2), Phase(boss) } });

        var waves = DungeonWaveSource.Build(so, 1);
        Assert.AreEqual(5, waves.Count, "通常ウェーブ×4 + ボス");
        for (int i = 0; i < 4; i++)
        {
            Assert.AreSame(n1, waves[i].enemies[0], "配信が無ければアセットの敵そのもの");
            Assert.AreSame(n2, waves[i].enemies[1]);
            Assert.AreEqual(DungeonPhaseType.Normal, waves[i].phaseType);
        }
        Assert.AreSame(boss, waves[4].enemies[0]);
        Assert.AreEqual(DungeonPhaseType.Boss, waves[4].phaseType);

        AssertSameComposition(waves, BattlePhases(so, 1), "実戦闘");
        AssertSameComposition(waves, ClearProbabilityCalculator.BuildPhases(new DungeonData(so), 1), "クリア見込み");
        Assert.AreEqual(10, so.GetLevelData(1).phases[0].enemies[0].hp, "アセットは書き換えない");
    }

    [Test]
    public void NoRemote_LegacyMonsters_StillConverted()
    {
        SetRepeat(1);
        var n1 = Enemy("n1", 10, 5, 1);
        var boss = Enemy("boss", 100, 20, 5, boss: true);
        var so = Dungeon(DungeonName.IceMistCave, new DungeonLevelData { monsters = { n1, boss } });

        var waves = DungeonWaveSource.Build(so, 1);
        Assert.AreEqual(2, waves.Count);
        Assert.AreSame(n1, waves[0].enemies[0]);
        Assert.AreSame(boss, waves[1].enemies[0]);
        AssertSameComposition(waves, BattlePhases(so, 1), "実戦闘");
        AssertSameComposition(waves, ClearProbabilityCalculator.BuildPhases(new DungeonData(so), 1), "クリア見込み");
    }

    // ─── 1. bal_enemies が実戦闘に効く ───

    [Test]
    public void RemoteEnemies_ApplyToRealBattle_AndEstimate()
    {
        SetRepeat(2);
        var n1 = Enemy("n1", 10, 5, 1);
        var boss = Enemy("boss", 100, 20, 5, boss: true);
        var so = Dungeon(DungeonName.DemonKingCastle, new DungeonLevelData { phases = { Phase(n1), Phase(boss) } });

        Assert.AreEqual(77, RemoteBalance.OverrideFromBundle(Bundle(
            "\"enemies\":[{\"id\":\"n1\",\"hp\":999,\"attackPower\":44},{\"id\":\"boss\",\"defensePower\":9}]")));

        var battle = BattlePhases(so, 1);
        Assert.AreEqual(3, battle.Count);
        Assert.AreEqual(999, battle[0].enemies[0].hp, "配信の hp が実戦闘の敵に入る");
        Assert.AreEqual(44, battle[0].enemies[0].attackPower);
        Assert.AreEqual(1, battle[0].enemies[0].defensePower, "配信に無い項目はアセットの値");
        Assert.AreEqual(9, battle[2].enemies[0].defensePower);
        Assert.AreEqual(10, n1.hp, "アセットは書き換えない");

        AssertSameComposition(battle, ClearProbabilityCalculator.BuildPhases(new DungeonData(so), 1), "クリア見込みも同じ値");
    }

    // ─── 2. ダンジョンごとの敵の倍率 ───

    [Test]
    public void DungeonEnemyRates_MultiplyOnlyThatDungeon_DefaultOne()
    {
        SetRepeat(1);
        var n1 = Enemy("n1", 100, 10, 7);
        var castle = Dungeon(DungeonName.DemonKingCastle, new DungeonLevelData { phases = { Phase(n1) } });
        var forest = Dungeon(DungeonName.DeepGreenBeastForest, new DungeonLevelData { phases = { Phase(n1) } });

        Assert.AreEqual(77, RemoteBalance.OverrideFromBundle(Bundle(
            "\"enemies\":[{\"id\":\"n1\",\"hp\":200}]," +
            "\"dungeons\":[{\"id\":\"DemonKingCastle\",\"enemyHpRate\":1.5,\"enemyAtkRate\":\"2\",\"enemyDefRate\":0}," +
            "{\"id\":\"DeepGreenBeastForest\",\"dungeonName\":\"森\"}]")));

        var c = DungeonWaveSource.Build(castle, 1)[0].enemies[0];
        Assert.AreEqual(300, c.hp, "bal_enemies の 200 × 1.5");
        Assert.AreEqual(20, c.attackPower, "数値の文字列も読む");
        Assert.AreEqual(7, c.defensePower, "0 以下は 1 扱い");

        var f = DungeonWaveSource.Build(forest, 1)[0].enemies[0];
        Assert.AreEqual(200, f.hp, "倍率の無いダンジョンは bal_enemies の値のまま");
        Assert.AreEqual(10, f.attackPower);

        Assert.AreEqual(300, BattlePhases(castle, 1)[0].enemies[0].hp, "実戦闘にも掛かる");
        Assert.AreEqual(300, ClearProbabilityCalculator.BuildPhases(new DungeonData(castle), 1)[0].enemies[0].hp, "見込みにも掛かる");
        Assert.AreEqual(100, n1.hp, "アセットは書き換えない");
    }

    // ─── 3. 配信の dungeonPhases ───

    [Test]
    public void RemotePhases_UsedAsIs_NoRepeat_WithPhaseType()
    {
        SetRepeat(4);
        var d1 = Enemy("d1", 10, 5, 1);
        var d2 = Enemy("d2", 11, 5, 1);
        var gate = Enemy("gate", 50, 9, 3);
        var king = Enemy("king", 500, 30, 8, boss: true);
        var outside = Enemy("outside", 77, 7, 7); // このダンジョンのアセットには出てこない敵（全敵の表から引く）
        DungeonWaveSource.EnemyCatalogOverride = id => id == "outside" ? outside : null;

        var lv1 = new DungeonLevelData { phases = { Phase(d1), Phase(king) } };
        var lv2 = new DungeonLevelData { phases = { Phase(d2), Phase(gate), Phase(king) } };
        var so = Dungeon(DungeonName.DemonKingCastle, lv1, lv2);

        Assert.AreEqual(77, RemoteBalance.OverrideFromBundle(Bundle("\"dungeonPhases\":[" +
            "{\"dungeonId\":\"DemonKingCastle\",\"level\":1,\"phase\":5,\"phaseType\":\"boss\",\"enemyIds\":[\"king\"]}," +
            "{\"dungeonId\":\"DemonKingCastle\",\"level\":1,\"phase\":1,\"phaseType\":\"normal\",\"enemyIds\":[\"d1\"]}," +
            "{\"dungeonId\":\"DemonKingCastle\",\"level\":1,\"phase\":2,\"phaseType\":\"normal\",\"enemyIds\":\"d1, d2\"}," +
            "{\"dungeonId\":\"DemonKingCastle\",\"level\":1,\"phase\":3,\"phaseType\":\"\",\"enemyIds\":[\"d2\",\"outside\",\"d1\"]}," +
            "{\"dungeonId\":\"DemonKingCastle\",\"level\":1,\"phase\":4,\"phaseType\":\"midboss\",\"enemyIds\":[\"gate\"]}" +
            "]")));

        var waves = DungeonWaveSource.Build(so, 1);
        Assert.AreEqual(5, waves.Count, "シートのウェーブをそのまま（normalWaveRepeat を掛けない）");
        Assert.AreEqual("d1", string.Join(",", waves[0].enemies.Select(e => e.enemyId)));
        Assert.AreEqual("d1,d2", string.Join(",", waves[1].enemies.Select(e => e.enemyId)), "カンマ区切りの文字列も読む");
        Assert.AreEqual("d2,outside,d1", string.Join(",", waves[2].enemies.Select(e => e.enemyId)), "出る順のまま・アセットに無い敵は全敵の表から");
        Assert.AreEqual("gate", waves[3].enemies[0].enemyId);
        Assert.AreEqual("king", waves[4].enemies[0].enemyId);
        Assert.AreEqual(DungeonPhaseType.Normal, waves[0].phaseType);
        Assert.AreEqual(DungeonPhaseType.Normal, waves[2].phaseType, "空の phaseType は敵から決める（ボスがいなければ通常）");
        Assert.AreEqual(DungeonPhaseType.MidBoss, waves[3].phaseType);
        Assert.AreEqual(DungeonPhaseType.Boss, waves[4].phaseType);

        var battle = BattlePhases(so, 1);
        AssertSameComposition(waves, battle, "実戦闘");
        AssertSameComposition(waves, ClearProbabilityCalculator.BuildPhases(new DungeonData(so), 1), "クリア見込み");
        var ctx = new BattleContext(so, 1, 0, 3);
        ctx.InitializePhases();
        for (int i = 0; i < 3; i++) ctx.AdvancePhase();
        Assert.AreEqual(DungeonPhaseType.MidBoss, ctx.CurrentPhaseType, "戦闘中に今のウェーブの種類が読める");

        AssertSameComposition(waves, DungeonWaveSource.Build(so, 0), "Lv0（未出資）は Lv1 の構成");

        // 行の無いレベルは今までどおり（通常×4 + ボス）
        var lv2Waves = DungeonWaveSource.Build(so, 2);
        Assert.AreEqual(9, lv2Waves.Count);
        Assert.AreSame(king, lv2Waves[8].enemies[0]);
    }

    [Test]
    public void RemotePhases_UnknownEnemy_FallsBackToAssetComposition()
    {
        SetRepeat(4);
        var d1 = Enemy("d1", 10, 5, 1);
        var king = Enemy("king", 500, 30, 8, boss: true);
        DungeonWaveSource.EnemyCatalogOverride = _ => null;
        var so = Dungeon(DungeonName.DemonKingCastle, new DungeonLevelData { phases = { Phase(d1), Phase(king) } });

        Assert.AreEqual(77, RemoteBalance.OverrideFromBundle(Bundle("\"dungeonPhases\":[" +
            "{\"dungeonId\":\"DemonKingCastle\",\"level\":1,\"phase\":1,\"phaseType\":\"normal\",\"enemyIds\":[\"d1\",\"typo\"]}," +
            "{\"dungeonId\":\"DemonKingCastle\",\"level\":1,\"phase\":2,\"phaseType\":\"boss\",\"enemyIds\":[\"king\"]}]")));

        var waves = DungeonWaveSource.Build(so, 1);
        Assert.AreEqual(5, waves.Count, "id が引けない組は使わず、アセットの構成（通常×4 + ボス）");
        AssertSameComposition(waves, BattlePhases(so, 1), "実戦闘");
    }

    [Test]
    public void RemotePhases_EnemiesGetRemoteStatsAndDungeonRates()
    {
        var d1 = Enemy("d1", 100, 10, 2);
        var so = Dungeon(DungeonName.AncientMechanicalCastle, new DungeonLevelData { phases = { Phase(d1) } });
        Assert.AreEqual(77, RemoteBalance.OverrideFromBundle(Bundle(
            "\"enemies\":[{\"id\":\"d1\",\"hp\":120}]," +
            "\"dungeons\":[{\"id\":\"AncientMechanicalCastle\",\"enemyHpRate\":2}]," +
            "\"dungeonPhases\":[{\"dungeonId\":\"AncientMechanicalCastle\",\"level\":1,\"phase\":1,\"phaseType\":\"normal\",\"enemyIds\":[\"d1\",\"d1\"]}]")));

        var waves = DungeonWaveSource.Build(so, 1);
        Assert.AreEqual(1, waves.Count);
        Assert.AreEqual(240, waves[0].enemies[0].hp);
        Assert.AreSame(waves[0].enemies[0], waves[0].enemies[1], "同じ敵は同じ複製を使い回す");
    }

    // ─── 4. 実戦闘・クリア見込み・自動プレイが同じ構成（全ダンジョン × 全レベル） ───

    private static List<DungeonInfoScriptableObj> AllDungeonAssets()
        => AssetDatabase.FindAssets("t:DungeonInfoScriptableObj")
            .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
            .OrderBy(p => p, System.StringComparer.Ordinal)
            .Select(path => AssetDatabase.LoadAssetAtPath<DungeonInfoScriptableObj>(path))
            .Where(d => d != null)
            .ToList();

    private static void AssertAllDungeonsConsistent(string label)
    {
        var dungeons = AllDungeonAssets();
        Assert.Greater(dungeons.Count, 0, "DungeonInfoScriptableObj が見つからない");
        foreach (var so in dungeons)
        {
            var data = new DungeonData(so);
            for (int lv = 0; lv <= 5; lv++)
            {
                var estimate = ClearProbabilityCalculator.BuildPhases(data, lv);
                if (estimate.Count == 0) continue; // 構成の無いレベルは実戦闘が Resources フォールバックになる（ここでは比べない）
                string where = $"{label} {so.key} Lv{lv}";
                AssertSameComposition(estimate, BattlePhases(so, lv), where + " 見込み vs 実戦闘");
                AssertSameComposition(estimate, AutoPlayBattleSurrogate.BuildPhases(data, lv), where + " 見込み vs 自動プレイ");
                var (count, _) = AutoPlayBattleSurrogate.DescribeEnemies(data, lv);
                Assert.AreEqual(estimate.Sum(p => p.enemies.Count), count, where + " 自動プレイの敵の数");
            }
        }
    }

    [Test]
    public void AllDungeons_EstimateBattleAndAutoPlay_UseSameComposition()
    {
        AssertAllDungeonsConsistent("配信なし");
    }

    [Test]
    public void AllDungeons_SameComposition_WithRemoteValues()
    {
        var castle = AllDungeonAssets().FirstOrDefault(d => d.key == DungeonName.DemonKingCastle);
        var lv1Ids = castle?.GetLevelData(1)?.phases?.SelectMany(p => p.enemies).Where(e => e != null).Select(e => e.enemyId).Distinct().ToList()
                     ?? new List<string>();
        string phasesJson = lv1Ids.Count == 0 ? "" :
            ",\"dungeonPhases\":[" +
            $"{{\"dungeonId\":\"DemonKingCastle\",\"level\":1,\"phase\":1,\"phaseType\":\"normal\",\"enemyIds\":[\"{lv1Ids[0]}\"]}}," +
            $"{{\"dungeonId\":\"DemonKingCastle\",\"level\":1,\"phase\":2,\"phaseType\":\"boss\",\"enemyIds\":[\"{lv1Ids[lv1Ids.Count - 1]}\"]}}]";
        Assert.AreEqual(77, RemoteBalance.OverrideFromBundle(Bundle(
            "\"enemies\":[{\"id\":\"demon1\",\"hp\":1234}]," +
            "\"dungeons\":[{\"id\":\"IceMistCave\",\"enemyHpRate\":1.3,\"enemyAtkRate\":0.9}]" + phasesJson)));
        AssertAllDungeonsConsistent("配信あり");
    }

    [Test]
    public void DungeonData_CopiesPhases_WithoutSharingLists()
    {
        var n1 = Enemy("n1", 10, 5, 1);
        var n2 = Enemy("n2", 10, 5, 1);
        var so = Dungeon(DungeonName.IceMistCave, new DungeonLevelData { phases = { Phase(n1, n1, n1), Phase(n2, n2) }, monsters = { n1 } });

        var data = new DungeonData(so);
        var copied = data.GetLevelData(1).phases;
        Assert.AreEqual(2, copied.Count, "phases を写す（以前は写さず、見込みが monsters から組み立てられていた）");
        Assert.AreEqual(3, copied[0].enemies.Count);
        copied[0].enemies.Clear();
        Assert.AreEqual(3, so.GetLevelData(1).phases[0].enemies.Count, "アセットのリストとは別物");
    }

    // ─── 5. 見込みに魔王の残り HP を入れる ───

    private static RuntimeHeroData Hero(int hp, int atk, int def)
    {
        var h = RuntimeHeroData.CreateDefault();
        h.hp.Value = hp;
        h.attackPower.Value = atk;
        h.defensePower.Value = def;
        return h;
    }

    [Test]
    public void LossProgress_CountByDefault_HpWhenEnabled()
    {
        Assert.AreEqual(0.75f, ClearProbabilityCalculator.LossProgress(false, 3, 4, 9000, 10003), 1e-4f);
        Assert.AreEqual(1f - 9000f / 10003f, ClearProbabilityCalculator.LossProgress(true, 3, 4, 9000, 10003), 1e-4f);
        Assert.AreEqual(0f, ClearProbabilityCalculator.LossProgress(true, 0, 0, 0, 0));
        Assert.IsFalse(new GameConstData().clearOutlookProgressByEnemyHp, "既定は今までどおり（倒した数）");
    }

    [Test]
    public void Estimate_BossRemainingHp_LowersOutlook_OnlyWhenEnabled()
    {
        SetRepeat(1);
        // 雑魚 3 体は一撃、魔王（HP 10000）は 1 ターン 10 しか削れず、勇者（HP 100）は魔王から毎ターン 1 受けて 100 ターンで倒れる
        var n1 = Enemy("n1", 1, 5, 0);
        var n2 = Enemy("n2", 1, 5, 0);
        var n3 = Enemy("n3", 1, 5, 0);
        var king = Enemy("king", 10000, 6, 0, boss: true);
        var so = Dungeon(DungeonName.DemonKingCastle, new DungeonLevelData { phases = { Phase(n1), Phase(n2), Phase(n3), Phase(king) } });
        var data = new DungeonData(so);
        var hero = Hero(100, 10, 5);

        float byCount = ClearProbabilityCalculator.Calculate(hero, null, data, 1);
        Assert.AreEqual(35f, byCount, 0.01f, "今まで: 4 体中 3 体倒す → 75% → 互角（35%）");
        Assert.AreEqual(ClearOutlook.Even, ClearProbabilityCalculator.ToOutlook(byCount));

        SetProgressByHp(true);
        float byHp = ClearProbabilityCalculator.Calculate(hero, null, data, 1);
        // 魔王に 100 ターン × 10 = 1000。残り HP 9000 / 合計 10003 → 進み具合 約 0.1 → 約 9%
        Assert.AreEqual(5f + (1f - 9000f / 10003f) * 40f, byHp, 0.05f);
        Assert.AreEqual(ClearOutlook.Hopeless, ClearProbabilityCalculator.ToOutlook(byHp), "魔王がほぼ無傷なら絶望");

        SetProgressByHp(false);
        Assert.AreEqual(byCount, ClearProbabilityCalculator.Calculate(hero, null, data, 1), 0.001f);
    }

    [Test]
    public void Estimate_Victory_Unchanged_ByHpSetting()
    {
        SetRepeat(1);
        var n1 = Enemy("n1", 10, 6, 0);
        var king = Enemy("king", 30, 8, 0, boss: true);
        var so = Dungeon(DungeonName.DemonKingCastle, new DungeonLevelData { phases = { Phase(n1), Phase(king) } });
        var data = new DungeonData(so);
        var hero = Hero(100, 10, 5);

        float before = ClearProbabilityCalculator.Calculate(hero, null, data, 1);
        Assert.GreaterOrEqual(before, 55f);
        SetProgressByHp(true);
        Assert.AreEqual(before, ClearProbabilityCalculator.Calculate(hero, null, data, 1), 0.001f, "勝つ見込みは残り HP の式のまま");
    }
}

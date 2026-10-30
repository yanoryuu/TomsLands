using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 実際に戦うウェーブ列（ダンジョン×レベル）を組み立てる唯一の入り口。
/// 実戦闘（BattleContext.InitializePhases）・クリア見込み（ClearProbabilityCalculator）・
/// 自動プレイの戦闘の代わり（AutoPlayBattleSurrogate）が同じものを使う（以前はそれぞれ写しを持っていて、
/// DungeonData が phases を写していなかったため氷のダンジョン Lv1 だけ見込みと実戦の敵が食い違っていた）。
///
/// 優先順:
///  1. 配信 balance.json の dungeonPhases にこのダンジョン×レベルの行がある → その行をそのままウェーブにする
///     （シートは通常ウェーブの繰り返しを展開済みの形なので normalWaveRepeat は掛けない）。
///     敵の id が 1 つでも見つからなければ、この組は使わず 2. にする（警告を出す）。
///  2. アセットの phases → 無ければ旧方式（monsters / bossName）から変換し、BattleWavePlan.Expand（normalWaveRepeat）で展開する。
/// どちらも敵は RemoteBalance.ResolveEnemy を通す（bal_enemies の値 × bal_dungeons の敵の倍率）。
/// 配信に何も無ければ敵はアセットそのもの・構成も今までどおり。
/// 各ウェーブの phaseType: 配信の phaseType（normal / midboss / boss）。空・不明・アセット由来は isBoss の敵がいればボス、ほかは通常。
/// </summary>
public static class DungeonWaveSource
{
    /// <summary>レベルはこの範囲にクランプする（GetLevelData と同じ。Lv0＝未出資は Lv1 の構成で戦う）。</summary>
    private const int MinLevel = 1;
    private const int MaxLevel = 5;

    /// <summary>
    /// 配信の enemyIds をアセットに引くときの全敵の表（テストで差し替える）。null なら Addressables のラベル "EnemyData" を一度だけ読む。
    /// そのダンジョンのアセットに出てくる敵は、これより先に引く。
    /// </summary>
    public static Func<string, EnemyData> EnemyCatalogOverride;

    private static Dictionary<string, EnemyData> _catalog;
    private static readonly HashSet<string> _warned = new();

    public static List<DungeonPhaseData> Build(DungeonData dungeon, int level)
        => dungeon == null ? new List<DungeonPhaseData>() : Build(dungeon.key.ToString(), dungeon.levelDataList, level);

    public static List<DungeonPhaseData> Build(DungeonInfoScriptableObj dungeon, int level)
        => dungeon == null ? new List<DungeonPhaseData>() : Build(dungeon.key.ToString(), dungeon.levelDataList, level);

    /// <summary>
    /// ウェーブ列を返す（新しいリスト・新しい DungeonPhaseData。アセットは書き換えない）。構成が取れなければ空。
    /// </summary>
    /// <param name="dungeonId">bal_dungeons / dungeonPhases の id（DungeonName の文字列）。</param>
    /// <param name="levelDataList">レベル 1〜5 の構成（配信の enemyIds を引く手がかりにも使う）。</param>
    public static List<DungeonPhaseData> Build(string dungeonId, DungeonLevelData[] levelDataList, int level)
    {
        int lv = Mathf.Clamp(level, MinLevel, MaxLevel);
        var rates = RemoteBalance.GetDungeonEnemyRates(dungeonId);

        var remote = BuildFromRemote(dungeonId, levelDataList, lv, rates);
        if (remote != null) return remote;

        var levelData = levelDataList != null && lv - 1 < levelDataList.Length ? levelDataList[lv - 1] : null;
        if (levelData == null) return new List<DungeonPhaseData>();

        var source = HasEnemies(levelData.phases) ? levelData.phases : DungeonPhaseBuilder.BuildFromLegacy(levelData);
        var resolved = source
            .Where(p => p?.enemies != null && p.enemies.Any(e => e != null))
            .Select(p => MakePhase(p.enemies, dungeonId, rates, null))
            .ToList();
        // 通常ウェーブの周回（GameConst.battleTempo.normalWaveRepeat）。同じウェーブの参照が繰り返し並ぶ
        return BattleWavePlan.Expand(resolved);
    }

    /// <summary>アセットの phases に敵が 1 体でもいるか（いなければ旧方式から変換する）。</summary>
    public static bool HasEnemies(List<DungeonPhaseData> phases)
        => phases != null && phases.Any(p => p?.enemies != null && p.enemies.Any(e => e != null));

    /// <summary>このダンジョン×レベルが配信の dungeonPhases で戦うか（敵の id がすべて引けるときだけ true）。</summary>
    public static bool UsesRemotePhases(string dungeonId, DungeonLevelData[] levelDataList, int level)
        => BuildFromRemote(dungeonId, levelDataList, Mathf.Clamp(level, MinLevel, MaxLevel), RemoteBalance.EnemyRates.One) != null;

    private static List<DungeonPhaseData> BuildFromRemote(string dungeonId, DungeonLevelData[] levelDataList, int lv, RemoteBalance.EnemyRates rates)
    {
        if (!RemoteBalance.TryGetDungeonPhases(dungeonId, lv, out var rows)) return null;

        var result = new List<DungeonPhaseData>(rows.Count);
        foreach (var row in rows)
        {
            var enemies = new List<EnemyData>(row.EnemyIds.Count);
            foreach (var id in row.EnemyIds)
            {
                var e = FindEnemy(id, levelDataList);
                if (e == null)
                {
                    WarnOnce($"{dungeonId}|{lv}|{id}",
                        $"[DungeonWaveSource] dungeonPhases の {dungeonId} Lv{lv} phase {row.Phase} の敵 '{id}' が見つからないため、このレベルは配信の構成を使わずアセットの構成で戦います。");
                    return null;
                }
                enemies.Add(e);
            }
            result.Add(MakePhase(enemies, dungeonId, rates, ParsePhaseType(row.PhaseType)));
        }
        return result.Count > 0 ? result : null;
    }

    private static DungeonPhaseData MakePhase(List<EnemyData> enemies, string dungeonId, RemoteBalance.EnemyRates rates, DungeonPhaseType? type)
    {
        var phase = new DungeonPhaseData
        {
            enemies = enemies.Where(e => e != null).Select(e => RemoteBalance.ResolveEnemy(e, dungeonId, rates)).ToList(),
        };
        phase.phaseType = type ?? (phase.ContainsBoss ? DungeonPhaseType.Boss : DungeonPhaseType.Normal);
        return phase;
    }

    /// <summary>"normal" / "midboss" / "boss"（大文字小文字・空白・「mid_boss」「mid-boss」も可）。空・不明は null（敵から決める）。</summary>
    public static DungeonPhaseType? ParsePhaseType(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        switch (s.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", ""))
        {
            case "normal": return DungeonPhaseType.Normal;
            case "midboss": return DungeonPhaseType.MidBoss;
            case "boss": return DungeonPhaseType.Boss;
            default: return null;
        }
    }

    private static EnemyData FindEnemy(string id, DungeonLevelData[] levelDataList)
    {
        if (string.IsNullOrEmpty(id)) return null;

        // 1. そのダンジョンのアセットに出てくる敵（全レベル）
        if (levelDataList != null)
        {
            foreach (var level in levelDataList)
            {
                if (level == null) continue;
                if (level.phases != null)
                    foreach (var p in level.phases)
                        if (p?.enemies != null)
                            foreach (var e in p.enemies)
                                if (e != null && e.enemyId == id) return e;
                if (level.monsters != null)
                    foreach (var e in level.monsters)
                        if (e != null && e.enemyId == id) return e;
            }
        }

        // 2. 全敵の表
        if (EnemyCatalogOverride != null) return EnemyCatalogOverride(id);
        if (_catalog == null)
        {
            _catalog = new Dictionary<string, EnemyData>();
            try
            {
                foreach (var e in AddressableLoader.LoadAll<EnemyData>("EnemyData"))
                    if (e != null && !string.IsNullOrEmpty(e.enemyId) && !_catalog.ContainsKey(e.enemyId))
                        _catalog[e.enemyId] = e;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DungeonWaveSource] EnemyData の読み込みに失敗しました: {ex.Message}");
            }
        }
        if (_catalog.TryGetValue(id, out var found) && found != null) return found;
        return null;
    }

    private static void WarnOnce(string key, string message)
    {
        if (_warned.Add(key)) Debug.LogWarning(message);
    }

    /// <summary>全敵の表と警告済みの記録を捨てる（テスト用）。</summary>
    public static void ResetCaches()
    {
        _catalog = null;
        _warned.Clear();
    }
}

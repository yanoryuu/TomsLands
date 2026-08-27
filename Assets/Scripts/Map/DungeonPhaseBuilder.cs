using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 旧方式のダンジョン構成（monsters + bossName）からフェーズ構成を組み立てるユーティリティ。
/// ランタイムのフォールバック（phases 未設定のSO）とエディタの一括変換ツールの両方で使う。
/// </summary>
public static class DungeonPhaseBuilder
{
    /// <summary>通常敵を3体ずつのフェーズに分割し、ボスがいれば最終フェーズとして追加する。</summary>
    public static List<DungeonPhaseData> Build(List<EnemyData> normalEnemies, EnemyData boss)
    {
        var phases = new List<DungeonPhaseData>();

        if (normalEnemies != null)
        {
            var valid = normalEnemies.Where(e => e != null).ToList();
            for (int i = 0; i < valid.Count; i += 3)
            {
                phases.Add(new DungeonPhaseData { enemies = valid.Skip(i).Take(3).ToList() });
            }
        }

        if (boss != null)
        {
            phases.Add(new DungeonPhaseData { enemies = new List<EnemyData> { boss } });
        }

        return phases;
    }

    /// <summary>旧方式の DungeonLevelData（monsters/bossName）からフェーズ構成を作る。</summary>
    public static List<DungeonPhaseData> BuildFromLegacy(DungeonLevelData level)
    {
        if (level?.monsters == null) return new List<DungeonPhaseData>();

        var boss = level.monsters.FirstOrDefault(m => m != null && IsBossOf(level, m));
        var normals = level.monsters.Where(m => m != null && m != boss).ToList();
        return Build(normals, boss);
    }

    private static bool IsBossOf(DungeonLevelData level, EnemyData enemy)
        => enemy.isBoss || (!string.IsNullOrEmpty(level.bossName) && enemy.enemyName == level.bossName);
}

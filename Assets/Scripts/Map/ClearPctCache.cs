using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// クリア見込み（ClearProbabilityCalculator.Calculate のドライラン）の覚え書き。
/// 鍵は (ダンジョン, Lv, 戦うときの勇者の HP・攻撃・防御)。HP・攻撃・防御は装備・レリックの勇者弱体化込みの値
/// （GetEffectiveStats。ドライランへ入る勇者の値そのもの）なので、装備を替えれば別の鍵になる。
/// 魔王軍支援の画面で選んだダンジョンの分だけ、必要になったときに計算する（所持金が変わるたびに作り直しても計算し直さない）。
/// </summary>
public sealed class ClearPctCache
{
    private readonly Dictionary<(DungeonName, int, int, int, int), float> cache = new();
    private readonly Func<DungeonData, int, float> compute;

    /// <summary>計算した回数（テスト用）。</summary>
    public int ComputeCount { get; private set; }

    public ClearPctCache(Func<DungeonData, int, float> compute) => this.compute = compute;

    public int Count => cache.Count;

    public void Clear() => cache.Clear();

    public float Get(DungeonData dungeon, int level, ClearProbabilityCalculator.HeroStats stats)
    {
        if (dungeon == null) return -1f;
        var key = (dungeon.key, level, stats.Hp, stats.Attack, stats.Defense);
        if (cache.TryGetValue(key, out float pct)) return pct;
        try { pct = compute(dungeon, level); }
        catch (Exception e)
        {
            Debug.LogWarning($"[ClearPctCache] クリア見込みを計算できませんでした ({dungeon.key} Lv{level}): {e.Message}");
            pct = -1f;
        }
        ComputeCount++;
        cache[key] = pct;
        return pct;
    }
}

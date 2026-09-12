using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// クリア確率（クリア見込み）の計算。
///
/// 実際の戦闘（BattleFlowManager/BattleActionExecutor）は
/// ダメージ = max(1, 攻撃力 - 防御力) の決定論シミュレーションのため、
/// ここでも同じルールで戦闘をドライランし、その結果から表示用の％を作る。
/// ・勝てる想定 → 55〜95%（残りHPが多いほど高い）
/// ・負ける想定 → 5〜45%（倒せた敵が多いほど高い）
/// つまり 50% 以上が表示されていれば現在のステータスでは勝てる見込み、という整合が保証される
/// （以前はレベル差ヒューリスティックだったため「95%表示で敗北」が起こり得た）。
/// </summary>
public static class ClearProbabilityCalculator
{
    /// <summary>BattleSequencer.maxConcurrentEnemies と同値（同時出現の最大数）。</summary>
    private const int MaxConcurrentEnemies = 3;

    /// <summary>ドライランの最大ターン数（お互い決定打がない場合の無限ループ保険）。</summary>
    private const int MaxSimTurns = 500;

    public static float Calculate(
        RuntimeHeroData hero,
        ItemModel itemModel,
        DungeonData dungeon,
        int dungeonLevel)
    {
        if (hero == null || dungeon == null) return 50f;

        var phases = BuildPhases(dungeon, dungeonLevel);
        if (phases.Count == 0)
        {
            // フェーズ構成が取れない場合は旧ヒューリスティックにフォールバック
            return CalculateLegacy(hero, dungeon);
        }

        return Simulate(hero, phases);
    }

    /// <summary>実戦闘（BattleContext.InitializePhases）と同じ優先順でフェーズ構成を作る。</summary>
    private static List<DungeonPhaseData> BuildPhases(DungeonData dungeon, int dungeonLevel)
    {
        var level = dungeon.GetLevelData(dungeonLevel);
        if (level == null) return new List<DungeonPhaseData>();

        List<DungeonPhaseData> phases;
        if (level.phases != null && level.phases.Any(p => p?.enemies != null && p.enemies.Any(e => e != null)))
        {
            phases = level.phases;
        }
        else
        {
            phases = DungeonPhaseBuilder.BuildFromLegacy(level);
        }

        return phases
            .Where(p => p?.enemies != null && p.enemies.Any(e => e != null))
            .ToList();
    }

    /// <summary>
    /// BattleActionExecutor と同じルールで戦闘をドライランし、表示用の％へ変換する。
    /// ・ターン順: 勇者 → 出現順の敵（そのターン中に倒された敵は行動しない）
    /// ・勇者は生存している先頭の敵を攻撃、敵は全員勇者を攻撃
    /// ・勇者死亡でそのターンの残りの敵は行動中断（実装の IsBattleEnded() break と同じ）
    /// ・ターン終了時: 死亡除去 → フェーズ全滅なら次フェーズ → 同時3体まで補充
    /// </summary>
    private static float Simulate(RuntimeHeroData hero, List<DungeonPhaseData> phases)
    {
        int heroMaxHp = Mathf.Max(1, hero.hp.Value);
        int heroHp    = heroMaxHp;
        int heroAtk   = Mathf.Max(1, hero.attackPower.Value);
        int heroDef   = Mathf.Max(0, hero.defensePower.Value);

        int totalEnemies = phases.Sum(p => p.enemies.Count(e => e != null));
        int defeated = 0;

        int phaseIndex = 0;
        var spawnQueue = new Queue<EnemyData>(phases[0].enemies.Where(e => e != null));
        // (残りHP, 攻撃力, 防御力)
        var field = new List<(int hp, int atk, int def)>();

        void Refill()
        {
            while (field.Count < MaxConcurrentEnemies && spawnQueue.Count > 0)
            {
                var e = spawnQueue.Dequeue();
                field.Add((Mathf.Max(1, e.hp), Mathf.Max(0, e.attackPower), Mathf.Max(0, e.defensePower)));
            }
        }

        Refill(); // 開幕スポーン（BattleFlowManager の初回 SpawnFromPhaseQueueAsync 相当）

        for (int turn = 0; turn < MaxSimTurns; turn++)
        {
            // --- 勇者の攻撃（先頭の生存敵） ---
            if (field.Count > 0)
            {
                var target = field[0];
                target.hp -= Mathf.Max(1, heroAtk - target.def);
                field[0] = target;
            }

            // --- 敵の攻撃（このターン中に倒された敵は行動しない） ---
            foreach (var enemy in field)
            {
                if (enemy.hp <= 0) continue;
                heroHp -= Mathf.Max(1, enemy.atk - heroDef);
                if (heroHp <= 0) break; // 勇者死亡で残りは行動中断
            }

            if (heroHp <= 0) break;

            // --- ターン終了評価 ---
            defeated += field.Count(e => e.hp <= 0);
            field.RemoveAll(e => e.hp <= 0);

            if (field.Count == 0 && spawnQueue.Count == 0)
            {
                phaseIndex++;
                if (phaseIndex >= phases.Count) break; // 全フェーズクリア＝勝利

                foreach (var e in phases[phaseIndex].enemies.Where(e => e != null))
                    spawnQueue.Enqueue(e);
            }

            Refill();
        }

        if (heroHp > 0 && phaseIndex >= phases.Count)
        {
            // 勝利見込み: 残りHP割合が高いほど確率を高く見せる（55〜95%）
            float hpRatio = Mathf.Clamp01(heroHp / (float)heroMaxHp);
            return Mathf.Clamp(55f + hpRatio * 40f, 55f, 95f);
        }

        // 敗北見込み（ターン上限による膠着含む）: 進行度が高いほど「惜しい」表示（5〜45%）
        float progress = totalEnemies > 0 ? Mathf.Clamp01(defeated / (float)totalEnemies) : 0f;
        return Mathf.Clamp(5f + progress * 40f, 5f, 45f);
    }

    /// <summary>
    /// フェーズ構成が取得できない場合のみ使う旧ヒューリスティック
    /// （推奨レベル差 ±10%/Lv・難易度補正）。
    /// </summary>
    private static float CalculateLegacy(RuntimeHeroData hero, DungeonData dungeon)
    {
        float recLv      = Mathf.Max(1, dungeon.recommendedLevel);
        float levelDiff  = hero.level.Value - recLv;
        float baseProbability = Mathf.Clamp(60f + levelDiff * 10f, 20f, 90f);

        float diffFactor = 10f / Mathf.Max(1f, dungeon.difficulty + 5f);
        baseProbability *= diffFactor;

        return Mathf.Clamp(baseProbability, 5f, 95f);
    }
}

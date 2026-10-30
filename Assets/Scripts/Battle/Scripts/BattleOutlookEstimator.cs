using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 配信中の勝敗メーター（SUMMARY G-5）の見込み計算。
/// いまの戦場（勇者の残りHP・場の敵・未出現の敵・残りのフェーズ・入っている介入の効果）から、
/// 実際の戦闘と同じ ダメージ = max(1, 攻撃 − 防御) の決定論ルールで最後までドライランする。
/// 店の勇者パネルのクリア確率（ClearProbabilityCalculator）と同じ目盛りで返す:
///   勝てる見込み → 0.55〜0.95（残りHPが多いほど高い） / 負ける見込み → 0.05〜0.45（倒せる敵が多いほど高い）
/// 視聴者スパチャ（赤スパの必殺技・魔王軍スパの罠）や増援の抽選など、まだ起きていない偶然は入れない。
/// GameConst.clearOutlookProgressByEnemyHp = true なら、負ける見込みの進み具合を「削れる HP の割合」で測る
/// （場にいるボス・魔王はいまの残り HP を使う）。既定 false＝倒せる敵の数の割合（今までどおり）。
/// </summary>
public static class BattleOutlookEstimator
{
    private const int MaxSimTurns = 500;

    private struct SimUnit
    {
        public int Hp, Atk, Def;
        public bool Boss;
    }

    /// <summary>勇者の勝ち見込み（0〜1）。戦闘がまだ始まっていなければ 0.5。</summary>
    public static float Estimate(BattleContext context, InterventionEffects effects, StreamingInteractionSettings settings)
    {
        var heroModel = context?.HeroPresenter?.GetModel();
        if (heroModel == null) return 0.5f;
        if (heroModel.IsDead) return 0f;

        int heroMaxHp = Mathf.Max(1, heroModel.MaxHp);
        int heroHp = heroModel.CurrentHp.CurrentValue;
        int heroAtk = heroModel.AttackPower;
        int heroDef = heroModel.DefensePower;

        float strikeMul = effects != null && effects.HasArmedStrike ? effects.ArmedStrikeMultiplier.Value : 1f;
        bool trap = effects != null && effects.TrapArmed;
        int curseHits = effects != null ? effects.CurseHitsLeft : 0;
        bool bossBuff = effects != null && effects.BossBuffActive;
        float curseMul = settings != null ? settings.curseDefenseMul : 0.5f;
        float trapRatio = settings != null ? settings.trapBonusDamageRatio : 0.3f;
        float bossAtkMul = settings != null ? settings.bossBuffAttackMul : 1.25f;
        float bossTakenMul = settings != null ? settings.bossBuffDamageTakenMul : 0.8f;
        float bossPierce = settings != null ? Mathf.Clamp01(settings.bossBuffDefensePierce) : 0.5f;

        var field = new List<SimUnit>();
        foreach (var p in context.EnemyPresenters)
        {
            var m = p?.GetModel();
            if (m == null || m.IsDead) continue;
            field.Add(new SimUnit { Hp = m.CurrentHp.CurrentValue, Atk = m.AttackPower, Def = m.DefensePower, Boss = m.IsBoss });
        }

        var queue = new Queue<EnemyData>();
        foreach (var e in context.PendingSpawns) if (e != null) queue.Enqueue(e);
        int phaseIndex = context.CurrentPhaseIndex;

        int remainingEnemies = field.Count + queue.Count;
        // 削った HP で測るとき（clearOutlookProgressByEnemyHp）の物差し: いまの残り HP の合計（場のボスはいまの HP）
        long hpAtStart = 0;
        foreach (var u in field) hpAtStart += Mathf.Max(0, u.Hp);
        foreach (var e in queue) hpAtStart += Mathf.Max(1, e.hp);
        for (int i = phaseIndex + 1; i < context.PhaseCount; i++)
            foreach (var e in context.Phases[i].enemies)
                if (e != null) { remainingEnemies++; hpAtStart += Mathf.Max(1, e.hp); }
        int defeated = 0;

        void Refill()
        {
            while (field.Count < context.MaxConcurrentEnemies && queue.Count > 0)
            {
                var e = queue.Dequeue();
                field.Add(new SimUnit { Hp = Mathf.Max(1, e.hp), Atk = Mathf.Max(0, e.attackPower), Def = Mathf.Max(0, e.defensePower), Boss = e.isBoss });
            }
        }
        Refill();

        bool won = false;
        for (int turn = 0; turn < MaxSimTurns; turn++)
        {
            if (field.Count == 0 && queue.Count == 0)
            {
                phaseIndex++;
                if (phaseIndex >= context.PhaseCount) { won = true; break; }
                foreach (var e in context.Phases[phaseIndex].enemies) if (e != null) queue.Enqueue(e);
                Refill();
                continue;
            }

            // 勇者の攻撃（先頭の生存敵）
            if (field.Count > 0)
            {
                var target = field[0];
                float mul = strikeMul;
                strikeMul = 1f;
                if (bossBuff && target.Boss) mul *= bossTakenMul;
                int atk = mul == 1f ? heroAtk : Mathf.Max(1, Mathf.RoundToInt(heroAtk * mul));
                target.Hp -= Mathf.Max(1, atk - target.Def);
                field[0] = target;
            }

            // 敵の攻撃（このターンに倒された敵は行動しない）
            foreach (var enemy in field)
            {
                if (enemy.Hp <= 0) continue;
                bool buffed = bossBuff && enemy.Boss;
                int atk = buffed ? Mathf.Max(1, Mathf.RoundToInt(enemy.Atk * bossAtkMul)) : enemy.Atk;
                float defFactor = 1f;
                if (curseHits > 0) { defFactor *= curseMul; curseHits--; }
                if (buffed) defFactor *= 1f - bossPierce;
                heroHp -= Mathf.Max(1, atk - Mathf.RoundToInt(heroDef * defFactor));
                if (trap) { trap = false; heroHp -= Mathf.Max(1, Mathf.RoundToInt(heroMaxHp * trapRatio)); }
                if (heroHp <= 0) break;
            }
            if (heroHp <= 0) break;

            defeated += field.RemoveAll(e => e.Hp <= 0);
            Refill();
        }

        if (won && heroHp > 0)
            return Mathf.Clamp(0.55f + Mathf.Clamp01(heroHp / (float)heroMaxHp) * 0.4f, 0.55f, 0.95f);

        long hpLeft = 0;
        foreach (var u in field) hpLeft += Mathf.Max(0, u.Hp);
        foreach (var e in queue) hpLeft += Mathf.Max(1, e.hp);
        for (int i = phaseIndex + 1; i < context.PhaseCount; i++)
            foreach (var e in context.Phases[i].enemies) if (e != null) hpLeft += Mathf.Max(1, e.hp);
        float progress = ClearProbabilityCalculator.LossProgress(
            ClearProbabilityCalculator.ProgressByEnemyHp, defeated, remainingEnemies, hpLeft, hpAtStart);
        return Mathf.Clamp(0.05f + progress * 0.4f, 0.05f, 0.45f);
    }
}

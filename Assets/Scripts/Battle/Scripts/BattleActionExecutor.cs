﻿using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

/// <summary>
/// 戦闘中の具体的なアクションの実行だけを担当
/// </summary>
public class BattleActionExecutor
{
    private readonly BattleContext context;
    private readonly BattleSequencer sequencer;
    
    public BattleActionExecutor(BattleContext ctx, BattleSequencer battleSequencer)
    {
        context = ctx;
        sequencer = battleSequencer;
    }

    /// <summary>
    /// 1ターン分のキャラクターの行動を実行します
    /// </summary>
    public async UniTask ExecuteTurnActionsAsync(BattleUIView uiView, CancellationToken token)
    {
        var turnOrder = GetTurnOrder();

        foreach (var presenter in turnOrder)
        {
            // 戦闘が既に決着している場合は即座にループを抜ける
            if (IsBattleEnded()) break;

            if (presenter.GetModel().IsDead) continue;

            var targetPresenter = GetAttackTarget(presenter);
            // ターゲットが既に死亡している場合はスキップ
            if (targetPresenter == null || targetPresenter.GetModel().IsDead) continue;

            int damageDealt = presenter.PerformAttack(targetPresenter);
            string logMessage = $"{presenter.GetModel().Name} の攻撃！ {targetPresenter.GetModel().Name} に {damageDealt} のダメージ！";
            await uiView.AddLogAsync(logMessage, token);

            // HPが0以下になった時点で即座に点滅→非表示
            if (targetPresenter.GetModel().IsDead)
            {
                await uiView.AddLogAsync($"{targetPresenter.GetModel().Name} を倒した！", token);
                
                // 敵が倒された場合、撃破イベントを発火（属性相性による価格変動に使用）
                if (targetPresenter.GetModel().Type == CharacterType.Enemy)
                {
                    sequencer.OnEnemyDefeated.OnNext(targetPresenter.GetModel());
                }
                
                await targetPresenter.GetView().PlayDeathEffectAsync(token);
            }
        }
    }

    /// <summary>
    /// ターン終了時の評価を実行します
    /// </summary>
    public async UniTask EvaluateEndOfTurnAsync(CharacterFactory factory, BattleUIView uiView, BattleSequencer sequencer, CancellationToken token)
    {
        ProcessDeadEnemies();

        bool bossAppeared = await CheckAndSpawnBossAsync(factory, uiView, sequencer, token);
        if (bossAppeared) return;

        await ReinforceNormalEnemiesAsync(factory, uiView, sequencer, token);
    }

    private void ProcessDeadEnemies()
    {
        context.FreeUpSpawnPoints();
        var deadEnemies = context.EnemyPresenters.Where(p => p.GetModel().IsDead).ToList();

        if (deadEnemies.Any())
        {
            foreach (var deadEnemy in deadEnemies)
            {
                // 点滅演出は攻撃時に実行済み。ここではオブジェクト破棄のみ
                Object.Destroy(deadEnemy.GetView().gameObject);
                deadEnemy.Dispose();
            }
            context.RemoveEnemies(deadEnemies);
            context.EnemiesDefeatedCount += deadEnemies.Count;
        }
    }

    private async UniTask<bool> CheckAndSpawnBossAsync(CharacterFactory factory, BattleUIView uiView, BattleSequencer sequencer, CancellationToken token)
    {
        if (!context.IsBossPhase && context.EnemiesDefeatedCount >= context.TotalNormalEnemies)
        {
            await SpawnBossAsync(factory, uiView, sequencer, token);
            return true;
        }
        return false;
    }

    private async UniTask ReinforceNormalEnemiesAsync(CharacterFactory factory, BattleUIView uiView, BattleSequencer sequencer, CancellationToken token)
    {
        if (context.IsBossPhase) return;

        while (context.EnemyPresenters.Count < context.MaxConcurrentEnemies &&
               context.EnemiesSpawnedCount < context.TotalNormalEnemies)
        {
            if (SpawnRandomEnemy(factory, sequencer))
            {
                int delayTime = 500;
                await uiView.AddLogAsync("敵の増援が現れた！", token);
                await UniTask.Delay(delayTime, cancellationToken: token);
            }
            else
            {
                break;
            }
        }
    }

    private async UniTask SpawnBossAsync(CharacterFactory factory, BattleUIView uiView, BattleSequencer sequencer, CancellationToken token)
    {
        context.IsBossPhase = true;
        await uiView.AddLogAsync("！！！不気味な気配がする！！！", token);
        var bossData = context.DungeonMonsters.FirstOrDefault(m => m.enemyName == context.DungeonBoss);
        if (bossData != null)
        {
            int? spawnIndex = context.FindEmptySpawnPoint(isBoss: true);
            if (spawnIndex.HasValue)
            {
                var bossPresenter = factory.CreateEnemy(bossData, spawnIndex.Value, sequencer);
                context.AddEnemy(bossPresenter);
                context.OccupySpawnPoint(spawnIndex.Value, bossPresenter);
                await uiView.AddLogAsync($"ボス【{bossData.enemyName}】が出現した！", token);
            }
        }
    }

    private bool SpawnRandomEnemy(CharacterFactory factory, BattleSequencer sequencer)
    {
        int? spawnIndex = context.FindEmptySpawnPoint();
        if (!spawnIndex.HasValue) return false;

        var normalEnemies = context.DungeonMonsters.Where(m => m.enemyName != context.DungeonBoss).ToList();
        if (!normalEnemies.Any()) return false;

        var enemyData = normalEnemies[Random.Range(0, normalEnemies.Count)];
        var enemyPresenter = factory.CreateEnemy(enemyData, spawnIndex.Value, sequencer);
        context.AddEnemy(enemyPresenter);
        context.EnemiesSpawnedCount++;
        context.OccupySpawnPoint(spawnIndex.Value, enemyPresenter);
        return true;
    }

    private List<CharacterPresenter> GetTurnOrder()
    {
        var list = new List<CharacterPresenter>();
        if (context.HeroPresenter != null) list.Add(context.HeroPresenter);
        list.AddRange(context.EnemyPresenters);
        return list.Where(p => !p.GetModel().IsDead).ToList();
    }

    public bool IsBattleEnded()
    {
        if (context.HeroPresenter == null) return true;
        bool isHeroDead = context.HeroPresenter.GetModel().IsDead;
        bool isVictory = context.IsBossPhase && !context.EnemyPresenters.Any(p => !p.GetModel().IsDead);
        return isHeroDead || isVictory;
    }

    private CharacterPresenter GetAttackTarget(CharacterPresenter attacker)
    {
        if (attacker.GetModel().Type == CharacterType.Hero)
        {
            return context.EnemyPresenters.FirstOrDefault(p => !p.GetModel().IsDead);
        }
        else
        {
            return context.HeroPresenter;
        }
    }
}
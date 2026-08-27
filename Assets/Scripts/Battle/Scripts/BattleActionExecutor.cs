﻿using Cysharp.Threading.Tasks;
using R3;
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
    /// ターン終了時の評価を実行します。
    /// フェーズ制: 現在フェーズの敵を全滅させると次のフェーズへ進み、全フェーズクリアで勝利。
    /// </summary>
    public async UniTask EvaluateEndOfTurnAsync(CharacterFactory factory, BattleUIView uiView, BattleSequencer sequencer, CancellationToken token)
    {
        ProcessDeadEnemies();

        bool anyAlive = context.EnemyPresenters.Any(p => !p.GetModel().IsDead);

        // フェーズクリア判定（生存なし＆未出現なし）
        if (!anyAlive && context.CurrentPhaseQueueEmpty)
        {
            bool hasNext = context.AdvancePhase();
            sequencer.UpdatePhaseGauge(context.CurrentPhaseIndex);

            if (!hasNext) return; // 全フェーズクリア → IsBattleEnded() が勝利を返す

            await uiView.AddLogAsync($"--- フェーズ {context.CurrentPhaseIndex + 1} / {context.PhaseCount} ---", token);
        }

        // 補充（最大同時数まで現在フェーズのキューから出現）
        await SpawnFromPhaseQueueAsync(factory, uiView, sequencer, token, announceReinforce: anyAlive);
    }

    /// <summary>
    /// 現在フェーズの未出現キューから、同時最大数まで敵を出現させる。
    /// ボス（isBoss）はボス用スポーン地点を優先し、出現時に演出イベントを発火する。
    /// </summary>
    public async UniTask SpawnFromPhaseQueueAsync(CharacterFactory factory, BattleUIView uiView, BattleSequencer sequencer, CancellationToken token, bool announceReinforce)
    {
        while (context.EnemyPresenters.Count(p => !p.GetModel().IsDead) < context.MaxConcurrentEnemies)
        {
            var enemyData = context.PeekNextSpawn();
            if (enemyData == null) break;

            // スポーン地点（ボスは中央=1を優先、埋まっていれば通常枠）
            int? spawnIndex = enemyData.isBoss && context.IsSpawnPointFree(1)
                ? 1
                : context.FindEmptySpawnPoint();
            if (!spawnIndex.HasValue) break;

            context.DequeueNextSpawn();

            if (enemyData.isBoss)
            {
                context.IsBossPhase = true;
                await uiView.AddLogAsync("！！！不気味な気配がする！！！", token);
            }

            var enemyPresenter = factory.CreateEnemy(enemyData, spawnIndex.Value, sequencer);
            context.AddEnemy(enemyPresenter);
            context.EnemiesSpawnedCount++;
            context.OccupySpawnPoint(spawnIndex.Value, enemyPresenter);

            if (enemyData.isBoss)
            {
                await uiView.AddLogAsync($"ボス【{enemyData.enemyName}】が出現した！", token);
                sequencer.OnBossAppeared.OnNext(Unit.Default);
            }
            else if (announceReinforce)
            {
                await uiView.AddLogAsync("敵の増援が現れた！", token);
                await UniTask.Delay(uiView.GetSpeedScaledDelay(500), cancellationToken: token);
            }
        }
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
        // 全フェーズをクリアしたら勝利（AdvancePhase が最終フェーズ全滅時に到達させる）
        bool isVictory = context.AllPhasesCleared;
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

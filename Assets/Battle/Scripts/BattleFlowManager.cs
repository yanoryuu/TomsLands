using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

public class BattleFlowManager
{
    private readonly BattleContext context;
    private readonly CharacterFactory factory;
    private readonly BattleUIView uiView;
    private readonly BattleSequencer sequencer;

    public BattleFlowManager(BattleContext ctx, CharacterFactory charaFactory, BattleUIView battleUI, BattleSequencer ownerSequencer)
    {
        context = ctx;
        factory = charaFactory;
        uiView = battleUI;
        sequencer = ownerSequencer;
    }

    public async UniTask ExecuteBattleAsync(HeroModel heroModel, CancellationToken token)
    {
        await SetupPhaseAsync(heroModel, token);

        int turnCount = 1;
        while (!IsBattleEnded())
        {
            await uiView.AddLogAsync($"--- ターン {turnCount} ---", token);
            var turnOrder = GetTurnOrder();

            foreach (var presenter in turnOrder)
            {
                if (presenter.GetModel().IsDead) continue;
                var targetPresenter = GetAttackTarget(presenter);
                if (targetPresenter != null)
                {
                    int damageDealt = presenter.PerformAttack(targetPresenter);
                    string logMessage = $"{presenter.GetModel().Name} の攻撃！ {targetPresenter.GetModel().Name} に {damageDealt} のダメージ！";
                    await uiView.AddLogAsync(logMessage, token);
                }
                if (IsBattleEnded()) break;
            }
            await EvaluateEndOfTurnAsync(token);
            turnCount++;
        }
        await ResultPhaseAsync(heroModel, token);
    }

    private async UniTask SetupPhaseAsync(HeroModel heroModel, CancellationToken token)
    {
        await uiView.AddLogAsync("--- 戦闘開始！ ---", token);
        var heroPresenter = factory.CreateHero(heroModel, sequencer);
        context.HeroPresenter = heroPresenter;
        context.EnemiesDefeatedCount = 0;
        context.EnemiesSpawnedCount = 0;
        context.IsBossPhase = false;

        // ★ Contextからルールを読み込みます
        int initialSpawnCount = Mathf.Min(context.MaxConcurrentEnemies, context.TotalNormalEnemies);
        for (int i = 0; i < initialSpawnCount; i++)
        {
            SpawnRandomEnemy();
        }
        await UniTask.Yield(token);
    }

    private async UniTask EvaluateEndOfTurnAsync(CancellationToken token)
    {
        context.FreeUpSpawnPoints();
        var deadEnemies = context.EnemyPresenters.Where(p => p.GetModel().IsDead).ToList();
        if (deadEnemies.Any())
        {
            foreach (var deadEnemy in deadEnemies)
            {
                await uiView.AddLogAsync($"{deadEnemy.GetModel().Name} を倒した！", token);
                context.EnemyPresenters.Remove(deadEnemy);
                Object.Destroy(deadEnemy.GetView().gameObject);
                deadEnemy.Dispose();
            }
            context.EnemiesDefeatedCount += deadEnemies.Count;
        }

        // ★ Contextからルールを読み込みます
        if (!context.IsBossPhase && context.EnemiesDefeatedCount >= context.TotalNormalEnemies)
        {
            await SpawnBossAsync(token);
            return;
        }

        if (!context.IsBossPhase)
        {
            await ReinforceEnemiesAsync(token);
        }
    }

    private async UniTask ReinforceEnemiesAsync(CancellationToken token)
    {
        // ★ Contextからルールを読み込みます
        while (context.EnemyPresenters.Count < context.MaxConcurrentEnemies &&
               context.EnemiesSpawnedCount < context.TotalNormalEnemies)
        {
            if (SpawnRandomEnemy())
            {
                await uiView.AddLogAsync("敵の増援が現れた！", token);
                await UniTask.Delay(500, cancellationToken: token);
            }
            else
            {
                break;
            }
        }
    }

    private async UniTask SpawnBossAsync(CancellationToken token)
    {
        context.IsBossPhase = true;
        await uiView.AddLogAsync("！！！不気味な気配がする！！！", token);

        // ★ dungeonBoss "名" からボスデータを検索します
        var bossData = context.CurrentStage.dungeonMonsters.FirstOrDefault(m => m.enemyName == context.CurrentStage.dungeonBoss);
        if (bossData != null)
        {
            int? spawnIndex = context.FindEmptySpawnPoint(isBoss: true);
            if (spawnIndex.HasValue)
            {
                var bossPresenter = factory.CreateEnemy(bossData, spawnIndex.Value, sequencer);
                context.EnemyPresenters.Add(bossPresenter);
                context.OccupySpawnPoint(spawnIndex.Value, bossPresenter);
                await uiView.AddLogAsync($"ボス【{bossData.enemyName}】が出現した！", token);
            }
        }
    }

    private bool SpawnRandomEnemy()
    {
        int? spawnIndex = context.FindEmptySpawnPoint();
        if (!spawnIndex.HasValue) return false;

        var normalEnemies = context.CurrentStage.dungeonMonsters.Where(m => m.enemyName != context.CurrentStage.dungeonBoss).ToList();
        if (!normalEnemies.Any()) return false;

        var enemyData = normalEnemies[Random.Range(0, normalEnemies.Count)];
        var enemyPresenter = factory.CreateEnemy(enemyData, spawnIndex.Value, sequencer);
        context.EnemyPresenters.Add(enemyPresenter);
        context.EnemiesSpawnedCount++;
        context.OccupySpawnPoint(spawnIndex.Value, enemyPresenter);
        return true;
    }

    private async UniTask ResultPhaseAsync(HeroModel heroModel, CancellationToken token)
    {
        var equippedWeaponId = "";
        var equippedArmorId = "";

        if (context.HeroPresenter.GetModel().IsDead)
        {
            await uiView.AddLogAsync("------ 敗北… ------", token);
            sequencer.OnBattleDefeat.OnNext((weaponId: equippedWeaponId, armorId: equippedArmorId));
        }
        else
        {
            await uiView.AddLogAsync("★★★★★★ 完全勝利！ ★★★★★★", token);
            sequencer.OnBattleWin.OnNext((weaponId: equippedWeaponId, armorId: equippedArmorId));
        }
    }

    private List<CharacterPresenter> GetTurnOrder()
    {
        var list = new List<CharacterPresenter>();
        if (context.HeroPresenter != null) list.Add(context.HeroPresenter);
        list.AddRange(context.EnemyPresenters);
        return list.Where(p => !p.GetModel().IsDead).ToList();
    }

    private bool IsBattleEnded()
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
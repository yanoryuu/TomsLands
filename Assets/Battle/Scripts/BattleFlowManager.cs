using Cysharp.Threading.Tasks;
using System.Linq;
using System.Threading;
using UnityEngine;

/// <summary>
/// 戦闘の大きな流れ（戦略）だけを管理する
/// </summary>
public class BattleFlowManager
{
    private readonly BattleContext context;
    private readonly CharacterFactory factory;
    private readonly BattleUIView uiView;
    private readonly BattleSequencer sequencer;
    private readonly BattleActionExecutor executor;

    public BattleFlowManager(BattleContext ctx, CharacterFactory charaFactory, BattleUIView battleUI, BattleSequencer ownerSequencer)
    {
        context = ctx;
        factory = charaFactory;
        uiView = battleUI;
        sequencer = ownerSequencer;
        executor = new BattleActionExecutor(ctx);
    }

    /// <summary>
    /// 戦闘の全体の流れを指揮します
    /// </summary>
    public async UniTask ExecuteBattleAsync(HeroModel heroModel, CancellationToken token)
    {
        // --- 準備フェーズ ---
        await uiView.AddLogAsync("--- 戦闘開始！ ---", token);
        SetupCharacters(heroModel);

        // --- 実行フェーズ ---
        int turnCount = 1;
        while (!executor.IsBattleEnded())
        {
            await uiView.AddLogAsync($"--- ターン {turnCount} ---", token);

            await executor.ExecuteTurnActionsAsync(uiView, token);
            await executor.EvaluateEndOfTurnAsync(factory, uiView, sequencer, token);

            turnCount++;
        }

        // --- 結果フェーズ ---
        await ResultPhaseAsync(heroModel, token);
    }

    /// <summary>
    /// キャラクターの初期配置を行います
    /// </summary>
    private void SetupCharacters(HeroModel heroModel)
    {
        var heroPresenter = factory.CreateHero(heroModel, sequencer);
        context.HeroPresenter = heroPresenter;
        context.EnemiesDefeatedCount = 0;
        context.EnemiesSpawnedCount = 0;
        context.IsBossPhase = false;

        int initialSpawnCount = Mathf.Min(context.MaxConcurrentEnemies, context.TotalNormalEnemies);
        for (int i = 0; i < initialSpawnCount; i++)
        {
            int? spawnIndex = context.FindEmptySpawnPoint();
            if (!spawnIndex.HasValue) continue;

            var normalEnemies = context.CurrentStage.dungeonMonsters.Where(m => m.enemyName != context.CurrentStage.dungeonBoss).ToList();
            if (!normalEnemies.Any()) continue;

            var enemyData = normalEnemies[Random.Range(0, normalEnemies.Count)];
            var enemyPresenter = factory.CreateEnemy(enemyData, spawnIndex.Value, sequencer);
            context.AddEnemy(enemyPresenter);
            context.EnemiesSpawnedCount++;
            context.OccupySpawnPoint(spawnIndex.Value, enemyPresenter);
        }
    }

    /// <summary>
    /// 戦闘結果を判定し、ログ表示とイベント発行を行います
    /// </summary>
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
}
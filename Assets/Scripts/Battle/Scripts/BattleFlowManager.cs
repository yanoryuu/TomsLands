﻿﻿using Cysharp.Threading.Tasks;
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
    private readonly StreamingSalesController salesController;
    private readonly BattlePauseController pauseController;

    public BattleFlowManager(BattleContext ctx, CharacterFactory charaFactory, BattleUIView battleUI, BattleSequencer ownerSequencer, StreamingSalesController salesCtrl = null, BattlePauseController pauseCtrl = null)
    {
        context = ctx;
        factory = charaFactory;
        uiView = battleUI;
        sequencer = ownerSequencer;
        salesController = salesCtrl;
        pauseController = pauseCtrl;
        executor = new BattleActionExecutor(ctx, ownerSequencer);
    }

    /// <summary>
    /// 戦闘の全体の流れを指揮します
    /// </summary>
    public async UniTask ExecuteBattleAsync(HeroModel heroModel, CancellationToken token)
    {
        // --- 準備フェーズ ---
        // ダンジョンの背景画像を設定（画像が設定されていれば差し替え、なければシーンの元の背景を維持）
        if (context.CurrentStage != null)
        {
            uiView.SetBackground(context.CurrentStage.dungeonImage);
        }

        await uiView.AddLogAsync("--- 戦闘開始！ ---", token);
        SetupCharacters(heroModel);

        // フェーズ1の敵を出現させる（最大3体。以降の補充・フェーズ進行はターン終了評価が行う）
        if (context.PhaseCount > 0)
        {
            await uiView.AddLogAsync($"--- フェーズ 1 / {context.PhaseCount} ---", token);
            await executor.SpawnFromPhaseQueueAsync(factory, uiView, sequencer, token, announceReinforce: false);
        }

        // --- 実行フェーズ ---
        int turnCount = 1;
        while (!executor.IsBattleEnded())
        {
            // ターン開始前にポーズチェック（ポーズ中はここで待機）
            if (pauseController != null)
                await pauseController.WaitIfPausedAsync(token);

            await uiView.AddLogAsync($"--- ターン {turnCount} ---", token);

            await executor.ExecuteTurnActionsAsync(uiView, token);

            // 戦闘が決着していたら、増援スポーンや売買処理をスキップして即結果へ
            if (executor.IsBattleEnded()) break;

            await executor.EvaluateEndOfTurnAsync(factory, uiView, sequencer, token);

            // 毎ターン売買を実行（売り場に出ているアイテムのみ対象）
            if (salesController != null)
            {
                salesController.ExecuteTurnSales();
            }

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

        // ダンジョンのモンスターデータをチェック
        if (context.CurrentStage == null)
        {
            Debug.LogError("[BattleFlowManager] CurrentStage (DungeonInfoScriptableObj) が null です！");
            return;
        }

        // フェーズ構成を確定（SOの phases → 旧方式変換 → Resources の順でフォールバック）
        context.InitializePhases();

        if (context.PhaseCount == 0)
        {
            Debug.LogError($"[BattleFlowManager] ダンジョン '{context.CurrentStage.dungeonName}' (key={context.CurrentStage.key}) のフェーズ構成が空です。ダンジョンSOの Inspector で levelDataList の phases を設定してください。");
            return;
        }

        Debug.Log($"[BattleFlowManager] ダンジョン '{context.CurrentStage.dungeonName}' Lv{context.DungeonLevel}: {context.PhaseCount}フェーズ構成で開始。");

        // フェーズ進捗ゲージを初期化
        sequencer.SetupPhaseGauge(context.PhaseCount);
        sequencer.UpdatePhaseGauge(0);
    }

    /// <summary>
    /// 戦闘結果を判定し、ログ表示とイベント発行を行います
    /// </summary>
    private async UniTask ResultPhaseAsync(HeroModel heroModel, CancellationToken token)
    {
        // HeroModel の装備リストから武器・防具IDを取得
        var equippedWeaponId = heroModel.EquippedItemIds.Count > 0 ? heroModel.EquippedItemIds[0] : "";
        var equippedArmorId  = heroModel.EquippedItemIds.Count > 1 ? heroModel.EquippedItemIds[1] : "";

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
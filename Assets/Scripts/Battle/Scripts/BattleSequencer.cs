﻿using UnityEngine;
using Cysharp.Threading.Tasks;
using R3;
using System.Collections.Generic;
using System.Threading;

public class BattleSequencer : MonoBehaviour
{
    [Header("戦闘系のマネージャー")]
    [SerializeField] private CharacterFactory characterFactory;
    [SerializeField] private BattleUIView battleUIView;
    [SerializeField] private StreamingSalesController streamingSalesController;

    [Header("戦闘ルール設定")]
    [Tooltip("この戦闘で倒すべき通常モンスターの総数")]
    [SerializeField] private const int totalNormalEnemies = 10;
    [Tooltip("フィールドに同時に出現できる敵の最大数")]
    [SerializeField] private const int maxConcurrentEnemies = 3;

    [Header("ステージデータ")]
    [SerializeField] private DungeonInfoScriptableObj currentDungeon;
    private int currentDungeonLevel = 1;

    public DungeonInfoScriptableObj CurrentDungeon => currentDungeon;

    /// <summary>
    /// 外部からダンジョンを設定する（GameFlowManagerから呼ばれる）
    /// </summary>
    public void SetDungeon(DungeonInfoScriptableObj dungeon, int dungeonLevel = 1)
    {
        currentDungeon = dungeon;
        currentDungeonLevel = Mathf.Clamp(dungeonLevel, 1, 5);
    }

    private BattleContext battleContext;
    private BattlePauseController _pauseController;

    /// <summary>バトル開始前に BattleSceneStarter から設定する。</summary>
    public void SetPauseController(BattlePauseController pc) => _pauseController = pc;

    public Subject<(string weaponId, string armorId)> OnBattleWin { get; } = new();
    public Subject<(string weaponId, string armorId)> OnBattleDefeat { get; } = new();
    public IReadOnlyList<CharacterPresenter> CharacterPresenters =>
        battleContext?.GetAllPresenters() ?? new List<CharacterPresenter>();
    public Subject<(CharacterModel attacker, CharacterModel target)> OnCharacterDamaged { get; } = new();
    
    /// <summary>
    /// 敵が倒された時に発火するイベント。倒された敵の CharacterModel を通知する。
    /// </summary>
    public Subject<CharacterModel> OnEnemyDefeated { get; } = new();

    /// <summary>
    /// ボスが出現した時に発火するイベント。
    /// </summary>
    public Subject<Unit> OnBossAppeared { get; } = new();


    public void StartBattle(HeroModel heroModel)
    {
        var token = this.GetCancellationTokenOnDestroy();
        BattleStartAsync(heroModel, token).Forget();
    }

    private async UniTaskVoid BattleStartAsync(HeroModel heroModel, CancellationToken token)
    {
        try
        {
            // ★ ここで戦闘ルールをContextに渡す
            battleContext = new BattleContext(currentDungeon, currentDungeonLevel, totalNormalEnemies, maxConcurrentEnemies);
            var flowManager = new BattleFlowManager(battleContext, characterFactory, battleUIView, this, streamingSalesController, _pauseController);

            await flowManager.ExecuteBattleAsync(heroModel, token);
            Debug.Log("戦闘が終了しました。 (BattleSequencer)");
        }
        catch (System.OperationCanceledException)
        {
            // シーン破棄による正常キャンセル
            Debug.Log("[BattleSequencer] 戦闘がキャンセルされました。");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[BattleSequencer] 戦闘中に例外が発生しました: {ex}");

            // 例外時でも結果を通知して、リザルト画面→シーン遷移が行われるようにする
            var weaponId = heroModel.EquippedItemIds.Count > 0 ? heroModel.EquippedItemIds[0] : "";
            var armorId  = heroModel.EquippedItemIds.Count > 1 ? heroModel.EquippedItemIds[1] : "";
            OnBattleDefeat.OnNext((weaponId, armorId));
        }
    }
}
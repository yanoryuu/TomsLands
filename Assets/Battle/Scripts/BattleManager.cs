using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;

public class BattleManager : MonoBehaviour
{
    public event Action<string> OnLogMessage;
    public event Action<List<BattleCharacter>> OnTurnOrderChanged;

    [Header("戦闘フィールド設定")]
    [SerializeField] private Transform heroSpawnPoint;
    [SerializeField] private Transform[] enemySpawnPoints;
    
    [Header("プレハブ・UI設定")]
    [SerializeField] private GameObject characterPrefab;
    [SerializeField] private CharacterStatusView heroStatusView;

    [Header("テスト実行用")]
    [SerializeField] private StageData testStageData;

    private StageData currentStage;
    private BattleCharacter hero;
    private List<BattleCharacter> enemies = new List<BattleCharacter>();

    async void Start()
    {
        if (testStageData != null)
        {
            var token = this.GetCancellationTokenOnDestroy();
            await StartBattle(testStageData, token).SuppressCancellationThrow();
        }
    }

    public async UniTask StartBattle(StageData stageData, CancellationToken token)
    {
        currentStage = stageData;
        OnLogMessage?.Invoke($"--- 戦闘開始！ --- ステージ: {currentStage.stageName}");
        
        await BattleFlow(token);
    }

    private async UniTask BattleFlow(CancellationToken token)
    {
        // 1. 準備フェーズ
        await SetupPhase(token);

        // 2. 戦闘ループフェーズ
        int turnCount = 1;
        while (!token.IsCancellationRequested)
        {
            OnLogMessage?.Invoke($"--- ターン {turnCount} ---");
            UpdateTurnOrder(); // ターン開始時に行動順を更新

            // 生きているキャラクターのリストを再取得
            var turnOrder = new List<BattleCharacter>();
            if (!hero.IsDead()) turnOrder.Add(hero);
            turnOrder.AddRange(enemies.Where(e => !e.IsDead()));

            // 行動順にキャラクターが行動
            foreach (var character in turnOrder)
            {
                 if (token.IsCancellationRequested) return;
                 if (character.IsDead()) continue;

                 // ターゲットを決定
                 BattleCharacter target = null;
                 if(character.IsEnemy)
                 {
                     target = hero; // 敵は勇者を狙う
                 }
                 else
                 {
                     // 勇者は生きている最初の敵を狙う
                     target = enemies.FirstOrDefault(e => !e.IsDead());
                 }

                 if(target != null)
                 {
                    character.Act(target);
                    OnLogMessage?.Invoke($"{character.CharacterName}の攻撃！");
                    await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
                 }


                // どちらかの陣営が全滅したかチェック
                if (enemies.All(e => e.IsDead()))
                {
                    await VictoryPhase(token);
                    return;
                }
                if (hero.IsDead())
                {
                    await DefeatPhase(token);
                    return;
                }
            }

            // ターン終了処理 (ギミックなど)
            await EndTurnPhase(token);
            turnCount++;
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
        }
    }

    private async UniTask SetupPhase(CancellationToken token)
    {
        // 勇者を生成
        GameObject heroGo = Instantiate(characterPrefab, heroSpawnPoint.position, Quaternion.identity);
        hero = heroGo.GetComponent<BattleCharacter>();
        hero.SetupHero();
        heroStatusView?.SetTargetCharacter(hero);

        // 敵を生成
        for (int i = 0; i < currentStage.normalEnemies.Count; i++)
        {
            if (i >= enemySpawnPoints.Length) break;
            
            GameObject enemyGo = Instantiate(characterPrefab, enemySpawnPoints[i].position, Quaternion.identity);
            var enemy = enemyGo.GetComponent<BattleCharacter>();
            enemy.Setup(currentStage.normalEnemies[i]);
            enemies.Add(enemy);
        }
        
        await UniTask.Yield(PlayerLoopTiming.Update, token);
    }
    

    private void UpdateTurnOrder()
    {
        var turnOrderList = new List<BattleCharacter>();
        if (!hero.IsDead())
        {
            turnOrderList.Add(hero);
        }
        turnOrderList.AddRange(enemies.Where(e => !e.IsDead()));

        OnTurnOrderChanged?.Invoke(turnOrderList);
    }

    private async UniTask EndTurnPhase(CancellationToken token)
    {
        OnLogMessage?.Invoke("ターン終了処理...");
        // TODO: ここでギミックや状態異常の処理
        await UniTask.Yield(token);
    }

    private async UniTask VictoryPhase(CancellationToken token)
    {
        OnLogMessage?.Invoke("★★★★★★ 勝利！ ★★★★★★");
        // TODO: リザルト処理
        await UniTask.Yield(token);
    }

    private async UniTask DefeatPhase(CancellationToken token)
    {
        OnLogMessage?.Invoke("------ 敗北… ------");
        // TODO: ゲームオーバー処理
        await UniTask.Yield(token);
    }
}

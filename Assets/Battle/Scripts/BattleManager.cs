using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using R3;

public class BattleManager : MonoBehaviour
{
    // --- イベント定義 ---
    public event Action<string> OnLogMessage;
    public event Action<List<BattleCharacter>> OnTurnOrderChanged;

    [Header("戦闘フロー設定")]
    [SerializeField] private int totalNormalEnemies = 10;
    [SerializeField] private int maxConcurrentEnemies = 3;

    [Header("戦闘キャラクター設定")]
    [SerializeField] private BattleCharacter hero;
    [SerializeField] private Transform[] enemySpawnPoints;
    
    [Header("プレハブ・UI設定")]
    [SerializeField] private GameObject enemyCharacterPrefab;
    [SerializeField] private CharacterStatusView heroStatusView;

    [Header("テスト実行用")]
    [SerializeField] private StageData testStageData;

    // --- 内部状態変数 ---
    private StageData currentStage;
    private List<BattleCharacter> enemies = new List<BattleCharacter>();
    private int enemiesSpawnedCount = 0;
    private bool isBossPhase = false;
    private ReactiveProperty<bool> battleEnded = new ReactiveProperty<bool>(); // 戦闘が終了したかどうかのフラグ
    
    public Subject<(string armor,string weapon)> OnWin = new Subject<(string armor, string weapon)>();
    public Subject<(string armor,string weapon)> OnDefeat = new Subject<(string armor, string weapon)>();

    //TODO:敵が死んだ際に通知するスクリプトを追加する

    public async void BattleStart()
    {
        if (testStageData != null)
        {
            var token = this.GetCancellationTokenOnDestroy();
            await StartBattle(testStageData, token).SuppressCancellationThrow();
        }
    }

    private async UniTask StartBattle(StageData stageData, CancellationToken token)
    {
        currentStage = stageData;
        OnLogMessage?.Invoke($"--- 戦闘開始！ --- ステージ: {currentStage.stageName}");
        await BattleFlow(token);
    }

    private async UniTask BattleFlow(CancellationToken token)
    {
        await SetupPhase(token);

        int turnCount = 1;
        while (!battleEnded.Value && !token.IsCancellationRequested)
        {
            // --- ターン開始処理 ---
            OnLogMessage?.Invoke($"--- ターン {turnCount} ---");
            var turnOrder = UpdateAndGetTurnOrder();

            // --- 各キャラクターの行動 ---
            foreach (var character in turnOrder)
            {
                if (token.IsCancellationRequested || character.IsDead()) continue;

                var target = character.IsEnemy() ? hero : enemies.FirstOrDefault(e => !e.IsDead());
                if (target != null)
                {
                    character.Act(target);
                    OnLogMessage?.Invoke($"{character.CharacterName}の攻撃！");
                    await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
                }

                // 勇者が行動中に倒されたら、即座に行動ループを抜ける
                if (hero.IsDead()) break;
            }
            
            await EvaluateEndOfTurn(token);
            
            turnCount++;
        }
    }

    private async UniTask SetupPhase(CancellationToken token)
    {
        enemies.Clear();
        enemiesSpawnedCount = 0;
        isBossPhase = false;
        battleEnded.Value = false;

        // 初期敵のスポーン
        int initialSpawnCount = Mathf.Min(maxConcurrentEnemies, totalNormalEnemies);
        for (int i = 0; i < initialSpawnCount; i++)
        {
            await SpawnRandomEnemy(token);
        }
    }
    
    // ターン終了時の評価を行うメソッド
    private async UniTask EvaluateEndOfTurn(CancellationToken token)
    {
        // 1. プレイヤーの敗北を最優先でチェック
        if (hero.IsDead())
        {
            await DefeatPhase(token);
            return;
        }

        // 2. 死んだ敵をフィールドから除去
        RemoveDeadEnemies();

        // 3. ボス戦中で、敵が全滅した場合（ボスを倒した場合）
        if (isBossPhase && !enemies.Any())
        {
            await VictoryPhase(token);
            return;
        }

        // 4. 通常フェーズで、規定数の敵を倒し、フィールドが空になった場合
        if (!isBossPhase && enemiesSpawnedCount >= totalNormalEnemies && !enemies.Any())
        {
            await StartBossPhase(token);
            return; // ボスが出現したので、このターンの評価は終わり
        }

        // 5. 上記のいずれでもない場合、増援条件を満たしていれば敵を補充する
        await ReinforceEnemies(token);
    }

    // 死んだ敵をリストから除去し、オブジェクトを破棄する
    private void RemoveDeadEnemies()
    {
        enemies.RemoveAll(e => 
        {
            if(e.IsDead())
            {
                OnLogMessage?.Invoke($"{e.gameObject.name}が死亡！");
                //TODO:敵が死んだ際に通知するスクリプトを追加する
                Destroy(e.gameObject);
                return true;
            }
            return false;
        });
    }

    // 敵の増援を呼び出すメソッド。
    private async UniTask ReinforceEnemies(CancellationToken token)
    {
        if (isBossPhase || enemiesSpawnedCount >= totalNormalEnemies) return;

        while (enemies.Count < maxConcurrentEnemies && enemiesSpawnedCount < totalNormalEnemies)
        {
            OnLogMessage?.Invoke("敵の増援が出現！");
            bool success = await SpawnRandomEnemy(token);
            if (!success)
            {
                OnLogMessage?.Invoke("増援を呼べる場所がない！");
                break; // 無限ループを避ける
            }
            await UniTask.Delay(500, cancellationToken: token);
        }
    }

    private async UniTask<bool> SpawnRandomEnemy(CancellationToken token)
    {
        if (currentStage.normalEnemies.Count == 0) return false;

        Transform spawnPoint = enemySpawnPoints.FirstOrDefault(sp => !enemies.Any(e => Vector3.Distance(e.transform.position, sp.position) < 0.1f));
        if (spawnPoint == null) return false;

        var enemyData = currentStage.normalEnemies[UnityEngine.Random.Range(0, currentStage.normalEnemies.Count)];
        GameObject enemyGo = Instantiate(enemyCharacterPrefab, spawnPoint.position, Quaternion.identity);
        var enemy = enemyGo.GetComponent<BattleCharacter>();
        enemy.Setup(enemyData);
        enemies.Add(enemy);
        enemiesSpawnedCount++;
        
        await UniTask.Yield(token);
        return true;
    }
    
    private async UniTask StartBossPhase(CancellationToken token)
    {
        isBossPhase = true;
        OnLogMessage?.Invoke("！！！！！");
        await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
        OnLogMessage?.Invoke($"ボス: {currentStage.bossEnemy.enemyName} が出現した！");

        var spawnPoint = enemySpawnPoints[enemySpawnPoints.Length / 2];
        GameObject enemyGo = Instantiate(enemyCharacterPrefab, spawnPoint.position, Quaternion.identity);
        var boss = enemyGo.GetComponent<BattleCharacter>();
        boss.Setup(currentStage.bossEnemy);
        enemies.Add(boss);
    }

    private List<BattleCharacter> UpdateAndGetTurnOrder()
    {
        var turnOrderList = new List<BattleCharacter>();
        if (!hero.IsDead()) turnOrderList.Add(hero);
        turnOrderList.AddRange(enemies.Where(e => !e.IsDead()));
        OnTurnOrderChanged?.Invoke(turnOrderList);
        return turnOrderList;
    }

    private async UniTask EndTurnPhase(CancellationToken token)
    {
        OnLogMessage?.Invoke("---");
        await UniTask.Yield(token);
    }

    private async UniTask VictoryPhase(CancellationToken token)
    {
        if (battleEnded.Value) return;
        battleEnded.Value = true;
        OnWin.OnNext((hero.HeroData.armorId.Value, hero.HeroData.weaponId.Value));
        OnLogMessage?.Invoke("★★★★★★ 完全勝利！ ★★★★★★");
        await UniTask.Yield(token);
    }

    private async UniTask DefeatPhase(CancellationToken token)
    {
        if (battleEnded.Value) return;
        battleEnded.Value = true;
        OnDefeat.OnNext((hero.HeroData.armorId.Value, hero.HeroData.weaponId.Value));
        OnLogMessage?.Invoke("------ 敗北… ------");
        await UniTask.Yield(token);
    }
}
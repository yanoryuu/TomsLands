using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;

public class BattleManager : MonoBehaviour
{
    // --- イベント定義 ---
    public event Action<string> OnLogMessage;
    public event Action<List<BattleCharacter>> OnTurnOrderChanged;

    [Header("戦闘フロー設定")]
    [SerializeField] private int totalNormalEnemies = 10; // ステージあたりの通常敵の総数
    [SerializeField] private int maxConcurrentEnemies = 3; // 敵の最大同時出現数

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
    private int enemiesSpawnedCount = 0; // これまでに出現した通常敵の累計
    private bool isBossPhase = false; // ボス戦フェーズかどうかのフラグ

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
        await SetupPhase(token);

        int turnCount = 1;
        while (!hero.IsDead()) // 勇者が生きている限りループ
        {
            // --- ターン開始処理 ---
            OnLogMessage?.Invoke($"--- ターン {turnCount} ---");
            UpdateTurnOrder();

            // --- 各キャラクターの行動 ---
            var turnOrder = new List<BattleCharacter>(OnTurnOrderChanged.GetInvocationList().Length > 0 ? enemies.Prepend(hero).ToList() : new List<BattleCharacter>());
            if (OnTurnOrderChanged.GetInvocationList().Length > 0)
            {
                var currentTurnOrder = new List<BattleCharacter>();
                if (!hero.IsDead()) currentTurnOrder.Add(hero);
                currentTurnOrder.AddRange(enemies.Where(e => !e.IsDead()));
                OnTurnOrderChanged?.Invoke(currentTurnOrder);
                turnOrder = currentTurnOrder;
            }
            
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

                if (hero.IsDead()) break; // 勇者が行動中に倒されたら即ループを抜ける
            }

            if (hero.IsDead()) break; // ターン終了時に勇者が倒れていたらループを抜ける

            // --- ターン終了処理 ---
            // 敵が倒されていたら、補充のチェックを行う
            await CheckAndReinforceEnemies(token);

            // ボス戦への移行チェック
            if (!isBossPhase && enemiesSpawnedCount >= totalNormalEnemies && enemies.All(e => e.IsDead()))
            {
                await StartBossPhase(token);
            }

            // 最終的な勝利条件のチェック
            if (isBossPhase && enemies.All(e => e.IsDead()))
            {
                await VictoryPhase(token);
                return; // 戦闘終了
            }
            
            await EndTurnPhase(token);
            turnCount++;
        }

        // ループを抜けたら敗北処理
        if (hero.IsDead())
        {
            await DefeatPhase(token);
        }
    }

    private async UniTask SetupPhase(CancellationToken token)
    {
        heroStatusView?.SetTargetCharacter(hero);
        enemies.Clear();
        enemiesSpawnedCount = 0;
        isBossPhase = false;

        // 初期の敵を最大数までスポーンさせる
        int initialSpawnCount = Mathf.Min(maxConcurrentEnemies, totalNormalEnemies);
        for (int i = 0; i < initialSpawnCount; i++)
        {
            await SpawnRandomEnemy(token);
        }
    }
    
    // 敵が倒されたかチェックし、必要なら補充する
    private async UniTask CheckAndReinforceEnemies(CancellationToken token)
    {
        // 死んだ敵をリストから除去
        enemies.RemoveAll(e => e.IsDead());

        // ボス戦中、または通常敵をすべて出し切ったら補充しない
        if (isBossPhase || enemiesSpawnedCount >= totalNormalEnemies) return;

        // 空きスロットがあれば補充
        while (enemies.Count < maxConcurrentEnemies && enemiesSpawnedCount < totalNormalEnemies)
        {
            OnLogMessage?.Invoke("敵の増援が出現！");
            await SpawnRandomEnemy(token);
            await UniTask.Delay(500, cancellationToken: token);
        }
    }

    // ランダムな敵1体を、空いている場所に出現させる
    private async UniTask SpawnRandomEnemy(CancellationToken token)
    {
        if (currentStage.normalEnemies.Count == 0) return;

        Transform spawnPoint = enemySpawnPoints.FirstOrDefault(sp => !enemies.Any(e => Vector3.Distance(e.transform.position, sp.position) < 0.1f));
        if (spawnPoint == null) return;

        var enemyData = currentStage.normalEnemies[UnityEngine.Random.Range(0, currentStage.normalEnemies.Count)];
        GameObject enemyGo = Instantiate(enemyCharacterPrefab, spawnPoint.position, Quaternion.identity);
        var enemy = enemyGo.GetComponent<BattleCharacter>();
        enemy.Setup(enemyData);
        enemies.Add(enemy);
        enemiesSpawnedCount++;
        
        await UniTask.Yield(token);
    }
    
    // ボス戦を開始する処理
    private async UniTask StartBossPhase(CancellationToken token)
    {
        isBossPhase = true;
        OnLogMessage?.Invoke("！！！！！");
        await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
        OnLogMessage?.Invoke($"ボス: {currentStage.bossEnemy.enemyName} が出現した！");

        // ボスをスポーンさせる
        // TODO: ボス専用のスポーン位置を用意するとより良い
        var spawnPoint = enemySpawnPoints[enemySpawnPoints.Length / 2]; // 中央に出現させる
        GameObject enemyGo = Instantiate(enemyCharacterPrefab, spawnPoint.position, Quaternion.identity);
        var boss = enemyGo.GetComponent<BattleCharacter>();
        boss.Setup(currentStage.bossEnemy);
        enemies.Add(boss);
    }

    private void UpdateTurnOrder()
    {
        var turnOrderList = new List<BattleCharacter>();
        if (!hero.IsDead()) turnOrderList.Add(hero);
        turnOrderList.AddRange(enemies.Where(e => !e.IsDead()));
        OnTurnOrderChanged?.Invoke(turnOrderList);
    }

    private async UniTask EndTurnPhase(CancellationToken token)
    {
        OnLogMessage?.Invoke("---");
        await UniTask.Yield(token);
    }

    private async UniTask VictoryPhase(CancellationToken token) => OnLogMessage?.Invoke("★★★★★★ 完全勝利！ ★★★★★★");
    private async UniTask DefeatPhase(CancellationToken token) => OnLogMessage?.Invoke("------ 敗北… ------");
}
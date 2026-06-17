using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;

public class BattleContext
{
    public DungeonInfoScriptableObj CurrentStage { get; }
    public int DungeonLevel { get; }
    public CharacterPresenter HeroPresenter { get; set; }

    // フォールバック用キャッシュ
    private List<EnemyData> _fallbackMonsters;

    /// <summary>
    /// 現在レベルの出現モンスター一覧。
    /// ダンジョンSOの levelDataList が空の場合、Resources/EnemyData から自動で読み込む。
    /// </summary>
    public List<EnemyData> DungeonMonsters
    {
        get
        {
            var data = CurrentStage?.GetLevelData(DungeonLevel);
            var monsters = data?.monsters;
            if (monsters != null && monsters.Count > 0)
                return monsters;

            // levelDataList が未設定の場合、Resources から EnemyData を自動収集してフォールバック
            if (_fallbackMonsters == null)
            {
                _fallbackMonsters = AddressableLoader.LoadAll<EnemyData>("EnemyData");
                if (_fallbackMonsters.Count > 0)
                {
                    Debug.LogWarning($"[BattleContext] ダンジョン '{CurrentStage?.dungeonName}' (Lv{DungeonLevel}) の levelDataList にモンスターが未設定のため、Resources/EnemyData から {_fallbackMonsters.Count} 体を自動読み込みしました。ダンジョンSOの Inspector で levelDataList を設定してください。");
                }
                else
                {
                    Debug.LogError("[BattleContext] Resources/EnemyData にも EnemyData が見つかりません。EnemyData アセットを作成してください。");
                }
            }
            return _fallbackMonsters;
        }
    }

    /// <summary>
    /// 現在レベルのボス名。
    /// levelDataList が未設定の場合、isBoss=true の EnemyData を自動検出する。
    /// </summary>
    public string DungeonBoss
    {
        get
        {
            var data = CurrentStage?.GetLevelData(DungeonLevel);
            if (!string.IsNullOrEmpty(data?.bossName))
                return data.bossName;

            // フォールバック: DungeonMonsters 内で isBoss=true のものを検出
            var boss = DungeonMonsters.FirstOrDefault(m => m.isBoss);
            return boss != null ? boss.enemyName : "";
        }
    }

    private readonly List<CharacterPresenter> _enemyPresenters = new List<CharacterPresenter>();

    // IReadOnlyListとして、安全に公開する
    public IReadOnlyList<CharacterPresenter> EnemyPresenters => _enemyPresenters;
    public ReactiveProperty<CharacterPresenter> SelectedTarget { get; } = new();

    // ★ 戦闘ルールを保持する変数
    public int TotalNormalEnemies { get; }
    public int MaxConcurrentEnemies { get; }

    public int EnemiesDefeatedCount { get; set; }
    public int EnemiesSpawnedCount { get; set; }
    public bool IsBossPhase { get; set; }

    private readonly Dictionary<int, CharacterPresenter> occupiedSpawnPoints = new Dictionary<int, CharacterPresenter>();

    // コンストラクタで戦闘ルールを受け取る
    public BattleContext(DungeonInfoScriptableObj stageData, int dungeonLevel, int totalEnemies, int maxConcurrent)
    {
        CurrentStage = stageData;
        DungeonLevel = dungeonLevel;
        TotalNormalEnemies = totalEnemies;
        MaxConcurrentEnemies = maxConcurrent;
    }

    public void AddEnemy(CharacterPresenter enemy)
    {
        _enemyPresenters.Add(enemy);
    }

    public void RemoveEnemies(IEnumerable<CharacterPresenter> enemiesToRemove)
    {
        foreach (var enemy in enemiesToRemove.ToList()) // ToList()で安全にループ
        {
            _enemyPresenters.Remove(enemy);
        }
    }

    public List<CharacterPresenter> GetAllPresenters()
    {
        var all = new List<CharacterPresenter>();
        if (HeroPresenter != null) all.Add(HeroPresenter);
        all.AddRange(EnemyPresenters);
        return all;
    }

    public int? FindEmptySpawnPoint(bool isBoss = false)
    {
        if (isBoss) return 1;

        for (int i = 0; i < MaxConcurrentEnemies; i++)
        {
            if (!occupiedSpawnPoints.ContainsKey(i))
            {
                return i;
            }
        }
        return null;
    }

    public void OccupySpawnPoint(int index, CharacterPresenter presenter)
    {
        occupiedSpawnPoints[index] = presenter;
    }

    public void FreeUpSpawnPoints()
    {
        var deadEntries = occupiedSpawnPoints
            .Where(pair => pair.Value == null || pair.Value.GetModel().IsDead)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in deadEntries)
        {
            occupiedSpawnPoints.Remove(key);
        }
    }
}
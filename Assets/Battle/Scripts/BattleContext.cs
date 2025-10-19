using System.Collections.Generic;
using System.Linq;
using R3;

public class BattleContext
{
    public DungeonInfoScriptableObj CurrentStage { get; }
    public CharacterPresenter HeroPresenter { get; set; }

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
    public BattleContext(DungeonInfoScriptableObj stageData, int totalEnemies, int maxConcurrent)
    {
        CurrentStage = stageData;
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
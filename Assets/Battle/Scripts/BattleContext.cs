using System.Collections.Generic;
using System.Linq;
using R3;

public class BattleContext
{
    public DungeonInfoScriptableObj CurrentStage { get; }
    public CharacterPresenter HeroPresenter { get; set; }
    public List<CharacterPresenter> EnemyPresenters { get; } = new List<CharacterPresenter>();
    public ReactiveProperty<CharacterPresenter> SelectedTarget { get; } = new();

    // ★ 戦闘ルールを保持する変数
    public int TotalNormalEnemies { get; }
    public int MaxConcurrentEnemies { get; }

    public int EnemiesDefeatedCount { get; set; }
    public int EnemiesSpawnedCount { get; set; }
    public bool IsBossPhase { get; set; }

    private readonly Dictionary<int, CharacterPresenter> occupiedSpawnPoints = new Dictionary<int, CharacterPresenter>();

    // ★ コンストラクタで戦闘ルールを受け取ります
    public BattleContext(DungeonInfoScriptableObj stageData, int totalEnemies, int maxConcurrent)
    {
        CurrentStage = stageData;
        TotalNormalEnemies = totalEnemies;
        MaxConcurrentEnemies = maxConcurrent;
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
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
                _fallbackMonsters = RemoteBalance.ApplyList("enemies", AddressableLoader.LoadAll<EnemyData>("EnemyData"), e => e.enemyId);
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

    // ===== フェーズ制（ダンジョンはフェーズ列で構成され、全クリアでダンジョンクリア） =====

    /// <summary>このダンジョンレベルのフェーズ一覧（InitializePhases で確定）</summary>
    public IReadOnlyList<DungeonPhaseData> Phases { get; private set; } = new List<DungeonPhaseData>();

    /// <summary>フェーズ総数</summary>
    public int PhaseCount => Phases.Count;

    /// <summary>現在のフェーズ番号（0始まり）。PhaseCount に達したら全フェーズクリア</summary>
    public int CurrentPhaseIndex { get; private set; }

    /// <summary>全フェーズをクリアしたか</summary>
    public bool AllPhasesCleared => CurrentPhaseIndex >= PhaseCount;

    /// <summary>現在フェーズの未出現の敵が残っていないか</summary>
    public bool CurrentPhaseQueueEmpty => _phaseSpawnQueue.Count == 0;

    private readonly Queue<EnemyData> _phaseSpawnQueue = new Queue<EnemyData>();

    /// <summary>
    /// フェーズ構成を確定する。SOの phases が未設定なら旧方式（monsters/bossName）から自動変換、
    /// それも無ければ Resources フォールバックのモンスターから組み立てる。
    /// </summary>
    public void InitializePhases()
    {
        var level = CurrentStage?.GetLevelData(DungeonLevel);

        List<DungeonPhaseData> phases = null;
        if (level?.phases != null && level.phases.Any(p => p?.enemies != null && p.enemies.Any(e => e != null)))
        {
            phases = level.phases;
        }
        else if (level?.monsters != null && level.monsters.Count > 0)
        {
            phases = DungeonPhaseBuilder.BuildFromLegacy(level);
            Debug.LogWarning($"[BattleContext] '{CurrentStage?.dungeonName}' Lv{DungeonLevel} の phases が未設定のため旧方式から自動変換しました（{phases.Count}フェーズ）。SOに phases を設定してください。");
        }
        else
        {
            // Resources フォールバックのモンスターから組み立て
            var all = DungeonMonsters;
            var boss = all.FirstOrDefault(m => m != null && m.isBoss);
            var normals = all.Where(m => m != null && m != boss).ToList();
            phases = DungeonPhaseBuilder.Build(normals, boss);
            Debug.LogWarning($"[BattleContext] フォールバックのモンスターからフェーズを自動構成しました（{phases.Count}フェーズ）。");
        }

        Phases = phases.Where(p => p?.enemies != null && p.enemies.Any(e => e != null)).ToList();
        CurrentPhaseIndex = 0;
        LoadPhaseQueue(0);
    }

    /// <summary>次のフェーズへ進む。次があれば true、全フェーズクリアなら false。</summary>
    public bool AdvancePhase()
    {
        CurrentPhaseIndex++;
        if (AllPhasesCleared) return false;
        LoadPhaseQueue(CurrentPhaseIndex);
        return true;
    }

    /// <summary>現在フェーズの次に出現する敵を覗く（無ければ null）。</summary>
    public EnemyData PeekNextSpawn() => _phaseSpawnQueue.Count > 0 ? _phaseSpawnQueue.Peek() : null;

    /// <summary>現在フェーズの次に出現する敵を取り出す（無ければ null）。</summary>
    public EnemyData DequeueNextSpawn() => _phaseSpawnQueue.Count > 0 ? _phaseSpawnQueue.Dequeue() : null;

    /// <summary>指定のスポーン地点が空いているか。</summary>
    public bool IsSpawnPointFree(int index) => !occupiedSpawnPoints.ContainsKey(index);

    private void LoadPhaseQueue(int index)
    {
        _phaseSpawnQueue.Clear();
        if (index < 0 || index >= Phases.Count) return;
        foreach (var e in Phases[index].enemies)
        {
            if (e != null) _phaseSpawnQueue.Enqueue(e);
        }
    }

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
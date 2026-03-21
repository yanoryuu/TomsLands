using UnityEngine;

public class CharacterFactory : MonoBehaviour
{
    [Header("データ")]
    [SerializeField] private HeroData heroData;

    [Header("Prefab")]
    [SerializeField] private CharacterView heroPrefab;
    [SerializeField] private CharacterView enemyPrefab;

    [Header("生成場所")]
    [SerializeField] private Transform heroSpawnPoint;
    [SerializeField] private Transform[] enemySpawnPoints;

    /// <summary>
    /// 勇者を生成し、そのPresenterを返す
    /// </summary>
    public CharacterPresenter CreateHero(HeroModel savedHeroModel, BattleSequencer sequencer)
    {
        var model = new CharacterModel(heroData, savedHeroModel);
        CharacterView view = Instantiate(heroPrefab, heroSpawnPoint.position, Quaternion.identity);
        var presenter = new CharacterPresenter(model, view, sequencer);
        presenter.Initialize();
        return presenter;
    }

    public CharacterPresenter CreateEnemy(EnemyData enemyData, int spawnIndex, BattleSequencer sequencer)
    {
        if (spawnIndex < 0 || spawnIndex >= enemySpawnPoints.Length)
        {
            Debug.LogError($"無効なスポーン地点インデックス({spawnIndex})が指定されました！", this);
            return null;
        }
        Transform spawnPoint = enemySpawnPoints[spawnIndex];

        var model = new CharacterModel(enemyData);
        CharacterView view = Instantiate(enemyPrefab, spawnPoint.position, Quaternion.identity);
        var presenter = new CharacterPresenter(model, view, sequencer);
        presenter.Initialize();
        return presenter;
    }
}
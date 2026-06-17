using UnityEngine;
using VContainer;
using VContainer.Unity;

public class GameOverLifetimeScope : LifetimeScope
{
    [SerializeField] private GameOverView gameOverView;

    protected override void Configure(IContainerBuilder builder)
    {
        // SceneTransitionService の依存解決に必要
        var battleInputData = AddressableLoader.Load<BattleInputData>("SceneData/BattleInputData")
                              ?? ScriptableObject.CreateInstance<BattleInputData>();
        var battleOutputData = AddressableLoader.Load<BattleOutputData>("SceneData/BattleOutputData")
                               ?? ScriptableObject.CreateInstance<BattleOutputData>();
        builder.RegisterInstance(battleInputData);
        builder.RegisterInstance(battleOutputData);

        // TomsModel はコンストラクタでセーブデータを自動ロード（CurrentTurn 取得用）
        builder.Register<TomsModel>(Lifetime.Singleton);
        builder.Register<SceneTransitionService>(Lifetime.Singleton);

        if (gameOverView != null)
            builder.RegisterComponent(gameOverView);
        else
            Debug.LogError("[GameOverLifetimeScope] gameOverView が Inspector で未設定です！");

        builder.RegisterEntryPoint<GameOverPresenter>();

        Debug.Log("[GameOverLifetimeScope] Configured.");
    }
}

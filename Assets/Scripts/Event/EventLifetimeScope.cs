using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// EventScene 用の LifetimeScope。
/// EventInputData / EventOutputData を使ってイベントを表示・実行する。
/// </summary>
public class EventLifetimeScope : LifetimeScope
{
    [Header("Scene References")]
    [SerializeField] private EventSceneView eventSceneView;

    [Header("Shared ScriptableObjects")]
    [SerializeField] private EventInputData eventInputData;
    [SerializeField] private EventOutputData eventOutputData;

    protected override void Configure(IContainerBuilder builder)
    {
        // EventInputData のロード
        if (eventInputData == null)
        {
            eventInputData = AddressableLoader.Load<EventInputData>("SceneData/EventInputData");
            if (eventInputData == null)
            {
                eventInputData = ScriptableObject.CreateInstance<EventInputData>();
                Debug.LogWarning("[EventLifetimeScope] EventInputData が未設定。Resourcesまたは実行時インスタンスを使用。");
            }
        }
        builder.RegisterInstance(eventInputData);

        // EventOutputData のロード
        if (eventOutputData == null)
        {
            eventOutputData = AddressableLoader.Load<EventOutputData>("SceneData/EventOutputData");
            if (eventOutputData == null)
            {
                eventOutputData = ScriptableObject.CreateInstance<EventOutputData>();
                Debug.LogWarning("[EventLifetimeScope] EventOutputData が未設定。Resourcesまたは実行時インスタンスを使用。");
            }
        }
        builder.RegisterInstance(eventOutputData);

        // BattleInputData / BattleOutputData（SceneTransitionService の依存）
        var battleInputData = AddressableLoader.Load<BattleInputData>("SceneData/BattleInputData");
        if (battleInputData == null) battleInputData = ScriptableObject.CreateInstance<BattleInputData>();
        var battleOutputData = AddressableLoader.Load<BattleOutputData>("SceneData/BattleOutputData");
        if (battleOutputData == null) battleOutputData = ScriptableObject.CreateInstance<BattleOutputData>();
        builder.RegisterInstance(battleInputData);
        builder.RegisterInstance(battleOutputData);

        // TomsModel（イベントコマンドの ChangeMoney / ChangeTrust 実行用）
        builder.Register<TomsModel>(Lifetime.Singleton);

        // ShopStatusModel（ChangeTrust 用。インライン経路 TomsEventExecutor と同じく店ステータスの信頼度を変更する）
        // GameLifetimeScope と同じロード手順。このシーンは親スコープを持たないため自前で登録する
        var gameBalanceData = AddressableLoader.Load<GameBalanceData>("Marketing/GameBalanceData");
        if (gameBalanceData == null)
        {
            gameBalanceData = ScriptableObject.CreateInstance<GameBalanceData>();
            Debug.LogWarning("[EventLifetimeScope] GameBalanceData が見つかりません。デフォルト値を使用します。");
        }
        gameBalanceData = RemoteBalance.ApplyOverwrite("gameBalance", gameBalanceData);
        builder.RegisterInstance(gameBalanceData);
        builder.Register<ShopStatusModel>(Lifetime.Singleton);

        // レリック補正（ChangeMoney の EventRewardMul 用。GameLifetimeScope と同じロード手順）
        var relicDefinitions = AddressableLoader.LoadAll<RelicDefinition>("RelicData");
        relicDefinitions = RemoteBalance.ApplyList("relics", relicDefinitions, r => r.relicId);
        builder.RegisterInstance(relicDefinitions);
        builder.Register<RelicInventoryModel>(Lifetime.Singleton);
        builder.Register<RelicEffectResolver>(Lifetime.Singleton);

        // SceneTransitionService
        builder.Register<SceneTransitionService>(Lifetime.Singleton);

        // View
        if (eventSceneView != null)
        {
            builder.RegisterComponent(eventSceneView);
        }
        else
        {
            Debug.LogError("[EventLifetimeScope] EventSceneView が Inspector で未設定です！");
        }

        // Presenter（EntryPoint）
        builder.RegisterEntryPoint<EventScenePresenter>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // デバッグメニュー（F11で開閉、リリースビルドには含まれない）
        builder.RegisterComponentOnNewGameObject<DebugMenuView>(Lifetime.Singleton, "DebugMenu");
        builder.RegisterBuildCallback(container => container.Resolve<DebugMenuView>());
#endif

        Debug.Log("[EventLifetimeScope] Configured.");
    }
}


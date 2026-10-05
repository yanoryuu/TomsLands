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
        var battleInputData = AddressableLoader.Load<BattleInputData>("SceneData/BattleInputData")
                              ?? ScriptableObject.CreateInstance<BattleInputData>();
        var battleOutputData = AddressableLoader.Load<BattleOutputData>("SceneData/BattleOutputData")
                               ?? ScriptableObject.CreateInstance<BattleOutputData>();
        builder.RegisterInstance(battleInputData);
        builder.RegisterInstance(battleOutputData);

        // TomsModel（イベントコマンドの ChangeMoney / ChangeTrust 実行用）
        builder.Register<TomsModel>(Lifetime.Singleton);

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

        Debug.Log("[EventLifetimeScope] Configured.");
    }
}


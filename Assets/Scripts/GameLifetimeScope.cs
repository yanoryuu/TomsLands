using System.Linq;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public class GameLifetimeScope : LifetimeScope
{
    [Header("Views")]
    [SerializeField] private BlackSmithView blackSmithView;
    [SerializeField] private TomsShopView tomsShopView;
    [SerializeField] private ItemSelectionView itemSelectionView;
    [SerializeField] private InfoBrokerView infoBrokerView;
    [SerializeField] private HeroInfoView heroInfoView;
    [SerializeField] private CommonView commonView;
    [SerializeField] private DungeonInfoView dungeonInfoView;
    [SerializeField] private MapView mapView;
    [SerializeField] private MapInfoView mapInfoView;
    [SerializeField] private TurnEndSummaryView turnEndSummaryView;

    [Header("Other References")]
    [SerializeField] private GamePanelManager gamePanelManager;
    [SerializeField] private DungeonRepository dungeonRepository;

    protected override void Configure(IContainerBuilder builder)
    {
        // --- 1. Infrastructure / Data Setup ---
        // マスターデータのロードと登録
        var masterItems = Resources.LoadAll<ItemData>("ItemData").ToList();
        builder.RegisterInstance(masterItems); // List<ItemData> としてどこでも注入可能に

        // シーン間共有データ（ScriptableObject）のロードと登録
        var battleInputData = Resources.Load<BattleInputData>("SceneData/BattleInputData");
        if (battleInputData == null)
        {
            battleInputData = ScriptableObject.CreateInstance<BattleInputData>();
            Debug.LogWarning("[GameLifetimeScope] Resources/SceneData/BattleInputData.asset が見つからなかったため、実行時インスタンスを生成しました。Tools > Create Scene Data Assets を実行してください。");
        }

        var battleOutputData = Resources.Load<BattleOutputData>("SceneData/BattleOutputData");
        if (battleOutputData == null)
        {
            battleOutputData = ScriptableObject.CreateInstance<BattleOutputData>();
            Debug.LogWarning("[GameLifetimeScope] Resources/SceneData/BattleOutputData.asset が見つからなかったため、実行時インスタンスを生成しました。Tools > Create Scene Data Assets を実行してください。");
        }

        builder.RegisterInstance(battleInputData);
        builder.RegisterInstance(battleOutputData);

        // EventInputData / EventOutputData のロードと登録
        var eventInputData = Resources.Load<EventInputData>("SceneData/EventInputData");
        if (eventInputData == null)
        {
            eventInputData = ScriptableObject.CreateInstance<EventInputData>();
            Debug.LogWarning("[GameLifetimeScope] Resources/SceneData/EventInputData.asset が見つからなかったため、実行時インスタンスを生成しました。Tools > Create Scene Data Assets を実行してください。");
        }

        var eventOutputData = Resources.Load<EventOutputData>("SceneData/EventOutputData");
        if (eventOutputData == null)
        {
            eventOutputData = ScriptableObject.CreateInstance<EventOutputData>();
            Debug.LogWarning("[GameLifetimeScope] Resources/SceneData/EventOutputData.asset が見つからなかったため、実行時インスタンスを生成しました。Tools > Create Scene Data Assets を実行してください。");
        }

        builder.RegisterInstance(eventInputData);
        builder.RegisterInstance(eventOutputData);

        // ShopEconomySettings のロードと登録
        var shopEconomySettings = Resources.Load<ShopEconomySettings>("ShopEconomySettings");
        if (shopEconomySettings == null)
        {
            shopEconomySettings = ScriptableObject.CreateInstance<ShopEconomySettings>();
            Debug.LogWarning("[GameLifetimeScope] Resources/ShopEconomySettings.asset が見つかりません。デフォルト値で生成しました。Unity メニューから Create > ScriptableObjects > ShopEconomySettings で作成してください。");
        }
        builder.RegisterInstance(shopEconomySettings);

        // シーン間共有データ（StartModeData）のロードと登録
        var startModeData = Resources.Load<StartModeData>("SceneData/StartModeData");
        if (startModeData == null)
        {
            startModeData = ScriptableObject.CreateInstance<StartModeData>();
            Debug.LogWarning("[GameLifetimeScope] Resources/SceneData/StartModeData.asset が見つかりません。");
        }
        builder.RegisterInstance(startModeData);

        // リポジトリや外部アセットの登録
        builder.RegisterComponent(dungeonRepository);
        builder.RegisterComponent(gamePanelManager);

        // --- 2. Models (Singletons) ---
        // 状態を持つモデルは Singleton で登録し、複数のPresenterで共有する
        builder.Register<ItemModel>(Lifetime.Singleton);
        builder.Register<TomsModel>(Lifetime.Singleton);
        builder.Register<ItemSelectionModel>(Lifetime.Singleton);
        builder.Register<InfoBrokerModel>(Lifetime.Singleton);
        builder.Register<HeroModel>(Lifetime.Singleton);
        builder.Register<MapModel>(Lifetime.Singleton);
        builder.Register<BlackSmithModel>(Lifetime.Singleton);
        builder.Register<StateManager>(Lifetime.Singleton);

        // シーン遷移サービス
        builder.Register<SceneTransitionService>(Lifetime.Singleton);

        // --- 3. Views (Components) ---
        // Scene上にあるViewを登録（null の場合はエラーログを出力）
        RegisterComponentSafe(builder, blackSmithView, nameof(blackSmithView));
        RegisterComponentSafe(builder, tomsShopView, nameof(tomsShopView));
        RegisterComponentSafe(builder, itemSelectionView, nameof(itemSelectionView));
        RegisterComponentSafe(builder, infoBrokerView, nameof(infoBrokerView));
        RegisterComponentSafe(builder, heroInfoView, nameof(heroInfoView));
        RegisterComponentSafe(builder, commonView, nameof(commonView));
        RegisterComponentSafe(builder, dungeonInfoView, nameof(dungeonInfoView));
        RegisterComponentSafe(builder, mapView, nameof(mapView));
        RegisterComponentSafe(builder, mapInfoView, nameof(mapInfoView));
        RegisterComponentSafe(builder, turnEndSummaryView, nameof(turnEndSummaryView));

        // --- 4. Presenters (EntryPoints) ---
        // RegisterEntryPoint を使うと、インスタンス化 + IStartable等のライフサイクル実行を自動化
        builder.RegisterEntryPoint<BlackSmithPresenter>();
        builder.RegisterEntryPoint<ItemSelectionPresenter>().AsSelf();
        builder.RegisterEntryPoint<TurnEndSummaryPresenter>().AsSelf();
        builder.RegisterEntryPoint<TomsShopPresenter>();
        builder.RegisterEntryPoint<MapPresenter>();
        builder.RegisterEntryPoint<CommonPresenter>();
        builder.RegisterEntryPoint<InfoBrokerPresenter>();
        builder.RegisterEntryPoint<GameFlowManager>().AsSelf();

        // --- 5. System Logic (Save/Delete) ---
        // セーブ削除や保存ロジックを独立したクラスとして登録
        builder.RegisterEntryPoint<GameLifecycleHandler>();
        
        // 戦闘結果の処理ハンドラ（BattleScene から帰還時に自動実行）
        builder.RegisterEntryPoint<BattleResultHandler>();

        // イベント結果の処理ハンドラ（EventScene から帰還時に自動実行）
        builder.RegisterEntryPoint<EventResultHandler>();
        
        Debug.Log($"GameLifetimeScope configured.");
    }

    /// <summary>
    /// コンポーネントを安全に登録する。nullの場合はエラーログを出力する。
    /// </summary>
    private static void RegisterComponentSafe<T>(IContainerBuilder builder, T component, string fieldName) where T : class
    {
        if (component == null)
        {
            Debug.LogError($"[GameLifetimeScope] {fieldName} が Inspector で未設定です！");
            return;
        }
        builder.RegisterComponent(component);
    }
}
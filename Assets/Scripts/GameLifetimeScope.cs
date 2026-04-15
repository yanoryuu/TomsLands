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
    [SerializeField] private EventView eventView;
    [SerializeField] private DungeonLevelUpView dungeonLevelUpView;
    [SerializeField] private AdvertisementView advertisementView;

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

        // =====================================================
        // マーケティングシステムのデータ登録
        // =====================================================

        // GameBalanceData のロードと登録
        var gameBalanceData = Resources.Load<GameBalanceData>("Marketing/GameBalanceData");
        if (gameBalanceData == null)
        {
            gameBalanceData = ScriptableObject.CreateInstance<GameBalanceData>();
            Debug.LogWarning("[GameLifetimeScope] Resources/Marketing/GameBalanceData.asset が見つかりません。Tools > Marketing > Create Default Data を実行してください。");
        }
        builder.RegisterInstance(gameBalanceData);

        // 広告データのロードと登録（Resourcesフォルダから全件ロード）
        var advertisementDataList = Resources.LoadAll<AdvertisementData>("Marketing").ToList();
        if (advertisementDataList.Count == 0)
        {
            Debug.LogWarning("[GameLifetimeScope] Resources/Marketing/ に AdvertisementData が見つかりません。Tools > Marketing > Create Default Data を実行してください。");
        }
        builder.RegisterInstance(advertisementDataList);

        // フォロワーマイルストーンデータのロードと登録
        var milestoneDataList = Resources.LoadAll<FollowerMilestoneData>("Marketing").ToList();
        if (milestoneDataList.Count == 0)
        {
            Debug.LogWarning("[GameLifetimeScope] Resources/Marketing/ に FollowerMilestoneData が見つかりません。Tools > Marketing > Create Default Data を実行してください。");
        }
        builder.RegisterInstance(milestoneDataList);

        // バズ効果データのロードと登録（タイプ別に個別登録）
        var allBuzzEffects = Resources.LoadAll<BuzzEffectData>("Marketing");
        BuzzEffectData flameBuzzData = null;
        BuzzEffectData normalBuzzData = null;
        BuzzEffectData bigBuzzData = null;

        foreach (var buzz in allBuzzEffects)
        {
            switch (buzz.buzzType)
            {
                case BuzzType.Flame: flameBuzzData = buzz; break;
                case BuzzType.Normal: normalBuzzData = buzz; break;
                case BuzzType.Big: bigBuzzData = buzz; break;
            }
        }

        // null の場合はデフォルトインスタンスを生成
        if (flameBuzzData == null)
        {
            flameBuzzData = ScriptableObject.CreateInstance<BuzzEffectData>();
            flameBuzzData.buzzType = BuzzType.Flame;
            Debug.LogWarning("[GameLifetimeScope] 炎上バズデータが見つかりません。デフォルト値で生成しました。");
        }
        if (normalBuzzData == null)
        {
            normalBuzzData = ScriptableObject.CreateInstance<BuzzEffectData>();
            normalBuzzData.buzzType = BuzzType.Normal;
            Debug.LogWarning("[GameLifetimeScope] 通常バズデータが見つかりません。デフォルト値で生成しました。");
        }
        if (bigBuzzData == null)
        {
            bigBuzzData = ScriptableObject.CreateInstance<BuzzEffectData>();
            bigBuzzData.buzzType = BuzzType.Big;
            Debug.LogWarning("[GameLifetimeScope] 大バズデータが見つかりません。デフォルト値で生成しました。");
        }

        // BuzzEffectData は BuzzSystem の WithParameter で名前付き注入する
        // （同じ型が3つあるため、RegisterInstance では区別できない）

        // ItemVisualSettings のロードと登録
        var itemVisualSettings = Resources.Load<ItemVisualSettings>("ItemVisualSettings");
        if (itemVisualSettings == null)
        {
            itemVisualSettings = ScriptableObject.CreateInstance<ItemVisualSettings>();
            Debug.LogWarning("[GameLifetimeScope] Resources/ItemVisualSettings.asset が見つかりません。Create > Settings > Item Visual Settings で作成してください。");
        }
        builder.RegisterInstance(itemVisualSettings);

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

        // イベント関連
        builder.Register<PendingEventData>(Lifetime.Singleton);
        builder.Register<TomsEventExecutor>(Lifetime.Singleton);
        builder.Register<DarkShopManager>(Lifetime.Singleton);
        builder.Register<EventFragManager>(Lifetime.Singleton);

        // =====================================================
        // マーケティングシステム（Models / Systems）
        // =====================================================
        builder.Register<ShopStatusModel>(Lifetime.Singleton);
        builder.Register<FollowerSystem>(Lifetime.Singleton);
        builder.Register<AdvertisementSystem>(Lifetime.Singleton);
        builder.Register<BuzzSystem>(Lifetime.Singleton)
            .WithParameter("flameData", flameBuzzData)
            .WithParameter("normalBuzzData", normalBuzzData)
            .WithParameter("bigBuzzData", bigBuzzData);
        builder.Register<SalesCalculator>(Lifetime.Singleton);

        // シーン遷移サービス
        builder.Register<SceneTransitionService>(Lifetime.Singleton);

        // ポップアップ管理（Additive ロードされる PopupScene にデータを渡す中継役）
        builder.Register<PopUpManager>(Lifetime.Singleton);

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
        RegisterComponentSafe(builder, eventView, nameof(eventView));
        RegisterComponentSafe(builder, dungeonLevelUpView, nameof(dungeonLevelUpView));
        RegisterComponentSafe(builder, advertisementView, nameof(advertisementView));

        // --- 4. Presenters (EntryPoints) ---
        // RegisterEntryPoint を使うと、インスタンス化 + IStartable等のライフサイクル実行を自動化
        builder.RegisterEntryPoint<BlackSmithPresenter>();
        builder.RegisterEntryPoint<ItemSelectionPresenter>().AsSelf();
        builder.RegisterEntryPoint<TurnEndSummaryPresenter>().AsSelf();
        builder.RegisterEntryPoint<TomsShopPresenter>();
        builder.RegisterEntryPoint<MapPresenter>();
        builder.RegisterEntryPoint<DungeonLevelUpPresenter>();
        builder.RegisterEntryPoint<CommonPresenter>();
        builder.RegisterEntryPoint<InfoBrokerPresenter>();
        builder.RegisterEntryPoint<GameFlowManager>().AsSelf();

        // 広告購入画面
        builder.RegisterEntryPoint<AdvertisementPresenter>();

        // --- 5. System Logic (Save/Delete) ---
        // セーブ削除や保存ロジックを独立したクラスとして登録
        builder.RegisterEntryPoint<GameLifecycleHandler>();
        
        // 戦闘結果の処理ハンドラ（BattleScene から帰還時に自動実行）
        builder.RegisterEntryPoint<BattleResultHandler>();

        // イベント結果の処理ハンドラ（EventScene から帰還時に自動実行）
        builder.RegisterEntryPoint<EventResultHandler>();

        // マーケティングシステムのファサード（統合窓口）
        builder.RegisterEntryPoint<MarketingFacade>().AsSelf();
        
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
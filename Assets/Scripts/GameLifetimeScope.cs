using System.Linq;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public class GameLifetimeScope : LifetimeScope
{
    [Header("Views")]
    [SerializeField] private BlackSmithView blackSmithView;
    [SerializeField] private TomsShopView tomsShopView;
    [SerializeField] private HeroPanelView heroPanelView;
    [SerializeField] private ItemSelectionView itemSelectionView;
    [SerializeField] private InfoBrokerView infoBrokerView;
    [SerializeField] private CommonView commonView;
    [SerializeField] private DungeonInfoView dungeonInfoView;
    [SerializeField] private MapView mapView;
    [SerializeField] private MapInfoView mapInfoView;
    [SerializeField] private TurnEndSummaryView turnEndSummaryView;
    [SerializeField] private EventView eventView;
    [SerializeField] private DungeonLevelUpView dungeonLevelUpView;
    [SerializeField] private AdvertisementView advertisementView;
    [SerializeField] private ProphetView prophetView;
    [SerializeField] private TurnActionHintView turnActionHintView;
    [SerializeField] private DebtView debtView;
    [SerializeField] private TurnPhaseView turnPhaseView;
    [SerializeField] private SalesPhaseView salesPhaseView;
    [SerializeField] private ShopUpgradeView shopUpgradeView;
    [SerializeField] private ShopMachineView shopMachineView;

    [Header("Other References")]
    [SerializeField] private GamePanelManager gamePanelManager;
    [SerializeField] private DungeonRepository dungeonRepository;

    protected override void Configure(IContainerBuilder builder)
    {
        // --- 1. Infrastructure / Data Setup ---
        // マスターデータのロードと登録
        // スプレッドシート由来の上書きを適用してから登録（以降の master 参照すべてに反映される）
        var masterItems = ItemMaster.ApplyOverrides(AddressableLoader.LoadAll<ItemData>("ItemData"));
        builder.RegisterInstance(masterItems); // List<ItemData> としてどこでも注入可能に

        // シーン間共有データ（ScriptableObject）のロードと登録
        var battleInputData = AddressableLoader.Load<BattleInputData>("SceneData/BattleInputData");
        if (battleInputData == null)
        {
            battleInputData = ScriptableObject.CreateInstance<BattleInputData>();
            Debug.LogWarning("[GameLifetimeScope] Resources/SceneData/BattleInputData.asset が見つからなかったため、実行時インスタンスを生成しました。Tools > TomsLands > データ生成 > SceneDataアセット生成 を実行してください。");
        }

        var battleOutputData = AddressableLoader.Load<BattleOutputData>("SceneData/BattleOutputData");
        if (battleOutputData == null)
        {
            battleOutputData = ScriptableObject.CreateInstance<BattleOutputData>();
            Debug.LogWarning("[GameLifetimeScope] Resources/SceneData/BattleOutputData.asset が見つからなかったため、実行時インスタンスを生成しました。Tools > TomsLands > データ生成 > SceneDataアセット生成 を実行してください。");
        }

        builder.RegisterInstance(battleInputData);
        builder.RegisterInstance(battleOutputData);

        // EventInputData / EventOutputData のロードと登録
        var eventInputData = AddressableLoader.Load<EventInputData>("SceneData/EventInputData");
        if (eventInputData == null)
        {
            eventInputData = ScriptableObject.CreateInstance<EventInputData>();
            Debug.LogWarning("[GameLifetimeScope] Resources/SceneData/EventInputData.asset が見つからなかったため、実行時インスタンスを生成しました。Tools > TomsLands > データ生成 > SceneDataアセット生成 を実行してください。");
        }

        var eventOutputData = AddressableLoader.Load<EventOutputData>("SceneData/EventOutputData");
        if (eventOutputData == null)
        {
            eventOutputData = ScriptableObject.CreateInstance<EventOutputData>();
            Debug.LogWarning("[GameLifetimeScope] Resources/SceneData/EventOutputData.asset が見つからなかったため、実行時インスタンスを生成しました。Tools > TomsLands > データ生成 > SceneDataアセット生成 を実行してください。");
        }

        builder.RegisterInstance(eventInputData);
        builder.RegisterInstance(eventOutputData);

        // RunSetupData（準備シーン → 新規ラン初期化の受け渡し）のロードと登録
        var runSetupData = AddressableLoader.Load<RunSetupData>("SceneData/RunSetupData")
                           ?? RunSetupData.GetOrCreateFallback();
        builder.RegisterInstance(runSetupData);

        // ShopEconomySettings のロードと登録
        var shopEconomySettings = AddressableLoader.Load<ShopEconomySettings>("ShopEconomySettings");
        if (shopEconomySettings == null)
        {
            shopEconomySettings = ScriptableObject.CreateInstance<ShopEconomySettings>();
            Debug.LogWarning("[GameLifetimeScope] Resources/ShopEconomySettings.asset が見つかりません。デフォルト値で生成しました。Unity メニューから Create > ScriptableObjects > ShopEconomySettings で作成してください。");
        }
        shopEconomySettings = RemoteBalance.ApplyOverwrite("shopEconomy", shopEconomySettings);
        builder.RegisterInstance(shopEconomySettings);

        // ShopLevelSettings（店レベルテーブル）のロードと登録
        var shopLevelSettings = AddressableLoader.Load<ShopLevelSettings>("ShopLevelSettings");
        if (shopLevelSettings == null)
        {
            shopLevelSettings = ScriptableObject.CreateInstance<ShopLevelSettings>();
            Debug.LogWarning("[GameLifetimeScope] ShopLevelSettings.asset が見つかりません。デフォルト値で生成しました。Create > ScriptableObjects > ShopLevelSettings で作成し Addressables に登録してください。");
        }
        shopLevelSettings = RemoteBalance.ApplyOverwrite("shopLevel", shopLevelSettings);
        builder.RegisterInstance(shopLevelSettings);

        // =====================================================
        // 金融システム（取引所）のデータ登録
        // =====================================================

        var financeSettings = AddressableLoader.Load<FinanceSettings>("FinanceSettings");
        if (financeSettings == null)
        {
            financeSettings = ScriptableObject.CreateInstance<FinanceSettings>();
            Debug.LogWarning("[GameLifetimeScope] FinanceSettings.asset が見つかりません。デフォルト値で生成しました。Create > ScriptableObjects > Finance > FinanceSettings で作成し Addressables に登録してください。");
        }
        financeSettings = RemoteBalance.ApplyOverwrite("finance", financeSettings);
        builder.RegisterInstance(financeSettings);

        var financialProducts = AddressableLoader.LoadAll<FinancialProductData>("FinancialProductData");
        if (financialProducts.Count == 0)
        {
            Debug.LogWarning("[GameLifetimeScope] FinancialProductData が見つかりません。取引所には商品が並びません。Create > ScriptableObjects > Finance > FinancialProductData で作成し、ラベル FinancialProductData を付与してください。");
        }
        financialProducts = RemoteBalance.ApplyList("financialProducts", financialProducts, p => p.productId);
        builder.RegisterInstance(financialProducts);

        // レリック（装備アイテム）マスターのロードと登録
        var relicDefinitions = AddressableLoader.LoadAll<RelicDefinition>("RelicData");
        if (relicDefinitions.Count == 0)
        {
            Debug.LogWarning("[GameLifetimeScope] RelicDefinition が見つかりません。レリックは獲得できません。Create > ScriptableObjects > Relic > RelicDefinition で作成し、ラベル RelicData を付与してください。");
        }
        relicDefinitions = RemoteBalance.ApplyList("relics", relicDefinitions, r => r.relicId);
        builder.RegisterInstance(relicDefinitions);

        // マシン（店カスタマイズ）マスターのロードと登録
        var shopMachines = AddressableLoader.LoadAll<ShopMachineData>("ShopMachineData");
        if (shopMachines.Count == 0)
        {
            Debug.LogWarning("[GameLifetimeScope] ShopMachineData が見つかりません。マシンショップには商品が並びません。Create > ScriptableObjects > ShopMachine > ShopMachineData で作成し、ラベル ShopMachineData を付与してください。");
        }
        shopMachines = RemoteBalance.ApplyList("shopMachines", shopMachines, m => m.machineId);
        builder.RegisterInstance(shopMachines);

        // =====================================================
        // マーケティングシステムのデータ登録
        // =====================================================

        // GameBalanceData のロードと登録
        var gameBalanceData = AddressableLoader.Load<GameBalanceData>("Marketing/GameBalanceData");
        if (gameBalanceData == null)
        {
            gameBalanceData = ScriptableObject.CreateInstance<GameBalanceData>();
            Debug.LogWarning("[GameLifetimeScope] Resources/Marketing/GameBalanceData.asset が見つかりません。Tools > TomsLands > データ生成 > マーケティング初期データ生成（全部入り） を実行してください。");
        }
        gameBalanceData = RemoteBalance.ApplyOverwrite("gameBalance", gameBalanceData);
        builder.RegisterInstance(gameBalanceData);

        // 広告データのロードと登録（Resourcesフォルダから全件ロード）
        var advertisementDataList = AddressableLoader.LoadAll<AdvertisementData>("AdvertisementData");
        if (advertisementDataList.Count == 0)
        {
            Debug.LogWarning("[GameLifetimeScope] Resources/Marketing/ に AdvertisementData が見つかりません。Tools > TomsLands > データ生成 > マーケティング初期データ生成（全部入り） を実行してください。");
        }
        advertisementDataList = RemoteBalance.ApplyList("advertisements", advertisementDataList, a => a.advertisementName);
        builder.RegisterInstance(advertisementDataList);

        // フォロワーマイルストーンデータのロードと登録
        var milestoneDataList = AddressableLoader.LoadAll<FollowerMilestoneData>("FollowerMilestoneData");
        if (milestoneDataList.Count == 0)
        {
            Debug.LogWarning("[GameLifetimeScope] Resources/Marketing/ に FollowerMilestoneData が見つかりません。Tools > TomsLands > データ生成 > マーケティング初期データ生成（全部入り） を実行してください。");
        }
        milestoneDataList = RemoteBalance.ApplyList("followerMilestones", milestoneDataList, m => m.requiredFollowers.ToString());
        builder.RegisterInstance(milestoneDataList);

        // バズ効果データのロードと登録（タイプ別に個別登録）
        var allBuzzEffects = AddressableLoader.LoadAll<BuzzEffectData>("BuzzEffectData");
        allBuzzEffects = RemoteBalance.ApplyList("buzzEffects", allBuzzEffects, b => b.buzzType.ToString());
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
        var itemVisualSettings = AddressableLoader.Load<ItemVisualSettings>("ItemVisualSettings");
        if (itemVisualSettings == null)
        {
            itemVisualSettings = ScriptableObject.CreateInstance<ItemVisualSettings>();
            Debug.LogWarning("[GameLifetimeScope] Resources/ItemVisualSettings.asset が見つかりません。Create > Settings > Item Visual Settings で作成してください。");
        }
        builder.RegisterInstance(itemVisualSettings);

        // シーン間共有データ（StartModeData）のロードと登録
        var startModeData = AddressableLoader.Load<StartModeData>("SceneData/StartModeData");
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
        builder.Register<SellOrderModel>(Lifetime.Singleton);
        builder.Register<PortfolioModel>(Lifetime.Singleton);
        builder.Register<ShopMachineModel>(Lifetime.Singleton);
        builder.Register<MorningReportModel>(Lifetime.Singleton);
        builder.Register<RelicInventoryModel>(Lifetime.Singleton);
        builder.Register<RelicEffectResolver>(Lifetime.Singleton);
        builder.Register<RelicBehaviourRegistry>(Lifetime.Singleton);
        builder.Register<RelicHookDispatcher>(Lifetime.Singleton);
        builder.Register<RelicRewardService>(Lifetime.Singleton);
        builder.Register<ItemSelectionModel>(Lifetime.Singleton);
        builder.Register<InfoBrokerModel>(Lifetime.Singleton);
        builder.Register<HeroModel>(Lifetime.Singleton);
        builder.Register<MapModel>(Lifetime.Singleton);
        builder.Register<BlackSmithModel>(Lifetime.Singleton);
        builder.Register<StateManager>(Lifetime.Singleton);
        builder.Register<TurnPhaseManager>(Lifetime.Singleton);

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
        builder.Register<PopUpManager>(Lifetime.Singleton)
            .WithParameter(typeof(LifetimeScope), this);

        // アイテム市場分析ポップアップ管理（Additive ロードされる ItemPopupScene にデータを渡す中継役）
        builder.Register<ItemPopUpManager>(Lifetime.Singleton)
            .WithParameter(typeof(LifetimeScope), this);

        // --- 3. Views (Components) ---
        // Scene上にあるViewを登録（null の場合はエラーログを出力）
        RegisterComponentSafe(builder, blackSmithView, nameof(blackSmithView));
        RegisterComponentSafe(builder, tomsShopView, nameof(tomsShopView));
        if (heroPanelView != null)
            builder.RegisterComponent(heroPanelView);
        else
            Debug.LogWarning("[GameLifetimeScope] heroPanelView is not assigned. Hero panel is disabled until the TomsShopScene reference is set.");
        RegisterComponentSafe(builder, itemSelectionView, nameof(itemSelectionView));
        RegisterComponentSafe(builder, infoBrokerView, nameof(infoBrokerView));
        RegisterComponentSafe(builder, commonView, nameof(commonView));
        RegisterComponentSafe(builder, dungeonInfoView, nameof(dungeonInfoView));
        RegisterComponentSafe(builder, mapView, nameof(mapView));
        RegisterComponentSafe(builder, mapInfoView, nameof(mapInfoView));
        RegisterComponentSafe(builder, turnEndSummaryView, nameof(turnEndSummaryView));
        RegisterComponentSafe(builder, eventView, nameof(eventView));
        RegisterComponentSafe(builder, dungeonLevelUpView, nameof(dungeonLevelUpView));
        RegisterComponentSafe(builder, advertisementView, nameof(advertisementView));
        RegisterComponentSafe(builder, prophetView, nameof(prophetView));

        if (turnActionHintView != null)
            builder.RegisterComponent(turnActionHintView);
        else
            Debug.LogWarning("[GameLifetimeScope] turnActionHintView が未設定のためヒントチェックリストは無効です。");

        RegisterComponentSafe(builder, debtView, nameof(debtView));
        RegisterComponentSafe(builder, turnPhaseView, nameof(turnPhaseView));
        RegisterComponentSafe(builder, salesPhaseView, nameof(salesPhaseView));
        RegisterComponentSafe(builder, shopUpgradeView, nameof(shopUpgradeView));
        RegisterComponentSafe(builder, shopMachineView, nameof(shopMachineView));

        // --- 4. Presenters (EntryPoints) ---
        // RegisterEntryPoint を使うと、インスタンス化 + IStartable等のライフサイクル実行を自動化
        builder.RegisterEntryPoint<BlackSmithPresenter>();
        builder.RegisterEntryPoint<ItemSelectionPresenter>().AsSelf();
        builder.RegisterEntryPoint<TurnEndSummaryPresenter>().AsSelf();
        builder.RegisterEntryPoint<TomsShopPresenter>();
        if (heroPanelView != null)
            builder.RegisterEntryPoint<HeroPanelPresenter>();
        builder.RegisterEntryPoint<MapPresenter>();
        builder.RegisterEntryPoint<DungeonLevelUpPresenter>();
        builder.RegisterEntryPoint<CommonPresenter>();
        builder.RegisterEntryPoint<InfoBrokerPresenter>();
        builder.RegisterEntryPoint<GameFlowManager>().AsSelf();
        builder.RegisterEntryPoint<TurnPhasePresenter>();

        // 広告購入画面
        builder.RegisterEntryPoint<AdvertisementPresenter>();

        // 預言者画面
        builder.RegisterEntryPoint<ProphetPresenter>();
        // shopUpgradeView が未配線の間は Presenter を登録しない（DI解決エラー防止）
        if (shopUpgradeView != null)
            builder.RegisterEntryPoint<ShopUpgradePresenter>();
        if (shopMachineView != null)
            builder.RegisterEntryPoint<ShopMachinePresenter>();

        // 借金返済画面（TomsShopPresenter から直接呼び出すため AsSelf で公開）
        builder.RegisterEntryPoint<DebtPresenter>().AsSelf();

        // ターン行動ヒント（InspectorでturnActionHintViewを設定した場合のみ有効）
        if (turnActionHintView != null)
            builder.RegisterEntryPoint<TurnActionHintPresenter>();

        // --- 5. System Logic (Save/Delete) ---
        // セーブ削除や保存ロジックを独立したクラスとして登録
        builder.RegisterEntryPoint<GameLifecycleHandler>();
        
        // 戦闘結果の処理ハンドラ（BattleScene から帰還時に自動実行）
        builder.RegisterEntryPoint<BattleResultHandler>();

        // イベント結果の処理ハンドラ（EventScene から帰還時に自動実行）
        builder.RegisterEntryPoint<EventResultHandler>();

        // マーケティングシステムのファサード（統合窓口）
        builder.RegisterEntryPoint<MarketingFacade>().AsSelf();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // --- 6. デバッグメニュー（F12で開閉、リリースビルドには含まれない） ---
        builder.RegisterComponentOnNewGameObject<DebugMenuView>(Lifetime.Singleton, "DebugMenu");
        builder.RegisterBuildCallback(container => container.Resolve<DebugMenuView>());
#endif

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

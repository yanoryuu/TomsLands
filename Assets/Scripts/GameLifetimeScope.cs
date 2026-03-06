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
    [SerializeField] private StreamingView streamingView;
    [SerializeField] private StreamingSettingView streamingSettingView;
    [SerializeField] private InfoBrokerView infoBrokerView;
    [SerializeField] private HeroInfoView heroInfoView;
    [SerializeField] private CommonView commonView;
    [SerializeField] private TitleView titleView;
    [SerializeField] private DungeonInfoView dungeonInfoView;
    [SerializeField] private MapView mapView;
    [SerializeField] private MapInfoView mapInfoView;

    [Header("Other References")]
    [SerializeField] private BattleSequencer battleSequencer;
    [SerializeField] private GamePanelManager gamePanelManager;
    [SerializeField] private DungeonRepository dungeonRepository;

    protected override void Configure(IContainerBuilder builder)
    {
        // --- 1. Infrastructure / Data Setup ---
        // マスターデータのロードと登録
        var masterItems = Resources.LoadAll<ItemData>("ItemData").ToList();
        builder.RegisterInstance(masterItems); // List<ItemData> としてどこでも注入可能に

        // リポジトリや外部アセットの登録
        builder.RegisterComponent(dungeonRepository);
        builder.RegisterComponent(battleSequencer);
        builder.RegisterComponent(gamePanelManager);

        // --- 2. Models (Singletons) ---
        // 状態を持つモデルは Singleton で登録し、複数のPresenterで共有する
        builder.Register<ItemModel>(Lifetime.Singleton);
        builder.Register<TomsModel>(Lifetime.Singleton);
        builder.Register<ItemSelectionModel>(Lifetime.Singleton);
        builder.Register<StreamingItemModel>(Lifetime.Singleton);
        builder.Register<StreamingSettingModel>(Lifetime.Singleton);
        builder.Register<InfoBrokerModel>(Lifetime.Singleton);
        builder.Register<HeroModel>(Lifetime.Singleton);
        builder.Register<MapModel>(Lifetime.Singleton);
        builder.Register<BlackSmithModel>(Lifetime.Singleton);
        builder.Register<StateManager>(Lifetime.Singleton);

        // --- 3. Views (Components) ---
        // Scene上にあるViewを登録
        builder.RegisterComponent(blackSmithView);
        builder.RegisterComponent(tomsShopView);
        builder.RegisterComponent(itemSelectionView);
        builder.RegisterComponent(streamingView);
        builder.RegisterComponent(streamingSettingView);
        builder.RegisterComponent(infoBrokerView);
        builder.RegisterComponent(heroInfoView);
        builder.RegisterComponent(commonView);
        builder.RegisterComponent(titleView);
        builder.RegisterComponent(dungeonInfoView);
        builder.RegisterComponent(mapView);
        builder.RegisterComponent(mapInfoView);

        // --- 4. Presenters (EntryPoints) ---
        // RegisterEntryPoint を使うと、インスタンス化 + IStartable等のライフサイクル実行を自動化
        builder.RegisterEntryPoint<BlackSmithPresenter>();
        builder.RegisterEntryPoint<ItemSelectionPresenter>().AsSelf();
        builder.RegisterEntryPoint<TomsShopPresenter>();
        builder.RegisterEntryPoint<MapPresenter>();
        builder.RegisterEntryPoint<StreamingItemPresenter>();
        builder.RegisterEntryPoint<StreamingSettingPresenter>();
        builder.RegisterEntryPoint<TitlePresenter>();
        builder.RegisterEntryPoint<CommonPresenter>();
        builder.RegisterEntryPoint<InfoBrokerPresenter>();
        builder.RegisterEntryPoint<GameFlowManager>();

        // --- 5. System Logic (Save/Delete) ---
        // セーブ削除や保存ロジックを独立したクラスとして登録
        builder.RegisterEntryPoint<GameLifecycleHandler>();
        
        Debug.Log($"GameLifetimeScope configured.");
    }
}
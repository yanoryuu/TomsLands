using System;
using R3;
using UnityEngine;
using VContainer.Unity;

public class MapPresenter : IPresenter, IStartable,IDisposable
{
    private CompositeDisposable disposable;
    private MapModel mapModel;
    private MapView mapView;
    private DungeonRepository dungeonRepository;
    private DungeonInfoView dungeonInfoView;
    private StateManager stateManager;
    private HeroModel heroModel;
    private ItemModel itemModel;

    public MapPresenter(MapModel mapModel, MapView mapView,DungeonRepository dungeonRepository,DungeonInfoView dungeonInfoView,StateManager stateManager, HeroModel heroModel, ItemModel itemModel)
    {
        disposable = new CompositeDisposable();

        this.mapModel = mapModel;
        this.mapView = mapView;
        this.dungeonRepository = dungeonRepository;
        this.dungeonInfoView = dungeonInfoView;
        this.stateManager = stateManager;
        this.heroModel = heroModel;
        this.itemModel = itemModel;

        stateManager.RegisterOnEnter(TomsShopGamePhase.Map,Entry);
    }

    public void Start()
    {
        Bind();
    }
    public void Dispose()
    {
        disposable.Dispose();
    }

    public void Entry()
    {
        mapView.SetDungeonStatus(dungeonRepository.availableDungeons);
    }

    private void Bind()
    {
        //閉じるボタンが押されたとき
        dungeonInfoView.OnCloseRequested.Subscribe(_ =>
        {
            dungeonInfoView.HideDungeonInfo();
        }).AddTo(disposable);

        mapView.OnMapIcon.Subscribe(key =>
        {
            var data = dungeonRepository.GetById(key);
            if (data == null) return;

            // 勇者の現在ステータスでのクリア確率（現在のダンジョンレベル基準）
            float clearProbability = ClearProbabilityCalculator.Calculate(
                heroModel.heroData, itemModel, data, data.currentDungeonLevel);

            dungeonInfoView.ShowDungeonInfo(data, clearProbability);
        }).AddTo(disposable);

        mapView.OnBackRequested.Subscribe(_ =>
        {
            stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop);
        }).AddTo(disposable);

    }
}
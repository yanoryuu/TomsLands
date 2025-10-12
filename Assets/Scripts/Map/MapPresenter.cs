using R3;
using UnityEngine;

public class MapPresenter : IPresenter
{
    private CompositeDisposable disposable;
    private MapModel mapModel;
    private MapView mapView;
    private DungeonRepository dungeonRepository;
    private DungeonInfoView dungeonInfoView;
    private StateManager stateManager;
    
    public MapPresenter(MapModel mapModel, MapView mapView,DungeonRepository dungeonRepository,DungeonInfoView dungeonInfoView,StateManager stateManager)
    {
        disposable = new CompositeDisposable();
        
        this.mapModel = mapModel;
        this.mapView = mapView;
        this.dungeonRepository = dungeonRepository;
        this.dungeonInfoView = dungeonInfoView;
        this.stateManager = stateManager;
        
        Bind();
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
            
            dungeonInfoView.ShowDungeonInfo(data);
        }).AddTo(disposable);

        mapView.OnBackRequested.Subscribe(_ =>
        {
            stateManager.ChangePhase(GamePhase.TomsShop);
        }).AddTo(disposable);

    }
}
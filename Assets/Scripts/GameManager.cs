using System.IO;
using R3;
using System.Linq;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BlackSmithView blackSmithView;
    [SerializeField] private PreparationView preparationView;
    [SerializeField] private StreamingView streamingView;
    [SerializeField] private EndPhaseView endPhaseView;
    [SerializeField] private TomsShopView tomsShopView;
    [SerializeField] private ItemSelectionView itemSelectionView;
    [SerializeField] private StreamingSettingView streamingSettingView;
    // [SerializeField] private InfoBrokerView infoBrokerView;
    [SerializeField] private CommonView commonView;
    [SerializeField] private BattleSequencer battleSequencer;
    [SerializeField] private TitleView titleView;
    [SerializeField] private GamePanelManager gamePanelManager;
    [SerializeField] private DungeonRepository dungeonRepository;
    [SerializeField] private DungeonInfoView dungeonInfoView;
    [SerializeField] private MapView mapView;
 
    private ItemModel itemModel;
    private TomsModel tomsModel;
    private BlackSmithPresenter blackSmithPresenter;
    private BlackSmithModel blackSmithModel;
    private StateManager stateManager;
    private TomsShopPresenter tomsShopPresenter;
    private ItemSelectionPresenter itemSelectionPresenter;
    private ItemSelectionModel itemSelectionModel;
    private StreamingItemModel streamingItemModel;
    private StreamingItemPresenter streamingItemPresenter;
    private StreamingSettingModel streamingSettingModel;
    private StreamingSettingPresenter streamingSettingPresenter;
    // private InfoBrokerPresenter infoBrokerPresenter;
    // private InfoBrokerModel infoBrokerModel;
    private HeroModel heroModel;
    private TitlePresenter titlePresenter;
    private DungeonCatalog dungeonCatalog;
    private MapPresenter mapPresenter;
    private MapModel mapModel;
    private CommonPresenter commonPresenter;

    private void Awake()
    {
        // ゲーム開始時にセーブファイルを削除
        DeleteAllSaveFiles();

        // マスターItemDataロード
        var masterItems = Resources.LoadAll<ItemData>("ItemData").ToList();
        itemModel = new ItemModel(masterItems);

        dungeonCatalog = dungeonRepository.CreateCatalog();
        dungeonRepository.SetCatalog(dungeonCatalog);

        //Model
        
        // TomsModel初期化
        tomsModel = new TomsModel();
        
        itemSelectionModel = new ItemSelectionModel();
        
        streamingSettingModel = new StreamingSettingModel();
        
        streamingItemModel = new StreamingItemModel();
        
        heroModel = new HeroModel();
        
        mapModel = new MapModel();
        
        blackSmithModel = new BlackSmithModel();
        
        
        stateManager = new StateManager(
            gamePanelManager
        );

        //Prenseter

        blackSmithPresenter = new BlackSmithPresenter(tomsModel, itemModel, blackSmithView, stateManager,blackSmithModel);;


        itemSelectionPresenter =
            new ItemSelectionPresenter(itemSelectionModel, itemSelectionView, itemModel, tomsModel);

        tomsShopPresenter = new TomsShopPresenter(
            tomsShopView,
            itemSelectionPresenter,
            itemModel,
            tomsModel,
            commonView,
            stateManager
        );

        mapPresenter = new MapPresenter(mapModel, mapView, dungeonRepository, dungeonInfoView, stateManager);
        
        streamingItemPresenter = new StreamingItemPresenter(streamingItemModel, streamingView ,itemModel,streamingSettingModel,tomsModel,battleSequencer);

        streamingSettingPresenter =
            new StreamingSettingPresenter(streamingSettingModel, streamingSettingView, itemModel);
        
        titlePresenter = new TitlePresenter(titleView, stateManager);
        
        commonPresenter = new CommonPresenter(commonView, tomsModel);
    }

    private void OnDestroy()
    {
        stateManager?.Dispose();
        tomsShopPresenter?.Dispose();
    }

    private void OnApplicationQuit()
    {
        itemModel.SaveData();
        tomsModel.SavePlayerMoney();
    }
    
    private void DeleteAllSaveFiles()
    {
        string dir = Application.persistentDataPath;
        string[] files = {
            "itemData.json",
            "tomsData.json",
            "displayItemData.json",
            "heroData.json",
        };

        foreach (var filename in files)
        {
            string path = Path.Combine(dir, filename);
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"Deleted save file: {filename}");
            }
        }
    }
}

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
    [SerializeField] private CommonView commonView;
    [SerializeField] private BattleCharacter battleCharacter;
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private TitleView titleView;
    [SerializeField] private GamePanelManager gamePanelManager;
    [SerializeField] private DungeonRepository dungeonRepository;
    [SerializeField] private DungeonInfoView dungeonInfoView;
    [SerializeField] private MapView mapView;
 
    private ItemModel itemModel;
    private TomsShopModel tomsShopModel;
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
        itemModel.LoadData();

        dungeonCatalog = dungeonRepository.CreateCatalog();
        dungeonRepository.SetCatalog(dungeonCatalog);

        //Model
        
        // TomsShopModel初期化
        tomsShopModel = new TomsShopModel();
        tomsShopModel.Initialize();
        tomsShopModel.LoadPlayerMoney();
        
        
        itemSelectionModel = new ItemSelectionModel();
        
        streamingSettingModel = new StreamingSettingModel();
        
        streamingItemModel = new StreamingItemModel();
        
        heroModel = new HeroModel();
        heroModel.LoadHeroData();
        
        mapModel = new MapModel();
        
        stateManager = new StateManager(
            streamingItemPresenter,
            blackSmithView,
            preparationView,
            streamingView,
            endPhaseView,
            tomsShopView,
            battleManager,
            titleView,
            gamePanelManager
        );

        //Prenseter

        blackSmithPresenter = new BlackSmithPresenter(tomsShopModel, itemModel, blackSmithView, stateManager,blackSmithModel);;


        itemSelectionPresenter =
            new ItemSelectionPresenter(itemSelectionModel, itemSelectionView, itemModel, tomsShopModel);

        tomsShopPresenter = new TomsShopPresenter(
            tomsShopView,
            itemSelectionPresenter,
            itemModel,
            tomsShopModel,
            commonView,
            stateManager
        );

        mapPresenter = new MapPresenter(mapModel, mapView, dungeonRepository, dungeonInfoView, stateManager);

        //battleCharacterに勇者のデータを注入
        battleCharacter.HeroData = heroModel.heroData;
        
        streamingItemPresenter = new StreamingItemPresenter(streamingItemModel, streamingView ,itemModel,streamingSettingModel,tomsShopModel,battleManager);

        streamingSettingPresenter =
            new StreamingSettingPresenter(streamingSettingModel, streamingSettingView, itemModel);
        
        titlePresenter = new TitlePresenter(titleView, stateManager);
        
        commonPresenter = new CommonPresenter(commonView, tomsShopModel);
    }

    private void OnDestroy()
    {
        stateManager?.Dispose();
        tomsShopPresenter?.Dispose();
    }

    private void OnApplicationQuit()
    {
        itemModel.SaveData();
        tomsShopModel.SavePlayerMoney();
    }
    
    private void DeleteAllSaveFiles()
    {
        string dir = Application.persistentDataPath;
        string[] files = {
            "itemData.json",
            "tomsShopData.json",
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

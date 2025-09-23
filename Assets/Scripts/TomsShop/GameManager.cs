using System.IO;
using R3;
using System.Linq;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ItemShopView itemShopView;
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

    private ItemModel itemModel;
    private TomsShopModel tomsShopModel;
    private ItemPresenter itemPresenter;
    private ItemShopPresenter itemShopPresenter;
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

    private void Awake()
    {
        // ゲーム開始時にセーブファイルを削除
        DeleteAllSaveFiles();

        // マスターItemDataロード
        var masterItems = Resources.LoadAll<ItemData>("ItemData").ToList();
        itemModel = new ItemModel(masterItems);
        itemModel.LoadData();

        dungeonCatalog = new DungeonCatalog();
        dungeonRepository.SetCatalog(dungeonCatalog);

        // TomsShopModel初期化
        tomsShopModel = new TomsShopModel();
        tomsShopModel.Initialize();
        tomsShopModel.LoadPlayerMoney();
        
        
        itemSelectionModel = new ItemSelectionModel();
        
        streamingSettingModel = new StreamingSettingModel();
        
        streamingItemModel = new StreamingItemModel();
        
        heroModel = new HeroModel();
        heroModel.LoadHeroData();

        // Presenter初期化
        itemPresenter = new ItemPresenter(itemModel, itemShopView, tomsShopView, tomsShopModel);
        itemPresenter.BindItemSelectionView(itemSelectionView);

        itemShopPresenter = new ItemShopPresenter(tomsShopModel, itemModel, itemShopView);

        stateManager = new StateManager(
            itemPresenter,
            streamingItemPresenter,
            itemShopView,
            preparationView,
            streamingView,
            endPhaseView,
            tomsShopView,
            battleManager,
            titleView,
            gamePanelManager
        );

        itemSelectionPresenter = new ItemSelectionPresenter(itemSelectionModel, itemSelectionView, itemModel);

        tomsShopPresenter = new TomsShopPresenter(
            tomsShopView,
            itemShopView,
            itemSelectionView,
            itemModel,
            tomsShopModel,
            commonView
        );

        //battleCharacterに勇者のデータを注入
        battleCharacter.HeroData = heroModel.heroData;
        
        streamingItemPresenter = new StreamingItemPresenter(streamingItemModel, streamingView ,itemModel,streamingSettingModel,tomsShopModel,battleManager);

        streamingSettingPresenter =
            new StreamingSettingPresenter(streamingSettingModel, streamingSettingView, itemModel);
        
        titlePresenter = new TitlePresenter(titleView, stateManager);
    }

    private void OnDestroy()
    {
        stateManager?.Dispose();
        tomsShopPresenter?.Dispose();
        itemPresenter?.Dispose();
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

using System.IO;
using R3;
using System.Linq;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public ReactiveProperty<GamePhase> CurrentPhase { get; private set; }

    [Header("References")]
    [SerializeField] private ItemShopView itemShopView;
    [SerializeField] private PreparationView preparationView;
    [SerializeField] private StreamingView streamingView;
    [SerializeField] private EndPhaseView endPhaseView;
    [SerializeField] private TomsShopView tomsShopView;
    [SerializeField] private ItemSelectionView itemSelectionView;
    [SerializeField] private StreamingSettingView streamingSettingView;

    private ItemModel itemModel;
    private TomsShopModel tomsShopModel;
    private ItemPresenter itemPresenter;
    private ItemShopPresenter itemShopPresenter;
    private GamePhasePresenter gamePhasePresenter;
    private TomsShopPresenter tomsShopPresenter;
    private ItemSelectionPresenter itemSelectionPresenter;
    private ItemSelectionModel itemSelectionModel;
    private StreamingItemModel streamingItemModel;
    private StreamingItemPresenter streamingItemPresenter;
    private StreamingSettingModel streamingSettingModel;
    private StreamingSettingPresenter streamingSettingPresenter;

    private void Awake()
    {
         // ゲーム開始時にセーブファイルを削除
         DeleteAllSaveFiles();
        
        // ReactiveProperty初期化
        CurrentPhase = new ReactiveProperty<GamePhase>();

        // マスターItemDataロード
        var masterItems = Resources.LoadAll<ItemData>("ItemData").ToList();
        itemModel = new ItemModel(masterItems);
        itemModel.LoadData();

        streamingItemModel = new StreamingItemModel(itemModel);

        // TomsShopModel初期化
        tomsShopModel = new TomsShopModel();
        tomsShopModel.Initialize();
        tomsShopModel.LoadPlayerMoney();
        
        itemSelectionModel = new ItemSelectionModel();
        
        streamingSettingModel = new StreamingSettingModel();

        // Presenter初期化
        itemPresenter = new ItemPresenter(itemModel, itemShopView, tomsShopView, tomsShopModel);
        itemPresenter.BindItemSelectionView(itemSelectionView);

        itemShopPresenter = new ItemShopPresenter(tomsShopModel, itemModel, itemShopView);

        gamePhasePresenter = new GamePhasePresenter(
            this,
            itemPresenter,
            itemShopView,
            preparationView,
            streamingView,
            endPhaseView,
            tomsShopView
        );

        itemSelectionPresenter = new ItemSelectionPresenter(itemSelectionModel, itemSelectionView, itemModel);

        tomsShopPresenter = new TomsShopPresenter(
            tomsShopView,
            itemShopView,
            itemSelectionView,
            itemModel,
            tomsShopModel
        );

        streamingItemPresenter = new StreamingItemPresenter(streamingItemModel, streamingView);

        streamingSettingPresenter =
            new StreamingSettingPresenter(streamingSettingModel, streamingSettingView, itemModel ,gamePhasePresenter);

        // 初期フェーズ
        CurrentPhase.Value = GamePhase.TomsShop;
    }

    private void OnDestroy()
    {
        gamePhasePresenter?.Dispose();
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
            "displayItemData.json"
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

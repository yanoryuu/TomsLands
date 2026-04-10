using System;
using System.IO;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// ゲーム初期化と保存・後始末を担当するクラス。
/// StartModeDataに基づき「初めから」と「続きから」で初期化処理を分岐する。
/// </summary>
public class GameLifecycleHandler : IStartable, IDisposable
{
    private readonly ItemModel _itemModel;
    private readonly TomsModel _tomsModel;
    private readonly HeroModel _heroModel;
    private readonly DungeonRepository _dungeonRepository;
    private readonly StartModeData _startModeData;
    private readonly GameFlowManager _gameFlowManager;
    private readonly BattleOutputData _battleOutputData;
    private readonly EventOutputData _eventOutputData;

    // コンストラクタ（依存関係はVContainerが注入）
    public GameLifecycleHandler(
        ItemModel itemModel,
        TomsModel tomsModel,
        HeroModel heroModel,
        DungeonRepository dungeonRepository,
        StartModeData startModeData,
        GameFlowManager gameFlowManager,
        BattleOutputData battleOutputData,
        EventOutputData eventOutputData)
    {
        _itemModel = itemModel;
        _tomsModel = tomsModel;
        _heroModel = heroModel;
        _dungeonRepository = dungeonRepository;
        _startModeData = startModeData;
        _gameFlowManager = gameFlowManager;
        _battleOutputData = battleOutputData;
        _eventOutputData = eventOutputData;
    }

    public void Start()
    {
        // ダンジョンカタログの初期化（共通）
        var dungeonCatalog = _dungeonRepository.CreateCatalog();
        _dungeonRepository.SetCatalog(dungeonCatalog);

        // FightScene / EventScene から帰還した場合は、
        // StartModeData に関わらず必ずセーブデータをロードする
        bool isReturningFromScene = _battleOutputData.HasResult || _eventOutputData.HasResult;

        if (isReturningFromScene)
        {
            Debug.Log("[GameLifecycleHandler] 戦闘/イベントシーンから帰還 → セーブデータをロード");
            InitializeContinue();
        }
        else if (_startModeData.Mode == StartMode.NewGame)
        {
            InitializeNewGame();
        }
        else
        {
            InitializeContinue();
        }

        // 以降のシーン再読み込み（FightScene→TomsShop等）で
        // NewGame 扱いにならないよう Continue に切り替える
        _startModeData.SetContinue();

        Debug.Log($"[GameLifecycleHandler] Initialized (Mode={_startModeData.Mode}, returningFromScene={isReturningFromScene})");
    }

    /// <summary>
    /// 「初めから」の初期化処理。
    /// セーブデータを削除し、マスターデータからゲームを新規開始する。
    /// </summary>
    private void InitializeNewGame()
    {
        // 既存セーブデータを削除
        DeleteAllSaveFiles();

        // マスターデータからアイテムを初期化
        _itemModel.InitializeRuntimeItemsFromMaster();

        // TomsModelを初期値で初期化
        _tomsModel.Initialize();

        // GameFlowManagerのインデックスを先頭に設定
        _gameFlowManager.RestoreIndex(0);

        Debug.Log("[GameLifecycleHandler] 初めから: マスターデータで新規ゲーム開始");
    }

    /// <summary>
    /// 「続きから」の初期化処理。
    /// セーブデータをロードし、前回の状態を復元する。
    /// </summary>
    private void InitializeContinue()
    {
        // 各モデルのロード処理（コンストラクタで既にロード済みだが、明示的に再ロード）
        _itemModel.LoadData();
        _tomsModel.LoadPlayerMoney();
        _heroModel.LoadHeroData();

        // GameFlowManagerのインデックスを復元（セーブデータから読み込んだ値を使用）
        _gameFlowManager.RestoreIndex(_tomsModel.GameFlowIndex);

        Debug.Log($"[GameLifecycleHandler] 続きから: セーブデータをロードして復帰 (FlowIndex={_tomsModel.GameFlowIndex})");
    }

    public void Dispose()
    {
        // GameFlowManagerの現在インデックスをTomsModelに反映してから保存
        _tomsModel.GameFlowIndex = _gameFlowManager.CurrentIndex;

        // アプリ終了時・シーン破棄時に呼ばれる保存処理
        _itemModel.SaveData();
        _tomsModel.SavePlayerMoney();
        _heroModel.SaveHeroData();

        Debug.Log($"Game Data Saved & Disposed (FlowIndex={_gameFlowManager.CurrentIndex})");
    }

    // ファイル削除ロジック
    private void DeleteAllSaveFiles()
    {
        string dir = Application.persistentDataPath;
        string[] files = {
            "itemData.json",
            "tomsData.json",
            "displayItemData.json",
            "heroData.json",
            "streamingSelection.json",
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
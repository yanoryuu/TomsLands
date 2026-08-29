using System;
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
    private readonly ShopStatusModel _shopStatusModel;
    private readonly StateManager _stateManager;
    private readonly SellOrderModel _sellOrderModel;
    private readonly PortfolioModel _portfolioModel;
    private readonly ShopMachineModel _shopMachineModel;
    private readonly RelicInventoryModel _relicInventory;
    private readonly RunSetupData _runSetupData;

    // コンストラクタ（依存関係はVContainerが注入）
    public GameLifecycleHandler(
        ItemModel itemModel,
        TomsModel tomsModel,
        HeroModel heroModel,
        DungeonRepository dungeonRepository,
        StartModeData startModeData,
        GameFlowManager gameFlowManager,
        BattleOutputData battleOutputData,
        EventOutputData eventOutputData,
        ShopStatusModel shopStatusModel,
        StateManager stateManager,
        SellOrderModel sellOrderModel,
        PortfolioModel portfolioModel,
        ShopMachineModel shopMachineModel,
        RelicInventoryModel relicInventory,
        RunSetupData runSetupData)
    {
        _itemModel = itemModel;
        _tomsModel = tomsModel;
        _heroModel = heroModel;
        _dungeonRepository = dungeonRepository;
        _startModeData = startModeData;
        _gameFlowManager = gameFlowManager;
        _battleOutputData = battleOutputData;
        _eventOutputData = eventOutputData;
        _shopStatusModel = shopStatusModel;
        _stateManager = stateManager;
        _sellOrderModel = sellOrderModel;
        _portfolioModel = portfolioModel;
        _shopMachineModel = shopMachineModel;
        _relicInventory = relicInventory;
        _runSetupData = runSetupData;
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

        // --- Shopフェーズ進入を発火する ---
        // StateManager はコンストラクタで初期フェーズを ForceNotify するが、その時点では
        // TomsShopPresenter の RegisterOnEnter(Shop, Entry) がまだ登録されていないため
        // Entry() が呼ばれない。全Presenterの登録とフロー構築が済んだここで再発火し、
        // Entry() → BeginTurnPhases()（ターン進行フェーズの開始）を確実に走らせる。
        _stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop);

        Debug.Log($"[GameLifecycleHandler] Initialized (Mode={_startModeData.Mode}, returningFromScene={isReturningFromScene})");
    }

    /// <summary>
    /// 「初めから」の初期化処理。
    /// セーブデータを削除し、マスターデータからゲームを新規開始する。
    /// </summary>
    private void InitializeNewGame()
    {
        // 既存のラン内セーブデータを削除（削除対象一覧は RunSaveCleaner に一元化）
        RunSaveCleaner.DeleteRunFiles();

        // ダンジョン進行をSO初期状態に戻す
        // （DungeonRepository.Awake が旧セーブを先にロードしているため、メモリ側のリセットが必須）
        _dungeonRepository.ResetToInitial();

        // マスターデータからアイテムを初期化
        _itemModel.InitializeRuntimeItemsFromMaster();

        // TomsModelを初期値で初期化
        _tomsModel.Initialize();

        _heroModel.InitializeRuntimeHeroFromMaster();
        _heroModel.SaveHeroData();

        // マーケティングステータスをリセット
        _shopStatusModel.Reset();

        // 売り注文・金融資産・マシン設置・レリックをリセット
        _sellOrderModel.Clear();
        _portfolioModel.Clear();
        _shopMachineModel.Clear();
        _relicInventory.Clear();

        // 準備シーンの設定（借入・持ち込み・スターターレリック・スタートダッシュ）を適用
        ApplyRunSetup();

        // --- フロー選択（自動/手動）とシードを確定して保存 ---
        bool useAuto = _startModeData.UseAutoGeneration;
        GameModeId mode = _startModeData.SelectedMode;
        int seed = GameConst.FlowGeneration.randomSeed != 0
            ? GameConst.FlowGeneration.randomSeed
            : unchecked((int)System.DateTime.Now.Ticks);

        _tomsModel.UseAutoFlow = useAuto;
        _tomsModel.GameMode = mode;
        _tomsModel.FlowSeed = seed;

        // フローを構築してからインデックスを先頭へ
        _gameFlowManager.InitializeFlow(useAuto, mode, seed);
        _gameFlowManager.RestoreIndex(0);

        // seed/mode を含めて永続化（最初の戦闘前にセーブを確定させる）
        _tomsModel.SavePlayerMoney();

        Debug.Log($"[GameLifecycleHandler] 初めから: 新規開始 (useAuto={useAuto}, mode={mode}, seed={seed})");
    }

    /// <summary>
    /// 準備シーンで確定した設定を新規ランへ適用する（消費型: 最後に必ず Clear する）。
    /// </summary>
    private void ApplyRunSetup()
    {
        if (_runSetupData == null || !_runSetupData.HasSetup) return;

        var settings = GameConst.Preparation;

        // 借入: 初期資金に加算し、初回返済に利息付きで上乗せされる（DebtCalculator 参照）
        if (_runSetupData.BorrowedAmount > 0)
        {
            _tomsModel.AddRevenue(_runSetupData.BorrowedAmount);
            _tomsModel.BorrowedPrincipal = _runSetupData.BorrowedAmount;
            Debug.Log($"[RunSetup] 借入 +{_runSetupData.BorrowedAmount}G（初回返済に利息{settings.borrowInterestRate:P0}付きで上乗せ）");
        }

        // 持ち込みアイテム: 初期在庫に加算
        for (int i = 0; i < _runSetupData.CarryItemIds.Count && i < _runSetupData.CarryItemCounts.Count; i++)
        {
            var runtime = _itemModel.GetRuntimeItem(_runSetupData.CarryItemIds[i]);
            int count = _runSetupData.CarryItemCounts[i];
            if (runtime == null || count <= 0) continue;
            runtime.UpdateStock(runtime.Stock.Value + count);
            Debug.Log($"[RunSetup] 持ち込み: {runtime.ItemName} ×{count}");
        }

        // スターターレリック
        if (!string.IsNullOrEmpty(_runSetupData.StarterRelicId))
        {
            _relicInventory.Add(_runSetupData.StarterRelicId, 1, GameConst.RelicMaxEquipSlots);
        }

        // スタートダッシュ: 宣伝ビラ（注目度・フォロワーの初期加算）
        if (_runSetupData.UseFlyer)
        {
            _shopStatusModel.ChangeAttention(settings.flyerAttention);
            _shopStatusModel.ChangeFollowers(settings.flyerFollowers);
            _shopStatusModel.SaveData();
            Debug.Log($"[RunSetup] 宣伝ビラ: 注目+{settings.flyerAttention}, フォロワー+{settings.flyerFollowers}");
        }

        // スタートダッシュ: 目利きの手引き（全アイテムの初期需要を上振れ）
        if (_runSetupData.UseAppraisal)
        {
            foreach (var runtime in _itemModel.RuntimeItems)
                runtime.UpdateDemand(runtime.Demand.Value + settings.appraisalDemandBoost);
            Debug.Log($"[RunSetup] 目利きの手引き: 全アイテム需要 +{settings.appraisalDemandBoost:P0}");
        }

        // スタートダッシュ: 返済猶予証（初回返済額の割引。DebtCalculator が参照）
        if (_runSetupData.UseGrace)
        {
            _tomsModel.FirstDebtDiscountRate = settings.graceDiscountRate;
            Debug.Log($"[RunSetup] 返済猶予証: 初回返済 -{settings.graceDiscountRate:P0}");
        }

        _itemModel.SaveData();

        // 一度使ったら無効化（別スロットへの漏れ防止。StartModeData.SetContinue と同じパターン）
        _runSetupData.Clear();
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
        _sellOrderModel.LoadData();
        _portfolioModel.LoadData();
        _shopMachineModel.LoadData();
        _relicInventory.LoadData();

        // 保存済みの seed / mode から同一フローを再生成してからインデックス復元
        _gameFlowManager.InitializeFlow(_tomsModel.UseAutoFlow, _tomsModel.GameMode, _tomsModel.FlowSeed);
        _gameFlowManager.RestoreIndex(_tomsModel.GameFlowIndex);

        Debug.Log($"[GameLifecycleHandler] 続きから: 復帰 (FlowIndex={_tomsModel.GameFlowIndex}, useAuto={_tomsModel.UseAutoFlow}, mode={_tomsModel.GameMode}, seed={_tomsModel.FlowSeed})");
    }

    public void Dispose()
    {
        // GameFlowManagerの現在インデックスをTomsModelに反映してから保存
        _tomsModel.GameFlowIndex = _gameFlowManager.CurrentIndex;

        // アプリ終了時・シーン破棄時に呼ばれる保存処理
        _itemModel.SaveData();
        _tomsModel.SavePlayerMoney();
        _heroModel.SaveHeroData();
        _sellOrderModel.SaveData();
        _portfolioModel.SaveData();
        _shopMachineModel.SaveData();
        _relicInventory.SaveData();

        Debug.Log($"Game Data Saved & Disposed (FlowIndex={_gameFlowManager.CurrentIndex})");
    }
}

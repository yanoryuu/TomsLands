using System;
using R3;
using UnityEngine;
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
    private readonly PendingEventData _pendingEventData;
    private readonly CompositeDisposable _disposables = new();

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
        RunSetupData runSetupData,
        PendingEventData pendingEventData)
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
        _pendingEventData = pendingEventData;
    }

    public void Start()
    {
        // ダンジョンカタログの初期化（共通）
        var dungeonCatalog = _dungeonRepository.CreateCatalog();
        _dungeonRepository.SetCatalog(dungeonCatalog);

        // 新規ランの判定。NewGame はタイトルの「初めから」と準備シーンの出店でだけ立つ
        // （TomsShop 入場後は下で Continue に戻すので、シーン帰還時に NewGame のことはない）。
        // 「続きから」でもラン内セーブが無ければ復帰する中身が無いので新規扱いにする
        // （空セーブのまま続きからに入ると、手動フロー・準備設定なしでランが進んでしまう）
        bool hasRunSave = SaveSlotManager.Exists(SaveSlotManager.CurrentSlot);
        bool isNewRun = _startModeData.Mode == StartMode.NewGame || !hasRunSave;
        if (_startModeData.Mode != StartMode.NewGame && !hasRunSave)
            Debug.LogWarning("[GameLifecycleHandler] 続きからだがラン内セーブが無い → 新規ランとして開始");

        // FightScene / EventScene から帰還した場合は、必ずセーブデータをロードする
        bool isReturningFromScene = !isNewRun && (_battleOutputData.HasResult || _eventOutputData.HasResult);

        if (isNewRun)
        {
            // 前のランの配信/イベント結果が SO に残っていたら捨てる
            // （残すと BattleResultHandler が前ランの売上・フロー位置を新しいランに適用してしまう）
            if (_battleOutputData.HasResult || _eventOutputData.HasResult)
                Debug.LogWarning("[GameLifecycleHandler] 前のランの配信/イベント結果が残っていたので破棄");
            _battleOutputData.Clear();
            _eventOutputData.Clear();
            InitializeNewGame();
        }
        else if (isReturningFromScene)
        {
            Debug.Log("[GameLifecycleHandler] 戦闘/イベントシーンから帰還 → セーブデータをロード");
            InitializeContinue();
        }
        else
        {
            InitializeContinue();
        }

        // 以降のシーン再読み込み（FightScene→TomsShop等）で
        // NewGame 扱いにならないよう Continue に切り替える
        _startModeData.SetContinue();

        // 表示待ちイベントが確認ボタンで消えたら、保存してある分（tomsData）も消して保存する
        // （残すと続きからで同じイベントがもう一度起きる）
        _pendingEventData.OnCleared
            .Subscribe(_ =>
            {
                _tomsModel.PendingEventId = string.Empty;
                _tomsModel.PendingEventIndex = 0;
                _tomsModel.SavePlayerMoney();
            })
            .AddTo(_disposables);

        // 「続きから」で前回が配信中・配信前の寄り道中・リザルトの前に終わっていたら、店を開かずにその場面へ戻す。
        // 配信から結果を持って帰ってきたとき（isReturningFromScene）は BattleResultHandler が処理するので対象外
        if (!isNewRun && !isReturningFromScene && _gameFlowManager.TryResumeInterruptedScene())
        {
            Debug.Log($"[GameLifecycleHandler] Initialized → 中断した場面へ復帰 (FlowIndex={_gameFlowManager.CurrentIndex})");
            return;
        }

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
            : unchecked((int)DateTime.Now.Ticks);

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

        // 持ち込み資金: 村資金から持ってきたGをそのまま初期資金に加算する（借入ではないので返済への上乗せはない）
        if (_runSetupData.CarriedGold > 0)
        {
            _tomsModel.AddRevenue(_runSetupData.CarriedGold);
            _tomsModel.BorrowedPrincipal = 0;
            Debug.Log($"[RunSetup] 持ち込み資金 +{_runSetupData.CarriedGold}G（村資金から）");
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

        RestorePendingEvent();

        Debug.Log($"[GameLifecycleHandler] 続きから: 復帰 (FlowIndex={_tomsModel.GameFlowIndex}, useAuto={_tomsModel.UseAutoFlow}, mode={_tomsModel.GameMode}, seed={_tomsModel.FlowSeed})");
    }

    /// <summary>
    /// 保存してある表示待ちイベントを PendingEventData に戻す（店の Entry がポップアップを出す）。
    /// PendingEventData はシーンの Singleton なので、配信から帰る・続きから再開するたびに空から作り直される。
    /// </summary>
    private void RestorePendingEvent()
    {
        if (string.IsNullOrEmpty(_tomsModel.PendingEventId) || _pendingEventData.HasPendingEvent) return;

        var tomsEvent = EventDataLoader.FindById(_tomsModel.PendingEventId);
        if (tomsEvent == null)
        {
            Debug.LogWarning($"[GameLifecycleHandler] 保存されていた表示待ちイベントが見つからないので破棄: {_tomsModel.PendingEventId}");
            _tomsModel.PendingEventId = string.Empty;
            _tomsModel.PendingEventIndex = 0;
            return;
        }
        _pendingEventData.Set(tomsEvent, _tomsModel.PendingEventIndex);
        Debug.Log($"[GameLifecycleHandler] 表示待ちイベントを復元: {tomsEvent.id} {tomsEvent.title}");
    }

    public void Dispose()
    {
        _disposables.Dispose();

        // GameFlowManagerの現在インデックスをTomsModelに反映してから保存
        _tomsModel.GameFlowIndex = _gameFlowManager.CurrentIndex;
        // 表示待ちイベントも今の状態に合わせる（確認前に終了したら残し、確認済みなら消す）
        _tomsModel.PendingEventId = _pendingEventData.HasPendingEvent ? _pendingEventData.PendingEvent.id : string.Empty;
        _tomsModel.PendingEventIndex = _pendingEventData.HasPendingEvent ? _pendingEventData.GameFlowIndex : 0;

        // アプリ終了時・シーン破棄時に呼ばれる保存処理。
        // 1つが例外で失敗しても残りは必ず保存する（SaveFile は書き込み失敗を投げないが、直列化などの例外に備える）
        TrySave(nameof(ItemModel), _itemModel.SaveData);
        TrySave(nameof(TomsModel), _tomsModel.SavePlayerMoney);
        TrySave(nameof(HeroModel), _heroModel.SaveHeroData);
        TrySave(nameof(SellOrderModel), _sellOrderModel.SaveData);
        TrySave(nameof(PortfolioModel), _portfolioModel.SaveData);
        TrySave(nameof(ShopMachineModel), _shopMachineModel.SaveData);
        TrySave(nameof(RelicInventoryModel), _relicInventory.SaveData);

        Debug.Log($"Game Data Saved & Disposed (FlowIndex={_gameFlowManager.CurrentIndex})");
    }

    private static void TrySave(string name, Action save)
    {
        try
        {
            save();
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameLifecycleHandler] {name} の保存に失敗しました（残りの保存は続けます）");
            Debug.LogException(e);
        }
    }
}

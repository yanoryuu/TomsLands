using Cysharp.Threading.Tasks;
using R3;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// FightScene の EntryPoint。
/// StreamingSetting → Battle → Result → TomsShop の順にフェーズを制御する。
/// パネルの表示/非表示は BattlePanelManager が一元管理する。
/// </summary>
public class BattleSceneStarter : IAsyncStartable
{
    private readonly BattleSequencer _battleSequencer;
    private readonly BattleInputData _inputData;
    private readonly BattleOutputData _outputData;
    private readonly SceneTransitionService _sceneTransition;
    private readonly IDungeonCatalog _dungeonCatalog;
    private readonly StreamingSalesController _salesController;
    private readonly ItemModel _itemModel;
    private readonly TomsModel _tomsModel;
    private readonly StreamingSettingPresenter _settingPresenter;
    private readonly BattleResultView _resultView;
    private readonly BattlePanelManager _panelManager;
    private readonly BattleControlView _controlView;

    // ポーズ・在庫切れ管理
    private readonly BattlePauseController _pauseController = new();
    private bool _isRestockPopupShowing = false;
    private int _battleSpending = 0; // バトル中の補充購入で使った金額
    // バトル中に補充した数量（itemId → 合計数）。売却数の集計（BuildSoldItems）で使用
    private readonly Dictionary<string, int> _restockedQuantities = new();
    // 補充時の単価（itemId → 単価）。売れ残り分の返金計算で使用
    private readonly Dictionary<string, int> _restockUnitCosts = new();
    private UniTaskCompletionSource<(BattleResult result, string weaponId, string armorId)> _battleTcs;

    public BattleSceneStarter(
        BattleSequencer battleSequencer,
        BattleInputData inputData,
        BattleOutputData outputData,
        SceneTransitionService sceneTransition,
        IDungeonCatalog dungeonCatalog,
        StreamingSalesController salesController,
        ItemModel itemModel,
        TomsModel tomsModel,
        StreamingSettingPresenter settingPresenter,
        BattleResultView resultView,
        BattlePanelManager panelManager,
        BattleControlView controlView)
    {
        _battleSequencer = battleSequencer;
        _inputData = inputData;
        _outputData = outputData;
        _sceneTransition = sceneTransition;
        _dungeonCatalog = dungeonCatalog;
        _salesController = salesController;
        _itemModel = itemModel;
        _tomsModel = tomsModel;
        _settingPresenter = settingPresenter;
        _resultView = resultView;
        _panelManager = panelManager;
        _controlView = controlView;
    }

    public async UniTask StartAsync(CancellationToken cancellation)
    {
        Debug.Log("[BattleSceneStarter] FightScene started. Beginning phase flow...");

        if (_battleSequencer == null)
        {
            Debug.LogError("[BattleSceneStarter] BattleSequencer is null!");
            return;
        }

        if (_inputData == null)
        {
            Debug.LogError("[BattleSceneStarter] BattleInputData is null!");
            return;
        }

        // --- Phase 1: StreamingSetting（品出し設定） ---
        Debug.Log("[BattleSceneStarter] Phase 1: StreamingSetting");
        _panelManager?.ShowPanel(StreamingGamePhase.StreamingSetting);
        var selectedItems = await _settingPresenter.RunAsync();

        // 選択結果を BattleInputData に書き込み
        var battleItems = new List<BattleInputItem>();
        foreach (var kv in selectedItems)
        {
            var runtime = _itemModel.GetRuntimeItem(kv.Key);
            battleItems.Add(new BattleInputItem
            {
                ItemId = kv.Key,
                Quantity = kv.Value,
                Price = runtime != null ? runtime.CurrentPrice.Value : 0
            });
        }
        _inputData.SelectedItems = new List<BattleInputItem>(battleItems);
        Debug.Log($"[BattleSceneStarter] StreamingSetting confirmed. {battleItems.Count} items selected.");

        // --- Phase 2: Battle（配信中・戦闘） ---
        Debug.Log("[BattleSceneStarter] Phase 2: Battle");
        _panelManager?.ShowPanel(StreamingGamePhase.Streaming);

        // 配信開始前の3秒カウントダウン（戦闘・販売ループはこの後に始まる）
        if (_controlView != null)
            await _controlView.PlayCountdownAsync(3, cancellation);

        var targetDungeon = ResolveTargetDungeon();
        if (targetDungeon == null)
        {
            Debug.LogError($"[BattleSceneStarter] Dungeon not found: {_inputData.DungeonKey}");
            return;
        }

        _battleSequencer.SetDungeon(targetDungeon, _inputData.DungeonLevel);

        // 勇者モデルを構築
        var heroModel = new HeroModel();
        heroModel.ApplyEquippedItems(_inputData.EquippedItemIds);
        Debug.Log($"[BattleSceneStarter] Hero equipped items: {_inputData.EquippedItemIds.Count}");

        // ポーズコントローラーを各コンポーネントへ配布
        _battleSequencer.SetPauseController(_pauseController);
        _salesController?.SetPauseController(_pauseController);
        _salesController?.SetHeroTactics(heroModel.heroData?.tactics.Value ?? HeroTactics.Balanced);

        // StreamingSalesController に選択アイテムを渡して初期化
        if (_salesController != null)
        {
            _salesController.Setup(_itemModel, _inputData.SelectedItems);
            Debug.Log($"[BattleSceneStarter] StreamingSalesController initialized with {_inputData.SelectedItems.Count} items.");
        }

        // バトル終了を待つための UniTaskCompletionSource
        _battleTcs = new UniTaskCompletionSource<(BattleResult result, string weaponId, string armorId)>();
        int defeatedMobCount = 0;
        int defeatedBossCount = 0;

        // バトル終了時のCancellationTokenSource（ポップアップ等を終わらせるため）
        using var battleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);

        var disposables = new CompositeDisposable();

        _battleSequencer.OnBattleWin
            .Subscribe(win => _battleTcs.TrySetResult((BattleResult.Victory, win.weaponId, win.armorId)))
            .AddTo(disposables);

        _battleSequencer.OnBattleDefeat
            .Subscribe(defeat => _battleTcs.TrySetResult((BattleResult.Defeat, defeat.weaponId, defeat.armorId)))
            .AddTo(disposables);

        _battleSequencer.OnEnemyDefeated
            .Subscribe(enemy =>
            {
                if (enemy.IsBoss)
                    defeatedBossCount++;
                else
                    defeatedMobCount++;
            })
            .AddTo(disposables);

        // 一時停止ボタン
        if (_controlView != null)
        {
            _controlView.OnPauseToggled
                .Subscribe(_ => TogglePause())
                .AddTo(disposables);

            // 配信終了ボタン
            _controlView.OnEndBattleRequested
                .Subscribe(_ => ForceEndBattle())
                .AddTo(disposables);
        }

        // 在庫切れ検知
        if (_salesController != null)
        {
            _salesController.OnItemStockDepleted
                .Subscribe(item => TryShowRestockAsync(item, battleCts.Token).Forget())
                .AddTo(disposables);
        }

        // 在庫切れスロットをクリックして補充ポップアップを再表示
        void OnDepletedItemClicked(ItemSlotView slot)
        {
            if (slot?.CurrentItem == null || slot.CurrentItem.Stock.Value > 0) return;
            TryShowRestockAsync(slot.CurrentItem, battleCts.Token).Forget();
        }
        ItemSlotView.OnItemClicked += OnDepletedItemClicked;

        _battleSequencer.StartBattle(heroModel);

        // バトル終了待ち
        var battleResult = await _battleTcs.Task;
        Debug.Log($"[BattleSceneStarter] Battle finished: {battleResult.result}");

        // バトル終了 → ポーズ解除してからループキャンセル（WaitIfPausedAsync が抜けられるように）
        _pauseController.Resume();
        battleCts.Cancel();
        ItemSlotView.OnItemClicked -= OnDepletedItemClicked;
        disposables.Dispose();

        // 販売ループを停止してから結果を集計する。
        // 停止しないとリザルト画面中も売れ続け、ここで取るスナップショットとの差分
        // （売上金・売却数）が精算されず消失する。
        _salesController?.StopSales();

        // BattleOutputData に結果を書き込み
        var soldItems = BuildSoldItems();
        int rawSales = _salesController != null ? _salesController.GetTotalSalesValue() : 0;
        int restockRefund = CalculateUnsoldRestockRefund(soldItems); // 補充分の売れ残りは返金
        int defeatReward = CalculateDefeatReward(battleResult.result); // 勇者敗北時（ダンジョン防衛成功）の報酬
        int totalEarnings = rawSales - _battleSpending + restockRefund + defeatReward;
        if (restockRefund > 0)
            Debug.Log($"[BattleSceneStarter] 補充分の売れ残りを返金: {restockRefund}G");
        _outputData.SetResult(battleResult.result, battleResult.weaponId, battleResult.armorId, soldItems, totalEarnings, defeatedMobCount, defeatedBossCount);

        // --- Phase 3: Result（配信リザルト画面） ---
        Debug.Log("[BattleSceneStarter] Phase 3: Result");
        _panelManager?.ShowPanel(StreamingGamePhase.StreamingResult);
        if (_resultView != null)
        {
            await _resultView.ShowResultAsync(battleResult.result, soldItems, totalEarnings);
        }
        else
        {
            Debug.LogWarning("[BattleSceneStarter] BattleResultView is null. Skipping result screen.");
        }

        // --- Phase 4: TomsShop に戻る ---
        Debug.Log("[BattleSceneStarter] Returning to TomsShop...");
        _sceneTransition.ReturnToTomsShop();
    }

    // =====================================================
    // ポーズ制御
    // =====================================================

    private void TogglePause()
    {
        _pauseController.Toggle();
        _controlView?.UpdatePauseButtonText(_pauseController.IsPaused);
        Debug.Log($"[BattleSceneStarter] {(_pauseController.IsPaused ? "一時停止" : "再開")}");
    }

    // =====================================================
    // 配信強制終了
    // =====================================================

    private void ForceEndBattle()
    {
        // ポーズ解除してから終了（ループが WaitIfPausedAsync で詰まらないように）
        _pauseController.Resume();
        string weaponId = _inputData.EquippedItemIds.Count > 0 ? _inputData.EquippedItemIds[0] : "";
        string armorId  = _inputData.EquippedItemIds.Count > 1 ? _inputData.EquippedItemIds[1] : "";
        _battleTcs?.TrySetResult((BattleResult.Defeat, weaponId, armorId));
        Debug.Log("[BattleSceneStarter] 配信を強制終了しました。");
    }

    // =====================================================
    // 在庫切れポップアップ
    // =====================================================

    private async UniTaskVoid TryShowRestockAsync(RuntimeItemData item, CancellationToken token)
    {
        if (_isRestockPopupShowing || _controlView == null) return;
        _isRestockPopupShowing = true;

        // ポップアップ中は一時停止（既にポーズ中でなければ）
        bool wasPaused = _pauseController.IsPaused;
        if (!wasPaused)
        {
            _pauseController.Pause();
            _controlView.UpdatePauseButtonText(true);
        }

        // 途中で例外が出てもフラグとポーズが固まらないようにする
        // （固まると以後の在庫切れでポップアップが二度と出なくなる）
        try
        {
            // コスト計算（マスターデータの基本価格）
            var master = _itemModel.GetMasterItem(item.ItemId);
            int unitCost = master != null ? master.basePrice : item.CurrentPrice.Value;
            unitCost = Mathf.Max(1, unitCost);

            // バトル中獲得金額も含めた利用可能残高から購入可能な最大数を算出
            // （UpdateStock が MaxStock で黙ってクランプするため、在庫の空き枠も上限に含める。
            //   超過分を請求すると精算が狂う）
            int battleEarnings = _salesController != null ? _salesController.GetTotalSalesValue() : 0;
            int availableForPurchase = (_tomsModel != null ? _tomsModel.PlayerMoney.Value : 0) + battleEarnings - _battleSpending;
            int stockRoom = Mathf.Max(0, item.MaxStock.Value - item.Stock.Value);
            int maxQuantity = Mathf.Clamp(Mathf.Min(availableForPurchase / unitCost, stockRoom), 0, 99);

            // 数量選択ポップアップ（鍛冶屋と同じ購入UI。0 = キャンセル）
            float recommendScore = _itemModel.GetRecommendScore(item, null);
            int quantity = await _controlView.ShowRestockQuantityPopupAsync(item, unitCost, maxQuantity, recommendScore, token);

            if (quantity > 0)
            {
                int totalCost = unitCost * quantity;
                item.UpdateStock(item.Stock.Value + quantity);
                _battleSpending += totalCost; // バトル中売上から充当（PlayerMoneyは戦闘終了時に精算）
                _restockedQuantities[item.ItemId] = (_restockedQuantities.TryGetValue(item.ItemId, out var prev) ? prev : 0) + quantity;
                _restockUnitCosts[item.ItemId] = unitCost; // 単価はマスター基準価格で固定
                Debug.Log($"[BattleSceneStarter] 在庫補充: {item.ItemName} +{quantity}個, 費用 {totalCost}G (利用可能残高: {availableForPurchase}G → {availableForPurchase - totalCost}G)");
            }
        }
        finally
        {
            // ポーズ状態を元に戻す
            if (!wasPaused)
            {
                _pauseController.Resume();
                _controlView.UpdatePauseButtonText(false);
            }

            _isRestockPopupShowing = false;
        }
    }

    // =====================================================
    // ユーティリティ
    // =====================================================

    private DungeonInfoScriptableObj ResolveTargetDungeon()
    {
        var fromCatalog = _dungeonCatalog?.GetDungeon(_inputData.DungeonKey);
        if (fromCatalog != null)
        {
            Debug.Log($"[BattleSceneStarter] Dungeon resolved from catalog: {_inputData.DungeonKey}");
            return fromCatalog;
        }

        if (_battleSequencer.CurrentDungeon != null && _battleSequencer.CurrentDungeon.key == _inputData.DungeonKey)
        {
            Debug.LogWarning($"[BattleSceneStarter] Dungeon {_inputData.DungeonKey} was missing from catalog, using BattleSequencer fallback.");
            return _battleSequencer.CurrentDungeon;
        }

        Debug.LogWarning($"[BattleSceneStarter] Catalog lookup failed for dungeon key: {_inputData.DungeonKey}");
        return null;
    }

    /// <summary>
    /// 勇者敗北時（＝ダンジョン防衛成功時）の報酬を計算する。
    /// 現在のダンジョンレベルの rewardGold（魔王軍側からの報酬）を受け取る。勝利時は 0。
    /// </summary>
    private int CalculateDefeatReward(BattleResult result)
    {
        if (result != BattleResult.Defeat) return 0;

        var dungeon = _dungeonCatalog?.GetDungeon(_inputData.DungeonKey);
        int reward = dungeon?.GetLevelData(_inputData.DungeonLevel)?.rewardGold ?? 0;

        // レリック補正（魔王ビルド: 防衛報酬アップ）。配信遷移時に GameFlowManager がセットする
        reward = Mathf.RoundToInt(reward * RelicBattleEffects.DefeatRewardMul);

        if (reward > 0)
            Debug.Log($"[BattleSceneStarter] 勇者敗北！ダンジョン防衛報酬: {reward}G ({_inputData.DungeonKey} Lv.{_inputData.DungeonLevel}, mul={RelicBattleEffects.DefeatRewardMul:F2})");
        return reward;
    }

    /// <summary>
    /// バトル中に補充したが売れ残った分の返金額を計算する。
    /// 補充分は前払い（_battleSpending）で購入済みのため、売れ残り数 × 補充単価 を戻す。
    /// </summary>
    private int CalculateUnsoldRestockRefund(List<BattleOutputSoldItem> soldItems)
    {
        int refund = 0;
        foreach (var sold in soldItems)
        {
            if (!_restockedQuantities.TryGetValue(sold.ItemId, out var restocked) || restocked <= 0) continue;

            // 補充分から売れた数 = 総売却数 - 持ち込み分から売れた数
            int soldFromRestock = Mathf.Max(0, sold.SoldQuantity - sold.SoldFromStock);
            int unsoldRestock = Mathf.Max(0, restocked - soldFromRestock);
            if (unsoldRestock <= 0) continue;

            int unitCost = _restockUnitCosts.TryGetValue(sold.ItemId, out var c) ? c : 0;
            refund += unsoldRestock * unitCost;
        }
        return refund;
    }

    /// <summary>
    /// BattleInputData の SelectedItems から BattleOutputSoldItem リストを構築する。
    /// 実際に売れた数 = (持ち込み数 + バトル中の補充数) - バトル終了時の残在庫。
    /// SoldFromStock はショップ在庫の減算用（持ち込み数が上限。補充分は店在庫に存在しないため含めない）。
    /// </summary>
    private List<BattleOutputSoldItem> BuildSoldItems()
    {
        var soldItems = new List<BattleOutputSoldItem>();
        foreach (var item in _inputData.SelectedItems)
        {
            var runtime = _itemModel.GetRuntimeItem(item.ItemId);
            int remainingStock = runtime != null ? runtime.Stock.Value : 0;
            int restocked = _restockedQuantities.TryGetValue(item.ItemId, out var r) ? r : 0;
            int totalSold = Mathf.Max(0, item.Quantity + restocked - remainingStock);
            soldItems.Add(new BattleOutputSoldItem
            {
                ItemId = item.ItemId,
                SoldQuantity = totalSold,
                SoldFromStock = Mathf.Min(totalSold, item.Quantity),
                SoldPrice = item.Price
            });
        }
        return soldItems;
    }
}

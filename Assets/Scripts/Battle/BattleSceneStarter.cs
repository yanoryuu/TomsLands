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

        _battleSequencer.StartBattle(heroModel);

        // バトル終了待ち
        var battleResult = await _battleTcs.Task;
        Debug.Log($"[BattleSceneStarter] Battle finished: {battleResult.result}");

        // バトル終了 → ポーズ解除してからループキャンセル（WaitIfPausedAsync が抜けられるように）
        _pauseController.Resume();
        battleCts.Cancel();
        disposables.Dispose();

        // BattleOutputData に結果を書き込み
        var soldItems = BuildSoldItems();
        int totalEarnings = _salesController != null ? _salesController.GetTotalSalesValue() : 0;
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

        // コスト計算（マスターデータの基本価格 × 10）
        var master = _itemModel.GetMasterItem(item.ItemId);
        int unitCost   = master != null ? master.basePrice : item.CurrentPrice.Value;
        int totalCost  = unitCost * 10;
        bool canAfford = _tomsModel != null && _tomsModel.PlayerMoney.Value >= totalCost;

        bool confirmed = await _controlView.ShowRestockPopupAsync(item.ItemName, totalCost, canAfford, token);

        if (confirmed && canAfford && _tomsModel != null)
        {
            item.UpdateStock(item.Stock.Value + 10);
            _tomsModel.PlayerMoney.Value -= totalCost;
            _tomsModel.SavePlayerMoney();
            Debug.Log($"[BattleSceneStarter] 在庫補充: {item.ItemName} +10個, 費用 {totalCost}G");
        }

        // ポーズ状態を元に戻す
        if (!wasPaused)
        {
            _pauseController.Resume();
            _controlView.UpdatePauseButtonText(false);
        }

        _isRestockPopupShowing = false;
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
    /// BattleInputData の SelectedItems から BattleOutputSoldItem リストを構築する。
    /// 実際に売れた数 = 持ち込み数 - バトル終了時の残在庫。
    /// </summary>
    private List<BattleOutputSoldItem> BuildSoldItems()
    {
        var soldItems = new List<BattleOutputSoldItem>();
        foreach (var item in _inputData.SelectedItems)
        {
            var runtime = _itemModel.GetRuntimeItem(item.ItemId);
            int remainingStock = runtime != null ? runtime.Stock.Value : 0;
            int actualSold = Mathf.Max(0, item.Quantity - remainingStock);
            soldItems.Add(new BattleOutputSoldItem
            {
                ItemId = item.ItemId,
                SoldQuantity = actualSold,
                SoldPrice = item.Price
            });
        }
        return soldItems;
    }
}

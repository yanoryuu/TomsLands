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
    private readonly StreamingSettingPresenter _settingPresenter;
    private readonly BattleResultView _resultView;

    public BattleSceneStarter(
        BattleSequencer battleSequencer,
        BattleInputData inputData,
        BattleOutputData outputData,
        SceneTransitionService sceneTransition,
        IDungeonCatalog dungeonCatalog,
        StreamingSalesController salesController,
        ItemModel itemModel,
        StreamingSettingPresenter settingPresenter,
        BattleResultView resultView)
    {
        _battleSequencer = battleSequencer;
        _inputData = inputData;
        _outputData = outputData;
        _sceneTransition = sceneTransition;
        _dungeonCatalog = dungeonCatalog;
        _salesController = salesController;
        _itemModel = itemModel;
        _settingPresenter = settingPresenter;
        _resultView = resultView;
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
        var targetDungeon = ResolveTargetDungeon();
        if (targetDungeon == null)
        {
            Debug.LogError($"[BattleSceneStarter] Dungeon not found: {_inputData.DungeonKey}");
            return;
        }

        _battleSequencer.SetDungeon(targetDungeon);

        // 勇者モデルを構築
        var heroModel = new HeroModel();
        heroModel.ApplyEquippedItems(_inputData.EquippedItemIds);
        Debug.Log($"[BattleSceneStarter] Hero equipped items: {_inputData.EquippedItemIds.Count}");

        // StreamingSalesController に選択アイテムを渡して初期化
        if (_salesController != null)
        {
            _salesController.Setup(_itemModel, _inputData.SelectedItems);
            Debug.Log($"[BattleSceneStarter] StreamingSalesController initialized with {_inputData.SelectedItems.Count} items.");
        }

        // バトル終了を待つための UniTaskCompletionSource
        var battleTcs = new UniTaskCompletionSource<(BattleResult result, string weaponId, string armorId)>();

        _battleSequencer.OnBattleWin
            .Subscribe(win => battleTcs.TrySetResult((BattleResult.Victory, win.weaponId, win.armorId)));

        _battleSequencer.OnBattleDefeat
            .Subscribe(defeat => battleTcs.TrySetResult((BattleResult.Defeat, defeat.weaponId, defeat.armorId)));

        _battleSequencer.StartBattle(heroModel);

        // バトル終了待ち
        var battleResult = await battleTcs.Task;
        Debug.Log($"[BattleSceneStarter] Battle finished: {battleResult.result}");

        // BattleOutputData に結果を書き込み
        var soldItems = BuildSoldItems();
        _outputData.SetResult(battleResult.result, battleResult.weaponId, battleResult.armorId, soldItems);

        // --- Phase 3: Result（配信リザルト画面） ---
        Debug.Log("[BattleSceneStarter] Phase 3: Result");
        if (_resultView != null)
        {
            await _resultView.ShowResultAsync(battleResult.result, soldItems);
        }
        else
        {
            Debug.LogWarning("[BattleSceneStarter] BattleResultView is null. Skipping result screen.");
        }

        // --- Phase 4: TomsShop に戻る ---
        Debug.Log("[BattleSceneStarter] Returning to TomsShop...");
        _sceneTransition.ReturnToTomsShop();
    }

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
    /// </summary>
    private List<BattleOutputSoldItem> BuildSoldItems()
    {
        var soldItems = new List<BattleOutputSoldItem>();
        foreach (var item in _inputData.SelectedItems)
        {
            soldItems.Add(new BattleOutputSoldItem
            {
                ItemId = item.ItemId,
                SoldQuantity = item.Quantity,
                SoldPrice = item.Price
            });
        }
        return soldItems;
    }
}

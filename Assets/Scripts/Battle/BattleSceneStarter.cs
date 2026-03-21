﻿using R3;
using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// FightScene の EntryPoint。
/// BattleInputData の情報を BattleSequencer に渡して戦闘を開始する。
/// 戦闘終了後に BattleOutputData に結果を書き込み、TomsShop に戻る。
/// </summary>
public class BattleSceneStarter : IStartable
{
    private readonly BattleSequencer _battleSequencer;
    private readonly BattleInputData _inputData;
    private readonly BattleOutputData _outputData;
    private readonly SceneTransitionService _sceneTransition;
    private readonly IDungeonCatalog _dungeonCatalog;
    private readonly StreamingSalesController _salesController;
    private readonly ItemModel _itemModel;

    public BattleSceneStarter(
        BattleSequencer battleSequencer,
        BattleInputData inputData,
        BattleOutputData outputData,
        SceneTransitionService sceneTransition,
        IDungeonCatalog dungeonCatalog,
        StreamingSalesController salesController,
        ItemModel itemModel)
    {
        _battleSequencer = battleSequencer;
        _inputData = inputData;
        _outputData = outputData;
        _sceneTransition = sceneTransition;
        _dungeonCatalog = dungeonCatalog;
        _salesController = salesController;
        _itemModel = itemModel;
    }

    public void Start()
    {
        Debug.Log("[BattleSceneStarter] Starting battle from BattleInputData...");

        if (_battleSequencer == null)
        {
            Debug.LogError("[BattleSceneStarter] BattleSequencer is null! FightScene の Inspector で BattleSequencer がアサインされているか確認してください。");
            return;
        }

        if (_inputData == null)
        {
            Debug.LogError("[BattleSceneStarter] BattleInputData is null!");
            return;
        }

        var targetDungeon = ResolveTargetDungeon();
        if (targetDungeon == null)
        {
            Debug.LogError($"[BattleSceneStarter] Dungeon not found: {_inputData.DungeonKey}. FightScene の BattleLifetimeScope.dungeonInfos に {_inputData.DungeonKey} の DungeonInfoScriptableObj を追加してください。");
            return;
        }

        _battleSequencer.SetDungeon(targetDungeon);

        // BattleInputData の装備情報を使って勇者モデルを構築
        var heroModel = new HeroModel();
        heroModel.ApplyEquippedItems(_inputData.EquippedItemIds);
        Debug.Log($"[BattleSceneStarter] Hero equipped items: {_inputData.EquippedItemIds.Count}");

        // BattleInputData のSelectedItemsをStreamingSalesControllerに渡して初期化
        if (_salesController != null)
        {
            _salesController.Setup(_itemModel, _inputData.SelectedItems);
            Debug.Log($"[BattleSceneStarter] StreamingSalesController initialized with {_inputData.SelectedItems.Count} items.");
        }
        else
        {
            Debug.LogWarning("[BattleSceneStarter] StreamingSalesController is null. Streaming sales will not work.");
        }

        _battleSequencer.OnBattleWin
            .Subscribe(OnWin);

        _battleSequencer.OnBattleDefeat
            .Subscribe(OnDefeat);

        _battleSequencer.StartBattle(heroModel);
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
            Debug.LogWarning($"[BattleSceneStarter] Dungeon {_inputData.DungeonKey} was missing from catalog, but BattleSequencer.CurrentDungeon matched so it will be used as fallback.");
            return _battleSequencer.CurrentDungeon;
        }

        Debug.LogWarning($"[BattleSceneStarter] Catalog lookup failed for dungeon key: {_inputData.DungeonKey}");
        return null;
    }

    private void OnWin((string weaponId, string armorId) win)
    {
        var soldItems = BuildSoldItems();
        _outputData.SetResult(
            BattleResult.Victory,
            win.weaponId,
            win.armorId,
            soldItems);
        _sceneTransition.ReturnToTomsShop();
    }

    private void OnDefeat((string weaponId, string armorId) defeat)
    {
        var soldItems = BuildSoldItems();
        _outputData.SetResult(
            BattleResult.Defeat,
            defeat.weaponId,
            defeat.armorId,
            soldItems);
        _sceneTransition.ReturnToTomsShop();
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

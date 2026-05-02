using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// TomsShop シーン復帰時に BattleOutputData を読み取り、
/// 戦闘結果を ItemModel 等に反映するハンドラ。
/// </summary>
public class BattleResultHandler : IStartable, IDisposable
{
    private readonly BattleOutputData _outputData;
    private readonly BattleInputData _inputData;
    private readonly ItemModel _itemModel;
    private readonly StateManager _stateManager;
    private readonly GameFlowManager _gameFlowManager;
    private readonly ShopEconomySettings _economySettings;
    private readonly TomsModel _tomsModel;
    private readonly HeroModel _heroModel;

    public BattleResultHandler(
        BattleOutputData outputData,
        BattleInputData inputData,
        ItemModel itemModel,
        StateManager stateManager,
        GameFlowManager gameFlowManager,
        ShopEconomySettings economySettings,
        TomsModel tomsModel,
        HeroModel heroModel)
    {
        _outputData = outputData;
        _inputData = inputData;
        _itemModel = itemModel;
        _stateManager = stateManager;
        _gameFlowManager = gameFlowManager;
        _economySettings = economySettings;
        _tomsModel = tomsModel;
        _heroModel = heroModel;
    }

    public void Start()
    {
        // 戦闘結果がない場合（初回起動時など）はスキップ
        if (!_outputData.HasResult) return;

        Debug.Log($"[BattleResultHandler] Processing battle result: {_outputData.Result}");

        // GameFlowManager のインデックスを復元（シーン再生成で失われるため）
        _gameFlowManager.RestoreIndex(_inputData.GameFlowIndex);

        // --- 売上処理（在庫を減らしてお金を加算）---
        foreach (var soldItem in _outputData.SoldItems)
        {
            if (soldItem.SoldQuantity > 0)
            {
                _itemModel.Settlement(soldItem.ItemId, soldItem.SoldQuantity);
            }
        }

        if (_outputData.TotalEarnings != 0)
        {
            _tomsModel.Settlement(_outputData.TotalEarnings);
            Debug.Log($"[BattleResultHandler] Battle net earnings applied to PlayerMoney: {_outputData.TotalEarnings}G");
        }

        // --- 勝敗ボーナス/ペナルティ ---
        if (_outputData.Result == BattleResult.Victory)
        {
            if (!string.IsNullOrEmpty(_outputData.WeaponId))
                _itemModel.BattleWinBonus(_outputData.WeaponId, 5);
            if (!string.IsNullOrEmpty(_outputData.ArmorId))
                _itemModel.BattleWinBonus(_outputData.ArmorId, 5);

            Debug.Log("[BattleResultHandler] Victory bonuses applied.");
        }
        else
        {
            if (!string.IsNullOrEmpty(_outputData.WeaponId))
                _itemModel.BattleDefeatPenalty(_outputData.WeaponId, 2);
            if (!string.IsNullOrEmpty(_outputData.ArmorId))
                _itemModel.BattleDefeatPenalty(_outputData.ArmorId, 2);

            Debug.Log("[BattleResultHandler] Defeat penalties applied.");
        }

        int gainedExp = _outputData.DefeatedMobCount * GameConst.HeroExpPerMob
                      + _outputData.DefeatedBossCount * GameConst.HeroExpPerBoss;
        if (gainedExp > 0)
        {
            int levelUps = _heroModel.AddExperience(gainedExp);
            Debug.Log($"[BattleResultHandler] Hero gained EXP: {gainedExp} (mobs={_outputData.DefeatedMobCount}, bosses={_outputData.DefeatedBossCount}, levelUps={levelUps})");
        }

        // --- 案D1: 戦闘結果の属性波及 ---
        // 使用装備と同属性の全アイテムに需要を緩やかに波及させる
        var usedItemIds = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(_outputData.WeaponId)) usedItemIds.Add(_outputData.WeaponId);
        if (!string.IsNullOrEmpty(_outputData.ArmorId)) usedItemIds.Add(_outputData.ArmorId);
        if (usedItemIds.Count > 0)
        {
            _itemModel.ApplyBattleAttributeSpread(
                _outputData.Result,
                usedItemIds,
                _economySettings,
                _tomsModel.BlacksmithLevel.Value);
            Debug.Log("[BattleResultHandler] D1: Attribute spread applied.");
        }

        // 結果をクリア（次回の戦闘まで誤発火しないように）
        _outputData.Clear();

        // 戦闘結果（在庫減少・需要/価格変動）を永続化
        _itemModel.SaveData();

        // 戦闘結果処理後、次のターンへ進む
        _gameFlowManager.NextTurn();
    }

    public void Dispose() { }
}

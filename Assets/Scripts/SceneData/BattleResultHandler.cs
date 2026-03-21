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

    public BattleResultHandler(
        BattleOutputData outputData,
        BattleInputData inputData,
        ItemModel itemModel,
        StateManager stateManager,
        GameFlowManager gameFlowManager)
    {
        _outputData = outputData;
        _inputData = inputData;
        _itemModel = itemModel;
        _stateManager = stateManager;
        _gameFlowManager = gameFlowManager;
    }

    public void Start()
    {
        // 戦闘結果がない場合（初回起動時など）はスキップ
        if (!_outputData.HasResult) return;

        Debug.Log($"[BattleResultHandler] Processing battle result: {_outputData.Result}");

        // GameFlowManager のインデックスを復元（シーン再生成で失われるため）
        _gameFlowManager.RestoreIndex(_inputData.GameFlowIndex);

        // --- 売上処理 ---
        foreach (var soldItem in _outputData.SoldItems)
        {
            if (soldItem.SoldQuantity > 0)
            {
                _itemModel.Settlement(soldItem.ItemId, soldItem.SoldQuantity);
            }
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

        // 結果をクリア（次回の戦闘まで誤発火しないように）
        _outputData.Clear();

        // 戦闘結果処理後、次のターンへ進む
        _gameFlowManager.NextTurn();
    }

    public void Dispose() { }
}


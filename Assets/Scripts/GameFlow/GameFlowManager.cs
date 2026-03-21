using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using VContainer.Unity;

public class GameFlowManager : IDisposable, IStartable
{
    private GameFlow _gameFlow;
    private readonly StateManager _stateManager;
    private readonly DungeonRepository _dungeonRepository;
    private readonly BattleInputData _battleInputData;
    private readonly ItemModel _itemModel;
    private readonly ShopEconomySettings _economySettings;
    private readonly TomsModel _tomsModel;
    private readonly SceneTransitionService _sceneTransition;
    private readonly HeroModel _heroModel;
    private int _currentIndex;

    /// <summary>
    /// 現在のターン番号（1始まり）
    /// </summary>
    public ReactiveProperty<int> CurrentTurn { get; } = new(1);

    /// <summary>
    /// 現在のGameFlowインデックス（シーン復帰時の保存/復元用）
    /// </summary>
    public int CurrentIndex => _currentIndex;

    public GameFlowManager(StateManager stateManager, DungeonRepository dungeonRepository, BattleInputData battleInputData,
        ItemModel itemModel, ShopEconomySettings economySettings, TomsModel tomsModel,
        SceneTransitionService sceneTransition, HeroModel heroModel)
    {
        _stateManager = stateManager;
        _dungeonRepository = dungeonRepository;
        _battleInputData = battleInputData;
        _itemModel = itemModel;
        _economySettings = economySettings;
        _tomsModel = tomsModel;
        _sceneTransition = sceneTransition;
        _heroModel = heroModel;
        _currentIndex = 0;
    }

    /// <summary>
    /// シーン復帰時にフローのインデックスを復元する
    /// </summary>
    public void RestoreIndex(int index)
    {
        _currentIndex = index;
        CurrentTurn.Value = _currentIndex + 1;
        Debug.Log($"[GameFlowManager] Index restored to {_currentIndex}, turn={CurrentTurn.Value}");
    }
    
    public void Start()
    {
        _gameFlow = Resources.Load<GameFlow>("GameFlow/GameFlow");

        if (_gameFlow == null)
        {
            Debug.LogError("GameFlow not found in Resources/GameFlow folder");
        }
    }

    /// <summary>
    /// 次のターンに進む。GameFlowStackの次のノードに基づいてフェーズを遷移する。
    /// </summary>
    public void NextTurn()
    {
        if (_gameFlow == null)
        {
            Debug.LogError("[GameFlowManager] GameFlow is not loaded.");
            return;
        }

        _currentIndex++;
        CurrentTurn.Value = _currentIndex + 1;

        // --- TomsShop の経済更新（S1 + S3 + D2） ---
        if (_itemModel != null && _economySettings != null && _tomsModel != null)
        {
            _itemModel.ApplyShopTurnEconomy(_economySettings, _tomsModel.BlacksmithLevel.Value);
            _itemModel.SaveData();
            Debug.Log("[GameFlowManager] Shop economy updated for new turn.");
        }

        if (_currentIndex >= _gameFlow.GameFlowStack.Count)
        {
            Debug.Log("[GameFlowManager] All turns completed. Transitioning to Result phase.");
            _stateManager.ChangePhase(GamePhase.Result);
            return;
        }

        var node = _gameFlow.GameFlowStack[_currentIndex];

        // Battle時はBattleInputDataをセットアップしてFightSceneへ直接遷移
        if (node.EventType == GameEvent.Battle)
        {
            var catalog = _dungeonRepository.CreateCatalog();
            var dungeonInfo = catalog.GetDungeon(node.BattleDungeon);
            if (dungeonInfo != null)
            {
                _battleInputData.DungeonKey = node.BattleDungeon;
                Debug.Log($"[GameFlowManager] Battle dungeon set to: {node.BattleDungeon}");
            }
            else
            {
                Debug.LogWarning($"[GameFlowManager] Dungeon not found for key: {node.BattleDungeon}");
            }

            // ItemModel の在庫データを保存してからシーン遷移
            _itemModel.SaveData();

            // BattleInputData にフロー情報を書き込み
            _battleInputData.Setup(
                node.BattleDungeon,
                new List<string>(_heroModel.EquippedItemIds),
                new List<BattleInputItem>(),
                _currentIndex
            );

            Debug.Log($"[GameFlowManager] NextTurn: index={_currentIndex}, event={node.EventType} → FightScene");
            _sceneTransition.GoToBattle();
            return;
        }

        var nextPhase = ConvertEventToPhase(node.EventType);
        Debug.Log($"[GameFlowManager] NextTurn: index={_currentIndex}, event={node.EventType} → phase={nextPhase}");
        _stateManager.ChangePhase(nextPhase);
    }

    /// <summary>
    /// GameEventからGamePhaseへ変換する。
    /// </summary>
    private GamePhase ConvertEventToPhase(GameEvent gameEvent)
    {
        return gameEvent switch
        {
            GameEvent.Start => GamePhase.TomsShop,
            GameEvent.Shop => GamePhase.TomsShop,
            GameEvent.Event => GamePhase.TomsShop,
            GameEvent.End => GamePhase.Result,
            _ => throw new ArgumentOutOfRangeException(nameof(gameEvent), gameEvent, "Unknown GameEvent")
        };
    }

    public void Dispose()
    {

    }
}
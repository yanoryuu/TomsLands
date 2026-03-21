using System;
using R3;
using UnityEngine;
using VContainer.Unity;

public class GameFlowManager : IDisposable, IStartable
{
    private GameFlow _gameFlow;
    private readonly StateManager _stateManager;
    private readonly DungeonRepository _dungeonRepository;
    private readonly BattleInputData _battleInputData;
    private int _currentIndex;

    /// <summary>
    /// 現在のターン番号（1始まり）
    /// </summary>
    public ReactiveProperty<int> CurrentTurn { get; } = new(1);

    /// <summary>
    /// 現在のGameFlowインデックス（シーン復帰時の保存/復元用）
    /// </summary>
    public int CurrentIndex => _currentIndex;

    public GameFlowManager(StateManager stateManager, DungeonRepository dungeonRepository, BattleInputData battleInputData)
    {
        _stateManager = stateManager;
        _dungeonRepository = dungeonRepository;
        _battleInputData = battleInputData;
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

        if (_currentIndex >= _gameFlow.GameFlowStack.Count)
        {
            Debug.Log("[GameFlowManager] All turns completed. Transitioning to Result phase.");
            _stateManager.ChangePhase(GamePhase.Result);
            return;
        }

        var node = _gameFlow.GameFlowStack[_currentIndex];
        var nextPhase = ConvertEventToPhase(node.EventType);

        // Battle時はGameFlowNodeに設定されたダンジョン情報をBattleInputDataに渡す
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
        }

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
            GameEvent.Battle => GamePhase.Streaming,
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
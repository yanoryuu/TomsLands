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
    private readonly EventInputData _eventInputData;
    private readonly EventOutputData _eventOutputData;
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
        SceneTransitionService sceneTransition, HeroModel heroModel,
        EventInputData eventInputData, EventOutputData eventOutputData)
    {
        _stateManager = stateManager;
        _dungeonRepository = dungeonRepository;
        _battleInputData = battleInputData;
        _itemModel = itemModel;
        _economySettings = economySettings;
        _tomsModel = tomsModel;
        _sceneTransition = sceneTransition;
        _heroModel = heroModel;
        _eventInputData = eventInputData;
        _eventOutputData = eventOutputData;
        _currentIndex = 0;
    }

    /// <summary>
    /// シーン復帰時にフローのインデックスを復元する
    /// </summary>
    public void RestoreIndex(int index)
    {
        _currentIndex = index;
        // ターン番号はEventノードを除外してカウント
        CurrentTurn.Value = CalculateTurnNumber(_currentIndex);
        Debug.Log($"[GameFlowManager] Index restored to {_currentIndex}, turn={CurrentTurn.Value}");
    }

    /// <summary>
    /// 指定インデックスまでのEvent以外のノード数からターン番号を計算する
    /// </summary>
    private int CalculateTurnNumber(int upToIndex)
    {
        if (_gameFlow == null) return upToIndex + 1;

        int turn = 1; // 初期ターン
        for (int i = 1; i <= upToIndex && i < _gameFlow.GameFlowStack.Count; i++)
        {
            if (_gameFlow.GameFlowStack[i].EventType != GameEvent.Event)
            {
                turn++;
            }
        }
        return turn;
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

        if (_currentIndex >= _gameFlow.GameFlowStack.Count)
        {
            Debug.Log("[GameFlowManager] All turns completed. Transitioning to Result phase.");
            _stateManager.ChangePhase(GamePhase.Result);
            return;
        }

        var node = _gameFlow.GameFlowStack[_currentIndex];

        // Event時はターン番号も経済更新も進めず、EventSceneへ遷移する
        if (node.EventType == GameEvent.Event)
        {
            Debug.Log($"[GameFlowManager] Event node detected at index={_currentIndex}. Turn does NOT advance.");
            ProcessEventNode(node);
            return;
        }

        // Event以外のノードではターン番号を進める
        CurrentTurn.Value = CalculateTurnNumber(_currentIndex);

        // --- TomsShop の経済更新（S1 + S3 + D2）--- Event以外のターンで実行
        if (_itemModel != null && _economySettings != null && _tomsModel != null)
        {
            _itemModel.ApplyShopTurnEconomy(_economySettings, _tomsModel.BlacksmithLevel.Value);
            _itemModel.SaveData();
            Debug.Log("[GameFlowManager] Shop economy updated for new turn.");
        }

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
    /// Eventノードを処理する。ターンは進めずEventSceneへ遷移する。
    /// </summary>
    private void ProcessEventNode(GameFlowNode node)
    {
        var eventId = node.EventData;
        if (string.IsNullOrEmpty(eventId))
        {
            Debug.LogWarning("[GameFlowManager] Event node has no EventData. Skipping to next turn.");
            NextTurn();
            return;
        }

        // CSVからイベントデータを検索
        var tomsEvent = EventDataLoader.FindById(eventId);
        if (tomsEvent == null)
        {
            Debug.LogWarning($"[GameFlowManager] Event not found in CSV: {eventId}. Skipping.");
            NextTurn();
            return;
        }

        // EventInputData にデータを書き込み
        // コマンドデータはCSVから再ロードするのでCommandsJsonは空でよい
        _eventInputData.Setup(
            tomsEvent.id,
            tomsEvent.title,
            tomsEvent.description,
            "",
            _currentIndex
        );

        // EventOutputData をクリア
        _eventOutputData.Clear();

        // データを保存してからシーン遷移
        _itemModel.SaveData();
        _tomsModel.SavePlayerMoney();

        Debug.Log($"[GameFlowManager] NextTurn: index={_currentIndex}, event=Event({eventId}) → EventScene");
        _sceneTransition.GoToEvent();
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

    /// <summary>
    /// 現在のインデックスから次のBattleノードまでの非Eventターン数を返す。
    /// Battleが見つからない場合は -1 を返す。
    /// </summary>
    public int GetTurnsUntilNextBattle()
    {
        if (_gameFlow == null) return -1;

        int turnsCount = 0;
        for (int i = _currentIndex + 1; i < _gameFlow.GameFlowStack.Count; i++)
        {
            var node = _gameFlow.GameFlowStack[i];

            if (node.EventType == GameEvent.Battle)
            {
                return turnsCount;
            }

            // Eventノードはターンとしてカウントしない
            if (node.EventType != GameEvent.Event)
            {
                turnsCount++;
            }
        }

        return -1; // Battleノードが見つからない
    }

    public void Dispose()
    {

    }
}
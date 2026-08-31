using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly PendingEventData _pendingEventData;
    private readonly MarketingFacade _marketingFacade;
    private readonly ShopStatusModel _shopStatusModel;
    private readonly SellOrderModel _sellOrderModel;
    private readonly PortfolioModel _portfolioModel;
    private int _currentIndex;

    /// <summary>
    /// 現在のターン番号（1始まり）
    /// </summary>
    public ReactiveProperty<int> CurrentTurn { get; } = new(1);

    /// <summary>
    /// 完了した配信（Battle）の回数
    /// </summary>
    public ReactiveProperty<int> BattleCount { get; } = new(0);

    /// <summary>
    /// 現在のGameFlowインデックス（シーン復帰時の保存/復元用）
    /// </summary>
    public int CurrentIndex => _currentIndex;

    public GameFlowManager(StateManager stateManager, DungeonRepository dungeonRepository, BattleInputData battleInputData,
        ItemModel itemModel, ShopEconomySettings economySettings, TomsModel tomsModel,
        SceneTransitionService sceneTransition, HeroModel heroModel,
        EventInputData eventInputData, EventOutputData eventOutputData,
        PendingEventData pendingEventData, MarketingFacade marketingFacade,
        ShopStatusModel shopStatusModel, SellOrderModel sellOrderModel,
        PortfolioModel portfolioModel)
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
        _pendingEventData = pendingEventData;
        _marketingFacade = marketingFacade;
        _shopStatusModel = shopStatusModel;
        _sellOrderModel = sellOrderModel;
        _portfolioModel = portfolioModel;
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
        BattleCount.Value = CalculateBattleCount(_currentIndex);
        Debug.Log($"[GameFlowManager] Index restored to {_currentIndex}, turn={CurrentTurn.Value}, battleCount={BattleCount.Value}");
    }

    private int CalculateBattleCount(int upToIndex)
    {
        if (_gameFlow == null) return 0;
        int count = 0;
        for (int i = 0; i < upToIndex && i < _gameFlow.GameFlowStack.Count; i++)
        {
            if (_gameFlow.GameFlowStack[i].EventType == GameEvent.Battle)
                count++;
        }
        return count;
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
        // フローの構築は GameLifecycleHandler が seed / mode を確定した後に
        // InitializeFlow() を呼んで行う（VContainer の Start 順では本クラスが
        // GameLifecycleHandler より先に走るため、ここでは何もしない）。
    }

    /// <summary>
    /// フロー本体を構築する。手動(SO)か自動生成かを切り替える。
    /// GameLifecycleHandler から RestoreIndex の前に呼ばれる。
    /// </summary>
    /// <param name="useAuto">true=自動生成 / false=手動SO</param>
    /// <param name="mode">自動生成時のゲームモード</param>
    /// <param name="seed">自動生成時の乱数シード</param>
    public void InitializeFlow(bool useAuto, GameModeId mode, int seed)
    {
        if (!useAuto)
        {
            _gameFlow = AddressableLoader.Load<GameFlow>("GameFlow/GameFlow");
            if (_gameFlow == null)
                Debug.LogError("[GameFlowManager] 手動フロー GameFlow/GameFlow が見つかりません。");
            else
                Debug.Log($"[GameFlowManager] 手動フローをロード（nodes={_gameFlow.GameFlowStack.Count}）");
            return;
        }

        // --- 自動生成 ---
        var config = GameConst.GetGameMode(mode);
        var settings = GameConst.FlowGeneration;
        var dungeons = _dungeonRepository != null ? _dungeonRepository.GetAll() : null;
        var eventIdPool = EventDataLoader.LoadAll().Select(e => e.id).ToList();

        _gameFlow = GameFlowGenerator.Generate(config, settings, dungeons, eventIdPool, seed);
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
            Debug.Log("[GameFlowManager] All turns completed. Transitioning to ResultScene.");
            TransitionToResult();
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
        BattleCount.Value = CalculateBattleCount(_currentIndex);

        if (_itemModel != null && _economySettings != null && _tomsModel != null)
        {
            _itemModel.ApplyShopTurnEconomy(_economySettings, _tomsModel.BlacksmithLevel.Value, _shopStatusModel);
            _itemModel.SaveData();
            _tomsModel.SavePlayerMoney();
            Debug.Log("[GameFlowManager] Shop economy updated for new turn.");
        }

        // 約定予定日を過ぎたのに営業サマリーを経由できなかった売り注文（配信日・イベント日を
        // 挟んだ場合）をここで精算する。借金の返済チェック(TomsShopPresenter.Entry)より
        // 前に必ず入金されるよう、この位置（日送りの朝）で行う。
        if (_sellOrderModel != null && _tomsModel != null)
        {
            var overdue = _sellOrderModel.SettleOverdue(CurrentTurn.Value, _itemModel, _economySettings, _marketingFacade);
            if (overdue.Settled.Count > 0)
            {
                _tomsModel.AddRevenue(overdue.TotalIncome);
                _sellOrderModel.SaveData();
                _tomsModel.SavePlayerMoney();
                Debug.Log($"[GameFlowManager] 持ち越し売り注文を精算: +{overdue.TotalIncome}G ({overdue.Settled.Count}件)");
            }
        }

        // 新しい日の仕入れ支出トラッカーをリセット（ターン評価「資金効率」用）
        _tomsModel?.ResetTurnProcurementSpend();

        // 金融資産のターン処理（価格更新後に行う）:
        // 満期債券の償還・ファンド基準価額の履歴記録・配当付き武器の配当入金
        if (_tomsModel != null && _itemModel != null)
        {
            int financeIncome = 0;

            if (_portfolioModel != null)
            {
                var finance = _portfolioModel.ApplyTurn(CurrentTurn.Value, _itemModel, _tomsModel.BlacksmithLevel.Value);
                if (finance.BondPayout > 0)
                {
                    financeIncome += finance.BondPayout;
                    foreach (var bond in finance.MaturedBonds)
                        Debug.Log($"[GameFlowManager] 債券償還: {bond.productName} 元本{bond.principal}G + 利息{bond.interest}G");
                }
            }

            int dividend = _itemModel.CalculateDividendIncome();
            if (dividend > 0)
            {
                financeIncome += dividend;
                Debug.Log($"[GameFlowManager] 配当収入: +{dividend}G");
            }

            if (financeIncome > 0)
            {
                _tomsModel.AddRevenue(financeIncome);
                _portfolioModel?.SaveData();
                _tomsModel.SavePlayerMoney();
            }
        }

        // マーケティングシステムのターン処理（バズ判定・持続効果適用）
        if (_marketingFacade != null)
        {
            var buzzResult = _marketingFacade.ProcessTurn();
            if (buzzResult.NewBuzzOccurred)
            {
                Debug.Log($"[GameFlowManager] バズ発生: {buzzResult.NewBuzzType}");
            }
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

            // ItemModel の在庫データ・売り注文・金融資産を保存してからシーン遷移
            _itemModel.SaveData();
            _sellOrderModel?.SaveData();
            _portfolioModel?.SaveData();

            // GameFlowIndexをTomsModelに反映して保存
            _tomsModel.GameFlowIndex = _currentIndex;
            _tomsModel.SavePlayerMoney();

            // BattleInputData にフロー情報を書き込み
            var dungeonData = _dungeonRepository.GetById(node.BattleDungeon);
            int dungeonLevel = dungeonData?.currentDungeonLevel ?? 1;
            _battleInputData.Setup(
                node.BattleDungeon,
                dungeonLevel,
                new List<string>(_heroModel.EquippedItemIds),
                new List<BattleInputItem>(),
                _currentIndex
            );

            Debug.Log($"[GameFlowManager] NextTurn: index={_currentIndex}, event={node.EventType} → FightScene");
            _sceneTransition.GoToBattle();
            return;
        }

        var nextPhase = ConvertEventToPhase(node.EventType);

        // End ノードの場合はリザルトシーンへ遷移
        if (node.EventType == GameEvent.End)
        {
            Debug.Log($"[GameFlowManager] NextTurn: index={_currentIndex}, event=End → ResultScene");
            TransitionToResult();
            return;
        }

        Debug.Log($"[GameFlowManager] NextTurn: index={_currentIndex}, event={node.EventType} → phase={nextPhase}");

        // TomsShop→TomsShop の場合、ChangePhase の同値ガードで Entry() が発火しないため
        // ChangeTomsShopPhase を使って ForceNotify で確実に Entry() を呼ぶ
        if (nextPhase == GamePhase.TomsShop)
        {
            _stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop);
        }
        else
        {
            _stateManager.ChangePhase(nextPhase);
        }
    }

    /// <summary>
    /// Eventノードを処理する。ターンは進めない。
    /// UseInlineEventPopup=true の場合、PendingEventDataにセットしてTomsShop内ポップアップで表示する。
    /// UseInlineEventPopup=false の場合、EventSceneへシーン遷移する（将来用）。
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

        // --- インラインポップアップモード（TomsShop内で表示）---
        if (_pendingEventData.UseInlineEventPopup)
        {
            Debug.Log($"[GameFlowManager] Inline event popup: {eventId}. Storing as pending event.");
            _pendingEventData.Set(tomsEvent, _currentIndex);

            // ターンを進めずに次のフローノードへ進む
            // 次がShopノードならTomsShopのEntryでポップアップが表示される
            NextTurn();
            return;
        }

        // --- EventSceneモード（将来用：シーン遷移して表示）---
        // EventInputData にデータを書き込み
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
        _tomsModel.GameFlowIndex = _currentIndex;
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

    /// <summary>
    /// 現在のインデックスから次のBattleノードのDungeonNameを返す。
    /// Battleが見つからない場合は null を返す。
    /// </summary>
    public DungeonName? GetNextBattleDungeon()
    {
        if (_gameFlow == null) return null;

        for (int i = _currentIndex + 1; i < _gameFlow.GameFlowStack.Count; i++)
        {
            var node = _gameFlow.GameFlowStack[i];
            if (node.EventType == GameEvent.Battle)
                return node.BattleDungeon;
        }
        return null;
    }

    /// <summary>
    /// リザルトシーンへ遷移する。遷移前にデータを保存する。
    /// </summary>
    private void TransitionToResult()
    {
        // 現在のターン番号を TomsModel に反映
        _tomsModel.CurrentTurn.Value = CurrentTurn.Value;
        _tomsModel.GameFlowIndex = _currentIndex;

        // データを保存してからシーン遷移
        _itemModel.SaveData();
        _tomsModel.SavePlayerMoney();

        Debug.Log($"[GameFlowManager] Saving data and transitioning to ResultScene. Turn={CurrentTurn.Value}");
        _sceneTransition.GoToResult();
    }

    public void Dispose()
    {

    }
}
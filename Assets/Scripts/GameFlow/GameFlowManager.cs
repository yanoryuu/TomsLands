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
    private readonly NewsModel _newsModel;
    private readonly NewsEffectResolver _newsEffects;
    private readonly SellOrderModel _sellOrderModel;
    private readonly PortfolioModel _portfolioModel;
    private readonly ShopMachineModel _shopMachineModel;
    private readonly MorningReportModel _morningReport;
    private readonly RelicEffectResolver _relicResolver;
    private readonly RelicHookDispatcher _relicHooks;
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

    /// <summary>
    /// 配信日に入ったが、まだ FightScene へ遷移していない（配信前の寄り道中）か。
    /// true の間は日送り(NextTurn)せず、ProceedToBattle() で配信へ進む。
    /// セーブしない（シーン内で完結する一時状態。中断時は配信ノードの index が保存済み）。
    /// </summary>
    public ReactiveProperty<bool> IsAwaitingStream { get; } = new(false);

    /// <summary>
    /// 配信前の寄り道を受け持つハンドラ（PreStreamPresenter が登録）。
    /// 引数: true=鍛冶屋から戻ってきたところ / false=配信日に入った直後。
    /// 未登録なら従来どおり即 FightScene へ遷移する。
    /// </summary>
    private Action<bool> _preStreamHandler;

    public GameFlowManager(StateManager stateManager, DungeonRepository dungeonRepository, BattleInputData battleInputData,
        ItemModel itemModel, ShopEconomySettings economySettings, TomsModel tomsModel,
        SceneTransitionService sceneTransition, HeroModel heroModel,
        EventInputData eventInputData, EventOutputData eventOutputData,
        PendingEventData pendingEventData, MarketingFacade marketingFacade,
        ShopStatusModel shopStatusModel, SellOrderModel sellOrderModel,
        PortfolioModel portfolioModel, ShopMachineModel shopMachineModel,
        MorningReportModel morningReport, RelicEffectResolver relicResolver,
        RelicHookDispatcher relicHooks,
        NewsModel newsModel, NewsEffectResolver newsEffects)
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
        _newsModel = newsModel;
        _newsEffects = newsEffects;
        _sellOrderModel = sellOrderModel;
        _portfolioModel = portfolioModel;
        _shopMachineModel = shopMachineModel;
        _morningReport = morningReport;
        _relicResolver = relicResolver;
        _relicHooks = relicHooks;
        _currentIndex = 0;

        // 営業で1日に来る客の数（売れる量の上限）とおすすめ（適正値）の計算に、経済設定と広告ステータスを渡す（H-2 / G-11）
        _itemModel?.ConfigureShopSales(economySettings, shopStatusModel);
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
        // 店の日付表示・新聞・ゲームオーバー等が参照する TomsModel 側のターンも毎ターン同期する
        if (_tomsModel != null) _tomsModel.CurrentTurn.Value = CurrentTurn.Value;
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
            _newsModel?.SetWorld(BuildNewsWorld());
            return;
        }

        // --- 自動生成 ---
        var config = GameConst.GetGameMode(mode);
        var settings = GameConst.FlowGeneration;
        var dungeons = _dungeonRepository != null ? _dungeonRepository.GetAll() : null;
        var eventIdPool = EventDataLoader.LoadAll().Select(e => e.id).ToList();

        _gameFlow = GameFlowGenerator.Generate(config, settings, dungeons, eventIdPool, seed);
        _newsModel?.SetWorld(BuildNewsWorld());
    }

    /// <summary>
    /// 朝刊の連載（Docs/News_Phase3_Spec.md §17）の分岐を、この朝の状態で決める。
    /// 直近の配信の勝敗は RunHistory の累計から、在庫は ItemModel から見る。経済計算より前に呼ぶ。
    /// </summary>
    private void ResolveNewsArcs()
    {
        if (_newsModel == null) return;
        var history = RunHistory.Load();
        if (history != null) _newsModel.ObserveStreamTotals(history.streamWins, history.streamLosses);
        _newsModel.ResolveArcs(CurrentTurn.Value, ev =>
            _itemModel != null && _itemModel.RuntimeItems.Exists(i => i.Stock.Value > 0 && ev.Matches(i)));
    }

    /// <summary>
    /// 朝刊の発行カレンダー用に、ダンジョンの名前・弱点と戦闘の予定（ターン）を渡す。
    /// 事象の差し込み枠（{dungeon}）と、因果の3法則（L1/L2）の対象決めに使う（Docs/News_Phase3_Spec.md §3）。
    /// </summary>
    private NewsWorld BuildNewsWorld()
    {
        var world = new NewsWorld();
        if (_dungeonRepository != null)
        {
            foreach (var d in _dungeonRepository.GetAll())
                world.dungeons.Add(new NewsWorld.Dungeon
                {
                    key = d.key.ToString(),
                    name = d.dungeonName,
                    weakness = d.requiredAttribute.ToString(),
                });
        }
        if (_gameFlow != null)
        {
            for (int i = 0; i < _gameFlow.GameFlowStack.Count; i++)
            {
                var node = _gameFlow.GameFlowStack[i];
                if (node.EventType == GameEvent.Battle)
                    world.battles.Add((CalculateTurnNumber(i), node.BattleDungeon.ToString()));
            }
        }
        return world;
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

        // 配信前の寄り道中は日を進めない（サマリーの確認ボタン再押下などでの二重進行を防ぐ）。
        // 判断ポップアップを出し直すだけにする。
        if (IsAwaitingStream.Value)
        {
            Debug.LogWarning("[GameFlowManager] NextTurn: 配信前の寄り道中のため日送りせず、配信の判断を出し直す");
            RequestPreStreamDecision(false);
            return;
        }

        RunHistory.RecordMoney(CurrentTurn.Value, _tomsModel != null ? _tomsModel.PlayerMoney.Value : 0); // リザルトの所持金グラフ用（終わった日の所持金）
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
        if (_tomsModel != null) _tomsModel.CurrentTurn.Value = CurrentTurn.Value;

        // 店の設備の故障（朝いちばん。壊れたマシンは今朝から効果が止まる＝冷蔵ケース・売上・お金・製造のどれも）。
        // 乱数はランのシード×日×マシンID なので、同じシードなら同じ朝に壊れる（AutoPlay の再現）
        if (_shopMachineModel != null && _tomsModel != null)
        {
            foreach (var broken in _shopMachineModel.AdvanceDurability(_tomsModel.FlowSeed, CurrentTurn.Value))
                _morningReport?.Add(_shopMachineModel.BreakdownLine(broken));
        }

        if (_itemModel != null && _economySettings != null && _tomsModel != null)
        {
            // 旧マシン効果 + レリックの需要下限ボーナスを渡す（どちらも無ければ 0 で従来挙動）
            float demandFloorBonus = _shopMachineModel?.TotalDemandFloorBonus ?? 0f;
            if (_relicResolver != null)
                demandFloorBonus = _relicResolver.Modify(RelicStatId.DemandFloorAdd, demandFloorBonus);
            // 発行カレンダーは周のシードから組む。既に同じシードで組んであれば何もしない。
            _newsModel?.Build(_tomsModel.FlowSeed);
            ResolveNewsArcs();

            _itemModel.ApplyShopTurnEconomy(_economySettings, _tomsModel.BlacksmithLevel.Value, _shopStatusModel, demandFloorBonus,
                CurrentTurn.Value, _tomsModel.FlowSeed, _newsEffects, GetStreamAnticipationAttribute(),
                _shopMachineModel?.NotDisplayedDemandDropRate ?? 1f);
            _itemModel.SaveData();
            _tomsModel.SavePlayerMoney();
            Debug.Log("[GameFlowManager] Shop economy updated for new turn.");
        }

        // 今朝の入金合計（朝レポートの見出し）を数えるため、朝の入金処理の前の所持金を記録する
        if (_tomsModel != null) _morningReport?.MarkMoneyBefore(_tomsModel.PlayerMoney.Value);

        // 約定予定日を過ぎたのに営業サマリーを経由できなかった売り注文（配信日・イベント日を
        // 挟んだ場合）をここで精算する。借金の返済チェック(TomsShopPresenter.Entry)より
        // 前に必ず入金されるよう、この位置（日送りの朝）で行う。
        if (_sellOrderModel != null && _tomsModel != null)
        {
            var overdue = _sellOrderModel.SettleOverdue(CurrentTurn.Value, _itemModel, _economySettings, _marketingFacade, _relicResolver);
            if (overdue.Settled.Count > 0)
            {
                _tomsModel.AddRevenue(overdue.TotalIncome);
                _sellOrderModel.SaveData();
                _tomsModel.SavePlayerMoney();
                _morningReport?.AddIncome("overdueSales", "持ち越した売上", null, overdue.TotalIncome);
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
                var finance = _portfolioModel.ApplyTurn(CurrentTurn.Value, _itemModel, _tomsModel.BlacksmithLevel.Value, _relicResolver);
                if (finance.BondPayout > 0)
                {
                    financeIncome += finance.BondPayout;
                    foreach (var bond in finance.MaturedBonds)
                    {
                        _morningReport?.AddIncome($"bond:{bond.productName}", $"{bond.productName}の償還", null, bond.principal + bond.interest);
                        Debug.Log($"[GameFlowManager] 債券償還: {bond.productName} 元本{bond.principal}G + 利息{bond.interest}G");
                    }
                }
            }

            int dividend = _itemModel.CalculateDividendIncome();
            if (dividend > 0)
            {
                // レリック補正（不労所得ビルド: DividendMul）
                if (_relicResolver != null)
                    dividend = Mathf.Max(0, _relicResolver.ModifyInt(RelicStatId.DividendMul, dividend));
                financeIncome += dividend;
                _morningReport?.AddIncome("dividend", "配当", null, dividend);
                Debug.Log($"[GameFlowManager] 配当収入: +{dividend}G");
            }

            // マシン（毎日発動型）の効果: お金製造・アイテム自動生成
            if (_shopMachineModel != null)
            {
                var machineResult = _shopMachineModel.ExecuteDailyEffects(_itemModel, _relicResolver);
                if (machineResult.TotalMoney > 0)
                    financeIncome += machineResult.TotalMoney;
                // お金を産むマシンはアイコン付きの入金の行にする（壊れているマシンは行を出さない）
                foreach (var m in machineResult.MoneySources)
                    _morningReport?.AddIncome($"machine:{m.Machine.machineId}", m.Machine.machineName, m.Machine.icon, m.Amount);
                _morningReport?.AddRange(machineResult.Lines);
                if (machineResult.ProducedAnyItem)
                    _itemModel.SaveData();
            }

            // レリックのターン開始フック（フック型効果。入金はビヘイビア内で行われる）
            if (_relicHooks != null)
            {
                var relicLines = _relicHooks.OnTurnStart(CurrentTurn.Value, _tomsModel, _itemModel,
                    _morningReport != null
                        ? (relic, gained) => _morningReport.AddIncome($"relic:{relic.relicId}", relic.relicName, relic.icon, gained)
                        : null);
                _morningReport?.AddRange(relicLines);
            }

            if (financeIncome > 0)
            {
                _tomsModel.AddRevenue(financeIncome);
                _portfolioModel?.SaveData();
                _tomsModel.SavePlayerMoney();
            }
        }

        if (_tomsModel != null) _morningReport?.MarkMoneyAfter(_tomsModel.PlayerMoney.Value);

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
            // レリックの戦闘系効果のスナップショットと BattleInputData の書き込み（続きからの配信やり直しと共用）
            PrepareBattleInput(node);

            // ItemModel の在庫データ・売り注文・金融資産を保存してからシーン遷移
            _itemModel.SaveData();
            _sellOrderModel?.SaveData();
            _portfolioModel?.SaveData();

            // GameFlowIndexをTomsModelに反映して保存
            _tomsModel.GameFlowIndex = _currentIndex;
            _tomsModel.SavePlayerMoney();

            // 配信前の寄り道（鍛冶屋での仕入れ）を受ける側がいれば、ここで一旦止めて判断を委ねる。
            // ここまでで日送り処理とセーブ（GameFlowIndex=配信ノード）は済んでいるので、
            // 寄り道中に終了しても「配信を FightScene 内で中断した」のと同じ状態になる。
            if (_preStreamHandler != null)
            {
                IsAwaitingStream.Value = true;
                Debug.Log($"[GameFlowManager] NextTurn: index={_currentIndex}, event={node.EventType} → 配信前の寄り道待ち");
                _preStreamHandler.Invoke(false);
                return;
            }

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

        // 日送りの区切り: 新しい日の index と、この日送りで動いた全モデルをここでまとめて保存する。
        // （途中の保存は index が前日のままなので、ここが無いと店の1日の間に落ちたとき
        //   続きからで同じ日送り＝値動き・配当・入金が二重に走る。tomsData を最後に書いてその日の確定とする）
        CommitDay();

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
            // 確認ボタンを押すまで tomsData に残す（表示中に終了しても続きからで同じイベントを出し直す。
            // 保存は直後の NextTurn の区切り/配信前の保存で行われ、確認後の消去は GameLifecycleHandler が保存する）
            if (_tomsModel != null)
            {
                _tomsModel.PendingEventId = tomsEvent.id;
                _tomsModel.PendingEventIndex = _currentIndex;
            }

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
    private static GamePhase ConvertEventToPhase(GameEvent gameEvent) => gameEvent switch
    {
        GameEvent.Start => GamePhase.TomsShop,
        GameEvent.Shop => GamePhase.TomsShop,
        GameEvent.Event => GamePhase.TomsShop,
        GameEvent.End => GamePhase.Result,
        _ => throw new ArgumentOutOfRangeException(nameof(gameEvent), gameEvent, "Unknown GameEvent")
    };

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
    /// 次に配信するダンジョン（配信前の寄り道中は今日これから配信するダンジョン）。無ければ null。
    /// 勇者画面・地図・占い師・鍛冶屋のヘッダーで同じ行き先を出すための共通の取り方。
    /// </summary>
    public DungeonName? GetUpcomingStreamDungeon()
        => IsAwaitingStream.Value ? PendingBattleDungeon : GetNextBattleDungeon();

    /// <summary>
    /// 表示用: 次の配信先（GetUpcomingStreamDungeon）の、その次に配信するダンジョン。無ければ null。
    /// 地図で「その次」を1つだけ見せるために使う（それより先は見せない。Docs/Map_Overlay_Spec.md R3）。
    /// </summary>
    public DungeonName? GetStreamDungeonAfterUpcoming()
    {
        if (_gameFlow == null) return null;

        // 寄り道中は今のノードが「次の配信」なので、この先で最初に見つかる配信が「その次」
        bool upcomingCounted = IsAwaitingStream.Value && PendingBattleDungeon.HasValue;
        for (int i = _currentIndex + 1; i < _gameFlow.GameFlowStack.Count; i++)
        {
            var node = _gameFlow.GameFlowStack[i];
            if (node.EventType != GameEvent.Battle) continue;
            if (!upcomingCounted)
            {
                upcomingCounted = true;
                continue;
            }
            return node.BattleDungeon;
        }
        return null;
    }

    /// <summary>
    /// 表示用: 次の配信まであと何日か。今日が配信日（寄り道中）なら 0、明日なら 1。配信が無ければ -1。
    /// GetTurnsUntilNextBattle は「次のノードから配信の手前まで」の日数で、前日に 0 になる
    /// （AutoPlay がその意味を前提にしているので変えない）。画面に出すときはこちらを使う。
    /// </summary>
    /// <summary>
    /// 配信前の期待を乗せる属性（次の配信先の弱点）。配信まで streamAnticipationDays 日以内のときだけ返す。
    /// 弱点は情報屋で買う情報だが、相場は世間の期待として動く（値動きから推せるのは「どれかが上がっている」まで。Docs/ShopEconomy.md §11）。
    /// </summary>
    private ItemTypeData.ItemAttribute? GetStreamAnticipationAttribute()
    {
        if (_economySettings == null || _economySettings.streamAnticipationDays <= 0
            || (_economySettings.streamAnticipationDemandUp == 0f && _economySettings.streamAnticipationPriceUp == 0f)) return null;
        int days = GetDaysUntilNextStream();
        if (days < 1 || days > _economySettings.streamAnticipationDays) return null;
        var key = GetNextBattleDungeon();
        if (!key.HasValue || _dungeonRepository == null) return null;
        var dungeon = _dungeonRepository.GetById(key.Value);
        return dungeon != null ? dungeon.requiredAttribute : (ItemTypeData.ItemAttribute?)null;
    }

    public int GetDaysUntilNextStream()
    {
        if (IsAwaitingStream.Value) return 0;
        int turns = GetTurnsUntilNextBattle();
        return turns < 0 ? -1 : turns + 1;
    }

    /// <summary>
    /// 表示用: 次の配信（寄り道中は今日の配信）がこのランで何回目か（upcoming、1 始まり。もう無ければ 0）と、配信の総数（total）。
    /// HUD の「配信 2/4」と、最後の配信の知らせ（K-11）に使う。
    /// </summary>
    public void GetStreamProgress(out int upcoming, out int total)
    {
        upcoming = 0;
        total = 0;
        if (_gameFlow == null) return;
        // 寄り道中は今のノードが配信。それ以外は次のノードから先で最初の配信
        int firstUpcomingIndex = IsAwaitingStream.Value ? _currentIndex : _currentIndex + 1;
        for (int i = 0; i < _gameFlow.GameFlowStack.Count; i++)
        {
            if (_gameFlow.GameFlowStack[i].EventType != GameEvent.Battle) continue;
            total++;
            if (upcoming == 0 && i >= firstUpcomingIndex) upcoming = total;
        }
    }

    /// <summary>表示用: 今日より後に残っている店の日数（配信日・イベント・終わりは数えない）。レリック3択の目安に使う。</summary>
    public int GetRemainingShopDays()
    {
        if (_gameFlow == null) return 0;
        int days = 0;
        for (int i = _currentIndex + 1; i < _gameFlow.GameFlowStack.Count; i++)
        {
            var t = _gameFlow.GameFlowStack[i].EventType;
            if (t == GameEvent.Shop) days++;
        }
        return days;
    }

    /// <summary>
    /// 今日より後に残っている朝の数（日送りで朝の処理が走る回数 = イベント以外のノード。配信日・終わりも数える）。
    /// 店の設備の強化・修理の「元が取れるか」の目安（AutoPlay）に使う。
    /// </summary>
    public int GetRemainingMornings()
    {
        if (_gameFlow == null) return 0;
        int mornings = 0;
        for (int i = _currentIndex + 1; i < _gameFlow.GameFlowStack.Count; i++)
            if (_gameFlow.GameFlowStack[i].EventType != GameEvent.Event) mornings++;
        return mornings;
    }

    /// <summary>表示用: 次の配信がこのランの最後の配信か（配信が残っていなければ false）。</summary>
    public bool IsUpcomingStreamFinal()
    {
        GetStreamProgress(out int upcoming, out int total);
        return upcoming > 0 && upcoming == total;
    }

    /// <summary>
    /// 表示用: 今日までに配信した日（ターン番号）の一覧。価格チャートの「配信の印」に使う。
    /// 寄り道中の今日の配信はまだ決着していないので含めない。
    /// </summary>
    public List<int> GetPastStreamTurns()
    {
        var result = new List<int>();
        if (_gameFlow == null) return result;
        int last = IsAwaitingStream.Value ? _currentIndex - 1 : _currentIndex;
        for (int i = 0; i <= last && i < _gameFlow.GameFlowStack.Count; i++)
        {
            if (_gameFlow.GameFlowStack[i].EventType == GameEvent.Battle)
                result.Add(CalculateTurnNumber(i));
        }
        return result;
    }

    /// <summary>
    /// 返済日の朝、現金が返済額に足りないときに、今日入金予定の売り注文を前倒しで約定して入金する。
    /// 入金は通常その日の夜（営業サマリー）だが、返済の判定は朝なので、
    /// 「前日に並べて現金化した分」が数えられずに破産してしまうのを防ぐ。
    /// 夜の精算は残りの注文だけで行われる（二重には入らない）。戻り値は入金額。
    /// </summary>
    public int SettleTodaySellOrdersForDebt()
    {
        if (_sellOrderModel == null || _tomsModel == null) return 0;

        var settled = _sellOrderModel.SettleDue(CurrentTurn.Value, _itemModel, _economySettings, _marketingFacade, _relicResolver);
        if (settled.Settled.Count == 0) return 0;

        _tomsModel.AddRevenue(settled.TotalIncome);
        _sellOrderModel.SaveData();
        _tomsModel.SavePlayerMoney();
        _morningReport?.Add($"納付日のため今日の売上を前倒しで入金: +{settled.TotalIncome:N0}G");
        Debug.Log($"[GameFlowManager] 返済日のため本日約定分を前倒し精算: +{settled.TotalIncome}G ({settled.Settled.Count}件)");
        return settled.TotalIncome;
    }

    // ========================================
    // 配信前の寄り道（鍛冶屋で仕入れてから配信へ）
    // ========================================

    /// <summary>配信前の寄り道ハンドラを登録する。null で解除（従来どおり即配信）。</summary>
    public void SetPreStreamHandler(Action<bool> handler) => _preStreamHandler = handler;

    /// <summary>
    /// 寄り道中に「配信へ進むか」の判断を求める（鍛冶屋を閉じたとき等）。
    /// ハンドラが無ければそのまま配信へ進む。
    /// </summary>
    public void RequestPreStreamDecision(bool fromProcurement)
    {
        if (!IsAwaitingStream.Value) return;
        if (_preStreamHandler == null)
        {
            ProceedToBattle();
            return;
        }
        _preStreamHandler.Invoke(fromProcurement);
    }

    /// <summary>
    /// 寄り道中の配信日のダンジョン（＝これから配信するダンジョン）。寄り道中でなければ null。
    /// GetNextBattleDungeon は「現在より先」を探すため、寄り道中は今日の配信を返せない。その補完。
    /// </summary>
    public DungeonName? PendingBattleDungeon
    {
        get
        {
            if (!IsAwaitingStream.Value || _gameFlow == null) return null;
            if (_currentIndex < 0 || _currentIndex >= _gameFlow.GameFlowStack.Count) return null;
            var node = _gameFlow.GameFlowStack[_currentIndex];
            return node.EventType == GameEvent.Battle ? node.BattleDungeon : (DungeonName?)null;
        }
    }

    /// <summary>
    /// 寄り道を終えて FightScene へ遷移する。寄り道中の仕入れ結果を保存してから遷移する。
    /// BattleInputData は NextTurn の配信分岐でセット済み（寄り道で変わるのは在庫と所持金のみ）。
    /// </summary>
    public void ProceedToBattle()
    {
        if (!IsAwaitingStream.Value) return;
        IsAwaitingStream.Value = false;

        _itemModel.SaveData();
        _sellOrderModel?.SaveData();
        _portfolioModel?.SaveData();
        _tomsModel.GameFlowIndex = _currentIndex;
        _tomsModel.SavePlayerMoney();

        Debug.Log($"[GameFlowManager] ProceedToBattle: index={_currentIndex} → FightScene");
        _sceneTransition.GoToBattle();
    }

    /// <summary>
    /// 配信ノードの BattleInputData（ダンジョン・レベル・装備・フロー位置）と、
    /// レリックの戦闘系効果（勇者弱体化・防衛報酬アップ）のスナップショットを作る。
    /// 日送りの配信分岐と、続きからの配信やり直し（TryResumeInterruptedScene）で共用する。
    /// </summary>
    private void PrepareBattleInput(GameFlowNode node)
    {
        RelicBattleEffects.SetFrom(_relicResolver);

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

        var dungeonData = _dungeonRepository.GetById(node.BattleDungeon);
        int dungeonLevel = dungeonData?.currentDungeonLevel ?? DungeonLevels.Unfunded; // Lv0 は戦闘は Lv1 と同じ・配当なし
        // 職人組合 Lv5「細工」: この配信に効く細工を確定する（業物ならこの配信先の弱点の属性が付く）
        ArtisanCraft.PrepareBattle(_tomsModel.FlowSeed, GetStreamNumberAt(_currentIndex), dungeonData?.requiredAttribute);
        _battleInputData.Setup(
            node.BattleDungeon,
            dungeonLevel,
            new List<string>(_heroModel.EquippedItemIds),
            new List<BattleInputItem>(),
            _currentIndex
        );
        // 配信開始時のクリア見込み（店の勇者パネルと同じ計算）。魔王軍支援の配当のオッズ（SupportOdds）と計測ログに使う
        _battleInputData.PreStreamClearPct = ComputePreStreamClearPct(dungeonData, dungeonLevel);
    }

    private float ComputePreStreamClearPct(DungeonData dungeonData, int dungeonLevel)
    {
        if (dungeonData == null || _heroModel?.heroData == null) return -1f;
        try
        {
            return ClearProbabilityCalculator.Calculate(_heroModel.heroData, _itemModel, dungeonData, dungeonLevel, _relicResolver);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameFlowManager] 配信開始時のクリア見込みを計算できませんでした: {e.Message}");
            return -1f;
        }
    }

    /// <summary>
    /// 「続きから」で、前回の終了が配信中・配信前の寄り道中・リザルトの前だったら、その場面へ直接戻す。
    /// GameLifecycleHandler が InitializeContinue（index 復元）の後、店の Entry を出す前に呼ぶ。
    ///
    /// - 配信ノード: その配信を最初からやり直す（FightScene へ直行）。日送り・経済更新は配信前に済んで
    ///   保存済みなので NextTurn は呼ばない（呼ぶとその日の値動き・入金が二重になる）。
    ///   寄り道（鍛冶屋での仕入れ）は保存しないので出し直さず、配信へ進む。
    /// - 最終ノードの先 / End ノード: リザルトへ（店をもう1日遊べてしまうのを防ぐ）。
    /// 戦闘結果を持って帰ってきた場合（BattleOutputData.HasResult）は呼ばないこと（BattleResultHandler が処理する）。
    /// </summary>
    /// <returns>シーン遷移したら true（呼び出し側は店の Entry を出さない）。</returns>
    public bool TryResumeInterruptedScene()
    {
        if (_gameFlow == null || _currentIndex < 0) return false;

        if (_currentIndex >= _gameFlow.GameFlowStack.Count
            || (_currentIndex > 0 && _gameFlow.GameFlowStack[_currentIndex].EventType == GameEvent.End))
        {
            Debug.Log($"[GameFlowManager] 続きから: リザルトの前に終了していた (index={_currentIndex}) → ResultScene");
            TransitionToResult();
            return true;
        }

        var node = _gameFlow.GameFlowStack[_currentIndex];
        if (node.EventType != GameEvent.Battle) return false;

        PrepareBattleInput(node);
        Debug.Log($"[GameFlowManager] 続きから: 配信中（または配信前の寄り道中）に終了していた (index={_currentIndex}) → 配信をやり直す");
        _sceneTransition.GoToBattle();
        return true;
    }

    /// <summary>店の日の日送りの確定。index を書き、各モデルを保存して、最後に tomsData を書く。</summary>
    private void CommitDay()
    {
        if (_tomsModel == null) return;
        _tomsModel.GameFlowIndex = _currentIndex;
        _itemModel?.SaveData();
        _sellOrderModel?.SaveData();
        _portfolioModel?.SaveData();
        _shopMachineModel?.SaveData();
        _tomsModel.SavePlayerMoney();
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

    /// <summary>その位置（フローの index）までの配信の回数（その位置が配信ならそれが何回目か）。職人組合 Lv5「細工」の配信の見分けに使う。</summary>
    public int GetStreamNumberAt(int index)
    {
        if (_gameFlow == null) return 0;
        int n = 0;
        for (int i = 0; i <= index && i < _gameFlow.GameFlowStack.Count; i++)
            if (_gameFlow.GameFlowStack[i].EventType == GameEvent.Battle) n++;
        return n;
    }

    public void Dispose()
    {
        IsAwaitingStream.Dispose();
    }

    /// <summary>
    /// 表示用: このランの配信の予定を、配信する順に全部返す（領主館 Lv5「配信の予定表」。Docs/Map_Overlay_Spec.md R6）。
    /// フローはラン開始時にシード（手動フローなら SO）から決まり、ランの途中で並びは変わらないので、最初から最後まで同じ予定が出る。
    /// 弱点・敵などは含めない（それは情報屋・新聞で知る）。寄り道中の今日の配信は「これから」に数える。
    /// </summary>
    public List<StreamScheduleEntry> GetStreamSchedule()
    {
        var result = new List<StreamScheduleEntry>();
        if (_gameFlow == null) return result;

        int firstUpcomingIndex = IsAwaitingStream.Value ? _currentIndex : _currentIndex + 1;
        bool upcomingFound = false;
        for (int i = 0; i < _gameFlow.GameFlowStack.Count; i++)
        {
            var node = _gameFlow.GameFlowStack[i];
            if (node.EventType != GameEvent.Battle) continue;
            bool past = i < firstUpcomingIndex;
            bool upcoming = !past && !upcomingFound;
            if (upcoming) upcomingFound = true;
            result.Add(new StreamScheduleEntry
            {
                Number = result.Count + 1,
                Day = CalculateTurnNumber(i),
                Dungeon = node.BattleDungeon,
                IsPast = past,
                IsUpcoming = upcoming,
            });
        }
        return result;
    }
}

/// <summary>配信の予定表の1行（GameFlowManager.GetStreamSchedule）。</summary>
public struct StreamScheduleEntry
{
    /// <summary>このランで何回目の配信か（1始まり）。</summary>
    public int Number;
    /// <summary>配信する日（何日目）。</summary>
    public int Day;
    public DungeonName Dungeon;
    /// <summary>もう配信した（決着済み）。</summary>
    public bool IsPast;
    /// <summary>次の配信（寄り道中は今日の配信）。</summary>
    public bool IsUpcoming;
}
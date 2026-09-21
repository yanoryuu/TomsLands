using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Utage の会話をゲーム進行に差し込むPresenter（TomsShop シーン担当）。
/// 発火条件の判定だけを持ち、再生と既読管理は TutorialScenarioService に任せる。
/// 既読フラグは MetaProgressModel（slot_N/metaData.json）に持つため、
/// セーブ復帰やランをまたいでも同じ会話が再生され直さない。
/// FightScene 側の配信チュートリアルは BattleSceneStarter が担当する。
/// </summary>
public class TutorialPresenter : IStartable, IDisposable
{
    // シナリオラベル（Assets/TomsScenario/Scenarios/*.csv のラベルと一致させること）
    private const string Opening           = "Tutorial_Opening";
    private const string ShopBasics        = "Tutorial_ShopBasics";
    private const string Blacksmith        = "Tutorial_Blacksmith";
    private const string AfterProcurement  = "Tutorial_AfterProcurement";
    private const string Display           = "Tutorial_Display";
    private const string ProphetMeeting    = "Tutorial_ProphetMeeting";
    private const string StreamingEnd      = "Tutorial_StreamingEnd";
    private const string EventTutorial     = "TurnEvent_EventTutorial";
    private const string BuzzTutorial      = "TurnEvent_BuzzTutorial";
    private const string IntroHero         = "Intro_Hero";
    private const string IntroDemonLord    = "Intro_DemonLord";
    private const string IntroProphet      = "Intro_Prophet";
    private const string IntroInfoBroker   = "Intro_InfoBroker";

    /// <summary>ランダムイベントの会話ラベルの接頭辞。イベントIDを付けて Event_001 のようになる。</summary>
    private const string EventLabelPrefix = "Event_";

    /// <summary>占い師と出会うターン。</summary>
    private const int ProphetMeetingTurn = 2;
    /// <summary>初配信から何ターン後にイベント／バズのチュートリアルを出すか。</summary>
    private const int EventTutorialDelay = 2;
    private const int BuzzTutorialDelay  = 5;

    private readonly TutorialScenarioService _scenario;
    private readonly MetaProgressModel   _meta;
    private readonly GameFlowManager     _gameFlow;
    private readonly StateManager        _stateManager;
    private readonly TurnPhaseManager    _turnPhaseManager;
    private readonly PendingEventData    _pendingEventData;
    private readonly CompositeDisposable _disposables = new();
    private readonly CancellationTokenSource _cts = new();

    // 会話が重ならないよう、発火した順に1本ずつ再生する
    private readonly Queue<QueuedScenario> _queue = new();
    private bool _pumping;

    private readonly struct QueuedScenario
    {
        public readonly string Label;
        public readonly bool OnceOnly;
        public QueuedScenario(string label, bool onceOnly) { Label = label; OnceOnly = onceOnly; }
    }

    public TutorialPresenter(
        TutorialScenarioService scenario,
        MetaProgressModel meta,
        GameFlowManager   gameFlow,
        StateManager      stateManager,
        TurnPhaseManager  turnPhaseManager,
        PendingEventData  pendingEventData)
    {
        _scenario         = scenario;
        _meta             = meta;
        _gameFlow         = gameFlow;
        _stateManager     = stateManager;
        _turnPhaseManager = turnPhaseManager;
        _pendingEventData = pendingEventData;

        _stateManager.RegisterOnEnter(TomsShopGamePhase.BlackSmith,     () => EnqueueOnce(Blacksmith));
        _stateManager.RegisterOnEnter(TomsShopGamePhase.Hero,           () => EnqueueOnce(IntroHero));
        _stateManager.RegisterOnEnter(TomsShopGamePhase.DungeonLevelUp, () => EnqueueOnce(IntroDemonLord));
        _stateManager.RegisterOnEnter(TomsShopGamePhase.Prophet,        () => EnqueueOnce(IntroProphet));
        _stateManager.RegisterOnEnter(TomsShopGamePhase.Broker,         () => EnqueueOnce(IntroInfoBroker));

        _stateManager.RegisterOnEnter(TomsShopGamePhase.Shop, OnEnterShop);
    }

    public void Start()
    {
        // ゲーム開始直後のプロローグと、最初の営業の手順説明
        EnqueueOnce(Opening);
        EnqueueOnce(ShopBasics);

        // 商品陳列フェーズに初めて入ったとき
        _turnPhaseManager.CurrentTurnPhase
            .Where(phase => phase == TurnPhase.Display)
            .Subscribe(_ => EnqueueOnce(Display))
            .AddTo(_disposables);

        // ランダムイベントのフェーズに入ったら、そのイベント専用の会話があれば再生する
        _turnPhaseManager.CurrentTurnPhase
            .Where(phase => phase == TurnPhase.Event)
            .Subscribe(_ => EnqueuePendingEventScenario())
            .AddTo(_disposables);

        // ターン数で発火するもの。CurrentTurn はセーブ復帰時にも流れるが、既読フラグで二重再生を防ぐ
        _gameFlow.CurrentTurn
            .Subscribe(OnTurnChanged)
            .AddTo(_disposables);
    }

    private void OnEnterShop()
    {
        // 鍛冶屋の説明を見たあとに店へ戻ってきたら、陳列へ進む案内を出す
        if (_meta.HasSeenTutorial(Blacksmith)) EnqueueOnce(AfterProcurement);

        // 初めての配信から帰ってきたら、占い師との後日談を出す
        if (_meta.FirstBattleTurn > 0) EnqueueOnce(StreamingEnd);
    }

    private void OnTurnChanged(int turn)
    {
        if (turn >= ProphetMeetingTurn) EnqueueOnce(ProphetMeeting);

        int firstBattleTurn = _meta.FirstBattleTurn;
        if (firstBattleTurn <= 0) return;

        if (turn >= firstBattleTurn + EventTutorialDelay) EnqueueOnce(EventTutorial);
        if (turn >= firstBattleTurn + BuzzTutorialDelay)  EnqueueOnce(BuzzTutorial);
    }

    /// <summary>保留中のランダムイベントに対応する会話があれば積む（毎回再生する）。</summary>
    private void EnqueuePendingEventScenario()
    {
        if (_pendingEventData == null || !_pendingEventData.HasPendingEvent) return;
        string id = _pendingEventData.PendingEvent?.id;
        if (string.IsNullOrEmpty(id)) return;
        Enqueue(new QueuedScenario(EventLabelPrefix + id, onceOnly: false));
    }

    /// <summary>未読なら再生キューに積む。既読・重複は無視する。</summary>
    private void EnqueueOnce(string label)
    {
        if (_scenario.HasSeen(label)) return;
        Enqueue(new QueuedScenario(label, onceOnly: true));
    }

    private void Enqueue(QueuedScenario item)
    {
        foreach (var queued in _queue)
        {
            if (queued.Label == item.Label) return;
        }
        _queue.Enqueue(item);
        if (!_pumping) PumpAsync().Forget();
    }

    private async UniTaskVoid PumpAsync()
    {
        _pumping = true;
        try
        {
            while (_queue.Count > 0)
            {
                var item = _queue.Dequeue();
                if (item.OnceOnly)
                {
                    await _scenario.PlayOnceAsync(item.Label, _cts.Token);
                }
                else
                {
                    await TutorialScenarioService.PlayAlwaysAsync(item.Label, _cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // シーン終了時のキャンセルは正常系
        }
        catch (Exception e)
        {
            Debug.LogError($"[TutorialPresenter] 会話の再生に失敗しました。\n{e}");
        }
        finally
        {
            _pumping = false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _disposables.Dispose();
    }
}

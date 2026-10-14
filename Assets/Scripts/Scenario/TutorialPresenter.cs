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
///
/// ランの開始会話（1周目はプロローグ、2周目以降は短い開始演出）もここで出す。
/// 新規ランで最初に店へ入ったとき、暗転幕（ScenarioCurtain）を張ってから会話を流し、
/// 終わってから幕を開けて店を見せる。途中復帰では TomsModel.RunOpeningPending が false なので流さない。
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

    // ランの開始会話（2周目以降。Assets/TomsScenario/Scenarios/TokoTalk.csv）
    private const string RunOpeningSecond = "Run_Opening_Second";
    private static readonly string[] RunOpeningAgain = { "Run_Opening_Again_1", "Run_Opening_Again_2", "Run_Opening_Again_3" };

    /// <summary>開始会話のあと、暗転幕を開けて店を見せるまでの時間（秒）。</summary>
    private const float CurtainFadeSeconds = 0.6f;

    /// <summary>ランダムイベントの会話ラベルの接頭辞。イベントIDを付けて Event_001 のようになる。</summary>
    private const string EventLabelPrefix = "Event_";

    /// <summary>占い師と出会うターン。</summary>
    private const int ProphetMeetingTurn = 2;
    /// <summary>初配信から何ターン後にイベント／バズのチュートリアルを出すか。</summary>
    private const int EventTutorialDelay = 2;
    private const int BuzzTutorialDelay  = 5;

    private readonly TutorialScenarioService _scenario;
    private readonly MetaProgressModel   _meta;
    private readonly TomsModel           _tomsModel;
    private readonly GameFlowManager     _gameFlow;
    private readonly StateManager        _stateManager;
    private readonly TurnPhaseManager    _turnPhaseManager;
    private readonly PendingEventData    _pendingEventData;
    private readonly CompositeDisposable _disposables = new();
    private readonly CancellationTokenSource _cts = new();

    // 会話が重ならないよう、発火した順に1本ずつ再生する
    private readonly Queue<QueuedScenario> _queue = new();
    private bool _pumping;

    /// <summary>このシーンで最初の店入場を処理済みか（開始会話の判定は一度だけ行う）。</summary>
    private bool _firstShopEntryHandled;

    /// <summary>開始会話の間だけ張る暗転幕。</summary>
    private ScenarioCurtain _curtain;

    private readonly struct QueuedScenario
    {
        public readonly string Label;
        public readonly bool OnceOnly;
        /// <summary>ランの開始会話か（再生後にラン内フラグを落とし、幕を開ける）。</summary>
        public readonly bool IsRunOpening;
        public QueuedScenario(string label, bool onceOnly, bool isRunOpening = false)
        {
            Label = label; OnceOnly = onceOnly; IsRunOpening = isRunOpening;
        }
    }

    public TutorialPresenter(
        TutorialScenarioService scenario,
        MetaProgressModel meta,
        TomsModel         tomsModel,
        GameFlowManager   gameFlow,
        StateManager      stateManager,
        TurnPhaseManager  turnPhaseManager,
        PendingEventData  pendingEventData)
    {
        _scenario         = scenario;
        _meta             = meta;
        _tomsModel        = tomsModel;
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
        // ランの開始会話（プロローグ・最初の営業の手順説明）は、ランの初期化が済んだ後の
        // 最初の店入場（OnEnterShop）で積む。VContainer の Start 順では本クラスが
        // GameLifecycleHandler より先に走り、ここではまだ新規ランかどうかが確定していないため。

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
        if (!_firstShopEntryHandled)
        {
            _firstShopEntryHandled = true;
            EnqueueRunOpening();
        }

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

    /// <summary>
    /// ランの開始会話を積む。新規ランの最初の店入場でだけ流す（途中復帰では RunOpeningPending が false）。
    /// 1周目はプロローグ（未読のときだけ）→ 最初の営業の手順説明、2周目以降は周回数に応じた短い会話。
    /// 店の画面を先に見せないよう、積んだ時点（＝最初の描画より前）で暗転幕を張る。
    /// </summary>
    private void EnqueueRunOpening()
    {
        bool firstRun = _meta == null || _meta.TotalRuns <= 0;

        if (_tomsModel != null && _tomsModel.RunOpeningPending)
        {
            string label = SelectRunOpeningLabel(firstRun);
            _curtain = ScenarioCurtain.Show();
            // プロローグだけは既読管理する（周回の開始会話は毎ラン流す）
            Enqueue(new QueuedScenario(label, onceOnly: label == Opening, isRunOpening: true));
        }

        // 最初の営業の手順説明は1周目だけ（既読なら何もしない）。幕を開けて店を見せてから流す
        if (firstRun) EnqueueOnce(ShopBasics);
    }

    /// <summary>開始会話のラベルを選ぶ。</summary>
    private string SelectRunOpeningLabel(bool firstRun)
    {
        if (firstRun && !_scenario.HasSeen(Opening)) return Opening;

        // TotalRuns は終えたランの数。1 なら2周目。
        // 1周目をリザルトまで行かずにやり直した場合（TotalRuns 0・プロローグ既読）も「二度目の初日」として扱う
        if (_meta == null || _meta.TotalRuns <= 1) return RunOpeningSecond;
        return RunOpeningAgain[UnityEngine.Random.Range(0, RunOpeningAgain.Length)];
    }

    /// <summary>開始会話を流し終えた（または流せなかった）ので、ラン内フラグを落として保存する。</summary>
    private void CompleteRunOpening()
    {
        if (_tomsModel == null || !_tomsModel.RunOpeningPending) return;
        _tomsModel.RunOpeningPending = false;
        _tomsModel.SavePlayerMoney();
    }

    /// <summary>暗転幕を開ける。張っていなければ何もしない。</summary>
    private async UniTask OpenCurtainAsync(CancellationToken cancellationToken)
    {
        var curtain = _curtain;
        _curtain = null;
        if (curtain == null) return;
        await curtain.FadeOutAndDestroyAsync(CurtainFadeSeconds, cancellationToken);
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
                try
                {
                    if (item.OnceOnly)
                    {
                        await _scenario.PlayOnceAsync(item.Label, _cts.Token);
                    }
                    else
                    {
                        await TutorialScenarioService.PlayAlwaysAsync(item.Label, _cts.Token);
                    }
                }
                finally
                {
                    // 開始会話は、再生できなかった場合（会話システム未配線・ラベル未定義）も含めて一度で済ませる。
                    // キャンセル（シーン破棄）時はフラグを残し、続きからで流し直す
                    if (item.IsRunOpening && !_cts.IsCancellationRequested) CompleteRunOpening();
                }

                if (item.IsRunOpening) await OpenCurtainAsync(_cts.Token);
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
            // 例外で抜けた場合も、幕が残って操作できなくならないようにする
            if (_curtain != null && !_cts.IsCancellationRequested) OpenCurtainAsync(CancellationToken.None).Forget();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _disposables.Dispose();
    }
}

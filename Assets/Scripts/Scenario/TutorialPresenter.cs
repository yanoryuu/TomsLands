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
    // 進行判定を他の Presenter と共有するものは TutorialSteps に置いている
    private const string Opening           = TutorialSteps.Opening;
    private const string ShopBasics        = TutorialSteps.ShopBasics;
    private const string Blacksmith        = TutorialSteps.Blacksmith;
    private const string AfterProcurement  = TutorialSteps.AfterProcurement;
    private const string Display           = TutorialSteps.Display;
    private const string FundsShortage     = TutorialSteps.FundsShortage;
    private const string FundsAdvance      = TutorialSteps.FundsAdvance;
    private const string FundsAdvanceAfter = TutorialSteps.FundsAdvanceAfter;
    private const string ProphetMeeting    = TutorialSteps.ProphetMeeting;
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

    /// <summary>
    /// 占い師と出会うターン。台詞が「ふぅ……なんとか少しは売れたけど……」なので2日目の営業後（=3ターン目の朝）。
    /// 1日目の営業後（2ターン目の朝）は「お金が増えてない」会話（FundsShortage）を出すため、ぶつからないよう3にしている。
    /// 自動生成フローは最初の配信までに店ターンが最低2つ入る（初配信は4ターン目以降）ので、配信の前に必ず出会える。
    /// </summary>
    private const int ProphetMeetingTurn = 3;
    /// <summary>入金の遅れに気づく会話を出すターン（1日目の営業後の朝）。</summary>
    private const int FundsShortageTurn = 2;
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
    private readonly TomsShopView        _shopView;
    private readonly TurnPhaseView       _turnPhaseView;
    private readonly BlackSmithModel     _blackSmithModel;
    private readonly CompositeDisposable _disposables = new();
    private readonly CancellationTokenSource _cts = new();

    // 会話が重ならないよう、発火した順に1本ずつ再生する
    private readonly Queue<QueuedScenario> _queue = new();
    private bool _pumping;

    /// <summary>店の「鍛冶屋以外クリック不可」を今かけているか（解除を一度だけ行うため）。</summary>
    private bool _shopLockApplied;

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
        /// <summary>
        /// 再生後の進行処理（操作制御・資金付与など）。OnceOnly の会話は「既読になったとき」だけ呼ぶ
        /// （ラベル未インポート等で再生されなかった場合に、進行だけが勝手に進まないように）。
        /// </summary>
        public readonly Action OnCompleted;
        public QueuedScenario(string label, bool onceOnly, bool isRunOpening = false, Action onCompleted = null)
        {
            Label = label; OnceOnly = onceOnly; IsRunOpening = isRunOpening; OnCompleted = onCompleted;
        }
    }

    public TutorialPresenter(
        TutorialScenarioService scenario,
        MetaProgressModel meta,
        TomsModel         tomsModel,
        GameFlowManager   gameFlow,
        StateManager      stateManager,
        TurnPhaseManager  turnPhaseManager,
        PendingEventData  pendingEventData,
        TomsShopView      shopView,
        TurnPhaseView     turnPhaseView,
        BlackSmithModel   blackSmithModel)
    {
        _scenario         = scenario;
        _meta             = meta;
        _tomsModel        = tomsModel;
        _gameFlow         = gameFlow;
        _stateManager     = stateManager;
        _turnPhaseManager = turnPhaseManager;
        _pendingEventData = pendingEventData;
        _shopView         = shopView;
        _turnPhaseView    = turnPhaseView;
        _blackSmithModel  = blackSmithModel;

        _stateManager.RegisterOnEnter(TomsShopGamePhase.BlackSmith,     OnEnterBlacksmith);
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

        // 初回は鍛冶屋以外を押せなくする（オート購入を済ませるまで）
        UpdateShopLock();

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

    private void OnEnterBlacksmith()
    {
        // 初回: 仕入れの説明 → 会話が終わったらオート購入（全額・調整不可）の予算ポップアップを出させる
        EnqueueOnce(Blacksmith, () => _blackSmithModel?.TutorialAutoBuyRequested.OnNext(Unit.Default));

        // 2日目: 入金ルールの説明 → 立て替え資金の付与 → 需要と価格の見方 → おすすめ商品の強調
        if (_meta.HasSeenTutorial(FundsShortage))
        {
            // 立て替えの会話を見たのに付与前に中断していた場合の保険（付与は一度きり）
            if (_meta.HasSeenTutorial(FundsAdvance)) GrantFundsAdvanceOnce();

            EnqueueOnce(FundsAdvance, GrantFundsAdvanceOnce);
            EnqueueOnce(FundsAdvanceAfter, () =>
            {
                if (_blackSmithModel != null) _blackSmithModel.TutorialBargainHighlight.Value = true;
            });
        }
    }

    private void OnEnterShop()
    {
        if (!_firstShopEntryHandled)
        {
            _firstShopEntryHandled = true;
            EnqueueRunOpening();
        }

        // 初回オート購入が済むまでは鍛冶屋以外を押せない
        UpdateShopLock();

        // 鍛冶屋で初回の仕入れを済ませて店へ戻ってきたら、陳列へ進む案内を出す
        if (_meta.HasSeenTutorial(Blacksmith) && !TutorialSteps.IsFirstAutoBuyPending(_meta))
            EnqueueOnce(AfterProcurement);

        // 1日目の営業後（2日目の朝）: 売上がまだ入っていないことに気づき、鍛冶屋へ聞きに行く。
        // 占い師と出会った後のセーブ（旧版で進めたもの）には出さない
        if (_gameFlow.CurrentTurn.Value >= FundsShortageTurn
            && _meta.HasSeenTutorial(Display)
            && !_meta.HasSeenTutorial(ProphetMeeting))
        {
            EnqueueOnce(FundsShortage, GoToBlacksmithForFundsTutorial);
        }

        // 初めての配信から帰ってきたら、占い師との後日談を出す
        if (_meta.FirstBattleTurn > 0) EnqueueOnce(StreamingEnd);
    }

    /// <summary>店の「鍛冶屋以外クリック不可」と「次へ」の停止を、初回オート購入待ちかどうかに合わせる。</summary>
    private void UpdateShopLock()
    {
        bool lockOn = TutorialSteps.IsFirstAutoBuyPending(_meta);
        if (!lockOn && !_shopLockApplied) return; // かけていない解除はしない（他の制御を上書きしない）
        _shopLockApplied = lockOn;
        if (_shopView != null) _shopView.SetTutorialBlacksmithOnly(lockOn);
        if (_turnPhaseView != null) _turnPhaseView.SetAdvanceLocked(lockOn);
    }

    /// <summary>「鍛冶屋さんに行って聞いてみよう！」の後、自動で鍛冶屋へ移動する。</summary>
    private void GoToBlacksmithForFundsTutorial()
    {
        if (_meta.HasSeenTutorial(FundsAdvanceAfter)) return;
        if (_gameFlow.IsAwaitingStream.Value) return;
        if (_stateManager.CurrentTomsShopPhase.Value != TomsShopGamePhase.Shop) return;
        if (!_stateManager.HasHandler(TomsShopGamePhase.BlackSmith)) return;
        _stateManager.ChangeTomsShopPhase(TomsShopGamePhase.BlackSmith);
    }

    /// <summary>
    /// 鍛冶屋の立て替え資金を所持金に加える。利息なしで次の納付に上乗せされる借り（H-6。TomsModel.GrantFundsAdvance）。段階フラグで一度きりにする。
    /// フラグを先に保存してから加算するので、途中で落ちても二重には入らない。
    /// </summary>
    private void GrantFundsAdvanceOnce()
    {
        if (_tomsModel == null || _meta.HasSeenTutorial(TutorialSteps.StepFundsAdvanceGranted)) return;
        _meta.MarkTutorialSeen(TutorialSteps.StepFundsAdvanceGranted);
        _tomsModel.GrantFundsAdvance(TutorialSteps.FundsAdvanceAmount);
        var soundManager = SoundManager.Instance;
        if (soundManager != null) soundManager.PlaySE("営業/SE_売上音");
        Debug.Log($"[TutorialPresenter] 鍛冶屋の立て替え資金 +{TutorialSteps.FundsAdvanceAmount}G → {_tomsModel.PlayerMoney.Value}G");
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

    /// <summary>未読なら再生キューに積む。既読・重複は無視する。onCompleted は既読になったときだけ呼ぶ。</summary>
    private void EnqueueOnce(string label, Action onCompleted = null)
    {
        if (_scenario.HasSeen(label)) return;
        Enqueue(new QueuedScenario(label, onceOnly: true, onCompleted: onCompleted));
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

                // 進行処理は幕を開けてから（OnceOnly は既読になったときだけ）
                if (item.OnCompleted != null && (!item.OnceOnly || _scenario.HasSeen(item.Label)))
                    InvokeCompleted(item);
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

    /// <summary>再生後の進行処理。失敗しても後続の会話は止めない。</summary>
    private static void InvokeCompleted(QueuedScenario item)
    {
        try
        {
            item.OnCompleted.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[TutorialPresenter] '{item.Label}' の後処理に失敗しました。\n{e}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _disposables.Dispose();
    }
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Utage;
using VContainer;

/// <summary>オートプレイの戦略（何を自動でやるか）。</summary>
public enum DebugAutoPlayStrategy
{
    /// <summary>お任せ仕入れ（おすすめ順・直近の納税額は残す）＋オート陳列。</summary>
    Standard = 0,
    /// <summary>仕入れはせず、手持ち在庫をオート陳列だけして進める。</summary>
    DisplayOnly = 1,
    /// <summary>何も買わず・並べずにフェーズを素通しする。</summary>
    Skip = 2,
}

/// <summary>オートプレイの実行範囲。</summary>
public enum DebugAutoPlayScope
{
    /// <summary>ターンを連続で進める（ターン数上限あり/なし）。</summary>
    Continuous = 0,
    /// <summary>仕入れ（鍛冶屋でお任せ購入）だけ行って止まる。</summary>
    ProcurementOnly = 1,
    /// <summary>オート陳列だけ行って止まる。</summary>
    DisplayOnly = 2,
}

/// <summary>
/// デバッグメニュー「オート」タブのランタイム自動進行ドライバ。仕様: Docs/DebugMenu_Spec.md「オート」
///
/// プレイ中の実ゲーム画面を、既存の View の Subject（ボタン押下と同じ経路）や Presenter / Model の
/// 公開APIを呼んで1ステップずつ進める。エディタ専用のヘッドレス試走（Editor/AutoPlay）とは別物で、
/// LLM（Jev）は一切使わない。
///
/// - 継続フラグ・設定・ログは static に持ち、シーン遷移（TomsShop ⇄ FightScene）で
///   DebugMenuView が作り直されても新しいシーンの DebugAutoPlayer が自動で再開する。
/// - 同時に複数のドライバが動かないよう、最後に起動したインスタンスだけが進行する。
/// - 自動で選べない確認ダイアログ・未対応の画面では「一時停止」して理由を出す。
///   エラーログ（LogError/例外）を検出したら一時停止する。
/// - 使うのは通常プレイと同じ操作経路のみ（セーブ削除・リセット等のデバッグ操作は呼ばない）。
/// </summary>
public class DebugAutoPlayer : MonoBehaviour
{
    private const string LogPrefix = "[AutoPlay]";
    private const string PopupSceneName = "PopupScene";
    private const int MaxLogLines = 10;
    private const int MaxMoneyHistory = 12;
    private const float StallSeconds = 30f;
    private const float RefireSeconds = 5f;
    private const float SceneErrorGraceSeconds = 3f;

    // =====================================================
    // static 状態（シーンをまたいで保持）
    // =====================================================

    private enum PauseKind { None, Manual, Error, Popup, Screen }

    public static bool Running { get; private set; }
    public static bool Paused => _pauseKind != PauseKind.None;
    public static string PauseReason { get; private set; } = "";
    public static string Status { get; private set; } = "停止中";
    public static DebugAutoPlayScope Scope { get; private set; } = DebugAutoPlayScope.Continuous;

    /// <summary>進めるターン数。0 = 無制限。</summary>
    public static int TurnLimit { get; private set; }
    public static int StartTurn { get; private set; } = -1;
    public static int LastKnownTurn { get; private set; } = -1;

    public static DebugAutoPlayStrategy Strategy = DebugAutoPlayStrategy.Standard;
    /// <summary>お任せ仕入れの予算（納税準備金を除いた所持金に対する%）。</summary>
    public static int BudgetPercent = 100;
    /// <summary>ステップ間のウェイト（秒）。</summary>
    public static float StepWait = 0.4f;
    /// <summary>true ならウェイトを Time.timeScale に連動させる（scaled time で待つ）。</summary>
    public static bool WaitFollowsTimeScale = true;
    /// <summary>Utage 会話を自動で送る（選択肢は先頭を選ぶ）。</summary>
    public static bool AutoAdvanceScenario = true;
    /// <summary>LogError / 例外を検出したら一時停止する。</summary>
    public static bool PauseOnErrorLog = true;

    public static readonly List<string> LogLines = new();
    public static readonly List<(int turn, int money)> MoneyHistory = new();

    private static PauseKind _pauseKind = PauseKind.None;
    private static string _boughtKey;
    private static string _displayedKey;
    private static DebugAutoPlayer _activeDriver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Running = false;
        _pauseKind = PauseKind.None;
        PauseReason = "";
        Status = "停止中";
        Scope = DebugAutoPlayScope.Continuous;
        TurnLimit = 0;
        StartTurn = -1;
        LastKnownTurn = -1;
        LogLines.Clear();
        MoneyHistory.Clear();
        _boughtKey = null;
        _displayedKey = null;
        _activeDriver = null;
    }

    // =====================================================
    // 依存（シーンごと。無いものは null）
    // =====================================================

    // TomsShop
    private GameFlowManager _gameFlow;
    private TurnPhaseManager _turnPhase;
    private StateManager _stateManager;
    private TomsModel _tomsModel;
    private ItemModel _itemModel;
    private PendingEventData _pendingEvent;
    private RelicEffectResolver _relicResolver;
    private ShopLevelSettings _shopLevelSettings;
    private TomsShopView _tomsShopView;
    private TurnPhaseView _turnPhaseView;
    private TurnEndSummaryView _summaryView;
    private EventView _eventView;
    private DebtView _debtView;
    private BlackSmithView _blackSmithView;
    private ItemSelectionPresenter _itemSelectionPresenter;
    private ItemSelectionView _itemSelectionView;

    // FightScene
    private BattlePanelManager _battlePanelManager;
    private StreamingSettingView _streamingSettingView;
    private BattleResultView _battleResultView;
    private BattleControlView _battleControlView;

    private ScenarioPlayer _scenarioPlayer;

    // このインスタンス（=このシーン）内の進行状態
    private bool _selectionOpenedByBot;
    private int _summaryFiredAtIndex = -1;
    private float _summaryFiredTime;
    private float _salesFiredTime = -999f;
    private float _battleSettingFiredTime = -999f;
    private float _battleResultFiredTime = -999f;
    private string _stallKey;
    private float _stallSince;
    private bool _stallExempt;
    private bool _initialized;
    private float _errorGraceUntil;

    public void Initialize(IObjectResolver resolver)
    {
        resolver.TryResolve(out _gameFlow);
        resolver.TryResolve(out _turnPhase);
        resolver.TryResolve(out _stateManager);
        resolver.TryResolve(out _tomsModel);
        resolver.TryResolve(out _itemModel);
        resolver.TryResolve(out _pendingEvent);
        resolver.TryResolve(out _relicResolver);
        resolver.TryResolve(out _shopLevelSettings);
        resolver.TryResolve(out _tomsShopView);
        resolver.TryResolve(out _turnPhaseView);
        resolver.TryResolve(out _summaryView);
        resolver.TryResolve(out _eventView);
        resolver.TryResolve(out _debtView);
        resolver.TryResolve(out _blackSmithView);
        resolver.TryResolve(out _itemSelectionPresenter);
        resolver.TryResolve(out _itemSelectionView);

        resolver.TryResolve(out _battlePanelManager);
        resolver.TryResolve(out _streamingSettingView);
        resolver.TryResolve(out _battleResultView);
        resolver.TryResolve(out _battleControlView);
        _initialized = true;
    }

    private bool IsShopScene => _gameFlow != null && _stateManager != null && _turnPhase != null;
    private bool IsBattleScene => _battlePanelManager != null || _streamingSettingView != null || _battleResultView != null;

    // =====================================================
    // 公開操作（UI から呼ぶ）
    // =====================================================

    /// <summary>オートプレイを開始する。turnLimit=0 で無制限。</summary>
    public void Begin(DebugAutoPlayScope scope, int turnLimit)
    {
        Scope = scope;
        TurnLimit = Mathf.Max(0, turnLimit);
        StartTurn = CurrentTurnOrMinus();
        LastKnownTurn = StartTurn;
        _boughtKey = null;
        _displayedKey = null;
        _selectionOpenedByBot = false;
        _summaryFiredAtIndex = -1;
        _pauseKind = PauseKind.None;
        PauseReason = "";
        Running = true;
        _activeDriver = this;
        RecordMoney(force: true);

        string what = scope switch
        {
            DebugAutoPlayScope.ProcurementOnly => "仕入れだけ自動",
            DebugAutoPlayScope.DisplayOnly => "陳列だけ自動",
            _ => TurnLimit > 0 ? $"{TurnLimit}ターン自動" : "無制限で自動",
        };
        AddLog($"開始: {what}（戦略: {StrategyLabel(Strategy)}）");
        SetStatus("開始");
    }

    public static void Stop(string reason)
    {
        if (!Running) return;
        Running = false;
        _pauseKind = PauseKind.None;
        PauseReason = "";
        Status = $"停止: {reason}";
        AddLog($"停止: {reason}");
    }

    public static void PauseManual() => Pause(PauseKind.Manual, "手動で一時停止");

    public static void Resume()
    {
        if (!Running || !Paused) return;
        _pauseKind = PauseKind.None;
        PauseReason = "";
        AddLog("再開");
    }

    public static string StrategyLabel(DebugAutoPlayStrategy s) => s switch
    {
        DebugAutoPlayStrategy.DisplayOnly => "陳列のみ（買わない）",
        DebugAutoPlayStrategy.Skip => "何もしない（素通し）",
        _ => "標準（お任せ仕入れ＋オート陳列）",
    };

    /// <summary>経過ターン（開始ターンからの差）。不明なら -1。</summary>
    public static int ElapsedTurns => StartTurn >= 0 && LastKnownTurn >= 0 ? LastKnownTurn - StartTurn : -1;

    // =====================================================
    // ライフサイクル
    // =====================================================

    private void OnEnable()
    {
        _errorGraceUntil = Time.unscaledTime + SceneErrorGraceSeconds;
        Application.logMessageReceived += OnLogMessage;
    }
    private void OnDisable() => Application.logMessageReceived -= OnLogMessage;

    private void Start()
    {
        // 継続中ならこのシーンのドライバが引き継ぐ
        if (Running) _activeDriver = this;
        _errorGraceUntil = Time.unscaledTime + SceneErrorGraceSeconds;
        LoopAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void OnDestroy()
    {
        if (_activeDriver == this) _activeDriver = null;
    }

    private async UniTaskVoid LoopAsync(CancellationToken ct)
    {
        // シーン読み込み直後は各 Presenter の Start / Entry を待つ
        if (await UniTask.Delay(TimeSpan.FromSeconds(1.0), DelayType.UnscaledDeltaTime, cancellationToken: ct)
                .SuppressCancellationThrow())
            return;

        while (!ct.IsCancellationRequested)
        {
            if (Running && _activeDriver == null) _activeDriver = this;

            if (Running && _activeDriver == this && _initialized)
            {
                if (Paused)
                {
                    TryAutoResume();
                }
                else
                {
                    try
                    {
                        _stallExempt = false;
                        Step();
                        CheckStall();
                    }
                    catch (Exception e)
                    {
                        Stop($"例外のため停止: {e.GetType().Name}: {e.Message}");
                        Debug.LogException(e);
                    }
                }
            }

            bool cancelled = await WaitStepAsync(ct).SuppressCancellationThrow();
            if (cancelled) return;
        }
    }

    private UniTask WaitStepAsync(CancellationToken ct)
    {
        if (!Running || Paused)
            return UniTask.Delay(TimeSpan.FromSeconds(0.2), DelayType.UnscaledDeltaTime, cancellationToken: ct);

        float wait = Mathf.Max(0.05f, StepWait);
        var type = WaitFollowsTimeScale ? DelayType.DeltaTime : DelayType.UnscaledDeltaTime;
        return UniTask.Delay(TimeSpan.FromSeconds(wait), type, cancellationToken: ct);
    }

    private void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (!Running || Paused || !PauseOnErrorLog || _activeDriver != this) return;
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (condition != null && condition.StartsWith(LogPrefix)) return;
        // シーン切替直後は旧シーンの残り処理・新シーンの初期化由来のエラーが出やすい（ゲーム既存の問題）ので、
        // 数秒だけ一時停止の対象から外す（ログ自体はコンソールに残る）
        if (Time.unscaledTime < _errorGraceUntil) return;

        string first = condition ?? "";
        int nl = first.IndexOf('\n');
        if (nl >= 0) first = first.Substring(0, nl);
        if (first.Length > 120) first = first.Substring(0, 120) + "…";
        Pause(PauseKind.Error, $"エラーログを検出: {first}");
    }

    private void TryAutoResume()
    {
        // ポップアップ待ち・画面待ちの一時停止は、原因が消えたら自動で再開する
        if (_pauseKind == PauseKind.Popup && PopUpManager.ActiveManager?.CurrentData == null)
        {
            Resume();
        }
        else if (_pauseKind == PauseKind.Screen && IsShopScene)
        {
            var sub = _stateManager.CurrentTomsShopPhase.Value;
            if (sub == TomsShopGamePhase.Shop || sub == TomsShopGamePhase.BlackSmith || sub == TomsShopGamePhase.TurnEndSummary)
                Resume();
        }
    }

    private void CheckStall()
    {
        if (!Running || Paused || _stallExempt)
        {
            _stallKey = null;
            return;
        }
        string key = Status;
        if (key != _stallKey)
        {
            _stallKey = key;
            _stallSince = Time.unscaledTime;
            return;
        }
        if (Time.unscaledTime - _stallSince > StallSeconds)
        {
            _stallKey = null;
            Pause(PauseKind.Error, $"{StallSeconds:0}秒進行が変わらないため一時停止（{Status}）");
        }
    }

    // =====================================================
    // 1ステップ
    // =====================================================

    private void Step()
    {
        if (HandleScenario()) return;
        if (HandlePopup()) return;

        if (IsShopScene)
        {
            StepShop();
            return;
        }
        if (IsBattleScene)
        {
            StepBattle();
            return;
        }

        Stop($"このシーン（{SceneManager.GetActiveScene().name}）は自動進行に未対応");
    }

    // ---------------- Utage 会話 ----------------

    private bool HandleScenario()
    {
        if (_scenarioPlayer == null)
            _scenarioPlayer = FindFirstObjectByType<ScenarioPlayer>(FindObjectsInactive.Include);
        if (_scenarioPlayer == null || !_scenarioPlayer.IsPlaying.CurrentValue) return false;

        _stallExempt = true;
        if (!AutoAdvanceScenario)
        {
            SetStatus("会話中（自動送りOFF・手で進めてください）");
            return true;
        }

        var engine = GetField<AdvEngine>(_scenarioPlayer, "engine");
        if (engine == null)
        {
            SetStatus("会話中（AdvEngine 不明のため待機）");
            return true;
        }

        var selection = engine.SelectionManager;
        if (selection != null && selection.IsWaitInput && selection.TotalCount > 0)
        {
            selection.SelectWithTotalIndex(0);
            AddLog("会話の選択肢: 先頭を選択");
            SetStatus("会話: 選択肢を選択");
            return true;
        }

        engine.Page?.InputSendMessage();
        SetStatus("会話を自動送り中");
        return true;
    }

    // ---------------- 汎用ポップアップ ----------------

    private bool HandlePopup()
    {
        var data = PopUpManager.ActiveManager?.CurrentData;
        if (data == null) return false;

        // PopupScene の読み込み完了前に閉じるとアンロードが失敗するので待つ
        if (!SceneManager.GetSceneByName(PopupSceneName).isLoaded)
        {
            SetStatus("ポップアップ読み込み待ち");
            _stallExempt = true;
            return true;
        }

        string title = data.Title ?? "";
        bool awaitingStream = _gameFlow != null && _gameFlow.IsAwaitingStream.Value;

        if (awaitingStream && data.ConfirmButtonText == "鍛冶屋へ寄る")
        {
            // 配信日の朝: 標準戦略なら鍛冶屋で仕入れてから配信、それ以外はそのまま配信へ
            bool visit = Strategy == DebugAutoPlayStrategy.Standard && _boughtKey != ActionKey();
            AddLog(visit ? "配信日: 鍛冶屋へ寄る" : "配信日: このまま配信へ");
            SetStatus(visit ? "配信日: 鍛冶屋へ寄る" : "配信日: このまま配信へ");
            if (visit) data.OnConfirm?.Invoke(); else data.OnCancel?.Invoke();
            return true;
        }
        if (awaitingStream && data.ConfirmButtonText == "配信を始める")
        {
            AddLog("配信を始める");
            SetStatus("配信を始める");
            data.OnConfirm?.Invoke();
            return true;
        }
        if (title == "品出しゼロで配信します")
        {
            AddLog("品出しゼロで配信（確認を承認）");
            SetStatus("品出しゼロの確認を承認");
            data.OnConfirm?.Invoke();
            return true;
        }
        if (data.ConfirmButtonText == "補充する" || (IsBattleScene && title.Contains("補充")))
        {
            AddLog("配信中の補充確認: やめる");
            SetStatus("補充確認を閉じる");
            data.OnCancel?.Invoke();
            return true;
        }
        if (data.IsCloseOnly)
        {
            AddLog($"お知らせを閉じる「{title}」");
            SetStatus($"お知らせを閉じる「{title}」");
            data.OnConfirm?.Invoke();
            return true;
        }

        Pause(PauseKind.Popup, $"確認ダイアログ「{title}」は自動で選べません。手で選ぶと自動で再開します");
        return true;
    }

    // =====================================================
    // TomsShop
    // =====================================================

    private void StepShop()
    {
        if (_stateManager.CurrentPhase.Value != GamePhase.TomsShop)
        {
            Stop($"店フェーズを離れた（{_stateManager.CurrentPhase.Value}）");
            return;
        }

        int turn = _gameFlow.CurrentTurn.Value;
        LastKnownTurn = turn;
        RecordMoney(force: false);

        var sub = _stateManager.CurrentTomsShopPhase.Value;
        var phase = _turnPhase.CurrentTurnPhase.Value;

        if (sub != TomsShopGamePhase.TurnEndSummary) _summaryFiredAtIndex = -1;

        // ターン数の上限（新しいターンの頭で止める）
        if (Scope == DebugAutoPlayScope.Continuous && TurnLimit > 0 && StartTurn >= 0 && turn >= StartTurn + TurnLimit
            && sub == TomsShopGamePhase.Shop)
        {
            Stop($"{TurnLimit}ターン進めたので停止（T{StartTurn}→T{turn}）");
            return;
        }

        // ---- 割り込みパネル（会話・ポップアップの次に優先） ----
        if (sub == TomsShopGamePhase.Shop)
        {
            if (HandleDebtPanel()) return;
            if (HandleEventPanel(phase)) return;
            if (HandleRelicChoice()) return;
            if (HandleMorningReport()) return;
        }

        switch (sub)
        {
            case TomsShopGamePhase.Shop:
                StepHome(phase);
                break;
            case TomsShopGamePhase.BlackSmith:
                StepBlackSmith();
                break;
            case TomsShopGamePhase.TurnEndSummary:
                if (Scope != DebugAutoPlayScope.Continuous)
                {
                    Stop("営業サマリー中のため単発操作は行わない");
                    return;
                }
                StepSummary();
                break;
            default:
                Pause(PauseKind.Screen, $"店ホーム以外の画面（{sub}）を開いています。閉じると自動で再開します");
                break;
        }
    }

    private void StepHome(TurnPhase phase)
    {
        string key = ActionKey();

        // 陳列パネルを開いた次のステップで閉じる（画面上で見えるように2ステップに分ける）
        if (_selectionOpenedByBot)
        {
            _selectionOpenedByBot = false;
            _itemSelectionPresenter?.OnCloseSelectionPanel();
            SetStatus("陳列: パネルを閉じる");
            if (Scope == DebugAutoPlayScope.DisplayOnly)
                Stop("陳列だけ自動: 完了");
            return;
        }

        switch (Scope)
        {
            case DebugAutoPlayScope.ProcurementOnly:
                if (_boughtKey == key)
                {
                    Stop("仕入れだけ自動: 完了");
                    return;
                }
                OpenBlackSmithOrBuyDirect(key);
                return;

            case DebugAutoPlayScope.DisplayOnly:
                DoAutoDisplay(key);
                return;
        }

        switch (phase)
        {
            case TurnPhase.Event:
                // 保留イベントは HandleEventPanel が処理済み。残っていなければ仕入れへ
                SetStatus($"T{_gameFlow.CurrentTurn.Value} イベント: 次へ");
                _turnPhase.AdvanceTurnPhase();
                break;

            case TurnPhase.Procurement:
                if (Strategy == DebugAutoPlayStrategy.Standard && _boughtKey != key)
                {
                    OpenBlackSmithOrBuyDirect(key);
                }
                else
                {
                    SetStatus($"T{_gameFlow.CurrentTurn.Value} 仕入れ: 次へ");
                    Advance();
                }
                break;

            case TurnPhase.Display:
                if (Strategy != DebugAutoPlayStrategy.Skip && _displayedKey != key)
                {
                    DoAutoDisplay(key);
                }
                else
                {
                    SetStatus($"T{_gameFlow.CurrentTurn.Value} 陳列: 次へ");
                    Advance();
                }
                break;

            case TurnPhase.Sales:
                // 演出（暗転）の後に TurnEndSummary へ遷移する。連打ガードは Presenter 側にもあるが一定間隔で再送のみ
                if (Time.unscaledTime - _salesFiredTime > RefireSeconds)
                {
                    _salesFiredTime = Time.unscaledTime;
                    if (_tomsShopView == null)
                    {
                        Pause(PauseKind.Error, "TomsShopView が見つからず営業開始できません");
                        return;
                    }
                    _tomsShopView.OnStartShopClicked.OnNext(Unit.Default);
                    AddLog($"T{_gameFlow.CurrentTurn.Value} 営業開始");
                }
                SetStatus($"T{_gameFlow.CurrentTurn.Value} 営業: 演出待ち");
                _stallExempt = true;
                break;
        }
    }

    private void Advance()
    {
        if (_turnPhaseView != null) _turnPhaseView.OnAdvanceClicked.OnNext(Unit.Default);
        else _turnPhase.AdvanceTurnPhase();
    }

    private void OpenBlackSmithOrBuyDirect(string key)
    {
        if (_stateManager.HasHandler(TomsShopGamePhase.BlackSmith) && _tomsShopView != null && _blackSmithView != null)
        {
            SetStatus($"T{_gameFlow.CurrentTurn.Value} 仕入れ: 鍛冶屋を開く");
            _tomsShopView.OnBlacksmithClicked.OnNext(Unit.Default);
            return;
        }

        // 鍛冶屋画面が無い構成ではモデルのお任せ仕入れを直接呼ぶ（鍛冶屋の HandleAutoBuy と同じAPI）
        if (_itemModel == null || _tomsModel == null)
        {
            Pause(PauseKind.Error, "鍛冶屋も ItemModel も無いため仕入れできません");
            return;
        }
        int before = _tomsModel.PlayerMoney.Value;
        int budget = ComputeBudget();
        var results = _itemModel.AutoPurchase(budget, _tomsModel.BlacksmithLevel.Value, _tomsModel, null, _relicResolver, AutoBuyStrategy.Recommend);
        _boughtKey = key;
        AddLog($"T{_gameFlow.CurrentTurn.Value} お任せ仕入れ（直接）: {results.Count}件 {before - _tomsModel.PlayerMoney.Value:N0}G");
        SetStatus("仕入れ完了");
    }

    private void DoAutoDisplay(string key)
    {
        _displayedKey = key;
        if (_itemSelectionPresenter != null && _itemSelectionView != null)
        {
            _itemSelectionPresenter.OnOpenSelectionPanel();
            _itemSelectionView.OnAutoDisplayRequested.OnNext(Unit.Default);
            _selectionOpenedByBot = true;
        }
        else if (_itemModel != null && _tomsModel != null)
        {
            _itemModel.AutoSetDisplay(_tomsModel.BlacksmithLevel.Value, ComputeMaxDisplayKinds());
            _tomsShopView?.RefreshDeskDisplay(_itemModel.RuntimeItems);
            if (Scope == DebugAutoPlayScope.DisplayOnly) Stop("陳列だけ自動: 完了");
        }
        else
        {
            Pause(PauseKind.Error, "陳列に必要な ItemModel が見つかりません");
            return;
        }

        int kinds = _itemModel != null ? _itemModel.CountDisplayedKinds() : 0;
        AddLog($"T{CurrentTurnOrMinus()} オート陳列: {kinds}種");
        SetStatus($"T{CurrentTurnOrMinus()} 陳列: オート陳列");
    }

    private void StepBlackSmith()
    {
        string key = ActionKey();
        bool wantBuy = (Scope == DebugAutoPlayScope.ProcurementOnly || Strategy == DebugAutoPlayStrategy.Standard)
                       && Scope != DebugAutoPlayScope.DisplayOnly;

        if (_blackSmithView == null)
        {
            Pause(PauseKind.Screen, "BlackSmithView が見つかりません。鍛冶屋を手で閉じてください");
            return;
        }

        if (wantBuy && _boughtKey != key)
        {
            int before = _tomsModel != null ? _tomsModel.PlayerMoney.Value : 0;
            int budget = ComputeBudget();
            _blackSmithView.OnAutoBuyBudgetConfirmed.OnNext((budget, AutoBuyStrategy.Recommend));
            _boughtKey = key;
            int spent = _tomsModel != null ? before - _tomsModel.PlayerMoney.Value : 0;
            AddLog($"T{CurrentTurnOrMinus()} お任せ仕入れ: 予算{budget:N0}G → 支出{spent:N0}G");
            SetStatus($"T{CurrentTurnOrMinus()} 仕入れ: お任せ購入");
            return;
        }

        SetStatus($"T{CurrentTurnOrMinus()} 仕入れ: 鍛冶屋を閉じる");
        _blackSmithView.OnCloseRequested.OnNext(Unit.Default);
    }

    private void StepSummary()
    {
        if (_summaryView == null)
        {
            Pause(PauseKind.Error, "TurnEndSummaryView が見つかりません");
            return;
        }

        int idx = _gameFlow.CurrentIndex;
        if (_summaryFiredAtIndex == -1)
        {
            _summaryFiredAtIndex = idx;
            _summaryFiredTime = Time.unscaledTime;
            AddLog($"T{_gameFlow.CurrentTurn.Value} 営業サマリー確認 → 次のターンへ（所持金 {Money():N0}G）");
            SetStatus($"T{_gameFlow.CurrentTurn.Value} サマリー: 次のターンへ");
            _summaryView.OnConfirmClicked.OnNext(Unit.Default);
            return;
        }

        // NextTurn でフロー位置が進んだ → シーン遷移（配信）や画面切替を待つ（二重の日送り防止のため再送しない）
        if (idx != _summaryFiredAtIndex)
        {
            SetStatus("次のターンへ遷移中");
            _stallExempt = Time.unscaledTime - _summaryFiredTime < StallSeconds;
            return;
        }

        // フロー位置が変わらないまま一定時間 → 確認が届かなかったとみなして再送
        if (Time.unscaledTime - _summaryFiredTime > RefireSeconds)
        {
            _summaryFiredAtIndex = -1;
        }
        SetStatus("サマリー: 応答待ち");
    }

    // ---------------- 割り込みパネル ----------------

    private bool HandleDebtPanel()
    {
        if (_debtView == null) return false;
        var panel = GetField<GameObject>(_debtView, "debtPanel");
        if (panel == null || !panel.activeInHierarchy) return false;

        var bankruptcy = GetField<GameObject>(_debtView, "bankruptcyPanel");
        if (bankruptcy != null && bankruptcy.activeSelf)
        {
            Stop("納税できず破産（ゲームオーバー画面の手前で停止）");
            return true;
        }

        var pay = GetField<Button>(_debtView, "payButton");
        if (pay != null && pay.interactable)
        {
            AddLog($"T{CurrentTurnOrMinus()} 納税を支払う（所持金 {Money():N0}G）");
            SetStatus("納税を支払う");
            _debtView.OnPayClicked.OnNext(Unit.Default);
            return true;
        }

        var close = GetField<Button>(_debtView, "closeButton");
        if (close != null && close.gameObject.activeSelf)
        {
            SetStatus("納税パネルを閉じる");
            _debtView.OnCloseClicked.OnNext(Unit.Default);
            return true;
        }

        Pause(PauseKind.Error, "納税パネルを自動で閉じられません");
        return true;
    }

    private bool HandleEventPanel(TurnPhase phase)
    {
        if (_pendingEvent == null || !_pendingEvent.HasPendingEvent || phase != TurnPhase.Event) return false;
        if (_eventView == null)
        {
            Pause(PauseKind.Error, "EventView が見つからずイベントを進められません");
            return true;
        }

        // ページ送り → 効果表示 → 確定 を本物のボタンと同じ処理で進める
        var confirm = GetField<Button>(_eventView, "confirmButton");
        if (confirm != null)
        {
            confirm.onClick.Invoke();
        }
        else
        {
            _eventView.Hide();
            _eventView.OnConfirmClicked.OnNext(Unit.Default);
        }

        if (!_pendingEvent.HasPendingEvent)
            AddLog($"T{CurrentTurnOrMinus()} イベントを確認");
        SetStatus($"T{CurrentTurnOrMinus()} イベント: 送り");
        _stallExempt = true;
        return true;
    }

    private bool HandleRelicChoice()
    {
        if (_tomsShopView == null) return false;
        var panel = GetField<GameObject>(_tomsShopView, "relicChoicePanel");
        if (panel == null || !panel.activeInHierarchy) return false;

        AddLog("レリック3択: 先頭を獲得");
        SetStatus("レリック3択: 先頭を選ぶ");
        _tomsShopView.OnRelicChoiceSelected.OnNext(0);
        return true;
    }

    private bool HandleMorningReport()
    {
        if (_tomsShopView == null) return false;
        var panel = GetField<GameObject>(_tomsShopView, "morningReportPanel");
        if (panel == null || !panel.activeInHierarchy) return false;

        SetStatus("朝レポートを閉じる");
        panel.SetActive(false);
        return true;
    }

    // =====================================================
    // FightScene（配信）
    // =====================================================

    private void StepBattle()
    {
        if (Scope != DebugAutoPlayScope.Continuous)
        {
            Stop("配信シーンでは単発操作は行わない");
            return;
        }

        bool setting = PanelActive("streamingSettingPanel");
        bool streaming = PanelActive("streamingPanel");
        bool result = PanelActive("streamingResultPanel");

        if (result)
        {
            if (Time.unscaledTime - _battleResultFiredTime > RefireSeconds)
            {
                _battleResultFiredTime = Time.unscaledTime;
                var confirm = _battleResultView != null ? GetField<Button>(_battleResultView, "confirmButton") : null;
                if (confirm == null)
                {
                    Pause(PauseKind.Error, "配信リザルトの確認ボタンが見つかりません");
                    return;
                }
                AddLog("配信リザルトを閉じて店へ戻る");
                confirm.onClick.Invoke();
            }
            SetStatus("配信リザルト: 店へ戻る");
            _stallExempt = true;
            return;
        }

        if (streaming)
        {
            // 在庫切れの補充ポップアップは「補充しない」で閉じる（配信中の追加支出をしない）
            var restock = _battleControlView != null ? GetField<RestockQuantityPopup>(_battleControlView, "restockQuantityPopup") : null;
            var restockPanel = restock != null ? GetField<GameObject>(restock, "panel") : null;
            if (restockPanel != null && restockPanel.activeInHierarchy)
            {
                InvokeMethod(restock, "Close", 0);
                AddLog("配信中の補充: しない");
                SetStatus("配信中: 補充ポップアップを閉じる");
                return;
            }

            SetStatus("配信中（自動で進行するのを待機）");
            _stallExempt = true;
            return;
        }

        if (setting)
        {
            if (_streamingSettingView == null)
            {
                Pause(PauseKind.Error, "StreamingSettingView が見つかりません");
                return;
            }
            // RunAsync が購読を始める前（チュートリアル会話中など）は届かないので一定間隔で再送する
            if (Time.unscaledTime - _battleSettingFiredTime > RefireSeconds)
            {
                _battleSettingFiredTime = Time.unscaledTime;
                _streamingSettingView.OnAutoSelectClicked.OnNext(Unit.Default);
                _streamingSettingView.OnConfirmClicked.OnNext(Unit.Default);
                AddLog("配信の品出し: オート選択して確定");
            }
            SetStatus("配信準備: 品出しを確定");
            _stallExempt = true;
            return;
        }

        SetStatus("配信シーン: 待機中");
        _stallExempt = true;
    }

    private bool PanelActive(string fieldName)
    {
        if (_battlePanelManager == null) return false;
        var go = GetField<GameObject>(_battlePanelManager, fieldName);
        return go != null && go.activeInHierarchy;
    }

    // =====================================================
    // ヘルパー
    // =====================================================

    /// <summary>「この日の行動」を識別するキー（フロー位置＋配信前の寄り道中か）。</summary>
    private string ActionKey()
    {
        if (_gameFlow == null) return "none";
        return $"{_gameFlow.CurrentIndex}:{_gameFlow.IsAwaitingStream.Value}";
    }

    private int CurrentTurnOrMinus()
    {
        if (_gameFlow != null) return _gameFlow.CurrentTurn.Value;
        if (_tomsModel != null) return _tomsModel.CurrentTurn.Value;
        return LastKnownTurn;
    }

    private int Money() => _tomsModel != null ? _tomsModel.PlayerMoney.Value : 0;

    /// <summary>
    /// お任せ仕入れの予算。直近（2ターン以内）の納税額は残す（AutoPlayGreedyBot と同じ考え方）。
    /// </summary>
    private int ComputeBudget()
    {
        if (_tomsModel == null) return 0;
        int money = _tomsModel.PlayerMoney.Value;
        int reserve = 0;
        try
        {
            int cycle = _tomsModel.DebtCycle.Value;
            int interval = GameConst.DebtPaymentInterval;
            int turnsUntilDebt = (cycle + 1) * interval - CurrentTurnOrMinus();
            if (interval > 0 && turnsUntilDebt <= 2)
                reserve = DebtCalculator.GetAmount(cycle + 1, _tomsModel, _relicResolver);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{LogPrefix} 納税額の計算に失敗したため準備金なしで仕入れます: {e.Message}");
        }
        long budget = (long)Mathf.Max(0, money - reserve) * Mathf.Clamp(BudgetPercent, 0, 100) / 100;
        return (int)budget;
    }

    private int ComputeMaxDisplayKinds()
    {
        if (_shopLevelSettings == null || _tomsModel == null) return int.MaxValue;
        int baseKinds = _shopLevelSettings.GetEntry(_tomsModel.ShopLevel.Value).maxDisplayKinds;
        return _relicResolver != null
            ? Mathf.Max(1, _relicResolver.ModifyInt(RelicStatId.DisplayKindsAdd, baseKinds))
            : baseKinds;
    }

    private void RecordMoney(bool force)
    {
        if (_tomsModel == null) return;
        int turn = CurrentTurnOrMinus();
        int money = _tomsModel.PlayerMoney.Value;
        if (!force && MoneyHistory.Count > 0 && MoneyHistory[MoneyHistory.Count - 1].turn == turn) return;
        if (MoneyHistory.Count > 0 && MoneyHistory[MoneyHistory.Count - 1].turn == turn)
            MoneyHistory[MoneyHistory.Count - 1] = (turn, money);
        else
            MoneyHistory.Add((turn, money));
        while (MoneyHistory.Count > MaxMoneyHistory) MoneyHistory.RemoveAt(0);
    }

    private static void Pause(PauseKind kind, string reason)
    {
        if (!Running) return;
        _pauseKind = kind;
        PauseReason = reason;
        Status = $"一時停止: {reason}";
        AddLog($"一時停止: {reason}");
    }

    private static void SetStatus(string status) => Status = status;

    private static void AddLog(string line)
    {
        string stamped = $"{DateTime.Now:HH:mm:ss} {line}";
        LogLines.Add(stamped);
        while (LogLines.Count > MaxLogLines) LogLines.RemoveAt(0);
        Debug.Log($"{LogPrefix} {line}");
    }

    // ---- リフレクション（既存 View の SerializeField を読むだけ。書き換えはしない） ----

    private static readonly Dictionary<(Type, string), FieldInfo> FieldCache = new();
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static T GetField<T>(object target, string name) where T : class
    {
        if (target == null) return null;
        // 破棄済み UnityEngine.Object を弾く
        if (target is UnityEngine.Object uo && uo == null) return null;

        var key = (target.GetType(), name);
        if (!FieldCache.TryGetValue(key, out var field))
        {
            field = target.GetType().GetField(name, AnyInstance);
            FieldCache[key] = field;
        }
        var value = field?.GetValue(target) as T;
        if (value is UnityEngine.Object o && o == null) return null;
        return value;
    }

    private static void InvokeMethod(object target, string name, params object[] args)
    {
        var method = target?.GetType().GetMethod(name, AnyInstance);
        if (method == null) throw new MissingMethodException(target?.GetType().Name, name);
        method.Invoke(target, args);
    }
}
#endif

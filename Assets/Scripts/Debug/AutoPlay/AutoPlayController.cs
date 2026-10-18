#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Utage;

/// <summary>
/// オートプレイの常駐ドライバ（AutoPlay.Start() が DontDestroyOnLoad で生成する。手動で配置しない）。仕様: Docs/AutoPlay_Usage.md
///
/// 毎フレーム:
///  1. timeScale を設定値に保つ / シーン・フェーズ・ターンを観測してイベントを出す / ターン上限で停止
///  2. 会話（Utage）が再生中なら自動で送る（選択肢は戦略が選ぶ）。会話中は他の操作をしない
///  3. シーンごとの行動（ボタンを「押す」）。ActionWait ごとに1行動
///     - タイトル / 村 / 出店準備 / リザルト / ゲームオーバー: このクラスがボタンを押す
///     - 店（TomsShop）/ 配信（FightScene）: 既存の DebugAutoPlayer をエンジンとして起動・監視し、
///       一時停止したら自動復帰（未知の確認ダイアログに答える / 店の別画面を閉じる / 汎用の閉じる・次へ）
///  4. 詰まり検知: 状態が StallSeconds 変わらなければログを出し、汎用の「閉じる/次へ」を試す
/// </summary>
[AddComponentMenu("")]
public sealed partial class AutoPlayController : MonoBehaviour
{
    private const string PopupSceneName = "PopupScene";
    private const float EngineRestartInterval = 3f;
    private const int MaxEngineRestartsPerTurn = 8;
    private const float RetryPressInterval = 3f;

    private CancellationTokenSource _cts;
    private bool _ended;

    // 開始前の値（停止時に戻す）
    private float _prevTimeScale = 1f;
    private float _prevStepWait;
    private bool _prevWaitFollows;
    private bool _prevAutoAdvance;
    private bool _prevPauseOnError;
    private DebugAutoPlayStrategy _prevEngineStrategy;
    private bool _engineStaticsSaved;

    // シーン
    private int _sceneHandle = int.MinValue;
    private string _sceneName = "";
    private float _sceneEnteredAt;
    private int _sceneStep;
    private float _sceneRetryAt;
    private bool _sceneScenarioSeen;
    private string _lastLoggedSceneAction;

    // 行動タイミング（実時間で管理し、WaitFollowsTimeScale なら timeScale で割る）
    private float _nextActionAt;

    // 会話
    private ScenarioPlayer _scenarioPlayer;
    private AdvEngine _advEngine;
    private bool _scenarioActive;
    private float _nextScenarioSendAt;

    // 店・配信エンジン（DebugAutoPlayer）
    private DebugAutoPlayer _driver;
    private bool _engineOwned;
    private bool _lastEngineRunning;
    private float _engineBeginAt = -999f;
    private float _enginePausedSince = -1f;
    private int _engineRestarts;
    private int _engineRestartsTurn = int.MinValue;

    // 観測
    private BattlePanelManager _battlePanels;
    private string _lastPhaseLabel;
    private string _titleDecision; // "continue" / "new"

    // 詰まり検知
    private string _stallKey;
    private float _stallSince;
    private int _stallRecoveries;

    private static float Now => Time.unscaledTime;

    // =====================================================
    // 開始 / 停止（AutoPlay から呼ぶ）
    // =====================================================

    internal void Begin()
    {
        _ended = false;
        _prevTimeScale = Time.timeScale;
        if (!_engineStaticsSaved)
        {
            _prevStepWait = DebugAutoPlayer.StepWait;
            _prevWaitFollows = DebugAutoPlayer.WaitFollowsTimeScale;
            _prevAutoAdvance = DebugAutoPlayer.AutoAdvanceScenario;
            _prevPauseOnError = DebugAutoPlayer.PauseOnErrorLog;
            _prevEngineStrategy = DebugAutoPlayer.Strategy;
            _engineStaticsSaved = true;
        }

        _sceneHandle = int.MinValue;
        _engineOwned = false;
        _lastEngineRunning = DebugAutoPlayer.Running;
        _engineBeginAt = -999f;
        _enginePausedSince = -1f;
        _engineRestarts = 0;
        _stallKey = null;
        _stallRecoveries = 0;
        _lastPhaseLabel = null;
        _nextActionAt = 0f;

        ApplySettings();

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        LoopAsync(_cts.Token).Forget();
    }

    /// <summary>設定を即時反映する（timeScale・エンジンの設定）。</summary>
    internal void ApplySettings()
    {
        if (!AutoPlay.IsPaused) Time.timeScale = AutoPlay.Settings.TimeScale;
        ConfigureEngineStatics();
    }

    internal void End()
    {
        _ended = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        // AutoPlay が動かしていたエンジンを止め、設定を開始前に戻す
        if (_engineOwned && DebugAutoPlayer.Running) DebugAutoPlayer.Stop("AutoPlay を停止");
        DebugAutoPlayer.ExternalStrategy = null;
        UninstallProcurementHooks();
        if (_engineStaticsSaved)
        {
            DebugAutoPlayer.StepWait = _prevStepWait;
            DebugAutoPlayer.WaitFollowsTimeScale = _prevWaitFollows;
            DebugAutoPlayer.AutoAdvanceScenario = _prevAutoAdvance;
            DebugAutoPlayer.PauseOnErrorLog = _prevPauseOnError;
            DebugAutoPlayer.Strategy = _prevEngineStrategy;
            _engineStaticsSaved = false;
        }
        Time.timeScale = _prevTimeScale;
    }

    private void OnDestroy()
    {
        // AutoPlay.Stop 以外で破棄された（Play 終了など）ときも設定を戻してから通知する
        if (!_ended && AutoPlay.IsRunning) End();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        AutoPlay.OnControllerDestroyed(this);
    }

    private void ConfigureEngineStatics()
    {
        var s = AutoPlay.Settings;
        DebugAutoPlayer.ExternalStrategy = AutoPlay.Strategy;
        DebugAutoPlayer.Strategy = DebugAutoPlayStrategy.Standard; // 実際の判断は ExternalStrategy
        DebugAutoPlayer.StepWait = Mathf.Max(0.05f, s.ActionWait);
        DebugAutoPlayer.WaitFollowsTimeScale = s.WaitFollowsTimeScale;
        DebugAutoPlayer.AutoAdvanceScenario = false; // 会話はこのクラスが送る
        DebugAutoPlayer.PauseOnErrorLog = s.PauseOnErrorLog;
        InstallProcurementHooks();
    }

    // =====================================================
    // ループ
    // =====================================================

    private async UniTaskVoid LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickAsync(ct);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                AutoPlay.AddLog($"例外（続行）: {e.GetType().Name}: {e.Message}");
                _nextActionAt = Now + 1f;
                try { await UniTask.Yield(PlayerLoopTiming.Update, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async UniTask TickAsync(CancellationToken ct)
    {
        if (!AutoPlay.IsRunning) return;

        Observe();
        if (!AutoPlay.IsRunning) return; // ターン上限などで停止した
        if (AutoPlay.IsPaused) return;
        // ローディング画面での遷移中は旧シーンを操作しない（新シーン有効化後も、フェードアウトが済むまで待つ）
        if (SceneLoader.IsLoading) return;

        if (!Mathf.Approximately(Time.timeScale, AutoPlay.Settings.TimeScale))
            Time.timeScale = AutoPlay.Settings.TimeScale;
        ConfigureEngineStatics();

        bool talking = HandleScenario();

        await CheckStallAsync(talking, ct);
        if (!AutoPlay.IsRunning || talking) return;

        if (Now < _nextActionAt) return;
        if (Now - _sceneEnteredAt < AutoPlay.Settings.SceneSettleSeconds) return;

        bool acted = await StepSceneAsync(ct);
        _nextActionAt = Now + (acted ? ScaledWait(AutoPlay.Settings.ActionWait) : 0.25f);
    }

    private static float ScaledWait(float seconds)
    {
        var s = AutoPlay.Settings;
        float wait = Mathf.Max(0.05f, seconds);
        return s.WaitFollowsTimeScale ? wait / Mathf.Max(0.01f, Time.timeScale) : wait;
    }

    // =====================================================
    // 観測（シーン・フェーズ・ターン）
    // =====================================================

    private void Observe()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.handle != _sceneHandle)
        {
            _sceneHandle = scene.handle;
            _sceneName = scene.name;
            _sceneEnteredAt = Now;
            _sceneStep = 0;
            _sceneRetryAt = 0f;
            _sceneScenarioSeen = false;
            _lastLoggedSceneAction = null;
            _driver = null;
            _battlePanels = null;
            ResetStreamState();
            _engineBeginAt = -999f;
            _enginePausedSince = -1f;
            AutoPlay.CurrentSceneName = _sceneName;
            AutoPlay.AddLog($"シーン: {_sceneName}");
        }

        var state = AutoPlayUi.Resolve<StateManager>();
        var turnPhase = AutoPlayUi.Resolve<TurnPhaseManager>();
        var flow = AutoPlayUi.Resolve<GameFlowManager>();

        AutoPlay.CurrentGamePhase = state != null ? state.CurrentPhase.Value : (GamePhase?)null;
        AutoPlay.CurrentShopPhase = state != null ? state.CurrentTomsShopPhase.Value : (TomsShopGamePhase?)null;
        AutoPlay.CurrentTurnPhase = turnPhase != null ? turnPhase.CurrentTurnPhase.Value : (TurnPhase?)null;

        // ターン
        if (flow != null)
        {
            int turn = flow.CurrentTurn.Value;
            if (turn != AutoPlay.CurrentTurn)
            {
                AutoPlay.CurrentTurn = turn;
                if (AutoPlay.StartTurn < 0) AutoPlay.StartTurn = turn;
                else AutoPlay.AddLog($"ターン {turn}（経過 {AutoPlay.TurnsElapsed}）");
                AutoPlay.NotifyTurnChanged(turn);

                int limit = AutoPlay.Settings.TurnLimit;
                if (limit > 0 && AutoPlay.TurnsElapsed >= limit)
                {
                    AutoPlay.Stop($"{limit}ターン進めたので停止（T{AutoPlay.StartTurn}→T{turn}）");
                    return;
                }
            }
        }

        // フェーズラベル
        string label = BuildPhaseLabel(state, turnPhase);
        if (_scenarioActive) label += "+会話";
        AutoPlay.CurrentPhaseLabel = label;
        if (label != _lastPhaseLabel)
        {
            _lastPhaseLabel = label;
            AutoPlay.NotifyPhaseChanged(label);
        }

        // デバッグメニュー「オート」タブで手動停止された → AutoPlay も止める
        bool engineRunning = DebugAutoPlayer.Running;
        if (_lastEngineRunning && !engineRunning && _engineOwned && DebugAutoPlayer.Status == "停止: 手動で停止")
        {
            _lastEngineRunning = false;
            AutoPlay.Stop("デバッグメニューのオート進行が手動で停止された");
            return;
        }
        _lastEngineRunning = engineRunning;
    }

    private string BuildPhaseLabel(StateManager state, TurnPhaseManager turnPhase)
    {
        if (_sceneName == "TomsShop" && state != null)
        {
            var main = state.CurrentPhase.Value;
            if (main != GamePhase.TomsShop) return $"TomsShop/{main}";
            var sub = state.CurrentTomsShopPhase.Value;
            if (sub == TomsShopGamePhase.Shop && turnPhase != null)
                return $"TomsShop/Shop/{turnPhase.CurrentTurnPhase.Value}";
            return $"TomsShop/{sub}";
        }
        if (_sceneName == "FightScene")
        {
            if (_battlePanels == null) _battlePanels = FindFirstObjectByType<BattlePanelManager>();
            var panels = _battlePanels;
            if (AutoPlayUi.IsActive(panels, "streamingResultPanel")) return "FightScene/Result";
            if (AutoPlayUi.IsActive(panels, "streamingPanel")) return "FightScene/Streaming";
            if (AutoPlayUi.IsActive(panels, "streamingSettingPanel")) return "FightScene/Setting";
            return "FightScene";
        }
        return _sceneName;
    }

    // =====================================================
    // 会話（Utage）
    // =====================================================

    /// <summary>会話中なら送って true（このフレームは他の行動をしない）。</summary>
    private bool HandleScenario()
    {
        if (_scenarioPlayer == null)
            _scenarioPlayer = FindFirstObjectByType<ScenarioPlayer>(FindObjectsInactive.Include);
        bool playing = _scenarioPlayer != null && _scenarioPlayer.IsPlaying.CurrentValue && !_scenarioPlayer.IsPausing;
        if (!playing)
        {
            if (_scenarioActive) _nextActionAt = Mathf.Max(_nextActionAt, Now + ScaledWait(AutoPlay.Settings.ActionWait));
            _scenarioActive = false;
            return false;
        }

        var s = AutoPlay.Settings;
        if (!_scenarioActive)
        {
            _scenarioActive = true;
            _sceneScenarioSeen = true;
            _nextScenarioSendAt = Now + ScaledWait(s.ScenarioReadInterval);
        }

        if (s.ScenarioMode == AutoPlayScenarioMode.Off)
        {
            AutoPlay.Status = "会話中（自動送りOFF・手で送ってください）";
            return true;
        }

        if (_advEngine == null && _scenarioPlayer != null)
            _advEngine = _scenarioPlayer.GetComponentInChildren<AdvEngine>(true);
        if (_advEngine == null)
        {
            AutoPlay.Status = "会話中（AdvEngine が見つからないため待機）";
            return true;
        }

        // バックログ（マウスホイール）やメッセージ非表示（右クリック）が開いていると送れないので通常表示へ戻す
        var ui = _advEngine.UiManager;
        if (ui != null && ui.Status != AdvUiManager.UiStatus.Default)
        {
            AutoPlay.AddLog($"会話: {ui.Status} を閉じて通常表示へ戻す");
            ui.Status = AdvUiManager.UiStatus.Default;
            _nextScenarioSendAt = Now + ScaledWait(s.ScenarioReadInterval);
            return true;
        }

        bool fast = s.ScenarioMode == AutoPlayScenarioMode.Fast;
        var selection = _advEngine.SelectionManager;
        if (selection != null && selection.IsWaitInput && selection.TotalCount > 0)
        {
            if (!fast && Now < _nextScenarioSendAt) return true;
            int count = selection.TotalCount;
            int index = Mathf.Clamp(AutoPlay.Strategy.ChooseScenarioSelection(CreateContext(), count), 0, count - 1);
            selection.SelectWithTotalIndex(index);
            AutoPlay.AddLog($"会話の選択肢: {index + 1}/{count} 番目を選択");
            _nextScenarioSendAt = Now + ScaledWait(s.ScenarioReadInterval);
            AutoPlay.Status = "会話: 選択肢を選択";
            return true;
        }

        if (fast || Now >= _nextScenarioSendAt)
        {
            if (_advEngine.Page != null) _advEngine.Page.InputSendMessage();
            _nextScenarioSendAt = Now + ScaledWait(s.ScenarioReadInterval);
        }
        AutoPlay.Status = fast ? "会話を早送り中" : "会話を自動送り中";
        return true;
    }

    // =====================================================
    // シーン別の行動
    // =====================================================

    private UniTask<bool> StepSceneAsync(CancellationToken ct)
    {
        switch (_sceneName)
        {
            case "TomsShop": return StepEngineSceneAsync(shopScene: true, ct);
            case "FightScene": return StepEngineSceneAsync(shopScene: false, ct);
        }

        // 店・配信以外ではエンジンを止めておく（未対応シーンで自停止するログを避ける）
        if (_engineOwned && DebugAutoPlayer.Running) DebugAutoPlayer.Stop("AutoPlay: 店/配信以外のシーン");

        if (HandleOutOfShopPopup()) return UniTask.FromResult(true);

        switch (_sceneName)
        {
            case "TitleScene": return StepTitleAsync(ct);
            case "VillageScene": return StepVillageAsync(ct);
            case "PreparationScene": return StepPreparationAsync(ct);
            case "ResultScene": return StepResultAsync(ct);
            case "GameOver": return StepGameOverAsync(ct);
            case "EventScene": return StepEventSceneAsync(ct);
        }

        AutoPlay.Status = $"待機（{_sceneName}）";
        return UniTask.FromResult(false);
    }

    // ---------------- 店 / 配信（DebugAutoPlayer をエンジンとして監視） ----------------

    private async UniTask<bool> StepEngineSceneAsync(bool shopScene, CancellationToken ct)
    {
        if (shopScene)
        {
            // 破産パネル: 押せるのは「結果へ」だけ（ゲームオーバー画面で会話を流してから停止/次ランへ）
            var debt = AutoPlayUi.Resolve<DebtView>();
            if (debt != null && AutoPlayUi.IsActive(debt, "bankruptcyPanel"))
            {
                if (DebugAutoPlayer.Running) DebugAutoPlayer.Stop("AutoPlay: 破産処理へ");
                return await PressAsync(AutoPlayUi.GetField<Button>(debt, "goToResultButton"), "破産 → ゲームオーバーへ", ct);
            }

            var state = AutoPlayUi.Resolve<StateManager>();
            if (state != null && state.CurrentPhase.Value != GamePhase.TomsShop)
            {
                AutoPlay.Status = $"店フェーズ外（{state.CurrentPhase.Value}）: 待機";
                return false;
            }
        }

        if (_driver == null) _driver = FindFirstObjectByType<DebugAutoPlayer>();
        if (_driver == null)
        {
            AutoPlay.Status = "DebugAutoPlayer（デバッグメニュー）の生成待ち";
            return false;
        }

        if (!DebugAutoPlayer.Running)
        {
            if (Now - _engineBeginAt < EngineRestartInterval) return false;

            int turn = AutoPlay.CurrentTurn;
            if (turn != _engineRestartsTurn) { _engineRestartsTurn = turn; _engineRestarts = 0; }
            if (_engineOwned && ++_engineRestarts > MaxEngineRestartsPerTurn)
            {
                AutoPlay.Stop($"店/配信エンジンが同じターンで{MaxEngineRestartsPerTurn}回以上停止した（最後: {DebugAutoPlayer.Status}）");
                return false;
            }
            if (_engineOwned) AutoPlay.AddLog($"エンジン再開（直前: {DebugAutoPlayer.Status}）");

            _engineBeginAt = Now;
            ConfigureEngineStatics();
            _driver.Begin(DebugAutoPlayScope.Continuous, 0); // ターン上限は AutoPlay 側で数える
            _engineOwned = true;
            _lastEngineRunning = true;
            AutoPlay.Status = "店/配信エンジンを開始";
            return false;
        }
        _engineOwned = true;

        if (DebugAutoPlayer.Paused)
        {
            string reason = DebugAutoPlayer.PauseReason ?? "";
            if (reason == "手動で一時停止")
            {
                AutoPlay.Status = "エンジンが手動で一時停止中（デバッグメニューで再開）";
                return false;
            }
            if (_enginePausedSince < 0f) _enginePausedSince = Now;
            if (Now - _enginePausedSince < AutoPlay.Settings.PauseRecoverySeconds)
            {
                AutoPlay.Status = $"エンジン一時停止: {reason}";
                return false;
            }

            _enginePausedSince = -1f;
            AutoPlay.AddLog($"エンジンの一時停止から自動復帰を試す: {reason}");
            await RecoverEnginePauseAsync(shopScene, ct);
            if (DebugAutoPlayer.Running && DebugAutoPlayer.Paused) DebugAutoPlayer.Resume();
            return true;
        }

        _enginePausedSince = -1f;
        AutoPlay.Status = DebugAutoPlayer.Status;
        // 配信中は介入・補充・棚の入れ替えなどを行う（AutoPlayController.Streaming.cs）
        return !shopScene && await StepStreamingAsync(ct);
    }

    private async UniTask RecoverEnginePauseAsync(bool shopScene, CancellationToken ct)
    {
        // 1) 自動で選べない確認ダイアログ
        var data = PopUpManager.ActiveManager?.CurrentData;
        if (data != null)
        {
            if (AutoPlay.Settings.AutoAnswerUnknownPopups) AnswerPopup(data, preferCancel: true);
            return;
        }

        // 2) 店ホーム以外の画面（新聞・地図など）を開いている
        var state = shopScene ? AutoPlayUi.Resolve<StateManager>() : null;
        if (state != null && state.CurrentPhase.Value == GamePhase.TomsShop)
        {
            var sub = state.CurrentTomsShopPhase.Value;
            if (sub != TomsShopGamePhase.Shop && sub != TomsShopGamePhase.BlackSmith && sub != TomsShopGamePhase.TurnEndSummary)
            {
                if (await TryGenericAdvanceAsync($"店の画面（{sub}）を閉じる", ct)) return;
                AutoPlay.AddLog($"店の画面（{sub}）を閉じるボタンが無いため店ホームへ戻す");
                state.ChangeTomsShopPhase(TomsShopGamePhase.Shop);
                return;
            }
        }

        // 3) エラーログ・詰まりなど → 汎用の閉じる/次へ
        await TryGenericAdvanceAsync("エンジン一時停止からの復帰", ct);
    }

    // ---------------- タイトル ----------------

    private async UniTask<bool> StepTitleAsync(CancellationToken ct)
    {
        var view = FindFirstObjectByType<TitleView>();
        if (view == null)
        {
            AutoPlay.Status = "タイトル画面の準備待ち";
            return false;
        }

        var action = AutoPlay.Settings.TitleAction;
        if (action == AutoPlayTitleAction.Stop)
        {
            AutoPlay.Stop("タイトル画面に到達");
            return false;
        }

        if (AutoPlayUi.IsActive(view, "saveDataScreenGroup"))
        {
            bool loadMode = AutoPlayUi.IsActive(view, "loadModeHeader")
                            || (!AutoPlayUi.IsActive(view, "newGameModeHeader") && _titleDecision == "continue");
            int slot = PickSlot(loadMode);
            if (slot < 0)
            {
                AutoPlay.Stop(loadMode ? "続きから遊べるスロットが無い" : "空きスロットが無い（上書きはしない）");
                return false;
            }
            if (Now < _sceneRetryAt) return false;
            _sceneRetryAt = Now + RetryPressInterval;

            foreach (var slotView in FindObjectsByType<SaveSlotView>(FindObjectsSortMode.None))
            {
                if (AutoPlayUi.GetFieldValue(slotView, "_slotIndex") is int index && index == slot)
                {
                    var button = AutoPlayUi.GetField<Button>(slotView, "selectButton");
                    if (await PressAsync(button, $"タイトル: スロット{slot + 1}を選択（{(loadMode ? "続きから" : "初めから")}）", ct))
                        return true;
                }
            }
            // ボタンが見つからない構成ではビューの通知を直接出す（ボタン押下と同じ経路）
            AutoPlay.AddLog($"タイトル: スロット{slot + 1}を選択（ボタン不明のため通知で代替）");
            view.OnSaveSlotSelected.OnNext(slot);
            return true;
        }

        if (AutoPlayUi.IsActive(view, "startMethodScreenGroup"))
        {
            bool any = SaveSlotManager.AnyExists();
            if (action == AutoPlayTitleAction.ContinueOnly && !any)
            {
                AutoPlay.Stop("続きから遊べるセーブが無い");
                return false;
            }
            bool wantContinue = action == AutoPlayTitleAction.ContinueOnly
                                || (action == AutoPlayTitleAction.ContinueOrNewGame && any);
            _titleDecision = wantContinue ? "continue" : "new";
            return wantContinue
                ? await PressFieldAsync(view, "continueButton", "タイトル: 続きから", ct)
                : await PressFieldAsync(view, "newGameButton", "タイトル: 初めから", ct);
        }

        if (AutoPlayUi.IsActive(view, "startScreenGroup"))
        {
            if (AutoPlayUi.IsClickable(AutoPlayUi.GetField<Button>(view, "startButton")))
                return await PressFieldAsync(view, "startButton", "タイトル: スタート", ct);

            // スタート画面は「任意のキー/クリックで進む」作り（TitleView.Update）。startButton が別画面の
            // ボタン（非表示）を指している構成もあるので、キー入力と同じ通知（OnStartRequested）で進める。
            if (Now < _sceneRetryAt) return false;
            _sceneRetryAt = Now + RetryPressInterval;
            AutoPlay.AddLog("タイトル: スタート（任意キー入力と同じ通知）");
            view.OnStartRequested.OnNext(R3.Unit.Default);
            return true;
        }

        AutoPlay.Status = "タイトル: 画面待ち";
        return false;
    }

    /// <summary>使うスロット。続きから=データのあるスロット / 初めから=データの無いスロット（上書きはしない）。</summary>
    private static int PickSlot(bool loadMode)
    {
        int max = SaveSlotManager.MaxSlots;
        int preferred = AutoPlay.Settings.PreferredSlot;
        bool Valid(int s) => s >= 0 && s < max && SaveSlotManager.Exists(s) == loadMode;

        if (Valid(preferred)) return preferred;
        if (loadMode && Valid(SaveSlotManager.CurrentSlot)) return SaveSlotManager.CurrentSlot;
        for (int i = 0; i < max; i++)
        {
            if (Valid(i)) return i;
        }
        return -1;
    }

    // ---------------- 村 ----------------

    private async UniTask<bool> StepVillageAsync(CancellationToken ct)
    {
        var view = FindFirstObjectByType<VillageView>();
        if (view == null)
        {
            AutoPlay.Status = "村: 画面待ち";
            return false;
        }

        if (AutoPlayUi.IsActive(view, "conversionPopup"))
            return await PressFieldAsync(view, "conversionCloseButton", "村: 帰還報告を閉じる", ct);
        if (view.IsInvestPanelOpen)
            return await PressFieldAsync(view, "investCloseButton", "村: 投資パネルを閉じる", ct);

        // 帰還演出・会話中は Presenter が出店を無視するので、一定間隔で押し直す
        if (Now < _sceneRetryAt)
        {
            AutoPlay.Status = "村: 出店準備へ（反応待ち）";
            return false;
        }
        _sceneRetryAt = Now + RetryPressInterval;
        return await PressFieldAsync(view, "departButton", "村: 出店準備へ", ct);
    }

    // ---------------- 出店準備 ----------------

    private async UniTask<bool> StepPreparationAsync(CancellationToken ct)
    {
        var view = FindFirstObjectByType<PreparationView>();
        if (view == null)
        {
            AutoPlay.Status = "出店準備: 画面待ち";
            return false;
        }

        var depart = AutoPlayUi.GetField<Button>(view, "departButton");
        if (!AutoPlayUi.IsClickable(depart))
        {
            AutoPlay.Status = "出店準備: 操作できるまで待機（会話・ロック中）";
            return false;
        }

        var ctx = CreateContext();
        switch (_sceneStep)
        {
            case 0:
                _sceneStep = 1;
                if (AutoPlay.Strategy.ShouldCarryMaxFunds(ctx))
                    return await PressFieldAsync(view, "borrowPlusButton", "出店準備: 村資金を上限まで持ち込む", ct);
                return false;

            case 1:
            {
                _sceneStep = 2;
                var parent = view.RelicCatalogParent;
                var slots = parent != null ? parent.GetComponentsInChildren<PreparationChoiceSlot>() : Array.Empty<PreparationChoiceSlot>();
                int index = AutoPlay.Strategy.ChooseStarterRelic(ctx, slots.Length);
                if (index < 0 || index >= slots.Length) return false;
                var button = AutoPlayUi.GetField<Button>(slots[index], "selectButton");
                return await PressAsync(button, $"出店準備: スターターレリック {index + 1}/{slots.Length} を選択", ct);
            }

            default:
                if (Now < _sceneRetryAt)
                {
                    AutoPlay.Status = "出店準備: 出店（反応待ち）";
                    return false;
                }
                _sceneRetryAt = Now + RetryPressInterval;
                return await PressAsync(depart, "出店準備: 出店する", ct);
        }
    }

    // ---------------- リザルト / ゲームオーバー ----------------

    private async UniTask<bool> StepResultAsync(CancellationToken ct)
    {
        var view = FindFirstObjectByType<ResultView>();
        var button = AutoPlayUi.GetField<Button>(view, "retryButton") ?? AutoPlayUi.GetField<Button>(view, "goToTitleButton");
        if (!AutoPlayUi.IsClickable(button))
        {
            AutoPlay.Status = "リザルト: 演出待ち";
            return false;
        }
        if (AutoPlay.Settings.RunEndAction == AutoPlayRunEndAction.Stop)
        {
            AutoPlay.Stop("ラン終了（リザルト表示まで完了）");
            return false;
        }
        if (Now < _sceneRetryAt) return false;
        _sceneRetryAt = Now + RetryPressInterval;
        return await PressAsync(button, "リザルト: 村へ帰還して次のランへ", ct);
    }

    private async UniTask<bool> StepGameOverAsync(CancellationToken ct)
    {
        var view = FindFirstObjectByType<GameOverView>();
        var button = AutoPlayUi.GetField<Button>(view, "retryButton") ?? AutoPlayUi.GetField<Button>(view, "goToTitleButton");
        // 破産の会話が始まる前に動かないよう、会話を見届ける（会話が無い構成でも数秒で進む）
        bool talkDone = _sceneScenarioSeen || Now - _sceneEnteredAt > 6f;
        if (!talkDone || !AutoPlayUi.IsClickable(button))
        {
            AutoPlay.Status = "ゲームオーバー: 演出待ち";
            return false;
        }
        if (AutoPlay.Settings.RunEndAction == AutoPlayRunEndAction.Stop)
        {
            AutoPlay.Stop("ラン終了（ゲームオーバー）");
            return false;
        }
        if (Now < _sceneRetryAt) return false;
        _sceneRetryAt = Now + RetryPressInterval;
        return await PressAsync(button, "ゲームオーバー: 村へ帰還して次のランへ", ct);
    }

    private async UniTask<bool> StepEventSceneAsync(CancellationToken ct)
    {
        var view = FindFirstObjectByType<EventSceneView>();
        if (Now < _sceneRetryAt) return false;
        _sceneRetryAt = Now + RetryPressInterval;
        return await PressFieldAsync(view, "confirmButton", "イベント: 確認", ct);
    }

    // ---------------- 店・配信以外の確認ダイアログ ----------------

    private bool HandleOutOfShopPopup()
    {
        var data = PopUpManager.ActiveManager?.CurrentData;
        if (data == null) return false;
        if (!SceneManager.GetSceneByName(PopupSceneName).isLoaded)
        {
            AutoPlay.Status = "ポップアップ読み込み待ち";
            return true;
        }

        string title = data.Title ?? "";
        if (title.Contains("削除") || title.Contains("上書き"))
        {
            AutoPlay.AddLog($"確認「{title}」: 戻る（データは消さない）");
            data.OnCancel?.Invoke();
            return true;
        }
        if (data.IsCloseOnly)
        {
            AnswerPopup(data, preferCancel: false);
            return true;
        }
        if (!AutoPlay.Settings.AutoAnswerUnknownPopups)
        {
            AutoPlay.Status = $"確認「{title}」待ち（手で選んでください）";
            return true;
        }
        AnswerPopup(data, preferCancel: true);
        return true;
    }

    private static void AnswerPopup(PopUpData data, bool preferCancel)
    {
        if (!SceneManager.GetSceneByName(PopupSceneName).isLoaded) return;
        string title = data.Title ?? "";
        bool hasCancel = !data.IsCloseOnly && !string.IsNullOrEmpty(data.CancelButtonText);
        if (preferCancel && hasCancel)
        {
            AutoPlay.AddLog($"確認「{title}」: {data.CancelButtonText}");
            data.OnCancel?.Invoke();
        }
        else
        {
            AutoPlay.AddLog($"確認「{title}」: {(string.IsNullOrEmpty(data.ConfirmButtonText) ? "OK" : data.ConfirmButtonText)}");
            data.OnConfirm?.Invoke();
        }
    }

    // =====================================================
    // 詰まり検知
    // =====================================================

    private async UniTask CheckStallAsync(bool talking, CancellationToken ct)
    {
        var s = AutoPlay.Settings;
        string engineStatus = DebugAutoPlayer.Running ? DebugAutoPlayer.Status : "-";
        bool exempt = talking
                      || AutoPlay.IsPaused
                      || (DebugAutoPlayer.Running && engineStatus.StartsWith("配信中"));
        if (exempt || s.StallSeconds <= 0f)
        {
            _stallKey = null;
            return;
        }

        string key = $"{_sceneName}|{AutoPlay.CurrentPhaseLabel}|{AutoPlay.CurrentTurn}|{engineStatus}|{_sceneStep}";
        if (key != _stallKey)
        {
            _stallKey = key;
            _stallSince = Now;
            return;
        }
        if (Now - _stallSince < s.StallSeconds) return;

        _stallSince = Now;
        _stallRecoveries++;
        Debug.LogWarning($"[AutoPlay] 詰まり検知: {s.StallSeconds:0}秒状態が変わらない（{AutoPlay.CurrentPhaseLabel} / {AutoPlay.Status}）" +
                         $" 復帰試行 {_stallRecoveries}/{s.MaxStallRecoveries}");
        AutoPlay.AddLog($"詰まり検知（{AutoPlay.CurrentPhaseLabel}）→ 汎用の閉じる/次へを試す");

        if (_stallRecoveries > Mathf.Max(0, s.MaxStallRecoveries))
        {
            AutoPlay.Stop($"詰まりから復帰できないため停止（{AutoPlay.CurrentPhaseLabel} / {AutoPlay.Status}）");
            return;
        }

        var data = PopUpManager.ActiveManager?.CurrentData;
        if (data != null && s.AutoAnswerUnknownPopups)
        {
            AnswerPopup(data, preferCancel: true);
            return;
        }
        await TryGenericAdvanceAsync("詰まり復帰", ct);
    }

    // =====================================================
    // ヘルパー
    // =====================================================

    private async UniTask<bool> TryGenericAdvanceAsync(string purpose, CancellationToken ct)
    {
        var button = AutoPlayUi.FindGenericAdvanceButton(out string label);
        if (button == null)
        {
            AutoPlay.AddLog($"{purpose}: 押せる「閉じる/次へ」ボタンが見つからない");
            return false;
        }
        return await PressAsync(button, $"{purpose}: {label}", ct);
    }

    private UniTask<bool> PressFieldAsync(object view, string buttonField, string description, CancellationToken ct) =>
        PressAsync(AutoPlayUi.GetField<Button>(view, buttonField), description, ct);

    private async UniTask<bool> PressAsync(Button button, string description, CancellationToken ct)
    {
        if (!AutoPlayUi.IsClickable(button))
        {
            AutoPlay.Status = $"{description}（ボタンが押せないため待機）";
            return false;
        }
        AutoPlay.Status = description;
        bool pressed = await AutoPlayUi.PressAsync(button, AutoPlay.Settings.ButtonHoldSeconds, ct);
        if (pressed && description != _lastLoggedSceneAction)
        {
            _lastLoggedSceneAction = description;
            AutoPlay.AddLog(description);
        }
        return pressed;
    }

    private AutoPlayContext CreateContext()
    {
        var toms = AutoPlayUi.Resolve<TomsModel>();
        var flow = AutoPlayUi.Resolve<GameFlowManager>();
        return AutoPlay.CreateContext(
            AutoPlay.CurrentTurn,
            toms != null ? toms.PlayerMoney.Value : 0,
            0,
            toms != null ? toms.BlacksmithLevel.Value : 0,
            toms != null ? toms.ShopLevel.Value : 0,
            flow != null && flow.IsAwaitingStream.Value);
    }
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 開発用オートプレイの静的 API（エディタ拡張・デバッグメニュー・録画ツールから呼ぶ口）。仕様: Docs/AutoPlay_Usage.md
///
/// - Start() で常駐の AutoPlayController（DontDestroyOnLoad）を生成し、タイトル〜村〜準備〜店〜配信〜リザルトを自動で進める。
/// - 店（TomsShop）と配信（FightScene）の1ステップ処理は既存の DebugAutoPlayer（デバッグメニュー「オート」タブ）を
///   エンジンとして再利用し、判断だけ IAutoPlayStrategy に差し替える（DebugAutoPlayer.ExternalStrategy）。
/// - Start() を呼ばない限り何も生成・購読しない（通常プレイには一切影響しない）。
/// - Play 中のみ有効。状態は Play ごとに初期化されるが、イベントの購読者（エディタ側）は消さない。
/// </summary>
public static class AutoPlay
{
    private const int MaxLogLines = 50;

    // =====================================================
    // 設定・戦略
    // =====================================================

    /// <summary>現在の設定（実行中に書き換えてもよい。次の行動から反映）。</summary>
    public static AutoPlaySettings Settings { get; private set; } = new();

    /// <summary>現在の戦略。null を代入すると標準戦略に戻る。</summary>
    public static IAutoPlayStrategy Strategy
    {
        get => _strategy ??= DefaultStrategy();
        set => _strategy = value ?? DefaultStrategy();
    }
    private static IAutoPlayStrategy _strategy;

    /// <summary>デバッグメニューで選べる戦略の一覧（差し替えたい戦略はここに Add してもよい）。</summary>
    public static readonly List<IAutoPlayStrategy> AvailableStrategies = new()
    {
        new DefaultAutoPlayStrategy(),
        new RandomAutoPlayStrategy(),
        new PassThroughAutoPlayStrategy(),
        // 人格プレイ（ローカルの Jev 互換モデルが判断。設定は UserSettings/PersonaPlay.json、tools/persona_play/README.md）
        new PersonaStrategy(PersonaProfile.Zundamon),
        new PersonaStrategy(PersonaProfile.Metan),
        // 相談プレイ（ずんだもんが遊び、判断ごとに四国めたんに相談する。会話は Recordings/consult_log_*.jsonl）
        new PersonaStrategy(PersonaProfile.Zundamon, PersonaProfile.Metan),
    };

    private static IAutoPlayStrategy DefaultStrategy() =>
        AvailableStrategies.Count > 0 && AvailableStrategies[0] != null ? AvailableStrategies[0] : new DefaultAutoPlayStrategy();

    /// <summary>戦略用の乱数（Start ごとに Settings.RandomSeed で作り直す）。</summary>
    public static System.Random Random { get; private set; } = new();

    // =====================================================
    // 状態（読み取り専用）
    // =====================================================

    /// <summary>オートプレイ中か（一時停止中も true）。</summary>
    public static bool IsRunning { get; internal set; }

    /// <summary>一時停止中か。</summary>
    public static bool IsPaused { get; internal set; }

    /// <summary>いま何をしているか（人が読む用の短い文）。</summary>
    public static string Status { get; internal set; } = "停止中";

    /// <summary>最後に停止した理由。</summary>
    public static string LastStopReason { get; internal set; } = "";

    /// <summary>現在のターン（GameFlowManager.CurrentTurn。店以外のシーンでは最後に分かった値。不明なら -1）。</summary>
    public static int CurrentTurn { get; internal set; } = -1;

    /// <summary>開始時（または最初にターンが分かった時）のターン。不明なら -1。</summary>
    public static int StartTurn { get; internal set; } = -1;

    /// <summary>開始してから進んだターン数（不明なら 0）。</summary>
    public static int TurnsElapsed => StartTurn >= 0 && CurrentTurn >= 0 ? Mathf.Max(0, CurrentTurn - StartTurn) : 0;

    /// <summary>アクティブシーン名。</summary>
    public static string CurrentSceneName { get; internal set; } = "";

    /// <summary>メインフェーズ（店・配信シーン以外では null）。</summary>
    public static GamePhase? CurrentGamePhase { get; internal set; }

    /// <summary>店のサブフェーズ（店シーン以外では null）。</summary>
    public static TomsShopGamePhase? CurrentShopPhase { get; internal set; }

    /// <summary>ターン内フェーズ（イベント/仕入れ/陳列/営業。店シーン以外では null）。</summary>
    public static TurnPhase? CurrentTurnPhase { get; internal set; }

    /// <summary>
    /// 現在フェーズの表示用ラベル。例: "TomsShop/Shop/Procurement", "TomsShop/BlackSmith", "FightScene", "VillageScene"。
    /// 会話中は末尾に "+会話" が付く。
    /// </summary>
    public static string CurrentPhaseLabel { get; internal set; } = "";

    /// <summary>開始してからの実時間（秒）。</summary>
    public static float ElapsedRealtime => IsRunning ? Time.realtimeSinceStartup - _startRealtime : _lastElapsed;
    internal static float _startRealtime;
    internal static float _lastElapsed;

    /// <summary>最近のログ（古い順）。</summary>
    public static IReadOnlyList<string> LogLines => _logLines;
    private static readonly List<string> _logLines = new();

    // =====================================================
    // イベント（エディタの録画ウィンドウ等から購読する。Play をまたいでも購読者は保持）
    // =====================================================

    /// <summary>開始した。</summary>
    public static event Action Started;
    /// <summary>停止した（引数: 理由）。</summary>
    public static event Action<string> Stopped;
    /// <summary>ターンが変わった（引数: 新しいターン）。</summary>
    public static event Action<int> TurnChanged;
    /// <summary>フェーズラベルが変わった（引数: CurrentPhaseLabel）。</summary>
    public static event Action<string> PhaseChanged;
    /// <summary>ログが出た（引数: 1行）。</summary>
    public static event Action<string> Logged;

    private static AutoPlayController _controller;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        IsRunning = false;
        IsPaused = false;
        Status = "停止中";
        LastStopReason = "";
        CurrentTurn = -1;
        StartTurn = -1;
        CurrentSceneName = "";
        CurrentGamePhase = null;
        CurrentShopPhase = null;
        CurrentTurnPhase = null;
        CurrentPhaseLabel = "";
        _startRealtime = 0f;
        _lastElapsed = 0f;
        _logLines.Clear();
        _controller = null;
    }

    // =====================================================
    // 操作
    // =====================================================

    /// <summary>
    /// オートプレイを開始する（Play 中のみ）。既に実行中なら設定・戦略だけ差し替えて true。
    /// </summary>
    /// <param name="settings">null なら現在の Settings をそのまま使う。渡した場合はコピーして使う。</param>
    /// <param name="strategy">null なら現在の Strategy のまま。</param>
    public static bool Start(AutoPlaySettings settings = null, IAutoPlayStrategy strategy = null)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[AutoPlay] Play 中のみ開始できます。");
            return false;
        }

        if (settings != null) Settings = settings.Clone();
        if (strategy != null) Strategy = strategy;

        if (IsRunning && _controller != null)
        {
            _controller.ApplySettings();
            AddLog($"設定を更新（戦略: {Strategy.Name} / x{Settings.TimeScale:0.#} / ウェイト {Settings.ActionWait:0.##}秒）");
            return true;
        }

        Random = Settings.RandomSeed != 0 ? new System.Random(Settings.RandomSeed) : new System.Random();
        IsRunning = true;
        IsPaused = false;
        LastStopReason = "";
        StartTurn = -1;
        _startRealtime = Time.realtimeSinceStartup;
        Status = "開始";

        if (_controller == null)
        {
            var go = new GameObject("[AutoPlay]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _controller = go.AddComponent<AutoPlayController>();
        }
        _controller.Begin();

        string limit = Settings.TurnLimit > 0 ? $"{Settings.TurnLimit}ターン" : "無制限";
        AddLog($"開始（戦略: {Strategy.Name} / x{Settings.TimeScale:0.#} / ウェイト {Settings.ActionWait:0.##}秒 / {limit}）");
        SafeInvoke(Started);
        return true;
    }

    /// <summary>オートプレイを止める（timeScale・DebugAutoPlayer の設定は開始前に戻す）。</summary>
    public static void Stop(string reason = "手動で停止")
    {
        if (!IsRunning) return;
        IsRunning = false;
        IsPaused = false;
        _lastElapsed = Time.realtimeSinceStartup - _startRealtime;
        LastStopReason = reason ?? "";
        Status = $"停止: {reason}";

        if (_controller != null)
        {
            _controller.End();
            if (_controller != null) UnityEngine.Object.Destroy(_controller.gameObject);
            _controller = null;
        }

        AddLog($"停止: {reason}");
        SafeInvoke(Stopped, LastStopReason);
    }

    /// <summary>開始/停止を切り替える。</summary>
    public static void Toggle()
    {
        if (IsRunning) Stop();
        else Start();
    }

    /// <summary>一時停止（ボタン操作・会話送りを止める。timeScale はそのまま）。</summary>
    public static void Pause()
    {
        if (!IsRunning || IsPaused) return;
        IsPaused = true;
        Status = "一時停止中";
        if (DebugAutoPlayer.Running && !DebugAutoPlayer.Paused) DebugAutoPlayer.PauseManual();
        AddLog("一時停止");
    }

    /// <summary>一時停止を解除する。</summary>
    public static void Resume()
    {
        if (!IsRunning || !IsPaused) return;
        IsPaused = false;
        if (DebugAutoPlayer.Running && DebugAutoPlayer.Paused) DebugAutoPlayer.Resume();
        AddLog("再開");
    }

    /// <summary>速度倍率（Time.timeScale）を変える。実行中ならすぐ反映。</summary>
    public static void SetSpeed(float timeScale)
    {
        Settings.TimeScale = Mathf.Clamp(timeScale, 0.1f, 16f);
        if (IsRunning) Time.timeScale = Settings.TimeScale;
    }

    /// <summary>行動間のウェイト（秒）を変える。</summary>
    public static void SetActionWait(float seconds)
    {
        Settings.ActionWait = Mathf.Clamp(seconds, 0.05f, 5f);
        if (IsRunning) DebugAutoPlayer.StepWait = Settings.ActionWait;
    }

    // =====================================================
    // 内部（AutoPlayController から呼ぶ）
    // =====================================================

    internal static void AddLog(string line)
    {
        string stamped = $"{DateTime.Now:HH:mm:ss} {line}";
        _logLines.Add(stamped);
        while (_logLines.Count > MaxLogLines) _logLines.RemoveAt(0);
        Debug.Log($"[AutoPlay] {line}");
        SafeInvoke(Logged, stamped);
    }

    internal static void NotifyTurnChanged(int turn) => SafeInvoke(TurnChanged, turn);
    internal static void NotifyPhaseChanged(string label) => SafeInvoke(PhaseChanged, label);

    /// <summary>コントローラーが外部要因（Play 終了など）で破棄された。</summary>
    internal static void OnControllerDestroyed(AutoPlayController controller)
    {
        if (_controller != controller) return;
        _controller = null;
        if (IsRunning) Stop("コントローラーが破棄された（Play 終了など）");
    }

    /// <summary>DebugAutoPlayer に渡す判断材料を作る。</summary>
    internal static AutoPlayContext CreateContext(int turn, int money, int taxReserve, int blacksmithLevel, int shopLevel, bool awaitingStream) =>
        new()
        {
            Turn = turn,
            Money = money,
            TaxReserve = taxReserve,
            BlacksmithLevel = blacksmithLevel,
            ShopLevel = shopLevel,
            AwaitingStream = awaitingStream,
            Random = Random,
        };

    private static void SafeInvoke(Action action)
    {
        try { action?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    private static void SafeInvoke<T>(Action<T> action, T arg)
    {
        try { action?.Invoke(arg); }
        catch (Exception e) { Debug.LogException(e); }
    }
}
#endif

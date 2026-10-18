#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// 【エディタ専用・開発用】自動録画ツール（Editor/Recording/GameplayAutoRecorder）へ
/// ゲーム内の出来事を伝える受け口。仕様: Docs/AutoRecording_Usage.md
///
/// 既存クラスには一切手を入れず、外から購読するだけで通知を作る:
/// - シーンロード: SceneManager.sceneLoaded
/// - フェーズ: StateManager.CurrentPhase / CurrentTomsShopPhase（各シーンの LifetimeScope から Resolve）
/// - ターン: GameFlowManager.CurrentTurn（同上）
/// - ラン終了: GameOver / ResultScene のロード、または GamePhase.Result への遷移
/// - 入力: 旧 Input（activeInputHandler=Both）を毎フレーム監視
///
/// Activate() が呼ばれるまで何も購読しない（録画していない時は完全に無害）。
/// ファイル全体が UNITY_EDITOR 限定なのでプレイヤービルドには含まれない。
/// </summary>
public static class GameplayRecordingSignals
{
    /// <summary>シーンがロードされた（引数: シーン名）。</summary>
    public static event Action<string> SceneLoaded;

    /// <summary>メインフェーズ（GamePhase）が変わった。</summary>
    public static event Action<GamePhase> GamePhaseChanged;

    /// <summary>トムの店サブフェーズ（TomsShopGamePhase）が変わった。</summary>
    public static event Action<TomsShopGamePhase> TomsShopPhaseChanged;

    /// <summary>ターン番号（GameFlowManager.CurrentTurn、1始まりの絶対値）が通知された。</summary>
    public static event Action<int> TurnObserved;

    /// <summary>ラン終了（引数: 理由）。</summary>
    public static event Action<string> RunEnded;

    /// <summary>最後に入力があった時刻（Time.realtimeSinceStartup）。</summary>
    public static float LastInputRealtime { get; private set; }

    /// <summary>購読中か。</summary>
    public static bool IsActive { get; private set; }

    private const int BindRetryFrames = 120;

    // バインド済みの LifetimeScope（同じスコープを二重購読しない）
    private static readonly HashSet<int> BoundScopeIds = new();
    // 録画ごとの購読（Deactivate で全解除。スコープ破棄時にも AddTo(scope) で解除される）
    private static readonly CompositeDisposable Subscriptions = new();
    private static GameplayRecordingSignalsDriver _driver;

    /// <summary>
    /// 自動録画ツールが「隔離セーブで遊ぶ」を指定したとき、Play 開始前（ExitingEditMode）にこのキーで
    /// 保存先フォルダを SessionState に置く。シーンロード前に SaveSlotManager.DevRootOverride へ流し込み、
    /// ユーザーのセーブスロットを一切読まず・書かないようにする。
    /// </summary>
    public const string IsolatedSaveRootSessionKey = "TomsLands.AutoRecorder.IsolatedSaveRoot";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ApplyIsolatedSaveRoot()
    {
        var root = UnityEditor.SessionState.GetString(IsolatedSaveRootSessionKey, "");
        if (string.IsNullOrEmpty(root)) return;
        SaveSlotManager.DevRootOverride = root;
        Debug.Log($"[AutoRecorder] 隔離セーブで Play: {root}");
    }

    /// <summary>
    /// Play 開始ごとに実行時状態だけ初期化する（イベントの購読者＝エディタ側は消さない）。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        IsActive = false;
        Subscriptions.Clear();
        BoundScopeIds.Clear();
        _driver = null;
        LastInputRealtime = 0f;
    }

    /// <summary>
    /// 購読を開始する（Play 中のみ有効・冪等）。
    /// 既にロード済みのシーンは「今の状態」を到達扱いにしないよう初回通知を読み飛ばす。
    /// </summary>
    public static void Activate()
    {
        if (!Application.isPlaying || IsActive) return;
        IsActive = true;
        LastInputRealtime = Time.realtimeSinceStartup;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (_driver == null)
        {
            var go = new GameObject("[GameplayRecordingSignals]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _driver = go.AddComponent<GameplayRecordingSignalsDriver>();
        }

        // 既存シーンの LifetimeScope へ（初回通知はスキップして）バインド
        TryBindScopes(skipInitial: true);
        _driver.RequestBind(BindRetryFrames, skipInitial: true);
    }

    /// <summary>購読を止める。</summary>
    public static void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_driver != null)
        {
            UnityEngine.Object.Destroy(_driver.gameObject);
            _driver = null;
        }
        // 次の録画で現在値（ターンの基準値など）を取り直せるよう、購読を全て解除する
        Subscriptions.Clear();
        BoundScopeIds.Clear();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsActive) return;
        SceneLoaded?.Invoke(scene.name);

        if (scene.name == "GameOver") RunEnded?.Invoke("GameOver シーンへ遷移");
        else if (scene.name == "ResultScene") RunEnded?.Invoke("ResultScene へ遷移");

        // LifetimeScope は Awake でビルドされるが、親待ち等で遅れる場合に備え数フレーム再試行
        TryBindScopes(skipInitial: false);
        if (_driver != null) _driver.RequestBind(BindRetryFrames, skipInitial: false);
    }

    internal static void NotifyInput() => LastInputRealtime = Time.realtimeSinceStartup;

    /// <summary>ロード済みの LifetimeScope から StateManager / GameFlowManager を探して購読する。</summary>
    internal static void TryBindScopes(bool skipInitial)
    {
        if (!IsActive) return;
        var scopes = UnityEngine.Object.FindObjectsByType<LifetimeScope>(FindObjectsSortMode.None);
        foreach (var scope in scopes)
        {
            if (scope == null || scope.Container == null) continue;
            var id = scope.GetInstanceID();
            if (BoundScopeIds.Contains(id)) continue;

            var bound = false;
            if (scope.Container.TryResolve<StateManager>(out var stateManager) && stateManager != null)
            {
                BindStateManager(stateManager, scope, skipInitial);
                bound = true;
            }
            if (scope.Container.TryResolve<GameFlowManager>(out var flow) && flow != null)
            {
                // ターンは「増えた分」を数えるだけなので初回値も渡す（基準値として使われる）
                flow.CurrentTurn
                    .Subscribe(turn => { if (IsActive) TurnObserved?.Invoke(turn); })
                    .AddTo(scope)
                    .AddTo(Subscriptions);
                bound = true;
            }
            if (bound) BoundScopeIds.Add(id);
        }
    }

    private static void BindStateManager(StateManager stateManager, LifetimeScope scope, bool skipInitial)
    {
        var skip = skipInitial ? 1 : 0;
        stateManager.CurrentPhase
            .Skip(skip)
            .Subscribe(phase =>
            {
                if (!IsActive) return;
                GamePhaseChanged?.Invoke(phase);
                if (phase == GamePhase.Result) RunEnded?.Invoke("GamePhase.Result へ遷移");
            })
            .AddTo(scope)
            .AddTo(Subscriptions);

        stateManager.CurrentTomsShopPhase
            .Skip(skip)
            .Subscribe(sub =>
            {
                if (!IsActive) return;
                // StateManager 側と同じく、メインが TomsShop の時だけ「その画面に入った」とみなす
                if (stateManager.CurrentPhase.Value != GamePhase.TomsShop) return;
                TomsShopPhaseChanged?.Invoke(sub);
            })
            .AddTo(scope)
            .AddTo(Subscriptions);
    }
}
#endif

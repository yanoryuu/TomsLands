using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// プレイ動画の自動録画（エディタ専用）。Unity Recorder の RecorderController を使って Game View を MP4 で録り、
/// 停止条件（OR）のどれかを満たしたら録画を止めて保存する。仕様: Docs/AutoRecording_Usage.md
///
/// ゲーム側の出来事は GameplayRecordingSignals（Assets/Scripts/Debug、エディタ専用）経由で受け取る。
/// ウィンドウを閉じていても動くよう、制御は [InitializeOnLoad] の静的クラスに置く。
/// </summary>
[InitializeOnLoad]
public static class GameplayAutoRecorder
{
    private const string LogPrefix = "[AutoRecorder]";

    private static RecorderController _controller;
    private static RecorderControllerSettings _controllerSettings;
    private static string _outputPath;
    private static int _startFrame;
    private static float _startRealtime;
    private static int _lastTurn = -1;
    private static string _pendingStopReason;
    private static double _pendingStopAt;

    /// <summary>録画中か。</summary>
    public static bool IsRecording => _controller != null;

    /// <summary>録画開始からのターン経過数。</summary>
    public static int TurnsElapsed { get; private set; }

    /// <summary>現在の出力先（拡張子付き）。</summary>
    public static string CurrentOutputPath => _outputPath;

    /// <summary>停止待ち（余韻中）の理由。無ければ null。</summary>
    public static string PendingStopReason => _pendingStopReason;

    /// <summary>録画した動画の長さ（秒）。固定FPS録画なのでフレーム数/FPS。</summary>
    public static float RecordedSeconds
    {
        get
        {
            if (!IsRecording) return 0f;
            var fps = Mathf.Max(1, GameplayAutoRecorderSettings.instance.frameRate);
            return Mathf.Max(0, Time.frameCount - _startFrame) / (float)fps;
        }
    }

    /// <summary>最後の入力からの経過秒（実時間）。</summary>
    public static float IdleSeconds =>
        IsRecording ? Time.realtimeSinceStartup - GameplayRecordingSignals.LastInputRealtime : 0f;

    /// <summary>状態が変わった（ウィンドウ再描画用）。</summary>
    public static event Action Changed;

    static GameplayAutoRecorder()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        switch (change)
        {
            case PlayModeStateChange.ExitingEditMode:
                PrepareIsolatedSaves();
                break;
            case PlayModeStateChange.EnteredEditMode:
                ClearIsolatedSaves();
                break;
            case PlayModeStateChange.EnteredPlayMode:
                if (GameplayAutoRecorderSettings.instance.recordOnPlay) StartRecording();
                break;
            case PlayModeStateChange.ExitingPlayMode:
                // 手動で Play を止めた場合もここで確実に保存する
                if (IsRecording) StopRecording(_pendingStopReason ?? "Play 停止", allowExitPlayMode: false);
                break;
        }
    }

    // ===================================================================
    // 開始
    // ===================================================================

    public static void StartRecording()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning($"{LogPrefix} Play 中のみ録画できます");
            return;
        }
        if (IsRecording) return;

        var s = GameplayAutoRecorderSettings.instance;
        try
        {
            var folder = ResolveOutputFolder(s.outputFolder);
            Directory.CreateDirectory(folder);
            var prefix = string.IsNullOrWhiteSpace(s.filePrefix) ? "TomsLands" : s.filePrefix.Trim();
            var fileNoExt = Path.Combine(folder, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}");

            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "GameplayAutoRecorder";
            movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };
            movie.CaptureAlpha = false;
            movie.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = Mathf.Max(16, s.width),
                OutputHeight = Mathf.Max(16, s.height),
            };
            movie.CaptureAudio = s.captureAudio;
            movie.OutputFile = fileNoExt;

            _controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            _controllerSettings.AddRecorderSettings(movie);
            _controllerSettings.SetRecordModeToManual();
            _controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
            _controllerSettings.FrameRate = Mathf.Max(1, s.frameRate);
            _controllerSettings.CapFrameRate = true;

            _controller = new RecorderController(_controllerSettings);
            _controller.PrepareRecording();
            if (!_controller.StartRecording())
            {
                Debug.LogError($"{LogPrefix} 録画の開始に失敗しました（Recorder の設定エラー。Console の Recorder ログを確認）");
                _controller = null;
                return;
            }

            _outputPath = fileNoExt + ".mp4";
            _startFrame = Time.frameCount;
            _startRealtime = Time.realtimeSinceStartup;
            _lastTurn = -1;
            TurnsElapsed = 0;
            _pendingStopReason = null;

            Subscribe();
            GameplayRecordingSignals.Activate();

            Debug.Log($"{LogPrefix} 録画開始 → {_outputPath} ({s.width}x{s.height} @{s.frameRate}fps) 停止条件: {DescribeConditions(s)}");

            if (s.linkAutoPlay) StartLinkedAutoPlay(s);
        }
        catch (Exception e)
        {
            Debug.LogError($"{LogPrefix} 録画の開始で例外: {e}");
            _controller = null;
        }
        Changed?.Invoke();
    }

    // ===================================================================
    // 停止
    // ===================================================================

    /// <summary>手動停止（Play は止めない）。</summary>
    public static void StopRecordingManually() => StopRecording("手動停止", allowExitPlayMode: false);

    private static void StopRecording(string reason, bool allowExitPlayMode)
    {
        if (!IsRecording) return;

        var seconds = RecordedSeconds;
        var realSeconds = Time.realtimeSinceStartup - _startRealtime;
        var path = _outputPath;

        Unsubscribe();
        GameplayRecordingSignals.Deactivate();
        try
        {
            _controller.StopRecording();
        }
        catch (Exception e)
        {
            Debug.LogError($"{LogPrefix} 録画の停止で例外: {e}");
        }
        _controller = null;
        if (_controllerSettings != null)
        {
            UnityEngine.Object.DestroyImmediate(_controllerSettings);
            _controllerSettings = null;
        }
        _pendingStopReason = null;

        var s = GameplayAutoRecorderSettings.instance;
        s.lastStopReason = reason;
        s.lastOutputPath = path;
        s.lastFinishedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        s.lastDurationSeconds = seconds;
        s.SaveSettings();

        Debug.Log($"{LogPrefix} 録画停止: 理由={reason} / 動画 {seconds:0.0} 秒（実時間 {realSeconds:0.0} 秒） / 保存先: {path}");

        // エンコーダの書き出し完了後にファイルを確認する
        // （delayCall は Play 停止と重なると取りこぼすことがあったので EditorApplication.update で確認する）
        _verifyPath = path;
        _verifyDeadline = EditorApplication.timeSinceStartup + 10.0;
        _verifyLastSize = -1;

        // 録画を止めたらオートプレイも止める（オートプレイ停止が理由なら既に止まっている）
        if (s.stopAutoPlayOnRecordStop && AutoPlay.IsRunning)
            AutoPlay.Stop($"録画停止（{reason}）");

        if (allowExitPlayMode && s.exitPlayModeOnAutoStop && EditorApplication.isPlaying)
        {
            Debug.Log($"{LogPrefix} 設定により Play を停止します");
            EditorApplication.ExitPlaymode();
        }
        Changed?.Invoke();
    }

    /// <summary>停止条件を満たした。余韻秒数後に停止する（最初に満たした理由を採用）。</summary>
    private static void RequestAutoStop(string reason)
    {
        if (!IsRecording || _pendingStopReason != null) return;
        _pendingStopReason = reason;
        _pendingStopAt = EditorApplication.timeSinceStartup + Mathf.Max(0f, GameplayAutoRecorderSettings.instance.stopDelaySeconds);
        Debug.Log($"{LogPrefix} 停止条件を満たしました: {reason}");
        Changed?.Invoke();
    }

    // ===================================================================
    // 停止条件の判定
    // ===================================================================

    private static void OnEditorUpdate()
    {
        VerifySavedFile();
        if (!IsRecording) return;
        if (!EditorApplication.isPlaying) return;

        var s = GameplayAutoRecorderSettings.instance;

        if (_pendingStopReason == null)
        {
            if (s.useMaxDuration && RecordedSeconds >= s.maxDurationSeconds)
                RequestAutoStop($"経過時間 {s.maxDurationSeconds:0.#} 秒に到達");
            // オートプレイ中は実入力が無いのが正常なので放置判定しない
            else if (s.useIdle && !EditorApplication.isPaused && !AutoPlay.IsRunning && IdleSeconds >= s.idleSeconds)
                RequestAutoStop($"入力なし {s.idleSeconds:0.#} 秒");
        }

        if (_pendingStopReason != null && EditorApplication.timeSinceStartup >= _pendingStopAt)
        {
            StopRecording(_pendingStopReason, allowExitPlayMode: true);
        }
    }

    private static string _verifyPath;
    private static double _verifyDeadline;
    private static double _verifyNextCheck;
    private static long _verifyLastSize;

    /// <summary>停止後、出力ファイルのサイズが落ち着いたら「保存完了」を出す（最大10秒待つ）。</summary>
    private static void VerifySavedFile()
    {
        if (_verifyPath == null) return;
        var now = EditorApplication.timeSinceStartup;
        if (now < _verifyNextCheck) return;
        _verifyNextCheck = now + 0.5;

        var size = File.Exists(_verifyPath) ? new FileInfo(_verifyPath).Length : -1;
        if (size > 0 && size == _verifyLastSize)
        {
            Debug.Log($"{LogPrefix} 保存完了: {_verifyPath} ({size / 1024f / 1024f:0.00} MB)");
            _verifyPath = null;
            return;
        }
        _verifyLastSize = size;
        if (now >= _verifyDeadline)
        {
            if (size <= 0) Debug.LogWarning($"{LogPrefix} 出力ファイルが無いか空です: {_verifyPath}");
            else Debug.Log($"{LogPrefix} 保存完了（書き込み継続中の可能性あり）: {_verifyPath} ({size / 1024f / 1024f:0.00} MB)");
            _verifyPath = null;
        }
    }

    private static void Subscribe()
    {
        Unsubscribe();
        GameplayRecordingSignals.TomsShopPhaseChanged += OnTomsShopPhaseChanged;
        GameplayRecordingSignals.TurnObserved += OnTurnObserved;
        GameplayRecordingSignals.SceneLoaded += OnSceneLoaded;
        GameplayRecordingSignals.RunEnded += OnRunEnded;
        AutoPlay.Stopped += OnAutoPlayStopped;
    }

    private static void Unsubscribe()
    {
        GameplayRecordingSignals.TomsShopPhaseChanged -= OnTomsShopPhaseChanged;
        GameplayRecordingSignals.TurnObserved -= OnTurnObserved;
        GameplayRecordingSignals.SceneLoaded -= OnSceneLoaded;
        GameplayRecordingSignals.RunEnded -= OnRunEnded;
        AutoPlay.Stopped -= OnAutoPlayStopped;
    }

    private static void OnAutoPlayStopped(string reason)
    {
        if (!GameplayAutoRecorderSettings.instance.useAutoPlayStopped) return;
        var r = string.IsNullOrEmpty(reason) ? AutoPlay.LastStopReason : reason;
        RequestAutoStop($"オートプレイ停止（{r}）");
    }

    // ===================================================================
    // オートプレイ連動
    // ===================================================================

    private static void StartLinkedAutoPlay(GameplayAutoRecorderSettings s)
    {
        // 速度・会話・タイトルの行動だけウィンドウの指定で上書きし、他は現在の AutoPlay.Settings を引き継ぐ
        var ap = AutoPlay.Settings.Clone();
        ap.TimeScale = Mathf.Clamp(s.autoPlayTimeScale, 0.1f, 16f);
        ap.ScenarioMode = s.autoPlayScenarioMode;
        ap.TitleAction = s.autoPlayTitleAction;
        if (AutoPlay.Start(ap))
            Debug.Log($"{LogPrefix} オートプレイ連動で開始（x{ap.TimeScale:0.#} / 会話 {ap.ScenarioMode} / タイトル {ap.TitleAction}）");
        else
            Debug.LogWarning($"{LogPrefix} オートプレイを開始できませんでした");
    }

    private static void OnTomsShopPhaseChanged(TomsShopGamePhase phase)
    {
        var s = GameplayAutoRecorderSettings.instance;
        if (s.useTomsShopPhase && phase == s.targetTomsShopPhase)
            RequestAutoStop($"フェーズ {phase} に到達");
    }

    private static void OnTurnObserved(int turn)
    {
        // 「1 だけ増えた」ときだけ 1 ターン進んだと数え、それ以外の変化は基準の付け直しにする。
        // TomsShop ⇄ FightScene の往復では GameFlowManager が作り直され、初期値 1 → 復元値（例: 4）と
        // 飛ぶ通知が来るが、これは進行ではないので数えない。新しいラン（1 に戻る）も同様に付け直し。
        if (_lastTurn >= 0 && turn == _lastTurn + 1) TurnsElapsed++;
        _lastTurn = turn;

        Changed?.Invoke();
        var s = GameplayAutoRecorderSettings.instance;
        if (s.useTurnCount && TurnsElapsed >= Mathf.Max(1, s.turnCount))
            RequestAutoStop($"{TurnsElapsed} ターン経過（ターン {turn}）");
    }

    private static void OnSceneLoaded(string sceneName)
    {
        var s = GameplayAutoRecorderSettings.instance;
        if (!s.useSceneLoaded || string.IsNullOrWhiteSpace(s.targetSceneNames)) return;
        foreach (var raw in s.targetSceneNames.Split(','))
        {
            var name = raw.Trim();
            if (name.Length > 0 && string.Equals(name, sceneName, StringComparison.OrdinalIgnoreCase))
            {
                RequestAutoStop($"シーン {sceneName} がロードされた");
                return;
            }
        }
    }

    private static void OnRunEnded(string reason)
    {
        if (GameplayAutoRecorderSettings.instance.useRunEnd)
            RequestAutoStop($"ラン終了（{reason}）");
    }

    // ===================================================================
    // 隔離セーブ（Play 開始と同時に録画する時だけ有効）
    // ===================================================================

    /// <summary>隔離セーブの保存先（プロジェクトの Temp/ 配下。git 管理外・エディタ終了で消えうる）。</summary>
    public static string IsolatedSaveRoot =>
        Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Temp", "AutoRecordSaves");

    /// <summary>
    /// 人格マラソン（PersonaMarathon）がランごとの隔離セーブの置き場所を SessionState に置くキー。
    /// 設定されていれば録画の有無にかかわらずそのフォルダで遊び、消さない（落ちた後に続きから再開するため）。
    /// </summary>
    public const string MarathonSaveRootKey = "TomsLands.PersonaMarathon.SaveRoot";

    private static void PrepareIsolatedSaves()
    {
        var marathonRoot = SessionState.GetString(MarathonSaveRootKey, "");
        if (!string.IsNullOrEmpty(marathonRoot))
        {
            try
            {
                Directory.CreateDirectory(marathonRoot);
                SessionState.SetString(GameplayRecordingSignals.IsolatedSaveRootSessionKey, marathonRoot);
                Debug.Log($"{LogPrefix} 人格マラソンの隔離セーブで Play: {marathonRoot}");
            }
            catch (Exception e)
            {
                // ユーザーのセーブに触れないよう、隔離できないときは Play を止める
                Debug.LogError($"{LogPrefix} 人格マラソンの隔離セーブを用意できないので Play を止めます: {e.Message}");
                SessionState.EraseString(GameplayRecordingSignals.IsolatedSaveRootSessionKey);
                EditorApplication.isPlaying = false;
            }
            return;
        }

        var s = GameplayAutoRecorderSettings.instance;
        if (!(s.recordOnPlay && s.isolateSaves))
        {
            SessionState.EraseString(GameplayRecordingSignals.IsolatedSaveRootSessionKey);
            return;
        }
        try
        {
            // 毎回まっさらな状態から始める（＝初回ラン。チュートリアル・プロローグ込み）
            if (Directory.Exists(IsolatedSaveRoot)) Directory.Delete(IsolatedSaveRoot, true);
            Directory.CreateDirectory(IsolatedSaveRoot);
            SessionState.SetString(GameplayRecordingSignals.IsolatedSaveRootSessionKey, IsolatedSaveRoot);
        }
        catch (Exception e)
        {
            Debug.LogError($"{LogPrefix} 隔離セーブの準備に失敗: {e.Message}");
            SessionState.EraseString(GameplayRecordingSignals.IsolatedSaveRootSessionKey);
        }
    }

    private static void ClearIsolatedSaves()
    {
        var root = SessionState.GetString(GameplayRecordingSignals.IsolatedSaveRootSessionKey, "");
        SessionState.EraseString(GameplayRecordingSignals.IsolatedSaveRootSessionKey);
        if (SaveSlotManager.DevRootOverride == IsolatedSaveRoot
            || (!string.IsNullOrEmpty(root) && SaveSlotManager.DevRootOverride == root))
            SaveSlotManager.DevRootOverride = null;
    }

    // ===================================================================
    // ユーティリティ
    // ===================================================================

    public static string ResolveOutputFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) folder = "Recordings";
        var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? ".";
        return Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(projectRoot, folder));
    }

    public static string DescribeConditions(GameplayAutoRecorderSettings s)
    {
        var list = new System.Collections.Generic.List<string>();
        if (s.useMaxDuration) list.Add($"{s.maxDurationSeconds:0.#}秒");
        if (s.useTomsShopPhase) list.Add($"フェーズ={s.targetTomsShopPhase}");
        if (s.useTurnCount) list.Add($"{s.turnCount}ターン");
        if (s.useSceneLoaded) list.Add($"シーン={s.targetSceneNames}");
        if (s.useRunEnd) list.Add("ラン終了");
        if (s.useIdle) list.Add($"放置{s.idleSeconds:0.#}秒");
        if (s.useAutoPlayStopped) list.Add("オートプレイ停止");
        return list.Count == 0 ? "なし（手動停止のみ）" : string.Join(" / ", list);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 人格マラソン（夜通しの人格プレイ）のエディタ側。外の見張り役（tools/persona_play/marathon/marathon.py）と
/// Library/PersonaMarathon/ のファイルでやり取りし、エディタを開いたまま 1 ランずつ Play に入って人格プレイを回す。
///
/// - command.json（見張り役が書く）: どの人格で・録画するか・隔離セーブの場所・何周するか・続きからか。
///   エディタが Play していない・コンパイル中でないときに読み、消してから始める。
/// - status.json（ここが書く）: starting → playing → finished / aborted / stopped / failed と、周ごとの所持金。
/// - heartbeat.json（ここが 5 秒ごとに書く）: 今のターン・フェーズ・オートプレイの状態。止まったら見張り役がエディタを落として開き直す。
/// - stop.flag（見張り役が置く）: 今のランを止めて Play を抜ける。
///
/// セーブは必ず隔離（GameplayAutoRecorder.MarathonSaveRootKey。人格ごとに 1 つ持ち続けるフォルダ、プロジェクトの Library 以下だけ）。
/// 毎ラン同じフォルダを「続きから」遊び（村・村資金・ラン数が引き継がれる）、最後の周はリザルトで精算して村に着いたところで止める。
/// 落ちた後も同じフォルダを「続きから」で再開する（command.resume）。ユーザーのセーブスロットは読まない・書かない。
/// 録画するランは実況の撮影プリセット（JikkyoRecordingPreset）と同じ設定、しないランは録画せず速め・会話は飛ばす・音は消す。
/// </summary>
[InitializeOnLoad]
public static class PersonaMarathon
{
    private const string LogPrefix = "[PersonaMarathon]";
    private const string TitleScenePath = "Assets/Scene/TitleScene.unity";
    private const string RunKey = "PersonaMarathon.Run";
    private const string StatusKey = "PersonaMarathon.Status";
    private const string MuteBackupKey = "PersonaMarathon.MuteBackup";

    public sealed class Command
    {
        [JsonProperty("id")] public string Id;
        /// <summary>試行ごとの印（見張り役が status.json を自分の試行のものか見分ける）。</summary>
        [JsonProperty("nonce")] public string Nonce;
        [JsonProperty("persona")] public string Persona;          // "zundamon" / "metan" / "consult"（ずんだもんが遊び、めたんに相談）
        [JsonProperty("record")] public bool Record;
        [JsonProperty("resume")] public bool Resume;
        [JsonProperty("saveRoot")] public string SaveRoot;        // 絶対パス（プロジェクトの Library 以下）
        [JsonProperty("laps")] public int Laps = 2;
        [JsonProperty("lapsDone")] public int LapsDone;
        [JsonProperty("timeScale")] public float TimeScale = 2f;  // 録画しないランだけ
        [JsonProperty("scenarioFast")] public bool ScenarioFast = true;
        [JsonProperty("maxMinutes")] public float MaxMinutes = 75f;
    }

    public sealed class LapResult
    {
        [JsonProperty("lap")] public int Lap;
        [JsonProperty("bankrupt")] public bool Bankrupt;
        [JsonProperty("money")] public int Money;
        [JsonProperty("delta")] public int Delta;
        [JsonProperty("at")] public string At;
    }

    public sealed class Status
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("nonce")] public string Nonce;
        [JsonProperty("state")] public string State;             // starting / playing / finished / aborted / stopped / failed
        [JsonProperty("reason")] public string Reason = "";
        [JsonProperty("persona")] public string Persona;
        [JsonProperty("record")] public bool Record;
        [JsonProperty("resume")] public bool Resume;
        [JsonProperty("saveRoot")] public string SaveRoot;
        [JsonProperty("startedAt")] public string StartedAt;
        [JsonProperty("startedUnix")] public long StartedUnix;
        [JsonProperty("updatedAt")] public string UpdatedAt;
        [JsonProperty("lapsTarget")] public int LapsTarget;
        [JsonProperty("lapsDone")] public int LapsDone;
        [JsonProperty("laps")] public List<LapResult> Laps = new();
        [JsonProperty("stopRequested")] public bool StopRequested;
        [JsonProperty("recording")] public string Recording;
        [JsonProperty("personaLog")] public string PersonaLog;
        [JsonProperty("consultLog")] public string ConsultLog;
    }

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string Dir => Path.Combine(ProjectRoot, "Library", "PersonaMarathon");
    private static string CommandPath => Path.Combine(Dir, "command.json");
    private static string StatusPath => Path.Combine(Dir, "status.json");
    private static string HeartbeatPath => Path.Combine(Dir, "heartbeat.json");
    private static string StopPath => Path.Combine(Dir, "stop.flag");

    private static double _nextPoll;
    private static double _nextBeat;
    private static double _exitAt = -1;
    private static bool _applied;
    private static int _logEvents;
    private static PersonaStrategy _strategy;

    /// <summary>相談プレイの人格 ID（マラソンの command.persona。ずんだもんが遊び、四国めたんに相談する）。</summary>
    public const string ConsultPersona = "consult";

    static PersonaMarathon()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AutoPlay.Started -= OnAutoPlayStarted;
        AutoPlay.Started += OnAutoPlayStarted;
        AutoPlay.Stopped -= OnAutoPlayStopped;
        AutoPlay.Stopped += OnAutoPlayStopped;
        AutoPlay.Logged -= OnAutoPlayLogged;
        AutoPlay.Logged += OnAutoPlayLogged;
        PersonaStrategy.RunFinished -= OnRunFinished;
        PersonaStrategy.RunFinished += OnRunFinished;
    }

    // =====================================================
    // 状態（ドメインの再読み込みをまたぐので SessionState に置く）
    // =====================================================

    private static Command CurrentRun()
    {
        string json = SessionState.GetString(RunKey, "");
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonConvert.DeserializeObject<Command>(json); }
        catch (Exception) { return null; }
    }

    private static Status CurrentStatus()
    {
        string json = SessionState.GetString(StatusKey, "");
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonConvert.DeserializeObject<Status>(json); }
        catch (Exception) { return null; }
    }

    private static void SaveStatus(Status status)
    {
        if (status == null) return;
        status.UpdatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        string json = JsonConvert.SerializeObject(status, Formatting.Indented);
        SessionState.SetString(StatusKey, json);
        WriteAtomic(StatusPath, json);
    }

    private static void WriteAtomic(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{LogPrefix} {path} を書けませんでした: {e.Message}");
        }
    }

    // =====================================================
    // 毎フレーム（エディタ）
    // =====================================================

    private static void Update()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now >= _nextBeat)
        {
            _nextBeat = now + 5.0;
            WriteHeartbeat();
        }

        if (_exitAt > 0 && now >= _exitAt)
        {
            _exitAt = -1;
            if (EditorApplication.isPlaying)
            {
                Debug.Log($"{LogPrefix} Play を抜けます");
                EditorApplication.ExitPlaymode();
            }
        }

        if (now < _nextPoll) return;
        _nextPoll = now + 2.0;

        if (File.Exists(StopPath)) HandleStop();

        var run = CurrentRun();
        if (run != null)
        {
            var status = CurrentStatus();
            // Play に入れなかった（コンパイルエラーなど）
            if (status != null && status.State == "starting" && !EditorApplication.isPlayingOrWillChangePlaymode
                && DateTimeOffset.Now.ToUnixTimeSeconds() - status.StartedUnix > 120)
                Finish("failed", "Play に入れなかった（コンパイルエラーか、Play の開始が取り消された）");
            return;
        }

        if (!File.Exists(CommandPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        Command cmd;
        try
        {
            cmd = JsonConvert.DeserializeObject<Command>(File.ReadAllText(CommandPath));
            File.Delete(CommandPath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{LogPrefix} command.json を読めません（次の確認で読み直す）: {e.Message}");
            return;
        }
        Begin(cmd);
    }

    private static void WriteHeartbeat()
    {
        var run = CurrentRun();
        var status = run != null ? CurrentStatus() : null;
        bool thinking = false;
        try { thinking = AutoPlay.IsRunning && AutoPlay.Strategy is IAutoPlayPrefetchStrategy { IsThinking: true }; }
        catch (Exception) { /* 状態が取れなくても心拍は出す */ }
        var beat = new Dictionary<string, object>
        {
            ["time"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            ["unix"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
            ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id,
            ["editor"] = EditorApplication.isCompiling ? "compiling" : EditorApplication.isPlaying ? "playing" : "idle",
            ["compileFailed"] = EditorUtility.scriptCompilationFailed,
            ["runId"] = run?.Id,
            ["state"] = status?.State,
            ["lapsDone"] = status?.LapsDone ?? 0,
            ["turn"] = AutoPlay.CurrentTurn,
            ["phase"] = AutoPlay.CurrentPhaseLabel,
            ["autoplay"] = AutoPlay.IsRunning,
            ["autoplayStatus"] = AutoPlay.Status,
            ["thinking"] = thinking,
            ["logEvents"] = _logEvents,
            ["recording"] = GameplayAutoRecorder.IsRecording,
            // フォーカスが無いときに Play が止まっているか（runInBackground が入っていれば止まらない）。見張り役はこれを固まりと見なさない
            ["focused"] = EditorApplication.isPlaying && Application.isFocused,
            ["runInBackground"] = Application.runInBackground,
            ["pausedNoFocus"] = EditorApplication.isPlaying && !Application.isFocused && !Application.runInBackground,
            ["inputGuard"] = AutoPlayInputGuard.Active,
            ["inputGuardDevices"] = AutoPlayInputGuard.DisabledCount,
            ["inputGuardProbe"] = AutoPlayInputGuard.LastProbe,
        };
        WriteAtomic(HeartbeatPath, JsonConvert.SerializeObject(beat, Formatting.Indented));
    }

    // =====================================================
    // ランの開始・終了
    // =====================================================

    private static void Begin(Command cmd)
    {
        var status = new Status
        {
            Id = cmd?.Id,
            Nonce = cmd?.Nonce,
            State = "starting",
            Persona = cmd?.Persona,
            Record = cmd?.Record ?? false,
            Resume = cmd?.Resume ?? false,
            SaveRoot = cmd?.SaveRoot,
            StartedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            StartedUnix = DateTimeOffset.Now.ToUnixTimeSeconds(),
            LapsTarget = Math.Max(1, cmd?.Laps ?? 1),
            LapsDone = Math.Max(0, cmd?.LapsDone ?? 0),
        };

        string error = Validate(cmd);
        if (error == null && EditorUtility.scriptCompilationFailed) error = "コンパイルエラーがあるので Play に入れない";
        if (error == null)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    error = $"未保存のシーン（{SceneManager.GetSceneAt(i).path}）があるので開始しない（変更を捨てないため）";
                    break;
                }
            }
        }
        if (error != null)
        {
            status.State = "failed";
            status.Reason = error;
            SaveStatus(status);
            SessionState.EraseString(StatusKey);
            Debug.LogWarning($"{LogPrefix} ランを始めません: {error}");
            return;
        }

        string saveRoot = Path.GetFullPath(cmd.SaveRoot);
        Directory.CreateDirectory(saveRoot);
        // Play 中だけ runInBackground を実行時に入れる。プロジェクトの設定（PlayerSettings）は Play を抜けたら必ず元の値に戻す
        SessionState.SetString(RunInBackgroundBackupKey, PlayerSettings.runInBackground ? "1" : "0");
        status.SaveRoot = saveRoot;

        ConfigureRecorder(cmd);
        SessionState.SetString(GameplayAutoRecorder.MarathonSaveRootKey, saveRoot);
        SessionState.SetString(RunKey, JsonConvert.SerializeObject(cmd));
        SaveStatus(status);

        // 音: 録画するランは Mute を外し、ゲームの BGM は 0・SE は既定値に（BGM は動画側で付ける。メニュー「録画にゲームの BGM を入れる」で戻せる。Play 終了で戻る）。録画しないランは夜中なので消す
        SessionState.SetString(MuteBackupKey, EditorUtility.audioMasterMute ? "1" : "0");
        if (cmd.Record)
        {
            EditorUtility.audioMasterMute = false;
            JikkyoRecordingPreset.OverrideVolumes();
        }
        else EditorUtility.audioMasterMute = true;

        _applied = false;
        _exitAt = -1;
        Debug.Log($"{LogPrefix} ラン {cmd.Id} を開始: {cmd.Persona} / 録画 {(cmd.Record ? "あり" : "なし")} / " +
                  $"{(cmd.Resume ? "続きから" : "新規")}（{cmd.LapsDone}/{cmd.Laps} 周済み）/ セーブ {saveRoot}");
        EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>コマンドの検査。隔離セーブはプロジェクト直下の Library 以下だけ許す（ユーザーのセーブを指さないように）。</summary>
    private static string Validate(Command cmd)
    {
        if (cmd == null) return "command.json が空";
        if (string.IsNullOrEmpty(cmd.Id)) return "id が無い";
        if (cmd.Persona != PersonaProfile.Zundamon.Id && cmd.Persona != PersonaProfile.Metan.Id && cmd.Persona != ConsultPersona)
            return $"人格 {cmd.Persona} を知らない";
        if (string.IsNullOrEmpty(cmd.SaveRoot)) return "saveRoot が無い";
        string full;
        try { full = Path.GetFullPath(cmd.SaveRoot); }
        catch (Exception e) { return "saveRoot が不正: " + e.Message; }
        string library = Path.GetFullPath(Path.Combine(ProjectRoot, "Library")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(library, StringComparison.OrdinalIgnoreCase)) return $"saveRoot がプロジェクトの Library 以下ではない: {full}";
        string persistent = Path.GetFullPath(Application.persistentDataPath);
        if (full.StartsWith(persistent, StringComparison.OrdinalIgnoreCase)) return "saveRoot がユーザーのセーブフォルダを指している";
        return null;
    }

    private static void ConfigureRecorder(Command cmd)
    {
        var s = GameplayAutoRecorderSettings.instance;
        if (cmd.Record)
        {
            s.outputFolder = "Recordings";
            s.filePrefix = $"Marathon{(cmd.Persona == PersonaProfile.Metan.Id ? "M" : cmd.Persona == ConsultPersona ? "C" : "Z")}_{cmd.Id}";
            s.frameRate = 30;
            s.width = 1920;
            s.height = 1080;
            s.captureAudio = true;
            s.recordOnPlay = true;
            s.exitPlayModeOnAutoStop = true;
            s.stopDelaySeconds = 4f;
            s.linkAutoPlay = true;
            s.autoPlayTimeScale = 1f;
            s.autoPlayScenarioMode = AutoPlayScenarioMode.Read;
            s.autoPlayTitleAction = AutoPlayTitleAction.ContinueOrNewGame;
            s.isolateSaves = true;
            s.stopAutoPlayOnRecordStop = true;
            s.useMaxDuration = true;
            s.maxDurationSeconds = Mathf.Max(5f, cmd.MaxMinutes) * 60f;
            s.useTomsShopPhase = false;
            s.useTurnCount = false;
            s.useSceneLoaded = false;
            s.useRunEnd = false;
            s.useIdle = false;
            s.useAutoPlayStopped = true;
        }
        else
        {
            s.recordOnPlay = false;   // 録画しない（オートプレイはこちらで始める）
        }
        s.SaveSettings();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        var run = CurrentRun();
        if (run == null) return;
        switch (change)
        {
            case PlayModeStateChange.EnteredPlayMode:
            {
                PersonaLog.RunTag = run.Id;
                var status = CurrentStatus();
                if (status != null && status.State == "starting")
                {
                    status.State = "playing";
                    SaveStatus(status);
                }
                StartAutoPlay(run);
                break;
            }
            case PlayModeStateChange.ExitingPlayMode:
                AutoPlayInputGuard.Release("Play を抜ける");
                break;
            case PlayModeStateChange.EnteredEditMode:
            {
                var status = CurrentStatus();
                string state, reason = status?.Reason ?? "";
                if (status != null && status.LapsDone >= status.LapsTarget) state = "finished";
                else if (status != null && status.StopRequested) state = "stopped";
                else
                {
                    state = "aborted";
                    if (string.IsNullOrEmpty(reason)) reason = "Play が終わった（周回が終わる前）";
                }
                Finish(state, reason);
                break;
            }
        }
    }

    /// <summary>ランを終える（状態を書き、SessionState と録画の設定と音を元に戻す）。</summary>
    private static void Finish(string state, string reason)
    {
        var run = CurrentRun();
        var status = CurrentStatus() ?? new Status { Id = run?.Id };
        status.State = state;
        if (!string.IsNullOrEmpty(reason)) status.Reason = reason;
        if (run != null && run.Record)
        {
            var rec = GameplayAutoRecorderSettings.instance.lastOutputPath;
            if (!string.IsNullOrEmpty(rec) && rec.Contains(run.Id)) status.Recording = rec;
        }
        if (_strategy?.LogFilePath != null) status.PersonaLog = _strategy.LogFilePath;
        if (_strategy?.ConsultLogPath != null) status.ConsultLog = _strategy.ConsultLogPath;
        SaveStatus(status);

        SessionState.EraseString(RunKey);
        SessionState.EraseString(StatusKey);
        SessionState.EraseString(GameplayAutoRecorder.MarathonSaveRootKey);
        var s = GameplayAutoRecorderSettings.instance;
        if (s.recordOnPlay)
        {
            s.recordOnPlay = false;   // ユーザーがあとで Play したときに録画が始まらないように
            s.SaveSettings();
        }
        AutoPlayInputGuard.Release("ランの終わり");
        RestoreRunInBackground();
        string mute = SessionState.GetString(MuteBackupKey, "");
        if (mute.Length > 0)
        {
            EditorUtility.audioMasterMute = mute == "1";
            SessionState.EraseString(MuteBackupKey);
        }
        _applied = false;
        _exitAt = -1;
        _strategy = null;
        Debug.Log($"{LogPrefix} ラン {status.Id} 終了: {state} {status.Reason}（{status.LapsDone}/{status.LapsTarget} 周）");
    }

    private const string RunInBackgroundBackupKey = "PersonaMarathon.RunInBackgroundBackup";

    /// <summary>PlayerSettings.runInBackground をランの前の値に戻す（実行時に Application.runInBackground を変えても ProjectSettings を書き換えないように）。</summary>
    private static void RestoreRunInBackground()
    {
        string v = SessionState.GetString(RunInBackgroundBackupKey, "");
        if (v.Length == 0) return;
        SessionState.EraseString(RunInBackgroundBackupKey);
        bool before = v == "1";
        if (PlayerSettings.runInBackground != before)
        {
            PlayerSettings.runInBackground = before;
            Debug.Log($"{LogPrefix} PlayerSettings.runInBackground を {before} に戻した");
        }
    }

    /// <summary>人格の戦略と設定でオートプレイを始める（録画ありでは録画側が先に始めていれば差し替える）。1 Play に 1 回。</summary>
    private static void StartAutoPlay(Command run)
    {
        if (_applied || !EditorApplication.isPlaying) return;
        _applied = true;
        bool metan = run.Persona == PersonaProfile.Metan.Id;
        var settings = new AutoPlaySettings
        {
            TimeScale = run.Record ? 1f : Mathf.Clamp(run.TimeScale, 1f, 4f),
            ScenarioMode = run.Record || !run.ScenarioFast ? AutoPlayScenarioMode.Read : AutoPlayScenarioMode.Fast,
            ScenarioReadInterval = 1.4f,
            PriceGraphPeekSeconds = run.Record ? 2f : 0.5f,
            TitleAction = AutoPlayTitleAction.ContinueOrNewGame,
            StreamSpendRatio = metan ? 0.4f : 0.8f,
            // 最後の周はリザルトで精算して村に着いたところで止める（人格ごとのセーブを次のランで「続きから」遊ぶため）
            RunEndAction = run.LapsDone + 1 >= Math.Max(1, run.Laps) ? AutoPlayRunEndAction.SettleAndStopAtVillage : AutoPlayRunEndAction.StartNextRun,
        };
        if (!metan) settings.StreamActionInterval = 2.5f;
        _strategy = run.Persona == ConsultPersona
            ? new PersonaStrategy(PersonaProfile.Zundamon, PersonaProfile.Metan)   // 遊ぶのはずんだもん（配信の使い方もずんだもん）
            : new PersonaStrategy(metan ? PersonaProfile.Metan : PersonaProfile.Zundamon);
        AutoPlay.Start(settings, _strategy);
        // フォーカスが無くても進める（runInBackground）のは、人の入力を全部遮るのと必ず一緒に（AutoPlayInputGuard。Play を抜けると戻す）
        AutoPlayInputGuard.Engage($"人格マラソン {run.Id}");
        Debug.Log($"{LogPrefix} オートプレイ開始: {_strategy.Name} / x{settings.TimeScale:0.#} / 会話 {settings.ScenarioMode} / 周回終わり {settings.RunEndAction}");
    }

    private static void OnAutoPlayStarted()
    {
        var run = CurrentRun();
        if (run != null && !_applied) StartAutoPlay(run);
    }

    private static void OnAutoPlayStopped(string reason)
    {
        var run = CurrentRun();
        if (run == null || !EditorApplication.isPlaying) return;
        var status = CurrentStatus();
        if (status != null)
        {
            status.Reason = reason ?? "";
            SaveStatus(status);
        }
        // 録画ありは録画側が余韻のあと Play を抜ける。抜けなかったときの保険も兼ねて時刻を決めておく
        _exitAt = EditorApplication.timeSinceStartup + (run.Record ? 45.0 : 3.0);
    }

    private static void OnAutoPlayLogged(string line) => _logEvents++;

    private static void OnRunFinished(string persona, bool bankrupt, int money, int delta)
    {
        var run = CurrentRun();
        var status = run != null ? CurrentStatus() : null;
        if (status == null) return;
        status.LapsDone++;
        status.Laps.Add(new LapResult
        {
            Lap = status.LapsDone, Bankrupt = bankrupt, Money = money, Delta = delta, At = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        });
        // 最後の周なら精算して村で止める（この通知はリザルトの手番の中で、止めるかどうかを見る前に来る）
        if (status.LapsDone >= status.LapsTarget) AutoPlay.Settings.RunEndAction = AutoPlayRunEndAction.SettleAndStopAtVillage;
        SaveStatus(status);
        Debug.Log($"{LogPrefix} {persona}: {status.LapsDone}/{status.LapsTarget} 周目が終わった（{(bankrupt ? "破産" : "完走")} {money:N0}G）");
    }

    private static void HandleStop()
    {
        try { File.Delete(StopPath); }
        catch (Exception) { /* 次の確認でまた消す */ }
        try { if (File.Exists(CommandPath)) File.Delete(CommandPath); }
        catch (Exception) { /* 始まらないように消せなくても、ランが無ければ何もしない */ }
        var run = CurrentRun();
        if (run == null) return;
        var status = CurrentStatus();
        if (status != null)
        {
            status.StopRequested = true;
            SaveStatus(status);
        }
        Debug.Log($"{LogPrefix} 止める指示を受けました（ラン {run.Id}）");
        if (!EditorApplication.isPlaying)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish("stopped", "マラソンの停止");
            return;
        }
        if (AutoPlay.IsRunning) AutoPlay.Stop("マラソンの停止");
        _exitAt = EditorApplication.timeSinceStartup + (run.Record ? 20.0 : 3.0);
    }
}

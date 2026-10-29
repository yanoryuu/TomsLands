using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 実況動画（tools/jikkyo_video）用の撮影プリセット。
/// 自動録画の設定を「初回ランを最初からラン終了まで・等速・会話は読める速さ・隔離セーブ」に揃えて、
/// TitleScene を開き、Play に入る（Play と同時に録画とオートプレイが始まる）。
/// 録画は Recordings/Jikkyo<種類>_yyyyMMdd_HHmmss.mp4 に出る。
///
/// 戦略（JikkyoAutoPlayStrategies.cs）:
///   A 最適解 … 毎朝新聞を読み、情報を買い、勇者側で張る（淡々プレイ・解説の素材）
///   B 解説   … 1回目は A と同じ、2回目は情報を買わず魔王軍支援に出資する実験（解説の素材）
///   C 初見   … 全額使い、1回目は情報を買って勇者側、2回目は情報を買わず魔王軍支援に大半を出資（初見プレイの素材）
///   D 魔王   … 情報を買わず、毎回の配信前に手持ちの大半を魔王軍支援に出資し、配信中はボス強化・罠で勇者を負けさせる（魔王に融資する店の素材）
///   Z ずんだもん … 人格プレイ。判断はローカルの Jev 互換モデル（kev 4B）。自信過剰・抽選・大きく張る（PersonaStrategy）
///   M 四国めたん … 人格プレイ。判断はローカルの Jev 互換モデル（kev 9B）。最尤・1 万G は残す・情報を先に読む
///   （Z/M は Ollama が http://localhost:11434 で動いていること。設定 UserSettings/PersonaPlay.json、tools/persona_play/README.md）
/// 戦略は Play に入ったあと AutoPlay.Started で差し替える（SessionState に種類を覚えておく）。
/// </summary>
[InitializeOnLoad]
public static class JikkyoRecordingPreset
{
    private const string TitleScenePath = "Assets/Scene/TitleScene.unity";
    private const string KindKey = "JikkyoRecordingPreset.Kind";

    /// <summary>
    /// 外から録画を始めるためのフラグ。Library/JikkyoRecordKind.txt に "A" / "B" / "C" / "D" / "Z" / "M" と書いてエディタを起動すると、
    /// コンパイルが終わった後に自動で Record(kind) を呼ぶ（-executeMethod を渡せない起動方法のため）。
    /// </summary>
    private const string AutoStartFlagPath = "Library/JikkyoRecordKind.txt";

    private const string QuitAfterKey = "JikkyoRecordingPreset.QuitAfter";

    // ゲームの音量（PlayerPrefs。全エディタ共通）。0 のままだと録画が無音になるので、録画中だけ録画用の値にして Play 終了で戻す
    // BGM は動画側（tools/jikkyo_video の CC0 BGM）で付けるので、既定では録画に入れない（0）。SE は既定値 1 のまま
    private static readonly string[] VolumeKeys = { "Settings.BgmVolume", "Settings.SeVolume" };
    private const float RecordBgmVolumeOn = 0.5f;
    private const float RecordSeVolume = 1f;

    /// <summary>録画にゲームの BGM を入れるか（EditorPrefs。既定 false = BGM 0 で録る）。</summary>
    private const string RecordGameBgmPref = "JikkyoRecordingPreset.RecordGameBgm";
    private const string RecordGameBgmMenu = "Tools/TomsLands/自動録画/録画にゲームの BGM を入れる";

    public static bool RecordGameBgm
    {
        get => EditorPrefs.GetBool(RecordGameBgmPref, false);
        set => EditorPrefs.SetBool(RecordGameBgmPref, value);
    }

    [MenuItem(RecordGameBgmMenu, priority = 40)]
    private static void ToggleRecordGameBgm() => RecordGameBgm = !RecordGameBgm;

    [MenuItem(RecordGameBgmMenu, true)]
    private static bool ToggleRecordGameBgmValidate()
    {
        Menu.SetChecked(RecordGameBgmMenu, RecordGameBgm);
        return true;
    }
    private const string VolumeBackupKey = "JikkyoRecordingPreset.VolumeBackup.";
    private const string NotSaved = "__not_saved__";
    private static double _nextFlagCheck;

    static JikkyoRecordingPreset()
    {
        AutoPlay.Started -= OnAutoPlayStarted;
        AutoPlay.Started += OnAutoPlayStarted;
        EditorApplication.update -= PollFlag;
        EditorApplication.update += PollFlag;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    /// <summary>数秒ごとにフラグファイルを見る。中身は "A" / "B" / "C" / "D" / "Z" / "M"。末尾に " quit" を付けると録画が終わったあとエディタを閉じる。</summary>
    private static void PollFlag()
    {
        if (EditorApplication.timeSinceStartup < _nextFlagCheck) return;
        _nextFlagCheck = EditorApplication.timeSinceStartup + 3.0;
        if (!System.IO.File.Exists(AutoStartFlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string text = System.IO.File.ReadAllText(AutoStartFlagPath).Trim();
        System.IO.File.Delete(AutoStartFlagPath);
        var parts = text.Split(' ');
        string kind = parts[0];
        if (kind != "A" && kind != "B" && kind != "C" && kind != "D" && kind != "Z" && kind != "M") return;
        SessionState.SetBool(QuitAfterKey, System.Array.IndexOf(parts, "quit") >= 0);
        Debug.Log($"[JikkyoRecordingPreset] フラグファイルから自動開始: {text}");
        Record(kind);
    }

    private const string RunInBackgroundBackupKey = "JikkyoRecordingPreset.RunInBackgroundBackup";

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode) AutoPlayInputGuard.Release("録画の Play を抜ける");
        if (change != PlayModeStateChange.EnteredEditMode) return;
        AutoPlayInputGuard.Release("録画の Play が終わった");
        // 実行時に入れた runInBackground がプロジェクトの設定に残らないよう、録画の前の値に戻す
        string rib = SessionState.GetString(RunInBackgroundBackupKey, "");
        if (rib.Length > 0)
        {
            SessionState.EraseString(RunInBackgroundBackupKey);
            if (PlayerSettings.runInBackground != (rib == "1")) PlayerSettings.runInBackground = rib == "1";
        }
        // 音量の復元に失敗しても、quit 指定のときにエディタを閉じられなくならないようにする
        try { RestoreVolumes(); }
        catch (System.Exception e) { Debug.LogWarning($"[JikkyoRecordingPreset] 音量の復元に失敗しました: {e.Message}"); }
        if (!SessionState.GetBool(QuitAfterKey, false)) return;
        SessionState.EraseBool(QuitAfterKey);
        Debug.Log("[JikkyoRecordingPreset] 録画が終わったのでエディタを閉じます");
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }

    [MenuItem("Tools/TomsLands/自動録画/実況動画用に録画して Play（A 最適解・ラン終了まで）", priority = 20)]
    public static void RecordOptimalRun() => Record("A");

    [MenuItem("Tools/TomsLands/自動録画/実況動画用に録画して Play（B 解説・2回目は魔王軍支援の実験）", priority = 21)]
    public static void RecordKaisetsuRun() => Record("B");

    [MenuItem("Tools/TomsLands/自動録画/実況動画用に録画して Play（C 初見ミスあり・ラン終了まで）", priority = 22)]
    public static void RecordNoviceRun() => Record("C");

    [MenuItem("Tools/TomsLands/自動録画/実況動画用に録画して Play（D 魔王軍に融資・ラン終了まで）", priority = 23)]
    public static void RecordMaouRun() => Record("D");

    [MenuItem("Tools/TomsLands/自動録画/実況動画用に録画して Play（Z ずんだもん人格・ローカルモデルが判断）", priority = 24)]
    public static void RecordZundamonRun() => Record("Z");

    [MenuItem("Tools/TomsLands/自動録画/実況動画用に録画して Play（M 四国めたん人格・ローカルモデルが判断）", priority = 25)]
    public static void RecordMetanRun() => Record("M");

    private static void Record(string kind)
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("実況動画用の録画", "Play を止めてから実行してください。", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var s = GameplayAutoRecorderSettings.instance;
        s.outputFolder = "Recordings";
        s.filePrefix = "Jikkyo" + kind;
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
        s.isolateSaves = true;            // ユーザーのセーブは読まない・書かない。毎回まっさらな初回ラン
        s.stopAutoPlayOnRecordStop = true;

        s.useMaxDuration = true;
        s.maxDurationSeconds = 40f * 60f; // 保険。ふつうはラン終了（オートプレイ停止）で止まる
        s.useTomsShopPhase = false;
        s.useTurnCount = false;
        s.useSceneLoaded = false;
        s.useRunEnd = false;              // リザルト／ゲームオーバーの演出まで撮るので、オートプレイ停止に任せる
        s.useIdle = false;
        s.useAutoPlayStopped = true;
        s.SaveSettings();

        SessionState.SetString(KindKey, kind);
        SessionState.SetString(RunInBackgroundBackupKey, PlayerSettings.runInBackground ? "1" : "0");

        // Game ビューの Mute Audio のままだと無音の動画になるので外す
        EditorUtility.audioMasterMute = false;
        OverrideVolumes();

        EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
        Debug.Log($"[JikkyoRecordingPreset] 実況動画用（{kind}）の設定で Play を開始します。停止後の動画: Recordings/Jikkyo{kind}_*.mp4");
        EditorApplication.EnterPlaymode();
    }

    /// <summary>ゲーム内の BGM・SE 音量を録画用の値にする（BGM は既定 0・SE は 1。元の値は SessionState に覚えておく）。</summary>
    internal static void OverrideVolumes()
    {
        for (int i = 0; i < VolumeKeys.Length; i++)
        {
            string key = VolumeKeys[i];
            // SessionState.GetString は既定値に null を渡しても "" を返すので、番兵の文字列で「未保存」を見分ける
            if (SessionState.GetString(VolumeBackupKey + key, NotSaved) == NotSaved)
                SessionState.SetString(VolumeBackupKey + key,
                    PlayerPrefs.HasKey(key) ? PlayerPrefs.GetFloat(key).ToString(System.Globalization.CultureInfo.InvariantCulture) : "none");
            float v = key == "Settings.BgmVolume" ? (RecordGameBgm ? RecordBgmVolumeOn : 0f) : RecordSeVolume;
            PlayerPrefs.SetFloat(key, v);
        }
        PlayerPrefs.Save();
        Debug.Log($"[JikkyoRecordingPreset] 録画のあいだだけゲームの音量を BGM {(RecordGameBgm ? RecordBgmVolumeOn : 0f)}・SE {RecordSeVolume} にします（Play 終了で戻す）");
    }

    /// <summary>録画のために上書きした音量を元に戻す。</summary>
    private static void RestoreVolumes()
    {
        bool restored = false;
        foreach (string key in VolumeKeys)
        {
            string prev = SessionState.GetString(VolumeBackupKey + key, NotSaved);
            if (prev == NotSaved) continue;
            SessionState.EraseString(VolumeBackupKey + key);
            if (prev == "none") { PlayerPrefs.DeleteKey(key); restored = true; }
            else if (float.TryParse(prev, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
            {
                PlayerPrefs.SetFloat(key, v);
                restored = true;
            }
            else Debug.LogWarning($"[JikkyoRecordingPreset] {key} の録画前の値「{prev}」を読めないので戻しません");
        }
        if (!restored) return;
        PlayerPrefs.Save();
        Debug.Log("[JikkyoRecordingPreset] ゲームの BGM・SE 音量を録画前の値に戻しました");
    }

    private static bool _seenVillage;

    /// <summary>村を経て 2 周目の店に入ったら、そのランの終わりで止まるようにする。</summary>
    private static void WatchSecondRun()
    {
        if (!AutoPlay.IsRunning)
        {
            if (!EditorApplication.isPlaying) EditorApplication.update -= WatchSecondRun;
            return;
        }
        string label = AutoPlay.CurrentPhaseLabel ?? "";
        if (label.StartsWith("VillageScene")) _seenVillage = true;
        else if (_seenVillage && label.StartsWith("TomsShop") && AutoPlay.Settings.RunEndAction != AutoPlayRunEndAction.Stop)
        {
            AutoPlay.Settings.RunEndAction = AutoPlayRunEndAction.Stop;
            Debug.Log("[JikkyoRecordingPreset] 2周目に入ったので、このランの終わりで止めます");
            EditorApplication.update -= WatchSecondRun;
        }
    }

    /// <summary>オートプレイが始まった直後に、実況用の戦略と設定へ差し替える。</summary>
    private static void OnAutoPlayStarted()
    {
        string kind = SessionState.GetString(KindKey, "");
        if (string.IsNullOrEmpty(kind)) return;
        SessionState.EraseString(KindKey);

        var settings = AutoPlay.Settings.Clone();
        IAutoPlayStrategy strategy;
        if (kind == "C")
        {
            strategy = new JikkyoNoviceStrategy();
            settings.StreamSpendRatio = 0.9f;    // スパチャを惜しまない
            settings.StreamActionInterval = 2.5f;
        }
        else if (kind == "B")
        {
            strategy = new JikkyoKaisetsuStrategy();
            settings.StreamSpendRatio = 0.6f;
        }
        else if (kind == "Z")
        {
            strategy = new PersonaStrategy(PersonaProfile.Zundamon);
            settings.StreamSpendRatio = 0.8f;    // 大きく張る
            settings.StreamActionInterval = 2.5f;
        }
        else if (kind == "M")
        {
            strategy = new PersonaStrategy(PersonaProfile.Metan);
            settings.StreamSpendRatio = 0.4f;    // 1 万G は残す（戦略側でも下限を守る）
        }
        else if (kind == "D")
        {
            strategy = new JikkyoMaouStrategy();
            settings.StreamSpendRatio = 0.9f;    // ボス強化・罠を惜しまない
            settings.StreamActionInterval = 2.5f;
        }
        else
        {
            strategy = new JikkyoOptimalStrategy();
            settings.StreamSpendRatio = 0.5f;
        }
        settings.ScenarioReadInterval = 1.4f;    // 会話は読める速さで
        settings.PriceGraphPeekSeconds = 2.0f;
        // 1周目 → 村（施設を調べる）→ 2周目まで撮る。2周目の店に入ったら WatchSecondRun が Stop に戻す
        settings.RunEndAction = AutoPlayRunEndAction.StartNextRun;
        _seenVillage = false;
        EditorApplication.update -= WatchSecondRun;
        EditorApplication.update += WatchSecondRun;
        AutoPlay.Start(settings, strategy);
        // 撮影中はフォーカスが無くても進め、人の入力は全部遮る（AutoPlayInputGuard。Play を抜けると戻す）
        AutoPlayInputGuard.Engage($"実況の録画 {kind}");
        Debug.Log($"[JikkyoRecordingPreset] 戦略を差し替え: {strategy.Name}");
    }
}

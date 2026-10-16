using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 自動録画ツールのウィンドウ（Tools/TomsLands/自動録画）。設定の編集と手動の開始/停止、直近結果の表示。
/// 実際の録画制御は GameplayAutoRecorder（ウィンドウを閉じても動く）。仕様: Docs/AutoRecording_Usage.md
/// </summary>
public sealed class GameplayAutoRecorderWindow : EditorWindow
{
    private Vector2 _scroll;

    [MenuItem("Tools/TomsLands/自動録画", priority = 20)]
    public static void Open()
    {
        var w = GetWindow<GameplayAutoRecorderWindow>();
        w.titleContent = new GUIContent("自動録画");
        w.minSize = new Vector2(360, 420);
        w.Show();
    }

    private void OnEnable() => GameplayAutoRecorder.Changed += Repaint;
    private void OnDisable() => GameplayAutoRecorder.Changed -= Repaint;

    // 録画中は経過時間などを更新表示する
    private void OnInspectorUpdate()
    {
        if (GameplayAutoRecorder.IsRecording) Repaint();
    }

    private void OnGUI()
    {
        var s = GameplayAutoRecorderSettings.instance;
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUI.BeginChangeCheck();

        // ---- 出力 ----
        EditorGUILayout.LabelField("出力", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            s.outputFolder = EditorGUILayout.TextField(new GUIContent("出力フォルダ", "相対パスはプロジェクト直下から"), s.outputFolder);
            if (GUILayout.Button("…", GUILayout.Width(24)))
            {
                var picked = EditorUtility.OpenFolderPanel("出力フォルダ", GameplayAutoRecorder.ResolveOutputFolder(s.outputFolder), "");
                if (!string.IsNullOrEmpty(picked)) s.outputFolder = picked;
            }
        }
        s.filePrefix = EditorGUILayout.TextField(new GUIContent("ファイル名の接頭辞", "<接頭辞>_yyyyMMdd_HHmmss.mp4"), s.filePrefix);
        s.frameRate = Mathf.Clamp(EditorGUILayout.IntField("FPS", s.frameRate), 1, 120);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel("解像度 (GameView)");
            s.width = Mathf.Max(16, EditorGUILayout.IntField(s.width));
            GUILayout.Label("x", GUILayout.Width(10));
            s.height = Mathf.Max(16, EditorGUILayout.IntField(s.height));
        }
        s.captureAudio = EditorGUILayout.Toggle("音声も録る", s.captureAudio);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("開始・終了", EditorStyles.boldLabel);
        s.recordOnPlay = EditorGUILayout.Toggle("Play 開始と同時に録画", s.recordOnPlay);
        s.exitPlayModeOnAutoStop = EditorGUILayout.Toggle(new GUIContent("自動停止したら Play も停止"), s.exitPlayModeOnAutoStop);
        s.stopDelaySeconds = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("停止の余韻（秒）", "条件を満たしてから止めるまで録り続ける秒数"), s.stopDelaySeconds));

        // ---- オートプレイ連動 ----
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("オートプレイ連動", EditorStyles.boldLabel);
        s.linkAutoPlay = EditorGUILayout.Toggle(new GUIContent("録画開始でオートプレイ開始", "AutoPlay.Start を録画開始と同時に呼ぶ"), s.linkAutoPlay);
        using (new EditorGUI.DisabledScope(!s.linkAutoPlay))
        {
            s.autoPlayTimeScale = EditorGUILayout.Slider(new GUIContent("速度倍率 (timeScale)"), s.autoPlayTimeScale, 0.5f, 4f);
            s.autoPlayScenarioMode = (AutoPlayScenarioMode)EditorGUILayout.EnumPopup("会話の送り方", s.autoPlayScenarioMode);
            s.autoPlayTitleAction = (AutoPlayTitleAction)EditorGUILayout.EnumPopup("タイトルでの行動", s.autoPlayTitleAction);
        }
        s.stopAutoPlayOnRecordStop = EditorGUILayout.Toggle("録画停止でオートプレイも停止", s.stopAutoPlayOnRecordStop);
        using (new EditorGUI.DisabledScope(!s.recordOnPlay))
        {
            s.isolateSaves = EditorGUILayout.Toggle(new GUIContent("隔離セーブで遊ぶ",
                "Play と同時に録画する時だけ有効。セーブを Temp/AutoRecordSaves に逃がし、毎回まっさら（初回ラン）から始める。ユーザーのスロットは読まない・書かない"), s.isolateSaves);
        }
        if (s.linkAutoPlay && s.autoPlayTimeScale > 1.01f)
            EditorGUILayout.HelpBox("固定FPS録画なので timeScale>1 だと動画も早回しになる（x2 なら動画内のゲームは2倍速）。等速の動画が欲しいときは 1。", MessageType.Info);
        if (AutoPlay.IsRunning)
            EditorGUILayout.LabelField("オートプレイ", $"{AutoPlay.Status}  [{AutoPlay.CurrentPhaseLabel}] T{AutoPlay.CurrentTurn}", EditorStyles.wordWrappedLabel);

        // ---- 停止条件 ----
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("停止条件（どれか1つでも満たしたら停止）", EditorStyles.boldLabel);

        s.useMaxDuration = ConditionRow("経過時間（動画秒）", s.useMaxDuration, () =>
            s.maxDurationSeconds = Mathf.Max(1f, EditorGUILayout.FloatField(s.maxDurationSeconds)));
        s.useTomsShopPhase = ConditionRow("フェーズに到達", s.useTomsShopPhase, () =>
            s.targetTomsShopPhase = (TomsShopGamePhase)EditorGUILayout.EnumPopup(s.targetTomsShopPhase));
        s.useTurnCount = ConditionRow("N ターン経過", s.useTurnCount, () =>
            s.turnCount = Mathf.Max(1, EditorGUILayout.IntField(s.turnCount)));
        s.useSceneLoaded = ConditionRow("シーンがロード", s.useSceneLoaded, () =>
            s.targetSceneNames = EditorGUILayout.TextField(s.targetSceneNames));
        s.useRunEnd = ConditionRow("ラン終了（GameOver/Result）", s.useRunEnd, null);
        s.useIdle = ConditionRow("入力なし（秒）", s.useIdle, () =>
            s.idleSeconds = Mathf.Max(1f, EditorGUILayout.FloatField(s.idleSeconds)));
        s.useAutoPlayStopped = ConditionRow("オートプレイ停止", s.useAutoPlayStopped, null);
        if (s.useIdle && s.linkAutoPlay)
            EditorGUILayout.HelpBox("オートプレイ実行中は「入力なし」を判定しない。", MessageType.None);

        if (EditorGUI.EndChangeCheck()) s.SaveSettings();

        // ---- 操作 ----
        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || GameplayAutoRecorder.IsRecording))
            {
                if (GUILayout.Button("● 録画開始", GUILayout.Height(28))) GameplayAutoRecorder.StartRecording();
            }
            using (new EditorGUI.DisabledScope(!GameplayAutoRecorder.IsRecording))
            {
                if (GUILayout.Button("■ 停止して保存", GUILayout.Height(28))) GameplayAutoRecorder.StopRecordingManually();
            }
        }
        if (!EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("録画は Play 中のみ。「Play 開始と同時に録画」を有効にすると Play と同時に始まります。", MessageType.Info);

        // ---- 状態 ----
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("状態", EditorStyles.boldLabel);
        if (GameplayAutoRecorder.IsRecording)
        {
            EditorGUILayout.LabelField("録画中", GameplayAutoRecorder.CurrentOutputPath, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("動画の長さ", $"{GameplayAutoRecorder.RecordedSeconds:0.0} 秒");
            EditorGUILayout.LabelField("ターン経過", GameplayAutoRecorder.TurnsElapsed.ToString());
            EditorGUILayout.LabelField("入力なし", $"{GameplayAutoRecorder.IdleSeconds:0.0} 秒");
            if (GameplayAutoRecorder.PendingStopReason != null)
                EditorGUILayout.HelpBox($"停止待ち: {GameplayAutoRecorder.PendingStopReason}", MessageType.None);
        }
        else
        {
            EditorGUILayout.LabelField("停止中");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("直近の結果", EditorStyles.boldLabel);
        if (string.IsNullOrEmpty(s.lastOutputPath))
        {
            EditorGUILayout.LabelField("（まだありません）");
        }
        else
        {
            EditorGUILayout.LabelField("停止理由", s.lastStopReason, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("日時", s.lastFinishedAt);
            EditorGUILayout.LabelField("長さ", $"{s.lastDurationSeconds:0.0} 秒");
            EditorGUILayout.SelectableLabel(s.lastOutputPath, EditorStyles.wordWrappedLabel, GUILayout.Height(32));
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!File.Exists(s.lastOutputPath)))
                {
                    if (GUILayout.Button("ファイルを表示")) EditorUtility.RevealInFinder(s.lastOutputPath);
                }
                if (GUILayout.Button("フォルダを開く"))
                {
                    var folder = GameplayAutoRecorder.ResolveOutputFolder(s.outputFolder);
                    Directory.CreateDirectory(folder);
                    EditorUtility.OpenWithDefaultApp(folder);
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private static bool ConditionRow(string label, bool enabled, System.Action drawValue)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            enabled = EditorGUILayout.ToggleLeft(label, enabled, GUILayout.Width(190));
            if (drawValue != null)
            {
                using (new EditorGUI.DisabledScope(!enabled)) drawValue();
            }
        }
        return enabled;
    }
}

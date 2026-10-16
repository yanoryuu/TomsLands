using UnityEditor;
using UnityEngine;

/// <summary>
/// 自動録画ツールの設定と直近の結果。UserSettings/ 配下に保存（git 管理外・個人設定）。
/// 仕様: Docs/AutoRecording_Usage.md
/// </summary>
[FilePath("UserSettings/GameplayAutoRecorderSettings.asset", FilePathAttribute.Location.ProjectFolder)]
public class GameplayAutoRecorderSettings : ScriptableSingleton<GameplayAutoRecorderSettings>
{
    // ---- 出力 ----
    [Tooltip("出力フォルダ。相対パスはプロジェクト直下からの相対")]
    public string outputFolder = "Recordings";
    public string filePrefix = "TomsLands";
    public int frameRate = 30;
    public int width = 1920;
    public int height = 1080;
    public bool captureAudio = true;

    // ---- 開始・終了 ----
    public bool recordOnPlay;
    public bool exitPlayModeOnAutoStop;
    [Tooltip("停止条件を満たしてから実際に止めるまでの余韻（秒）。ラン終了画面などを映すため")]
    public float stopDelaySeconds = 2f;

    // ---- オートプレイ連動（Assets/Scripts/Debug/AutoPlay） ----
    [Tooltip("録画開始と同時に AutoPlay.Start する")]
    public bool linkAutoPlay;
    public float autoPlayTimeScale = 1f;
    public AutoPlayScenarioMode autoPlayScenarioMode = AutoPlayScenarioMode.Read;
    public AutoPlayTitleAction autoPlayTitleAction = AutoPlayTitleAction.ContinueOrNewGame;
    [Tooltip("Play と同時に録画する時、セーブを Temp/AutoRecordSaves に隔離して毎回まっさらから遊ぶ（ユーザーのスロットに触れない）")]
    public bool isolateSaves = true;
    [Tooltip("録画を止めたらオートプレイも止める")]
    public bool stopAutoPlayOnRecordStop = true;

    // ---- 停止条件（どれか1つでも満たしたら停止） ----
    public bool useMaxDuration = true;
    public float maxDurationSeconds = 60f;

    public bool useTomsShopPhase;
    public TomsShopGamePhase targetTomsShopPhase = TomsShopGamePhase.TurnEndSummary;

    public bool useTurnCount;
    public int turnCount = 1;

    public bool useSceneLoaded;
    [Tooltip("カンマ区切りで複数指定可")]
    public string targetSceneNames = "GameOver,ResultScene";

    public bool useRunEnd = true;

    public bool useIdle;
    public float idleSeconds = 30f;

    [Tooltip("オートプレイが停止（ターン上限・ラン終了・詰まり等）したら録画も止める")]
    public bool useAutoPlayStopped = true;

    // ---- 直近の結果 ----
    public string lastStopReason = "";
    public string lastOutputPath = "";
    public string lastFinishedAt = "";
    public float lastDurationSeconds;

    public void SaveSettings() => Save(true);
}

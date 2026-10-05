using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// エディタ再生を BootScene から開始するためのツール。
/// どのシーンを開いていても、リモートコンフィグ取得（Boot）を通した起動フローで再生できる。
///
/// - Tools > TomsLands > デバッグ > BootSceneから再生      : 今回だけ Boot から再生（終了後は元に戻る）
/// - Tools > TomsLands > デバッグ > 常にBootSceneから再生   : トグル。ON の間は通常の再生ボタンでも Boot から始まる
/// </summary>
[InitializeOnLoad]
public static class BootPlayTool
{
    private const string BootScenePath = "Assets/Scene/BootScene.unity";
    private const string AlwaysPrefKey = "TomsLands.AlwaysPlayFromBoot";
    private const string OneShotKey = "TomsLands.PlayFromBootOneShot";

    private const string MenuPlayOnce = "Tools/TomsLands/デバッグ/BootSceneから再生";
    private const string MenuAlways = "Tools/TomsLands/デバッグ/常にBootSceneから再生";

    private static bool AlwaysOn
    {
        get => EditorPrefs.GetBool(AlwaysPrefKey, false);
        set => EditorPrefs.SetBool(AlwaysPrefKey, value);
    }

    static BootPlayTool()
    {
        ApplyStartScene();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    // ---- 今回だけBootから再生 ----

    [MenuItem(MenuPlayOnce, priority = 0)]
    private static void PlayFromBoot()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath);
        if (scene == null)
        {
            EditorUtility.DisplayDialog("BootSceneから再生", $"{BootScenePath} が見つかりません。", "OK");
            return;
        }

        EditorSceneManager.playModeStartScene = scene;
        // 「常に」がOFFのときだけ、再生終了後に設定を元へ戻す（SessionStateはドメインリロードを跨げる）
        SessionState.SetBool(OneShotKey, !AlwaysOn);
        EditorApplication.EnterPlaymode();
    }

    [MenuItem(MenuPlayOnce, validate = true)]
    private static bool ValidatePlayFromBoot() => !EditorApplication.isPlayingOrWillChangePlaymode;

    // ---- 常にBootから再生（トグル） ----

    [MenuItem(MenuAlways, priority = 1)]
    private static void ToggleAlways()
    {
        AlwaysOn = !AlwaysOn;
        ApplyStartScene();
        Debug.Log($"[BootPlayTool] 常にBootSceneから再生: {(AlwaysOn ? "ON" : "OFF")}");
    }

    [MenuItem(MenuAlways, validate = true)]
    private static bool ValidateToggleAlways()
    {
        Menu.SetChecked(MenuAlways, AlwaysOn);
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    // ---- 内部処理 ----

    /// <summary>トグル状態に応じて playModeStartScene を設定/解除する。</summary>
    private static void ApplyStartScene()
    {
        if (AlwaysOn)
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath);
            if (scene != null) EditorSceneManager.playModeStartScene = scene;
        }
        else if (!SessionState.GetBool(OneShotKey, false))
        {
            EditorSceneManager.playModeStartScene = null;
        }
    }

    /// <summary>ワンショット再生の後始末（再生終了時に開始シーン設定を解除）。</summary>
    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        if (!SessionState.GetBool(OneShotKey, false)) return;

        SessionState.SetBool(OneShotKey, false);
        ApplyStartScene();
    }
}

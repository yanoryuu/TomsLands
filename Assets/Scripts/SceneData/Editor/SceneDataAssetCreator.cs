﻿#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// シーン間共有データの ScriptableObject アセットを自動生成するエディタツール。
/// メニュー: Tools > TomsLands > データ生成 > SceneDataアセット生成
/// </summary>
public static class SceneDataAssetCreator
{
    [MenuItem("Tools/TomsLands/データ生成/SceneDataアセット生成")]
    public static void CreateAssets()
    {
        CreateAssetIfNotExists<BattleInputData>("Assets/Resources/SceneData/BattleInputData.asset");
        CreateAssetIfNotExists<BattleOutputData>("Assets/Resources/SceneData/BattleOutputData.asset");
        CreateAssetIfNotExists<StartModeData>("Assets/Resources/SceneData/StartModeData.asset");
        CreateAssetIfNotExists<EventInputData>("Assets/Resources/SceneData/EventInputData.asset");
        CreateAssetIfNotExists<EventOutputData>("Assets/Resources/SceneData/EventOutputData.asset");
        CreateAssetIfNotExists<RunSetupData>("Assets/Resources/SceneData/RunSetupData.asset");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SceneDataAssetCreator] Scene data assets created/verified.");
    }

    private static void CreateAssetIfNotExists<T>(string path) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
        {
            Debug.Log($"[SceneDataAssetCreator] Already exists: {path}");
            return;
        }

        // フォルダが無ければ作る
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!AssetDatabase.IsValidFolder(dir))
        {
            var parts = dir.Replace("\\", "/").Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        var asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        Debug.Log($"[SceneDataAssetCreator] Created: {path}");
    }
}
#endif


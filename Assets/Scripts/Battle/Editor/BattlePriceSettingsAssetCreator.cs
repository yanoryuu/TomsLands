#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class BattlePriceSettingsAssetCreator
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string AssetPath = ResourcesFolder + "/BattlePriceSettings.asset";

    [MenuItem("Tools/Battle/Create Battle Price Settings")]
    public static void CreateBattlePriceSettings()
    {
        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        var existing = AssetDatabase.LoadAssetAtPath<BattlePriceSettings>(AssetPath);
        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            Debug.Log($"[BattlePriceSettingsAssetCreator] 既に存在します: {AssetPath}");
            return;
        }

        var settings = ScriptableObject.CreateInstance<BattlePriceSettings>();
        AssetDatabase.CreateAsset(settings, AssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
        Debug.Log($"[BattlePriceSettingsAssetCreator] 作成しました: {AssetPath}");
    }
}
#endif

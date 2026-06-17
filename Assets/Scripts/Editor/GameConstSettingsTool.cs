#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GameConstSettings アセットの生成と JSON 入出力を行うエディタツール。
/// JSON 入出力は将来の「SpreadSheet → サーバー → ダウンロード」パイプラインの橋渡し
/// （現在値をエクスポートしてアップロード、配信 JSON をインポートして反映）に使う。
/// </summary>
public static class GameConstSettingsTool
{
    // Resources_moved 配下に置くことで、Tools > Addressables > Register Moved Resources の対象になり
    // アドレス "GameConstSettings" で登録される（GameConst.Address と一致）。
    private const string AssetPath = "Assets/Resources_moved/GameConstSettings.asset";

    [MenuItem("Tools/GameConst/Create Settings Asset")]
    public static void CreateAsset()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameConstSettings>(AssetPath);
        if (existing != null)
        {
            Debug.Log($"[GameConstSettingsTool] 既に存在します: {AssetPath}");
            Selection.activeObject = existing;
            return;
        }

        var dir = Path.GetDirectoryName(AssetPath);
        if (!AssetDatabase.IsValidFolder(dir))
            Directory.CreateDirectory(dir);

        var asset = ScriptableObject.CreateInstance<GameConstSettings>();
        AssetDatabase.CreateAsset(asset, AssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = asset;

        Debug.Log($"[GameConstSettingsTool] 作成しました: {AssetPath}\n" +
                  "続けて Tools > Addressables > Register Moved Resources を実行し、Addressable 登録してください。");
    }

    [MenuItem("Tools/GameConst/Export JSON")]
    public static void ExportJson()
    {
        var asset = LoadOrWarn();
        if (asset == null) return;

        var path = EditorUtility.SaveFilePanel("Export GameConst JSON", Application.dataPath, "GameConstData", "json");
        if (string.IsNullOrEmpty(path)) return;

        File.WriteAllText(path, JsonUtility.ToJson(asset.data, true));
        Debug.Log($"[GameConstSettingsTool] JSON を書き出しました: {path}");
    }

    [MenuItem("Tools/GameConst/Import JSON")]
    public static void ImportJson()
    {
        var asset = LoadOrWarn();
        if (asset == null) return;

        var path = EditorUtility.OpenFilePanel("Import GameConst JSON", Application.dataPath, "json");
        if (string.IsNullOrEmpty(path)) return;

        var data = JsonUtility.FromJson<GameConstData>(File.ReadAllText(path));
        if (data == null)
        {
            Debug.LogError("[GameConstSettingsTool] JSON の解析に失敗しました。");
            return;
        }

        Undo.RecordObject(asset, "Import GameConst JSON");
        asset.data = data;
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Debug.Log($"[GameConstSettingsTool] JSON を取り込みました: {path}");
    }

    private static GameConstSettings LoadOrWarn()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameConstSettings>(AssetPath);
        if (asset == null)
            Debug.LogError("[GameConstSettingsTool] 設定アセットがありません。" +
                           "先に Tools > GameConst > Create Settings Asset を実行してください。");
        return asset;
    }
}
#endif

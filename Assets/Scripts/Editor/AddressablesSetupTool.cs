#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Resources から移動したアセットを一括で Addressable 登録するエディタツール。
/// - アドレス = 旧 Resources からの相対パス（拡張子なし）にするため、コード側のキーを変えずに済む。
/// - LoadAll をラベルで束ねるため、各アセットに「型名ラベル」を付与する
///   （例: ItemData → ラベル "ItemData", AdvertisementData → "AdvertisementData"）。
/// メニュー: Tools > TomsLands > データ生成 > Addressables一括登録（Resources_moved）
/// </summary>
public static class AddressablesSetupTool
{
    // 旧 Resources の中身を移動した先。アドレスはこのフォルダからの相対パスにする。
    private const string MovedResourcesRoot = "Assets/Resources_moved";
    // EnemyData は別フォルダに移動済み。アドレスは "EnemyData/<ファイル名>"。
    private const string EnemyDataRoot = "Assets/EnemyData";

    [MenuItem("Tools/TomsLands/データ生成/Addressables一括登録（Resources_moved）")]
    public static void RegisterMovedResources()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[AddressablesSetupTool] Addressable 設定が見つかりません。" +
                           "Window > Asset Management > Addressables > Groups で初期化してください。");
            return;
        }

        var group = settings.DefaultGroup;
        int count = 0;

        count += RegisterFolder(settings, group, MovedResourcesRoot, useRelativeAddress: true);
        count += RegisterFolder(settings, group, EnemyDataRoot, useRelativeAddress: false);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[AddressablesSetupTool] {count} 件のアセットを Addressable 登録しました。");
    }

    /// <param name="useRelativeAddress">
    /// true: アドレス = rootFolder からの相対パス（拡張子なし）。旧 Resources キーと一致させる用。
    /// false: アドレス = "<rootフォルダ名>/<ファイル名>"。
    /// </param>
    private static int RegisterFolder(AddressableAssetSettings settings, AddressableAssetGroup group,
                                      string rootFolder, bool useRelativeAddress)
    {
        if (!AssetDatabase.IsValidFolder(rootFolder))
        {
            Debug.LogWarning($"[AddressablesSetupTool] フォルダが見つかりません: {rootFolder}");
            return 0;
        }

        string rootName = System.IO.Path.GetFileName(rootFolder);
        int count = 0;

        foreach (var guid in AssetDatabase.FindAssets("t:Object", new[] { rootFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path)) continue; // フォルダ自体は除外

            var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);

            string address = useRelativeAddress
                ? StripExtension(path.Substring(rootFolder.Length + 1))
                : rootName + "/" + System.IO.Path.GetFileNameWithoutExtension(path);
            entry.SetAddress(address, postEvent: false);

            // 型名ラベルを付与（LoadAll をラベルで束ねるため）。force:true で未登録ラベルも自動追加。
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type != null)
                entry.SetLabel(type.Name, enable: true, force: true, postEvent: false);

            count++;
        }

        return count;
    }

    private static string StripExtension(string p)
    {
        int dot = p.LastIndexOf('.');
        return dot >= 0 ? p.Substring(0, dot) : p;
    }
}
#endif

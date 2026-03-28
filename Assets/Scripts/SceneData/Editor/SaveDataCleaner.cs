using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// エディタメニューからセーブデータを一括削除するツール。
/// 古い形式のセーブデータをクリアして「初めから」相当の状態にする。
/// </summary>
public static class SaveDataCleaner
{
    [MenuItem("Tools/Delete All Save Data")]
    public static void DeleteAllSaveData()
    {
        string dir = Application.persistentDataPath;
        string[] files = {
            "itemData.json",
            "tomsData.json",
            "displayItemData.json",
            "heroData.json",
            "streamingSelection.json",
        };

        int deletedCount = 0;
        foreach (var filename in files)
        {
            string path = Path.Combine(dir, filename);
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"[SaveDataCleaner] Deleted: {filename}");
                deletedCount++;
            }
        }

        if (deletedCount > 0)
        {
            Debug.Log($"[SaveDataCleaner] {deletedCount} file(s) deleted from: {dir}");
            EditorUtility.DisplayDialog("Save Data Cleaned",
                $"{deletedCount} 件のセーブデータを削除しました。\n\n場所: {dir}",
                "OK");
        }
        else
        {
            Debug.Log("[SaveDataCleaner] No save files found.");
            EditorUtility.DisplayDialog("Save Data Cleaned",
                "削除するセーブデータが見つかりませんでした。",
                "OK");
        }
    }
}


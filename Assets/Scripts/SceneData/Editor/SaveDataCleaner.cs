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
        string root = Application.persistentDataPath;

        // 旧形式（persistentDataPath直下）の残存ファイル
        string[] legacyFiles = {
            "itemData.json",
            "tomsData.json",
            "displayItemData.json",
            "heroData.json",
            "streamingSelection.json",
            "shopStatusData.json",
            "save.json",
            "save.json.tmp",
            "save.json.bak",
        };

        int deletedCount = 0;

        foreach (var filename in legacyFiles)
        {
            string path = Path.Combine(root, filename);
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"[SaveDataCleaner] Deleted (legacy): {filename}");
                deletedCount++;
            }
        }

        // スロット別フォルダ（slot_0 〜 slot_N）を丸ごと削除
        for (int slot = 0; slot < SaveSlotManager.MaxSlots; slot++)
        {
            string slotDir = SaveSlotManager.SlotRoot(slot);
            if (Directory.Exists(slotDir))
            {
                Directory.Delete(slotDir, recursive: true);
                Debug.Log($"[SaveDataCleaner] Deleted slot folder: {slotDir}");
                deletedCount++;
            }
        }

        if (deletedCount > 0)
        {
            Debug.Log($"[SaveDataCleaner] {deletedCount} item(s) deleted from: {root}");
            EditorUtility.DisplayDialog("Save Data Cleaned",
                $"{deletedCount} 件のセーブデータ（ファイル/スロット）を削除しました。\n\n場所: {root}",
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


using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class SaveSystem
{
    private const string FileName = "save.json";
    private static string SavePath => Path.Combine(Application.persistentDataPath, FileName);
    private static string TempPath => SavePath + ".tmp";
    private static string BackupPath => SavePath + ".bak";

    // ランタイムの DungeonData → GameSaveData に変換して保存
    public static void Save(List<DungeonData> runtimeDungeons)
    {
        if (runtimeDungeons == null) return;

        var game = new GameSaveData();
        foreach (var d in runtimeDungeons)
        {
            if (d == null || string.IsNullOrWhiteSpace(d.dungeonName)) continue;
            game.dungeons.Add(new DungeonSaveData
            {
                dungeonKey = d.dungeonName,
                currentDungeonLevel = d.currentDungeonLevel,
                dungeonStatus = d.dungeonStatus,
                isShowedInfo = d.isShowedInfo
            });
        }

        var json = JsonUtility.ToJson(game, prettyPrint: true);
        WriteTextAtomic(SavePath, TempPath, BackupPath, json);
#if UNITY_EDITOR
        Debug.Log($"[SaveSystem] Saved: {SavePath}\n{json}");
#endif
    }

    // 失敗時は false。成功時は save を返す
    public static bool TryLoad(out GameSaveData save)
    {
        save = null;

        if (!File.Exists(SavePath))
            return false;

        try
        {
            var json = File.ReadAllText(SavePath);
            var loaded = JsonUtility.FromJson<GameSaveData>(json);
            if (loaded?.dungeons == null) return false;
            save = loaded;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveSystem] Load failed: {e.Message}. Try backup...");
            // 壊れていたらバックアップから復旧を試みる
            if (File.Exists(BackupPath))
            {
                try
                {
                    var json = File.ReadAllText(BackupPath);
                    var loaded = JsonUtility.FromJson<GameSaveData>(json);
                    if (loaded?.dungeons != null)
                    {
                        save = loaded;
                        File.Copy(BackupPath, SavePath, overwrite: true);
                        Debug.LogWarning("[SaveSystem] Restored from backup.");
                        return true;
                    }
                }
                catch (Exception e2)
                {
                    Debug.LogWarning($"[SaveSystem] Backup load failed: {e2.Message}");
                }
            }
            return false;
        }
    }

    public static bool Exists() => File.Exists(SavePath);

    public static void Delete()
    {
        foreach (var path in new[] { SavePath, TempPath, BackupPath })
        {
            if (File.Exists(path)) File.Delete(path);
        }
#if UNITY_EDITOR
        Debug.Log("[SaveSystem] Save data deleted.");
#endif
    }

    // 一時ファイル→置換の疑似アトミック書き込み
    private static void WriteTextAtomic(string path, string tempPath, string backupPath, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        File.WriteAllText(tempPath, content);

        // 旧ファイルをバックアップ
        if (File.Exists(path))
        {
            try
            {
                File.Copy(path, backupPath, overwrite: true);
            }
            catch { /* 失敗しても致命ではない */ }
        }

        // 置換
        if (File.Exists(path)) File.Delete(path);
        File.Move(tempPath, path);
    }
}
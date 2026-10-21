using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class SaveSystem
{
    private const string FileName = "save.json";
    // 選択中スロット配下（persistentDataPath/slot_N/save.json）に保存・ロードする。
    private static string SavePath => SaveSlotManager.GetPath(FileName);

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
                // 以前は dungeonName（日本語名）を書いていて、読み込みの Enum.TryParse で必ず外れて
                // セーブのレベルが SO の初期値に戻っていた。enum 名で書く（読み込みは日本語名も受け付ける）
                dungeonKey = d.key.ToString(),
                currentDungeonLevel = d.currentDungeonLevel,
                dungeonStatus = d.dungeonStatus,
                isShowedInfo = d.isShowedInfo,
                levelVersion = 1
            });
        }

        var json = JsonUtility.ToJson(game, prettyPrint: true);
        // 一時ファイル→置き換え→.bak（全セーブ共通の書き方。SaveFile）
        SaveFile.WriteAtomic(SavePath, json);
#if UNITY_EDITOR
        Debug.Log($"[SaveSystem] Saved: {SavePath}\n{json}");
#endif
    }

    // 失敗時は false。成功時は save を返す（壊れていたら .bak から復旧する）
    public static bool TryLoad(out GameSaveData save)
        => SaveFile.TryReadJson(SavePath, out save, loaded => loaded.dungeons != null);

    public static bool Exists() => File.Exists(SavePath);

    public static void Delete()
    {
        // 本体と .tmp / .bak をまとめて消す
        SaveFile.DeleteWithBackups(SavePath);
#if UNITY_EDITOR
        Debug.Log("[SaveSystem] Save data deleted.");
#endif
    }
}

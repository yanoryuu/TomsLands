using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DungeonData
{
    // ----------------------
    // 識別・基本情報
    // ----------------------
    public DungeonName key;
    public string dungeonName;

    // ----------------------
    // 表示用情報
    // ----------------------
    public string dungeonDescription;
    public Sprite dungeonImage;

    // ----------------------
    // レベル・難易度
    // ----------------------
    public int initDungeonLevel;
    public int recommendedLevel;
    public int difficulty;
    public ItemTypeData.ItemAttribute requiredAttribute;

    // ----------------------
    // 敵データ
    // ----------------------
    public List<EnemyData> dungeonMonsters;
    public string dungeonBoss;

    // ----------------------
    // 進行状況・報酬
    // ----------------------
    public int currentDungeonLevel;   // 周回・進行度
    public int rewardGold;            // 魔王軍勝利時の報酬
    public DungeonStatus dungeonStatus; // 未攻略 / クリア / 失敗 など

    
    // ----------------------
    // ScriptableObject からの注入
    // ----------------------
    public DungeonData(DungeonInfoScriptableObj so)
    {
        // 基本情報
        key                = so.key;
        dungeonName        = so.dungeonName;

        // 表示情報
        dungeonDescription = so.dungeonDescription;
        dungeonImage       = so.dungeonImage;

        // レベル・難易度
        initDungeonLevel   = so.initDungeonLevel;
        recommendedLevel   = so.recommendedLevel;
        difficulty         = Mathf.Clamp(so.difficulty, 1, 10);
        requiredAttribute  = so.requiredAttribute;

        // 敵データ
        dungeonMonsters    = so.dungeonMonsters != null
            ? new List<EnemyData>(so.dungeonMonsters)
            : new List<EnemyData>();
        dungeonBoss        = so.dungeonBoss;

        currentDungeonLevel = so.currentDungeonLevel;
        rewardGold          = so.rewardGold;
        dungeonStatus       = so.dungeonStatus;
    }
    
    public DungeonSaveData ToSave() => new DungeonSaveData {
        dungeonKey = key.ToString(),
        currentDungeonLevel = currentDungeonLevel
    };


    public static DungeonData FromSave(DungeonSaveData save, IDungeonCatalog catalog)
    {
        if (!Enum.TryParse<DungeonName>(save.dungeonKey, out var key))
        {
            Debug.LogError($"Unknown dungeon key in save: {save.dungeonKey}");
            return null;
        }
        var so = catalog.GetDungeon(key);
        if (so == null)
        {
            Debug.LogError($"SO not found for key: {key}");
            return null;
        }
        var d = new DungeonData(so);
        d.currentDungeonLevel = save.currentDungeonLevel;
        return d;
    }
}

[Serializable]
public enum DungeonName
{
    GreenRest,
    FrostReach,
    DuskHeaven,
    CenterCity,
    MausoleumOblivion,
    ScorchingVolcanoPrison,
    IceMistCave,
    DeepGreenBeastForest,
    AncientMechanicalCastle,
    DemonKingCastle
}

[Serializable]
public enum DungeonStatus
{
    Clear,
    Fail,
    Still
}
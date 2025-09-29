using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DungeonData
{
    // ---- 基本情報 ----
    public DungeonName key;
    public string dungeonName;

    // ---- 表示用 ----
    public string dungeonDescription;
    public Sprite dungeonImage;

    // ---- レベル/難易度 ----
    public int initDungeonLevel;
    public int recommendedLevel;
    public int difficulty; // 1-10

    // ---- 入場条件 ----
    public ItemTypeData.ItemAttribute requiredAttribute;

    // ---- 敵データ ----
    public List<EnemyData> dungeonMonsters;
    public string dungeonBoss;

    // ---- 進行状況 ----
    public int currentDungeonLevel;
    public int rewardGold;
    public DungeonStatus dungeonStatus;

    /// <summary>
    /// ScriptableObject から全データを注入
    /// </summary>
    public DungeonData(DungeonInfoScriptableObj so)
    {
        key          = so.key;
        dungeonName        = so.dungeonName;
        dungeonDescription = so.dungeonDescription;
        dungeonImage       = so.dungeonImage;

        initDungeonLevel   = so.initDungeonLevel;
        recommendedLevel   = so.recommendedLevel;
        difficulty         = Mathf.Clamp(so.difficulty, 1, 10);
        requiredAttribute  = so.requiredAttribute;

        dungeonMonsters    = so.dungeonMonsters != null ? new List<EnemyData>(so.dungeonMonsters) : new();
        dungeonBoss        = so.dungeonBoss;

        // デフォルトの進行値はSO準拠のみ
        currentDungeonLevel = so.currentDungeonLevel;
        rewardGold          = so.rewardGold;
        
        dungeonStatus = so.dungeonStatus;
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
    None,
    Clear,
    Fail,
    Still
}
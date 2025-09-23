using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DungeonData
{
    // ---- 基本情報 ----
    public string dungeonId;
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

    /// <summary>
    /// ScriptableObject から全データを注入
    /// </summary>
    public DungeonData(DungeonInfoScriptableObj so)
    {
        dungeonId          = so.dungeonId;
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
    }
    
    public DungeonSaveData ToSave() => new DungeonSaveData
    {
        dungeonId = dungeonId,
        currentDungeonLevel = currentDungeonLevel
    };

    public static DungeonData FromSave(DungeonSaveData save, IDungeonCatalog catalog)
    {
        var so = catalog.GetDungeon(save.dungeonId);
        if (so == null)
        {
            Debug.LogError($"Dungeon SO not found for id: {save.dungeonId}");
            return null;
        }

        var data = new DungeonData(so);
        data.currentDungeonLevel = save.currentDungeonLevel; // 可変なのはここだけ反映
        return data;
    }
}

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
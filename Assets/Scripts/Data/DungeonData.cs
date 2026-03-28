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
    public Sprite dungeonIcon;
    public Sprite dungeonNameImage;

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
    public bool isShowedInfo;           // 情報を購入済みかどうか

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
        dungeonIcon        = so.dungeonIcon;
        dungeonNameImage   = so.dungeonNameImage;

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
        
        // マップに表示するかどうかは、初期状態では false に設定
        isShowedInfo        = false;
    }
}

[Serializable]
public enum DungeonName
{
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
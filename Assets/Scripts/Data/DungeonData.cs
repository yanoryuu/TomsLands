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
    // 敵データ（レベル別）
    // ----------------------
    public DungeonLevelData[] levelDataList;

    /// <summary>
    /// 現在レベルのモンスター一覧（後方互換プロパティ）
    /// </summary>
    public List<EnemyData> dungeonMonsters
    {
        get
        {
            var data = GetLevelData(currentDungeonLevel);
            return data?.monsters ?? new List<EnemyData>();
        }
    }

    /// <summary>
    /// 現在レベルのボス名（後方互換プロパティ）
    /// </summary>
    public string dungeonBoss
    {
        get
        {
            var data = GetLevelData(currentDungeonLevel);
            return data?.bossName ?? "";
        }
    }

    /// <summary>
    /// 指定レベル（1～5）のデータを取得する。
    /// </summary>
    public DungeonLevelData GetLevelData(int level)
    {
        int index = Mathf.Clamp(level, 1, 5) - 1;
        if (levelDataList == null || levelDataList.Length == 0) return null;
        if (index >= levelDataList.Length) return null;
        return levelDataList[index];
    }

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

        // 敵データ（レベル別をコピー）
        if (so.levelDataList != null)
        {
            levelDataList = new DungeonLevelData[so.levelDataList.Length];
            for (int i = 0; i < so.levelDataList.Length; i++)
            {
                var src = so.levelDataList[i];
                if (src != null)
                {
                    levelDataList[i] = new DungeonLevelData
                    {
                        monsters = src.monsters != null
                            ? new List<EnemyData>(src.monsters)
                            : new List<EnemyData>(),
                        bossName = src.bossName
                    };
                }
                else
                {
                    levelDataList[i] = new DungeonLevelData();
                }
            }
        }
        else
        {
            levelDataList = new DungeonLevelData[5];
            for (int i = 0; i < 5; i++)
                levelDataList[i] = new DungeonLevelData();
        }

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
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DungeonInfo", menuName = "ScriptableObjects/DungeonInfo", order = 1)]
public class DungeonInfoScriptableObj : ScriptableObject
{
    // ----------------------
    // 識別・基本情報
    // ----------------------
    [Header("基本情報")]
    [Tooltip("ダンジョンを一意に識別するID（セーブやDB用）")]
    public DungeonName key;

    [Tooltip("ダンジョン名（表示用）")]
    public string dungeonName;


    // ----------------------
    // 表示用情報
    // ----------------------
    [Header("表示情報")]
    [Tooltip("ダンジョンの説明文（UI表示用）")]
    [TextArea]
    public string dungeonDescription;

    [Tooltip("ダンジョンの背景画像（FightScene用）")]
    public Sprite dungeonImage;

    [Tooltip("ダンジョンのアイコン画像（マップ・情報画面用）")]
    public Sprite dungeonIcon;

    [Tooltip("ダンジョン名の画像（ダンジョン情報パネル用）")]
    public Sprite dungeonNameImage;


    // ----------------------
    // 難易度・レベル関連
    // ----------------------
    [Header("レベル・難易度")]
    [Tooltip("初期レベル（基準値）")]
    public int initDungeonLevel;

    [Tooltip("推奨レベル（挑戦目安）")]
    public int recommendedLevel;

    [Tooltip("難易度（1～10）")]
    [Range(1, 10)]
    public int difficulty;


    // ----------------------
    // 入場条件
    // ----------------------
    [Header("入場条件")]
    [Tooltip("入場に必要な属性")]
    public ItemTypeData.ItemAttribute requiredAttribute;


    // ----------------------
    // 敵データ（レベル別）
    // ----------------------
    [Header("敵データ（レベル別）")]
    [Tooltip("レベル1～5ごとの出現モンスター・ボスデータ")]
    public DungeonLevelData[] levelDataList = new DungeonLevelData[5];

    /// <summary>
    /// 指定レベル（1～5）のデータを取得する。範囲外はクランプ。
    /// </summary>
    public DungeonLevelData GetLevelData(int level)
    {
        int index = Mathf.Clamp(level, 1, 5) - 1;
        if (levelDataList == null || levelDataList.Length == 0) return null;
        if (index >= levelDataList.Length) return null;
        return levelDataList[index];
    }

    // ----------------------
    // 魔王軍が勝った時の報酬（※レベルごとに DungeonLevelData.rewardGold で設定）
    // ----------------------
}
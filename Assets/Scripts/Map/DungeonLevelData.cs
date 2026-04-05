using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ダンジョンの各レベルにおける敵データ・報酬・レベルアップ費用を保持するクラス。
/// </summary>
[Serializable]
public class DungeonLevelData
{
    [Tooltip("このレベルで出現するモンスター一覧")]
    public List<EnemyData> monsters = new List<EnemyData>();

    [Tooltip("このレベルのボス名")]
    public string bossName;

    [Tooltip("勇者が敗北した時（魔王軍勝利時）にプレイヤーが受け取る報酬ゴールド")]
    public int rewardGold;

    [Tooltip("このレベルから次のレベルへ上げるために必要なゴールド（最大レベルでは0でOK）")]
    public int levelUpCost;
}


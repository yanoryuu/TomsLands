using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ダンジョンの各レベルにおける敵データを保持するクラス。
/// </summary>
[Serializable]
public class DungeonLevelData
{
    [Tooltip("このレベルで出現するモンスター一覧")]
    public List<EnemyData> monsters = new List<EnemyData>();

    [Tooltip("このレベルのボス名")]
    public string bossName;
}


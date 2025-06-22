using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "New StageData", menuName = "Battle/StageData")]
public class StageData : ScriptableObject
{
    [Header("基本情報")]
    public string stageName = "新しいステージ";
    [TextArea(3, 5)]
    public string description = "ステージの説明文。";

    [Header("出現モンスター")]
    public List<EnemyData> normalEnemies;
    public EnemyData bossEnemy;

    //TODO:拡張性がないので、後でリファクタリングする
    [Header("ステージギミック")]
    public bool hasFrostbiteDamage = false; // 「極寒エリア」効果の有無
    public int frostbiteDamagePerTurn = 5;  // ターンごとのHP減少量
}
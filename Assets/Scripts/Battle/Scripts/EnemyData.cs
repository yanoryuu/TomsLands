using System;
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "New EnemyData", menuName = "Battle/EnemyData")]
public class EnemyData : ScriptableObject
{
    [Header("基本ステータス")]
    public string enemyId = "";              // 判別用ID
    public string enemyName = "モンスター"; // 表示用の名前
    public int hp = 100;                   // HP
    public int attackPower = 10;           // 攻撃力
    public int defensePower = 5;           // 防御力

    [Header("属性・見た目")]
    public ElementType elementType;        // 属性
    public Sprite enemySprite;             // 敵のスプライト

    [Header("使用スキル")]
    public List<SkillData> skills;
    
    [Header("モンスターの説明")]
    public String description;
    
    [Header("ボス")]
    public bool isBoss = false;
}

public enum ElementType 
{ 
    None,
    Water, 
    Fire, 
    Wood, 
    Light, 
    Dark 
}
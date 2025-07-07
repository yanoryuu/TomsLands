using UnityEngine;
using System.Collections.Generic;

// --- これもお馴染み、アセット作成メニューを追加する魔法です ---
[CreateAssetMenu(fileName = "New SkillData", menuName = "Battle/SkillData")]
public class SkillData : ScriptableObject
{
    [Header("基本情報")]
    public string skillName = "スキル";

    [TextArea(3, 5)]
    public string description = "スキルの説明文。";
    public int mpCost = 10;                // 消費MP

    [Header("効果詳細")]
    public SkillType skillType = SkillType.Attack;         // スキルの種類
    public ElementType elementType = ElementType.None;     // スキルの属性
    public TargetType targetType = TargetType.EnemySingle; // スキルの対象
    public float power = 30f;              // ダメージや回復の基本威力

    [Header("追加効果")]
    // どんな状態異常を、どのくらいの確率・持続ターンで付与するか
    public List<StatusEffectData> additionalEffects; 
}

// --- スキルの種類 ---
public enum SkillType 
{ 
    Attack,     // 攻撃
    Heal,       // 回復
    Buff,       // 味方への強化
    Debuff      // 敵への弱体・状態異常
}

// --- スキルの対象 ---
public enum TargetType
{
    EnemySingle, // 敵単体
    EnemyAll,    // 敵全体
    Self,        // 自分自身
    AllySingle,  // 味方単体
    AllyAll      // 味方全体
}

// --- 状態異常の種類 ---
public enum StatusEffectType
{
    None,
    Poison,     // 毒
    Paralysis,  // 麻痺
    Petrify,    // 石化
    Freeze      // 凍結
}

[System.Serializable]
public class StatusEffectData
{
    public StatusEffectType effectType; // どの状態異常か
    [Range(0, 1)]
    public float chance = 1.0f;         // 付与する確率 (1.0 = 100%)
    public int duration = 3;            // 持続ターン数
}
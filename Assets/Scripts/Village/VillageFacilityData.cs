using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 村施設のマスターデータ（1施設=1アセット）。
/// 配置: Assets/Resources_moved/Village/、Addressablesラベル "VillageFacilityData"。
/// 配信: balance.json の "villageFacilities" リスト区画（facilityIdキー）で上書き可能。
/// V1では効果はexpectText（表示）のみ。実効果の適用は V2以降（startBonus/modifiers/unlockRelicTier の器だけ先行）。
/// </summary>
[CreateAssetMenu(fileName = "VillageFacilityData", menuName = "ScriptableObjects/Village/VillageFacilityData")]
public class VillageFacilityData : ScriptableObject
{
    public string facilityId;
    public string facilityName;
    [TextArea] public string description;   // フレーバー1行
    public Sprite icon;

    [Tooltip("Lv1の建設に必要な領主館Lv（0=最初から建設可）。Lv2以降は共通ルール（領主館Lv >= 目標Lv-1）")]
    public int requiredHallLevel;

    [Tooltip("index 0 = Lv1。配列の長さが最大レベル")]
    public List<FacilityLevelEntry> levels = new();

    public int MaxLevel => levels?.Count ?? 0;

    /// <summary>1始まりのレベルのエントリ（範囲外はnull）。</summary>
    public FacilityLevelEntry GetLevel(int level)
    {
        if (levels == null || level < 1 || level > levels.Count) return null;
        return levels[level - 1];
    }
}

/// <summary>施設の1レベルぶんの定義（JsonUtility対応のフラット構造）。</summary>
[Serializable]
public class FacilityLevelEntry
{
    [Tooltip("このレベルへ上げるための村資金コスト")]
    public int cost;

    [Tooltip("このレベルの効果説明（UI表示用）")]
    [TextArea] public string effectText;

    // --- 以下は V2以降で使う器（V1では未使用） ---
    [Tooltip("開始時型の効果キー（例: startMoney / carrySlots / blacksmithLevel）")]
    public string startBonusKey;
    public float startBonusValue;

    [Tooltip("常時型の効果（RelicEffectResolverに合成される恒常modifier）")]
    public List<RelicModifier> modifiers = new();

    [Tooltip("抽選型: このレベルで解禁されるレリックTier（0=なし）")]
    public int unlockRelicTier;
}

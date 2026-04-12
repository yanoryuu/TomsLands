using UnityEngine;

/// <summary>
/// フォロワーマイルストーンデータ（ScriptableObject）
/// 一定のフォロワー数を達成した際に得られるボーナスを定義する。
/// 複数のマイルストーンを配列で管理し、達成済みの最高レベルの効果を適用する。
/// </summary>
[CreateAssetMenu(fileName = "FollowerMilestoneData", menuName = "ScriptableObjects/Marketing/FollowerMilestoneData")]
public class FollowerMilestoneData : ScriptableObject
{
    [Header("マイルストーン条件")]
    [Tooltip("このマイルストーン達成に必要なフォロワー数")]
    public int requiredFollowers;

    [Header("ボーナス効果")]
    [Tooltip("基本売上ボーナス率（例: 0.05 = 5%UP）")]
    [Range(0f, 5f)]
    public float salesBonusRate;

    [Tooltip("バズ発生確率ボーナス（例: 5.0 = +5%）")]
    [Range(0f, 50f)]
    public float buzzChanceBonus;

    [Tooltip("広告費割引率（例: 0.1 = 10%OFF）")]
    [Range(0f, 1f)]
    public float adDiscountRate;
}


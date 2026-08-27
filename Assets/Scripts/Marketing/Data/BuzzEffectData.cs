using System;
using UnityEngine;

/// <summary>
/// バズの種類を定義する列挙型
/// </summary>
public enum BuzzType
{
    /// <summary>炎上（ネガティブバズ）</summary>
    Flame,
    /// <summary>通常バズ</summary>
    Normal,
    /// <summary>超バズ（旧: 大バズ）</summary>
    Big
}

/// <summary>
/// バズ効果データ（ScriptableObject）
/// 各バズタイプ（炎上・通常・大バズ）ごとに1つずつ作成する。
/// 即時効果・持続効果・終了後効果の3段階のパラメータを保持。
/// </summary>
[CreateAssetMenu(fileName = "BuzzEffectData", menuName = "ScriptableObjects/Marketing/BuzzEffectData")]
public class BuzzEffectData : ScriptableObject
{
    [Header("バズタイプ")]
    [Tooltip("このデータが対応するバズの種類")]
    public BuzzType buzzType;

    // =====================================================
    // 即時効果
    // =====================================================
    [Header("即時効果 ─ 売上")]
    [Tooltip("売上倍率の基礎値（例: 炎上=0.5, 通常=1.5, 大バズ=2.5）")]
    public float immediateRevenueMultiplierBase = 1.5f;

    [Tooltip("拡散力による売上倍率ボーナス係数（売上倍率 += 拡散力 × この値）")]
    public float immediateRevenueSpreadCoeff = 0.01f;

    [Header("即時効果 ─ ステータス変化")]
    [Tooltip("信頼度の変化量（マイナスも可）")]
    public int immediateTrustChange;

    [Tooltip("注目度の変化量")]
    public int immediateAttentionChange;

    [Header("即時効果 ─ フォロワー")]
    [Tooltip("フォロワー変化の基礎値（炎上時はマイナス固定値など）")]
    public int immediateFollowerBase;

    [Tooltip("フォロワー変化の拡散力係数（フォロワー += 拡散力 × この値）")]
    public float immediateFollowerSpreadCoeff;

    [Tooltip("trueの場合、フォロワー変化は固定値（immediateFollowerBase）のみ使用")]
    public bool immediateFollowerFixed;

    // =====================================================
    // 持続効果
    // =====================================================
    [Header("持続効果 ─ ターン数")]
    [Tooltip("持続ターン数の基礎値")]
    public int durationBase = 3;

    [Tooltip("顧客維持力による持続ターン数ボーナス係数（持続ターン += 顧客維持力 ÷ この値）")]
    public float durationRetentionDivisor = 25f;

    [Header("持続効果 ─ 毎ターン")]
    [Tooltip("毎ターンの全ステータス上昇量")]
    public int sustainedAllStatGain;

    [Tooltip("毎ターンのフォロワー獲得量")]
    public int sustainedFollowerGain;

    [Tooltip("毎ターンの信頼度変化量（炎上時はマイナス）")]
    public int sustainedTrustChange;

    [Tooltip("持続中の売上倍率（0の場合は即時倍率を継続使用）")]
    public float sustainedRevenueMultiplier;

    [Tooltip("広告費割引率（0〜1, 例: 0.3 = 30%OFF）")]
    [Range(0f, 1f)]
    public float sustainedAdDiscountRate;

    // =====================================================
    // 終了後効果
    // =====================================================
    [Header("終了後効果")]
    [Tooltip("終了後の信頼度変化量")]
    public int afterTrustChange;

    [Tooltip("終了後の注目度変化量")]
    public int afterAttentionChange;

    [Tooltip("特殊効果: 無料広告1回分の効果を付与するか")]
    public bool afterGrantFreeMarketing;

    [Tooltip("afterGrantFreeMarketing=true時に適用する広告データ（総合マーケティングなど）")]
    public AdvertisementData afterFreeMarketingData;

    // =====================================================
    // 計算ヘルパーメソッド
    // =====================================================

    /// <summary>
    /// 即時売上倍率を計算する
    /// 計算式: 基礎値 + (拡散力 × 拡散力係数)
    /// </summary>
    public float CalculateRevenueMultiplier(int spreadPower)
    {
        return immediateRevenueMultiplierBase + spreadPower * immediateRevenueSpreadCoeff;
    }

    /// <summary>
    /// 即時フォロワー変化量を計算する
    /// 固定値モード: 基礎値のみ使用
    /// 変動モード: 基礎値 + (拡散力 × 拡散力係数)
    /// </summary>
    public int CalculateFollowerChange(int spreadPower)
    {
        if (immediateFollowerFixed)
        {
            return immediateFollowerBase;
        }
        return immediateFollowerBase + Mathf.RoundToInt(spreadPower * immediateFollowerSpreadCoeff);
    }

    /// <summary>
    /// 持続ターン数を計算する
    /// 計算式: 基礎値 + (顧客維持力 ÷ 除数)
    /// </summary>
    public int CalculateDuration(int retentionPower)
    {
        if (durationRetentionDivisor <= 0f)
        {
            Debug.LogWarning($"[BuzzEffectData] durationRetentionDivisor が 0 以下です: {name}");
            return durationBase;
        }
        return durationBase + Mathf.FloorToInt(retentionPower / durationRetentionDivisor);
    }
}


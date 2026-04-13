using UnityEngine;

/// <summary>
/// ゲームバランスデータ（ScriptableObject）
/// マーケティングシステム全体のバランス調整用パラメータを一元管理する。
/// ステータス上限、バズ確率計算の係数、バズ判定の閾値などを含む。
/// </summary>
[CreateAssetMenu(fileName = "GameBalanceData", menuName = "ScriptableObjects/Marketing/GameBalanceData")]
public class GameBalanceData : ScriptableObject
{
    // =====================================================
    // ステータス設定
    // =====================================================
    [Header("ステータス上下限")]
    [Tooltip("全ステータスの最小値")]
    public int statMin = 0;

    [Tooltip("全ステータスの最大値")]
    public int statMax = 100;

    [Tooltip("フォロワー数の最小値（0以上）")]
    public int followerMin = 0;

    // =====================================================
    // バズ確率計算
    // =====================================================
    [Header("バズ確率計算")]
    [Tooltip("バズ発生確率における注目度の係数（基本確率 = 注目度 × この値 + 信頼度 × 信頼度係数）")]
    public float buzzAttentionCoeff = 0.5f;

    [Tooltip("バズ発生確率における信頼度の係数")]
    public float buzzTrustCoeff = 0.2f;

    [Tooltip("バズ発生確率の最大値（フォロワーボーナスを除く、%単位）")]
    public float buzzMaxBaseChance = 50f;

    // =====================================================
    // バズ種類判定
    // =====================================================
    [Header("バズ種類判定")]
    [Tooltip("炎上が発生しうる信頼度の閾値（信頼度がこの値未満のとき炎上判定が行われる）")]
    public int flameTrustThreshold = 50;

    [Tooltip("炎上発生確率（%単位、信頼度が閾値未満の場合）")]
    [Range(0f, 100f)]
    public float flameChance = 30f;

    [Tooltip("大バズになる信頼度の閾値（信頼度がこの値以上のとき大バズになる）")]
    public int bigBuzzTrustThreshold = 80;

    // =====================================================
    // 初期値
    // =====================================================
    [Header("ステータス初期値")]
    [Tooltip("信頼度の初期値")]
    public int initialTrust = 50;

    [Tooltip("注目度の初期値")]
    public int initialAttention = 0;

    [Tooltip("拡散力の初期値")]
    public int initialSpread = 0;

    [Tooltip("顧客維持力の初期値")]
    public int initialRetention = 0;

    [Tooltip("フォロワーの初期値")]
    public int initialFollowers = 0;
}


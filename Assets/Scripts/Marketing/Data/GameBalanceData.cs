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
    [Header("バズ確率計算（強化度合いの算出用）")]
    [Tooltip("強化度合いにおける注目度の係数（強化ボーナス = 注目度 × この値 + 信頼度 × 信頼度係数 + フォロワーボーナス）")]
    public float buzzAttentionCoeff = 0.5f;

    [Tooltip("強化度合いにおける信頼度の係数")]
    public float buzzTrustCoeff = 0.2f;

    [Tooltip("強化ボーナスの正規化基準値。強化ボーナスがこの値に達すると発生率が最大になる（%単位）")]
    public float buzzMaxBaseChance = 50f;

    [Header("バズ発生率")]
    [Tooltip("通常バズの基礎発生率（%）。強化なしのときの毎ターン発生率")]
    [Range(0f, 100f)]
    public float buzzBaseChance = 10f;

    [Tooltip("通常バズの最大発生率（%）。強化が最大のときの発生率")]
    [Range(0f, 100f)]
    public float buzzMaxChance = 20f;

    [Tooltip("超バズの基礎発生率（%）。強化なしのときの毎ターン発生率")]
    [Range(0f, 100f)]
    public float bigBuzzBaseChance = 1f;

    [Tooltip("超バズの最大発生率（%）。強化が最大のときの発生率")]
    [Range(0f, 100f)]
    public float bigBuzzMaxChance = 5f;

    [Header("バズ継続・発展")]
    [Tooltip("バズが次のターンも継続する確率（%）。失敗するとそのターンで終了する")]
    [Range(0f, 100f)]
    public float buzzContinueChance = 50f;

    [Tooltip("通常バズが超バズへ発展する確率（%）。バズ継続中に毎ターン判定される")]
    [Range(0f, 100f)]
    public float buzzEvolveToBigChance = 20f;

    // =====================================================
    // バズ種類判定
    // =====================================================
    [Header("バズ種類判定")]
    [Tooltip("炎上が発生しうる信頼度の閾値（信頼度がこの値未満のとき炎上判定が行われる）")]
    public int flameTrustThreshold = 50;

    [Tooltip("炎上発生確率（%単位、信頼度が閾値未満の場合）")]
    [Range(0f, 100f)]
    public float flameChance = 30f;

    // ※旧 bigBuzzTrustThreshold（信頼度閾値による大バズ確定）は
    //   バズ確率の新方式（bigBuzzBaseChance～bigBuzzMaxChance の独立ロール）移行に伴い廃止。

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


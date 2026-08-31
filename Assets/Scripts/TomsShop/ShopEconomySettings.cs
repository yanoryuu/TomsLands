using UnityEngine;

/// <summary>
/// TomsShop のターン毎の緩やかな経済変動設定。
/// Inspector で全パラメータを調整可能。
/// </summary>
[CreateAssetMenu(fileName = "ShopEconomySettings", menuName = "ScriptableObjects/ShopEconomySettings")]
public class ShopEconomySettings : ScriptableObject
{
    [Header("案S1: 需要連動型じわじわ価格変動")]
    [Tooltip("人気商品（Demand≥この値）の毎ターン価格上昇率（例: 1.02 = 2%UP）")]
    public float highDemandThreshold = 0.7f;
    [Tooltip("人気商品の価格上昇率の下限")]
    public float highDemandPriceRateMin = 1.01f;
    [Tooltip("人気商品の価格上昇率の上限")]
    public float highDemandPriceRateMax = 1.03f;

    [Tooltip("不人気商品（Demand≤この値）の毎ターン価格下落率（例: 0.97 = 3%DOWN）")]
    public float lowDemandThreshold = 0.3f;
    [Tooltip("不人気商品の価格下落率の下限")]
    public float lowDemandPriceRateMin = 0.97f;
    [Tooltip("不人気商品の価格下落率の上限")]
    public float lowDemandPriceRateMax = 0.99f;

    [Tooltip("普通の商品（中間Demand）の価格変動率の下限")]
    public float normalDemandPriceRateMin = 0.99f;
    [Tooltip("普通の商品（中間Demand）の価格変動率の上限")]
    public float normalDemandPriceRateMax = 1.01f;

    [Header("価格の上下限（元値に対する倍率）")]
    [Tooltip("価格の下限（例: 0.3 = 元値の30%）")]
    public float shopPriceFloorRate = 0.3f;
    [Tooltip("価格の上限（例: 3.0 = 元値の3倍）")]
    public float shopPriceCeilingRate = 3.0f;

    [Header("案D1: 戦闘結果の属性波及")]
    [Tooltip("勝利時、使用装備と同属性の全アイテムの需要UP量")]
    public float victoryAttributeDemandUp = 0.05f;
    [Tooltip("敗北時、使用装備と同属性の全アイテムの需要DOWN量")]
    public float defeatAttributeDemandDown = 0.05f;

    [Header("案D2: 品出し陳列効果")]
    [Tooltip("品出し中のアイテムの需要UP量（毎ターン）")]
    public float displayDemandUp = 0.02f;
    [Tooltip("品出ししていないアイテムの需要DOWN量（毎ターン）")]
    public float notDisplayDemandDown = 0.01f;

    [Header("需要の上下限")]
    [Tooltip("需要の下限")]
    public float demandFloor = 0.05f;
    [Tooltip("需要の上限")]
    public float demandCeiling = 1.0f;

    // ====================================================================
    // 広告ステータス連動係数（役割分担方式）
    // Trust/Attention/Spread/Retention/Followers が 0 のとき従来挙動と
    // 完全一致するよう、すべての係数は加算/乗算前の補正項として設計する。
    // ====================================================================

    [Header("案A1: Trust → 価格Floor底上げ")]
    [Tooltip("信頼度MAX(100)時にFloor倍率に加算される量。例: 0.4 → Floor 30% が 70% まで底上げ。" +
             "Trust=0 のときは加算 0（従来挙動）。想定レンジ: 0.0〜0.7")]
    public float trustFloorBoost = 0.4f;

    [Header("案A2: Attention → S1上振れ増幅")]
    [Tooltip("注目度MAX(100)時に S1 価格上昇率の上限を引き上げる追加倍率。" +
             "例: 0.5 → s1Max 1.03 が 1.03 × 1.5 = 1.545 まで増幅。Attention=0 のときは増幅 0。想定レンジ: 0.0〜1.0")]
    public float attentionPriceAmplify = 0.5f;
    [Tooltip("Attention 増幅を low 需要レンジの max にも適用するか。" +
             "false なら高需要・通常需要レンジの max のみ増幅（= 上振れだけ強化）。")]
    public bool attentionAffectsLowDemand;

    [Header("案A3: Spread → D2品出し変動増幅")]
    [Tooltip("拡散力MAX(100)時に displayDemandUp / notDisplayDemandDown を何倍にするかの追加分。" +
             "例: 1.0 → +0.02 が +0.04 / -0.01 が -0.02 に増幅。Spread=0 のときは増幅 0。想定レンジ: 0.0〜2.0")]
    public float spreadDemandAmplify = 1.0f;

    [Header("案A4: Retention → 価格率を1.0へ寄せる安定化")]
    [Tooltip("顧客維持力MAX(100)時に S1/S3 価格率を Mathf.Lerp(rate, 1.0, t) で 1.0 寄りに引き寄せる強度 t。" +
             "例: 0.6 → 1.05 が 1.02 程度まで圧縮（= 常連は安定価格）。Retention=0 のときは 0（無圧縮）。想定レンジ: 0.0〜0.9")]
    public float retentionStabilizer = 0.6f;

    [Header("案A5: Followers → 需要ベース底上げ")]
    [Tooltip("フォロワー数による需要ベースシフトの強度係数。最終加算量は " +
             "followerWeight * Log10(1 + Followers / followerScale)。" +
             "例: weight=0.1, scale=1000, Followers=1000 → +0.1 * Log10(2) ≒ +0.030。" +
             "Followers=0 のときは 0。想定レンジ: 0.0〜0.3")]
    public float followerWeight = 0.1f;
    [Tooltip("Followers の対数スケール基準。例: 1000 にすると 1000フォロワーで Log10(2)≒0.301、" +
             "10000 で Log10(11)≒1.041。値を大きくするほど効きが緩やかになる。想定レンジ: 100〜10000")]
    public float followerScale = 1000f;

    [Header("売り注文（遅延約定）")]
    [Tooltip("売り注文が約定するまでのターン数。1 = 陳列した翌日の営業で全量売れる。")]
    public int sellOrderDelayTurns = 1;
    [Tooltip("約定価格を注文時価格の ±この割合 にクランプする（例: 0.2 = ±20%）。" +
             "配信勝利ボーナス(×5)等で約定額が跳ねる事故を防ぐ安全弁。")]
    public float sellOrderPriceClampRate = 0.2f;
    [Tooltip("売却手数料率（0 = 手数料なし）。将来のバランス調整用。")]
    public float sellOrderFeeRate = 0f;
    [Tooltip("true にすると旧仕様（需要×売率の確率販売・即時入金なし版）に戻す退避フラグ。")]
    public bool useProbabilisticShopSales = false;

    [Header("流行度（Trend）自然需要収束モデル")]
    [Tooltip("Trend=±1.0 のとき naturalDemand が 0.5 からどれだけずれるか。" +
             "例: 0.30 → Trend=+1 で naturalDemand=0.80 / Trend=−1 で 0.20。想定レンジ: 0.1〜0.5")]
    public float trendAmplitude = 0.30f;
    [Tooltip("毎ターン需要が naturalDemand へ近づく速度（Lerp の t）。" +
             "例: 0.15 → 毎ターン差分の 15% だけ均衡値へ引き寄せる。想定レンジ: 0.05〜0.30")]
    public float trendConvergenceRate = 0.15f;
    [Tooltip("毎ターンの Trend ランダムウォーク幅の最大値。" +
             "例: 0.12 → ±0.12 の一様乱数で漂流。想定レンジ: 0.05〜0.20")]
    public float trendDriftMax = 0.12f;
    [Tooltip("毎ターン Trend が 0 へ戻る減衰率（現在 Trend への比率）。" +
             "例: 0.10 → 現在値の 10% 分だけ 0 方向へ引き戻す。想定レンジ: 0.05〜0.20")]
    public float trendDecayRate = 0.10f;
}


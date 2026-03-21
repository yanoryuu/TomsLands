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

    [Header("案S3: 品出し販売結果フィードバック")]
    [Tooltip("前ターンで品出しして売れた時の価格上昇率の下限")]
    public float soldPriceRateMin = 1.01f;
    [Tooltip("前ターンで品出しして売れた時の価格上昇率の上限")]
    public float soldPriceRateMax = 1.02f;
    [Tooltip("前ターンで品出しして売れなかった時の価格下落率の下限")]
    public float unsoldPriceRateMin = 0.98f;
    [Tooltip("前ターンで品出しして売れなかった時の価格下落率の上限")]
    public float unsoldPriceRateMax = 0.99f;

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
}


using UnityEngine;

/// <summary>
/// Battle シーンの価格調整用設定。
/// StreamingSalesController に割り当てるか、Resources/BattlePriceSettings.asset に置くと自動ロードされる。
/// </summary>
[CreateAssetMenu(fileName = "BattlePriceSettings", menuName = "ScriptableObjects/Battle/BattlePriceSettings")]
public class BattlePriceSettings : ScriptableObject
{
    [Header("戦闘パフォーマンスによる価格変動")]
    [Tooltip("Heroが敵を撃破した時の武器価格倍率。例: 1.10 = 10%上昇。")]
    [Min(0f)]
    public float weaponPriceUpOnHit = 1.10f;
    [Tooltip("Heroが攻撃を当てたが敵を倒せなかった時の武器価格倍率。例: 0.95 = 5%下落。")]
    [Min(0f)]
    public float weaponPriceDownOnNonKill = 0.95f;
    [Tooltip("Heroがダメージを受けた時の防具価格倍率。例: 0.90 = 10%下落。")]
    [Min(0f)]
    public float armorPriceDownOnHit = 0.90f;
    [Tooltip("Heroが攻撃を防いだ時の防具価格倍率。例: 1.10 = 10%上昇。")]
    [Min(0f)]
    public float armorPriceUpOnBlock = 1.10f;

    [Header("属性相性による価格変動")]
    [Tooltip("敵撃破時、有利属性の装備に適用する価格倍率。例: 1.30 = 30%上昇。")]
    [Min(0f)]
    public float effectiveAttributeRate = 1.30f;
    [Tooltip("敵撃破時、不利属性の装備に適用する価格倍率。例: 0.85 = 15%下落。")]
    [Min(0f)]
    public float weakAttributeRate = 0.85f;

    [Header("ストップ高/ストップ安（元値基準）")]
    [Tooltip("価格の下限倍率。元値に対する倍率で指定する。例: 0.2 = 元値の20%が下限。")]
    [Min(0f)]
    public float priceFloorRate = 0.20f;
    [Tooltip("価格の上限倍率。元値に対する倍率で指定する。例: 5.0 = 元値の5倍が上限。")]
    [Min(0f)]
    public float priceCeilingRate = 5.00f;

    [Header("配信熱")]
    [Tooltip("Battle開始時の配信熱。0〜100で指定する。")]
    [Range(0f, 100f)]
    public float initialHeat = 30f;
    [Tooltip("ターン終了時に減少する配信熱の量。")]
    [Min(0f)]
    public float heatTurnDecay = 5f;
    [Tooltip("冷め気味ティアの上限値。この値未満なら coldPriceMultiplier を使う。")]
    [Range(0f, 100f)]
    public float coldTierMax = 25f;
    [Tooltip("普通ティアの上限値。この値未満なら normalPriceMultiplier を使う。")]
    [Range(0f, 100f)]
    public float normalTierMax = 50f;
    [Tooltip("盛り上がり中ティアの上限値。この値未満なら hotPriceMultiplier を使う。これ以上は超人気扱い。")]
    [Range(0f, 100f)]
    public float hotTierMax = 75f;

    [Header("配信熱による価格倍率")]
    [Tooltip("配信熱が coldTierMax 未満の時、ターン終了時に適用する価格倍率。例: 0.97 = 3%下落。")]
    [Min(0f)]
    public float coldPriceMultiplier = 0.97f;
    [Tooltip("配信熱が normalTierMax 未満の時、ターン終了時に適用する価格倍率。例: 1.00 = 変化なし。")]
    [Min(0f)]
    public float normalPriceMultiplier = 1.00f;
    [Tooltip("配信熱が hotTierMax 未満の時、ターン終了時に適用する価格倍率。例: 1.02 = 2%上昇。")]
    [Min(0f)]
    public float hotPriceMultiplier = 1.02f;
    [Tooltip("配信熱が hotTierMax 以上の時、ターン終了時に適用する価格倍率。例: 1.05 = 5%上昇。")]
    [Min(0f)]
    public float superHotPriceMultiplier = 1.05f;

    [Header("BattleDemand: 属性相性による需要変動")]
    [Tooltip("有利属性の敵を撃破した時の需要UP量。")]
    [Min(0f)]
    public float demandEffectiveAttributeUp = 0.20f;
    [Tooltip("不利属性の敵を撃破した時の需要DOWN量。")]
    [Min(0f)]
    public float demandWeakAttributeDown = 0.10f;

    [Header("BattleDemand: 連続販売トレンド")]
    [Tooltip("2ターン連続販売時の需要UP量。口コミ効果として扱う。")]
    [Min(0f)]
    public float buzzBonus2Turn = 0.05f;
    [Tooltip("3ターン以上連続販売時の需要UP量。バズ効果として扱う。")]
    [Min(0f)]
    public float buzzBonus3PlusTurn = 0.10f;
    [Tooltip("2ターン以上未販売時の需要DOWN量。話題消失として扱う。")]
    [Min(0f)]
    public float unsoldPenalty = 0.08f;

    [Header("価格と需要の逆相関")]
    [Tooltip("高値とみなす価格倍率。元値に対する倍率で指定する。例: 2.0 = 元値の2倍以上で高値扱い。")]
    [Min(0f)]
    public float highPriceThreshold = 2.0f;
    [Tooltip("安値とみなす価格倍率。元値に対する倍率で指定する。例: 0.5 = 元値の半分以下で安値扱い。")]
    [Min(0f)]
    public float lowPriceThreshold = 0.5f;
    [Tooltip("高値時に毎ターン需要へ掛ける倍率。例: 0.92 = 8%減少。")]
    [Min(0f)]
    public float highPriceDemandDecay = 0.92f;
    [Tooltip("安値時に毎ターン需要へ掛ける倍率。例: 1.08 = 8%増加。")]
    [Min(0f)]
    public float lowPriceDemandGrowth = 1.08f;

    private void OnValidate()
    {
        if (priceCeilingRate < priceFloorRate)
        {
            priceCeilingRate = priceFloorRate;
        }

        normalTierMax = Mathf.Max(normalTierMax, coldTierMax);
        hotTierMax = Mathf.Max(hotTierMax, normalTierMax);
    }
}

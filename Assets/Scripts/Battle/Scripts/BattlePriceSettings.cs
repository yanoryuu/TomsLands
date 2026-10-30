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
    [Tooltip("Heroが攻撃を当てたが敵を倒せず、その一撃が「効かなかった」時の武器価格倍率。例: 0.95 = 5%下落。")]
    [Min(0f)]
    public float weaponPriceDownOnNonKill = 0.95f;
    [Tooltip("Heroが攻撃を当てたが敵を倒せず、その一撃が「効いた」時の武器価格倍率。例: 1.05 = 5%上昇。")]
    [Min(0f)]
    public float weaponPriceUpOnEffectiveHit = 1.05f;
    [Tooltip("倒しきれなかった一撃が「効いた」確率 = 与えたダメージ ÷ 敵の最大HP × この値（下限・上限で収める）。例: 2 = 最大HPの25%を削れば50%。")]
    [Min(0f)]
    public float weaponEffectiveHitChanceScale = 2f;
    [Tooltip("「効いた」確率の下限。例: 0.05 = どんなに硬い敵でも5%は効く。")]
    [Range(0f, 1f)]
    public float weaponEffectiveHitChanceMin = 0.05f;
    [Tooltip("「効いた」確率の上限。例: 0.9 = 大きく削っても10%は効かなかった扱い。")]
    [Range(0f, 1f)]
    public float weaponEffectiveHitChanceMax = 0.9f;
    [Tooltip("Heroがダメージを受けた時の防具価格倍率。例: 0.90 = 10%下落。")]
    [Min(0f)]
    public float armorPriceDownOnHit = 0.90f;
    [Tooltip("Heroが攻撃を防いだ時の防具価格倍率。例: 1.10 = 10%上昇。")]
    [Min(0f)]
    public float armorPriceUpOnBlock = 1.10f;
    [Tooltip("防御成功の確率の下限。確率は 勇者の防御力 ÷ 敵の攻撃力 をこの下限と上限の間に収めたもの。例: 0.05 = どんな強敵でも5%は防げる。")]
    [Range(0f, 1f)]
    public float armorBlockChanceMin = 0.05f;
    [Tooltip("防御成功の確率の上限。例: 0.9 = どんな弱い敵でも10%は被弾する。")]
    [Range(0f, 1f)]
    public float armorBlockChanceMax = 0.9f;

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

    [Header("配信中の在庫補充")]
    [Tooltip("補充の単価 = その時の配信の値段 × この値。元値で補充してその場で高く売る抜け道を塞ぐ。")]
    [Min(0f)]
    public float restockPriceRate = 0.75f;

    [Header("勇者敗北の返金騒ぎ")]
    [Tooltip("勇者が負けた配信で、その配信で売れた武器・防具（勇者側の品）の売上のうち視聴者に返金する割合。例: 0.4 = 40%。道具・魔王側の配当には掛からない。")]
    [Range(0f, 1f)]
    public float heroDefeatRefundRate = 0.4f;

    [Header("配信熱")]
    [Tooltip("Battle開始時の配信熱。0〜100で指定する。")]
    [Range(0f, 100f)]
    public float initialHeat = 30f;
    [Tooltip("見せ場も撃破もないターンが coolDownStartTurns 続いた後、ターン終了ごとに減る配信熱の量（heatRestingPoint より下にも下がる）。")]
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

    [Header("配信熱: 見せ場（勇者の作戦の話題性倍率を掛ける）")]
    [Tooltip("勇者の HP がこの割合を下回った瞬間を「ピンチ」とする。例: 0.5 = 最大HPの50%。")]
    [Range(0f, 1f)]
    public float pinchHpRatio = 0.5f;
    [Tooltip("勇者の HP がこの割合を下回った瞬間を「大ピンチ」とする。例: 0.25 = 最大HPの25%。")]
    [Range(0f, 1f)]
    public float criticalPinchHpRatio = 0.25f;
    [Tooltip("ピンチ（pinchHpRatio を下回った瞬間）の配信熱。")]
    public float pinchHeat = 10f;
    [Tooltip("大ピンチ（criticalPinchHpRatio を下回った瞬間）の配信熱。")]
    public float criticalPinchHeat = 15f;
    [Tooltip("ピンチ中（HP が pinchHpRatio 未満）に敵を倒した「逆転」の配信熱。")]
    public float comebackKillHeat = 25f;
    [Tooltip("ピンチでないとき（HP が pinchHpRatio 以上）に敵を倒した「楽勝」の配信熱。見せ場には数えない。")]
    public float easyKillHeat = 3f;
    [Tooltip("ボス出現の配信熱。")]
    public float bossAppearHeat = 40f;
    [Tooltip("勇者が倒れたときの配信熱。")]
    public float heroDownHeat = 20f;

    [Header("配信熱: 見せ場のないターンの冷め方")]
    [Tooltip("見せ場のないターンが続き始めたとき、配信熱が戻っていく先。")]
    [Range(0f, 100f)]
    public float heatRestingPoint = 30f;
    [Tooltip("見せ場のないターン（冷め始める前）に、heatRestingPoint との差のうち戻す割合。例: 0.1 = 差の10%。")]
    [Range(0f, 1f)]
    public float heatReturnRate = 0.1f;
    [Tooltip("見せ場も撃破もないターンがこの数だけ続いたターンから、毎ターン heatTurnDecay ずつ冷める（楽勝の撃破があったターンで数え直す）。")]
    [Min(1)]
    public int coolDownStartTurns = 3;

    // 段の倍率は値段に 1 回分だけ乗る。段が変わったターンの終わりに「新しい倍率 ÷ 今乗っている倍率」を全品に掛け直すだけで、
    // 同じ段が何ターン続いても積み上がらない（複利にならない。StreamingSalesController.ExecuteTurnSales）
    [Header("配信熱による価格倍率（段ごと。値段に 1 回だけ乗り、毎ターン積み上がらない）")]
    [Tooltip("配信熱が coldTierMax 未満の段の価格倍率。段に入ったとき 1 回だけ乗る（毎ターンは掛けない）。例: 0.97 = 3%下落。")]
    [Min(0f)]
    public float coldPriceMultiplier = 0.97f;
    [Tooltip("配信熱が normalTierMax 未満の段の価格倍率。段に入ったとき 1 回だけ乗る（毎ターンは掛けない）。例: 1.00 = 変化なし。")]
    [Min(0f)]
    public float normalPriceMultiplier = 1.00f;
    [Tooltip("配信熱が hotTierMax 未満の段の価格倍率。段に入ったとき 1 回だけ乗る（毎ターンは掛けない）。例: 1.02 = 2%上昇。")]
    [Min(0f)]
    public float hotPriceMultiplier = 1.02f;
    [Tooltip("配信熱が hotTierMax 以上の段の価格倍率。段に入ったとき 1 回だけ乗る（毎ターンは掛けない）。例: 1.05 = 5%上昇。")]
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
        criticalPinchHpRatio = Mathf.Min(criticalPinchHpRatio, pinchHpRatio);
        armorBlockChanceMax = Mathf.Max(armorBlockChanceMax, armorBlockChanceMin);
        weaponEffectiveHitChanceMax = Mathf.Max(weaponEffectiveHitChanceMax, weaponEffectiveHitChanceMin);
    }
}

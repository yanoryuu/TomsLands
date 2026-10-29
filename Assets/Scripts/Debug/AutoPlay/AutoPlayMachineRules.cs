#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// 店の設備（マシン）の強化・修理の素直な決め方（AutoPlay の戦略ボットと人格プレイの「元が取れそうか」で共用）。
/// - 強化: 強化費 ÷ 増える 1 日の価値 ≤ 残りの朝の数 ÷ 2 なら強化する
/// - 修理: 壊れたお金・製造の設備だけ。修理費 ÷ 1 日の価値 ≤ 残りの朝の数 ÷ 2 なら修理する
/// 1 日の価値: お金 = 毎朝の G、選択式の製造機 = 毎朝の製造の進み G（作った品の基準価格ぶん）、
/// 客寄せ = 売上への加算 × 1 日の店の売上の目安。冷蔵ケースは値で測れないので 0（強化しない）。
/// </summary>
public static class AutoPlayMachineRules
{
    /// <summary>客寄せの価値を測る 1 日の店の売上の目安（試走の営業日の中央値 25,000〜32,000G）。</summary>
    public const int DefaultDailyShopSales = 30000;

    /// <summary>その効果の値の 1 日あたりの価値（G）。</summary>
    public static float DailyValue(ShopMachineEffectType type, float effectValue, int dailyShopSales = DefaultDailyShopSales) => type switch
    {
        ShopMachineEffectType.DailyMoney => effectValue,
        ShopMachineEffectType.DailyItem => effectValue,
        ShopMachineEffectType.RevenueMultiplier => effectValue * Mathf.Max(0, dailyShopSales),
        _ => 0f,
    };

    /// <summary>元が取れる日数（価値が無ければ -1）。</summary>
    public static float PaybackDays(int cost, float dailyValue) => cost > 0 && dailyValue > 0f ? cost / dailyValue : -1f;

    /// <summary>強化するか（強化費が増える価値の「残りの朝 ÷ 2」日ぶん以内）。</summary>
    public static bool ShouldUpgrade(int upgradeCost, float addedDailyValue, int remainingMornings)
    {
        float days = PaybackDays(upgradeCost, addedDailyValue);
        return days > 0f && days <= remainingMornings / 2f;
    }

    /// <summary>修理するか（壊れたお金・製造の設備だけ。修理費が 1 日の価値の「残りの朝 ÷ 2」日ぶん以内）。</summary>
    public static bool ShouldRepair(ShopMachineEffectType type, int repairCost, float dailyValue, int remainingMornings)
    {
        if (type != ShopMachineEffectType.DailyMoney && type != ShopMachineEffectType.DailyItem) return false;
        if (repairCost <= 0) return dailyValue > 0f;
        float days = PaybackDays(repairCost, dailyValue);
        return days > 0f && days <= remainingMornings / 2f;
    }
}
#endif

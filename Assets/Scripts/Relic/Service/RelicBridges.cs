using UnityEngine;

/// <summary>
/// レリック効果を FightScene（配信）へ持ち込むための静的ブリッジ。
/// FightScene は独自の LifetimeScope で Model を再生成するため、
/// GameFlowManager が配信遷移の直前に SetFrom() で値を確定させ、戦闘側はこれを読むだけにする
/// （＝配信中にレリックが変わることはないので、スナップショットで十分）。
/// FightScene を直接再生させた場合は初期値(1.0)で従来挙動になる。
/// </summary>
public static class RelicBattleEffects
{
    /// <summary>勇者のHP/攻撃/防御への倍率（1未満で負けやすい）。</summary>
    public static float HeroPowerMul = 1f;

    /// <summary>勇者敗北時（防衛成功）の報酬への倍率。</summary>
    public static float DefeatRewardMul = 1f;

    /// <summary>配信遷移の直前に呼び、現在の所持レリックから値を確定する。</summary>
    public static void SetFrom(RelicEffectResolver resolver)
    {
        HeroPowerMul = resolver != null ? Mathf.Max(0.1f, resolver.Modify(RelicStatId.HeroPowerMul, 1f)) : 1f;
        DefeatRewardMul = resolver != null ? Mathf.Max(0f, resolver.Modify(RelicStatId.DefeatRewardMul, 1f)) : 1f;
        if (!Mathf.Approximately(HeroPowerMul, 1f) || !Mathf.Approximately(DefeatRewardMul, 1f))
            Debug.Log($"[RelicBattleEffects] HeroPowerMul={HeroPowerMul:F2}, DefeatRewardMul={DefeatRewardMul:F2}");
    }
}

/// <summary>
/// 仕入れ価格へのレリック補正を一元化するヘルパー。
/// 注文ウィジェットの表示単価・購入上限・実際の決済がすべてここを通ることで、
/// 「表示と請求のズレ」を構造的に防ぐ（一覧の市場価格表示は補正しない＝相場情報のまま）。
/// </summary>
public static class RelicPricing
{
    public static int GetBuyUnitPrice(int marketUnitPrice, RelicEffectResolver resolver)
    {
        if (resolver == null) return Mathf.Max(1, marketUnitPrice);
        return Mathf.Max(1, resolver.ModifyInt(RelicStatId.ProcurementCostMul, marketUnitPrice));
    }
}

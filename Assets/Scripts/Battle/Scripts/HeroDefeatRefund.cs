using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 勇者敗北の「返金騒ぎ」。勇者が負けた配信では、その配信で売れた勇者側の品（武器・防具）の売上の一部を視聴者に返金する。
/// 勇者側の品を多く仕込んで売った（勇者に張った）ほど痛い。道具と魔王側の配当（防衛報酬）には掛からない。
/// 返金は配信の売上から引くので、割合が 1 以下なら配信の売上を超えることはない。
/// </summary>
public static class HeroDefeatRefund
{
    /// <summary>勇者側の品か（配信中に勇者の戦いぶりで値段が動く種類 = 武器・防具）。</summary>
    public static bool IsHeroSide(ItemTypeData.ItemType type)
        => type == ItemTypeData.ItemType.Weapon || type == ItemTypeData.ItemType.Armor;

    /// <summary>
    /// 配信で売れた勇者側の品の売上合計。typeOf は商品IDから種類を引く（分からなければ null = 数えない）。
    /// 売上は Revenue（売れたときの価格の合計）、未設定なら品出し時価格×個数（BattleResultHandler.RawStreamSales と同じ）。
    /// </summary>
    public static int HeroSideSales(IEnumerable<BattleOutputSoldItem> soldItems, Func<string, ItemTypeData.ItemType?> typeOf)
    {
        if (soldItems == null || typeOf == null) return 0;
        long total = 0;
        foreach (var s in soldItems)
        {
            if (s == null || string.IsNullOrEmpty(s.ItemId)) continue;
            var type = typeOf(s.ItemId);
            if (!type.HasValue || !IsHeroSide(type.Value)) continue;
            int revenue = s.Revenue > 0 ? s.Revenue : s.SoldQuantity * s.SoldPrice;
            if (revenue > 0) total += revenue;
        }
        return (int)Math.Min(total, int.MaxValue);
    }

    /// <summary>
    /// 返金額 = 勇者側の品の売上 × rate（0〜1 に収める）。勇者が負けた配信（Defeat）だけ。勝ち・途中終了は 0。
    /// </summary>
    public static int Calculate(BattleResult result, IEnumerable<BattleOutputSoldItem> soldItems,
        Func<string, ItemTypeData.ItemType?> typeOf, float rate)
    {
        if (result != BattleResult.Defeat) return 0;
        float r = Mathf.Clamp01(rate);
        if (r <= 0f) return 0;
        int sales = HeroSideSales(soldItems, typeOf);
        return (int)Math.Min((long)Math.Round(sales * (double)r, MidpointRounding.AwayFromZero), sales);
    }

    /// <summary>ItemModel から種類を引く関数（BattleSceneStarter・AutoPlay の代理配信で共通）。</summary>
    public static Func<string, ItemTypeData.ItemType?> TypeLookup(ItemModel itemModel)
        => id =>
        {
            if (itemModel == null) return null;
            var runtime = itemModel.GetRuntimeItem(id);
            if (runtime != null) return runtime.ItemType;
            var master = itemModel.GetMasterItem(id);
            return master != null ? master.itemType : (ItemTypeData.ItemType?)null;
        };
}

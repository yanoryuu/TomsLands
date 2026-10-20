using UnityEngine;

/// <summary>
/// 返済額の計算を一元化するヘルパー。
/// 表示（TomsShopPresenter）と実際の支払い（DebtPresenter）が必ず同じ額になるようにする。
/// 適用順: 基本額 → 猶予証割引(初回のみ) → 借入元本+利息の上乗せ(初回のみ) → レリック補正 → 鍛冶屋の立て替えの上乗せ（次の納付）。
/// 納付が済んだら <see cref="OnPaid"/> で立て替えを消す。
/// </summary>
public static class DebtCalculator
{
    public static int GetAmount(int cycle, TomsModel tomsModel, RelicEffectResolver relicResolver = null)
    {
        int amount = GameConst.GetDebtAmount(cycle);

        if (cycle == 1 && tomsModel != null)
        {
            // スタートダッシュ「返済猶予証」: 初回返済額の割引
            if (tomsModel.FirstDebtDiscountRate > 0f)
                amount = Mathf.RoundToInt(amount * (1f - tomsModel.FirstDebtDiscountRate));

            // 借入レバレッジ: 借入元本+利息が初回返済に上乗せされる
            if (tomsModel.BorrowedPrincipal > 0)
            {
                float interest = GameConst.Preparation.borrowInterestRate;
                amount += Mathf.RoundToInt(tomsModel.BorrowedPrincipal * (1f + interest));
            }
        }

        // レリック補正（DebtAmountMul）
        if (relicResolver != null)
            amount = relicResolver.ModifyInt(RelicStatId.DebtAmountMul, amount);

        // 鍛冶屋の立て替え（1周目）: 次の納付にそのまま上乗せする（利息なし・レリック補正の対象外）
        if (tomsModel != null && tomsModel.AdvancePrincipal > 0)
            amount = TomsModel.SaturatingAdd(amount, tomsModel.AdvancePrincipal);

        return Mathf.Max(0, amount);
    }

    /// <summary>
    /// 在庫を投げ売りしたら調達できる額（相場 × GameConst.DebtFireSaleRate）。強制返済の判定用（H-8）。
    /// </summary>
    public static int GetFireSaleValue(ItemModel itemModel)
    {
        float rate = GameConst.DebtFireSaleRate;
        if (itemModel == null || rate <= 0f) return 0;
        long total = 0;
        foreach (var r in itemModel.RuntimeItems)
            if (r.Stock.Value > 0) total += FireSaleUnitPrice(r, rate) * (long)r.Stock.Value;
        return total >= int.MaxValue ? int.MaxValue : (int)total;
    }

    /// <summary>
    /// 強制返済で現金と金融資産を足しても足りないとき、在庫を相場の DebtFireSaleRate で投げ売りして不足を埋める（H-8）。
    /// 1個あたりの売値が高い品から売り、不足額に届いたら止める。売った品は陳列からも外れる。
    /// 戻り値は入金額。所持金・在庫の保存も行う。届かなくても売れる分は売らない（呼ぶ前に GetFireSaleValue で判定すること）。
    /// </summary>
    public static int LiquidateInventoryForDebt(int shortfall, TomsModel tomsModel, ItemModel itemModel, System.Collections.Generic.List<string> soldLines = null)
    {
        float rate = GameConst.DebtFireSaleRate;
        if (shortfall <= 0 || tomsModel == null || itemModel == null || rate <= 0f) return 0;

        int raised = 0;
        var items = new System.Collections.Generic.List<RuntimeItemData>(itemModel.RuntimeItems);
        items.Sort((a, b) => FireSaleUnitPrice(b, rate).CompareTo(FireSaleUnitPrice(a, rate)));
        foreach (var r in items)
        {
            if (raised >= shortfall) break;
            if (r.Stock.Value <= 0) continue;
            int unit = FireSaleUnitPrice(r, rate);
            if (unit <= 0) continue;
            int need = Mathf.CeilToInt((shortfall - raised) / (float)unit);
            int qty = Mathf.Min(r.Stock.Value, need);
            int income = unit * qty;
            itemModel.ConsumeStock(r.ItemId, qty);
            tomsModel.AddRevenue(income);
            raised += income;
            soldLines?.Add($"{r.ItemName} ×{qty}（{unit:N0}G）");
            Debug.Log($"[Debt] 返済のため在庫を投げ売り: {r.ItemName} ×{qty} @ {unit}G（相場 {r.CurrentPrice.Value}G の {rate:P0}）");
        }
        itemModel.SaveData();
        tomsModel.SavePlayerMoney();
        return raised;
    }

    private static int FireSaleUnitPrice(RuntimeItemData r, float rate) =>
        r == null ? 0 : Mathf.Max(1, Mathf.FloorToInt(r.CurrentPrice.Value * rate));

    /// <summary>納付が済んだときに呼ぶ。上乗せしていた立て替えを返し終えた扱いにする。</summary>
    public static void OnPaid(TomsModel tomsModel)
    {
        if (tomsModel != null) tomsModel.AdvancePrincipal = 0;
    }
}

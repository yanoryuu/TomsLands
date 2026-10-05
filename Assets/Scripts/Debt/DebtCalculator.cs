using UnityEngine;

/// <summary>
/// 返済額の計算を一元化するヘルパー。
/// 表示（TomsShopPresenter）と実際の支払い（DebtPresenter）が必ず同じ額になるようにする。
/// 適用順: 基本額 → 猶予証割引(初回のみ) → 借入元本+利息の上乗せ(初回のみ) → レリック補正。
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

        return Mathf.Max(0, amount);
    }
}

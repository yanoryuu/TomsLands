using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 準備シーンの選択状態（永続化しない。出撃時に RunSetupData へ書き出す）。
/// </summary>
public class PreparationModel
{
    private const int BorrowStep = 1000;

    /// <summary>借入額（0〜借入枠）。</summary>
    public int BorrowAmount { get; private set; }

    /// <summary>持ち込みアイテム（itemId → 個数）。合計個数 ≦ 持ち込みスロット数。</summary>
    public Dictionary<string, int> CarryItems { get; } = new();

    /// <summary>選択中のスターターレリック（空=なし）。</summary>
    public string StarterRelicId { get; private set; } = "";

    public bool UseFlyer { get; private set; }
    public bool UseAppraisal { get; private set; }
    public bool UseGrace { get; private set; }

    public int CarryTotal => CarryItems.Values.Sum();

    public int CarrySlots => GameConst.Preparation.baseCarrySlots;

    /// <summary>現在の借入枠（creditLineLevel に応じた上限額）。</summary>
    public int GetCreditLine(MetaProgressModel meta)
    {
        var amounts = GameConst.Preparation.creditLineAmounts;
        if (amounts == null || amounts.Length == 0) return 0;
        int index = Mathf.Clamp(meta.CreditLineLevel, 0, amounts.Length - 1);
        return amounts[index];
    }

    /// <summary>借入枠拡張の次コスト。最大なら -1。</summary>
    public int GetCreditUpgradeCost(MetaProgressModel meta)
    {
        var costs = GameConst.Preparation.creditLineUpgradeCosts;
        var amounts = GameConst.Preparation.creditLineAmounts;
        if (costs == null || amounts == null) return -1;
        if (meta.CreditLineLevel >= amounts.Length - 1) return -1;
        if (meta.CreditLineLevel >= costs.Length) return -1;
        return costs[meta.CreditLineLevel];
    }

    public void AddBorrow(int creditLine) =>
        BorrowAmount = Mathf.Min(BorrowAmount + BorrowStep, creditLine);

    public void SubtractBorrow() =>
        BorrowAmount = Mathf.Max(0, BorrowAmount - BorrowStep);

    public void ClampBorrow(int creditLine) =>
        BorrowAmount = Mathf.Clamp(BorrowAmount, 0, creditLine);

    public bool TryAddCarry(string itemId)
    {
        if (CarryTotal >= CarrySlots) return false;
        CarryItems.TryGetValue(itemId, out int count);
        CarryItems[itemId] = count + 1;
        return true;
    }

    public void RemoveCarry(string itemId)
    {
        if (!CarryItems.TryGetValue(itemId, out int count)) return;
        if (count <= 1) CarryItems.Remove(itemId);
        else CarryItems[itemId] = count - 1;
    }

    public void SelectStarterRelic(string relicId)
    {
        // 同じものをもう一度選ぶと解除
        StarterRelicId = StarterRelicId == relicId ? "" : relicId;
    }

    public void ToggleFlyer() => UseFlyer = !UseFlyer;
    public void ToggleAppraisal() => UseAppraisal = !UseAppraisal;
    public void ToggleGrace() => UseGrace = !UseGrace;

    /// <summary>選択中のスタートダッシュの合計メタ通貨コスト。</summary>
    public int StartDashTotalCost
    {
        get
        {
            var settings = GameConst.Preparation;
            int total = 0;
            if (UseFlyer) total += settings.flyerCost;
            if (UseAppraisal) total += settings.appraisalCost;
            if (UseGrace) total += settings.graceCost;
            return total;
        }
    }
}

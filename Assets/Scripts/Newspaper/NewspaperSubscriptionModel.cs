using System;
using System.Collections.Generic;

/// <summary>
/// 新聞の購読契約と購読枠（Docs/News_Spec.md §8）。
///
/// ・契約は社ごとに <see cref="NewspaperCompanyData.contractTurns"/>（5ターン）。開始時に一括払い
/// ・枠は店レベルで 1 → 2 → 3（<see cref="NewsTuning.SubscriptionSlotsFor"/>）
/// ・<b>契約中は解約・乗り換えできない</b>。契約が切れたターンに枠が空き、そこで選び直す
///   （＝変更できるのは契約更新ターンのみ）。自動更新はしない
/// ・購読は紙面の「見える範囲」を決めるだけ。発行カレンダーと経済効果は購読と無関係に全社分が動く
/// </summary>
public class NewspaperSubscriptionModel
{
    [Serializable]
    public class Contract
    {
        public string companyId;
        public int startTurn;
        public int endTurn;   // この日から未購読（startTurn + contractTurns）
    }

    private readonly List<Contract> contracts = new();

    /// <summary>そのターンに有効な契約。</summary>
    public List<Contract> ActiveOn(int turn)
    {
        var list = new List<Contract>();
        foreach (var c in contracts)
            if (turn >= c.startTurn && turn < c.endTurn) list.Add(c);
        return list;
    }

    public bool IsSubscribed(string companyId, int turn)
    {
        foreach (var c in contracts)
            if (c.companyId == companyId && turn >= c.startTurn && turn < c.endTurn) return true;
        return false;
    }

    public int UsedSlots(int turn) => ActiveOn(turn).Count;

    /// <summary>
    /// 一番早く切れる契約まで、あと何ターンか（そのターンを含む）。契約が無ければ -1。
    /// </summary>
    public int TurnsUntilRenewal(int turn)
    {
        int best = -1;
        foreach (var c in ActiveOn(turn))
        {
            int left = c.endTurn - turn;
            if (best < 0 || left < best) best = left;
        }
        return best;
    }

    /// <summary>購読できるか（枠・所持金・二重契約を見る）。</summary>
    public bool CanSubscribe(NewspaperCompanyData company, int turn, int slots, int money)
    {
        if (company == null) return false;
        if (IsSubscribed(company.companyId, turn)) return false;
        if (UsedSlots(turn) >= slots) return false;
        return money >= Math.Max(0, company.price);
    }

    /// <summary>契約を結ぶ。支払いは呼び出し側（TomsModel）で行う。</summary>
    public void Subscribe(NewspaperCompanyData company, int turn)
    {
        if (company == null) return;
        contracts.RemoveAll(c => c.companyId == company.companyId && c.endTurn <= turn);
        contracts.Add(new Contract
        {
            companyId = company.companyId,
            startTurn = turn,
            endTurn = turn + Math.Max(1, company.contractTurns),
        });
    }

    public void Clear() => contracts.Clear();

    // --- セーブ/ロード ---

    public List<Contract> ToPlain()
    {
        var list = new List<Contract>();
        foreach (var c in contracts)
            list.Add(new Contract { companyId = c.companyId, startTurn = c.startTurn, endTurn = c.endTurn });
        return list;
    }

    public void FromPlain(IEnumerable<Contract> plain)
    {
        contracts.Clear();
        if (plain == null) return;
        foreach (var c in plain)
            if (c != null && !string.IsNullOrEmpty(c.companyId)) contracts.Add(c);
    }
}

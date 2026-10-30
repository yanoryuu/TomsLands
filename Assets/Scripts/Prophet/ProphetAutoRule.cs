using System;
using System.Collections.Generic;

/// <summary>AI が占い師の枠を考えるときの品 1 つ（解放済みの品だけを渡す）。</summary>
public struct ProphetAutoCandidate
{
    public string ItemId;
    public int Price;
    public int Stock;
    /// <summary>新聞を読んで「上がる」と思った品（記事・予想メモ）。</summary>
    public bool NewsUp;
    /// <summary>安く仕入れたい品（次の配信の弱点の品・新聞で後から上がると読んだ品など）。</summary>
    public bool WantToBuy;

    public long StockValue => (long)Price * Stock;
}

/// <summary>AI の答え（ItemId が null なら入れない。Reason はログ用）。</summary>
public readonly struct ProphetAutoPick
{
    public readonly string ItemId;
    public readonly ProphetDirection Direction;
    public readonly string Reason;

    public ProphetAutoPick(string itemId, ProphetDirection direction, string reason)
    {
        ItemId = itemId;
        Direction = direction;
        Reason = reason;
    }

    public bool IsNone => string.IsNullOrEmpty(ItemId);

    public static ProphetAutoPick None(string reason) => new(null, ProphetDirection.Up, reason);
}

/// <summary>
/// 占い師の枠を AI がいつ・どの品に使うかの決まり（Docs/Prophet_Intervention.md「AI に足してほしい判断」）。
/// ヘッドレスの戦略ボット（Editor/AutoPlay）と、プレイ中の自動プレイ・人格（Debug/AutoPlay）の両方がこれを呼ぶ。
///   ・力が満タン（MaxPower）のときだけ使う（配信で戻る分をあふれさせない）。1 日に 1 つまで
///   ・代金を払っても ProphetTuning.AutoReserveMoney 残るときだけ
///   ・上げる枠（先に見る）: 新聞で上がると読んだ品のうち、在庫の値打ち（値段×在庫）が代金の AutoUpMinStockValueRate 倍以上で一番大きい品
///   ・下げる枠: 安く仕入れたい品のうち、まだあまり持っていない（在庫の値打ちが代金の AutoDownMaxStockValueRate 倍未満）品で一番高い品
/// </summary>
public static class ProphetAutoRule
{
    /// <summary>本物の仕組み（IProphetIntervention.CanPlace）で入れられるかを見る版。プレイ中の自動プレイ用。</summary>
    public static ProphetAutoPick Choose(IProphetIntervention intervention, int money, IReadOnlyList<ProphetAutoCandidate> candidates)
    {
        if (intervention == null) return ProphetAutoPick.None("占い師の仕組みが無い");
        return Choose(intervention.Power, intervention.MaxPower, intervention.Cost, money, candidates,
            (id, d) => intervention.CanPlace(id, d) == ProphetPlaceResult.Ok);
    }

    /// <param name="canPlace">その品をその枠に入れられるか（枠が空いている・もう片方と別の品・解放済み）。</param>
    public static ProphetAutoPick Choose(int power, int maxPower, int cost, int money,
        IReadOnlyList<ProphetAutoCandidate> candidates, Func<string, ProphetDirection, bool> canPlace)
    {
        if (power < maxPower) return ProphetAutoPick.None($"力 {power}/{maxPower}（満タンになるまで取っておく）");
        if (money - cost < ProphetTuning.AutoReserveMoney) return ProphetAutoPick.None($"所持金 {money:N0}G では代金 {cost:N0}G を払う余裕がない");
        if (candidates == null || candidates.Count == 0) return ProphetAutoPick.None("品が無い");

        long upMin = (long)Math.Ceiling(Math.Max(1, cost) * (double)ProphetTuning.AutoUpMinStockValueRate);
        ProphetAutoCandidate? up = null;
        foreach (var c in candidates)
        {
            if (!c.NewsUp || c.StockValue < upMin || !canPlace(c.ItemId, ProphetDirection.Up)) continue;
            if (!up.HasValue || c.StockValue > up.Value.StockValue) up = c;
        }
        if (up.HasValue)
            return new ProphetAutoPick(up.Value.ItemId, ProphetDirection.Up,
                $"新聞で上がると読んだ品で在庫の値打ち {up.Value.StockValue:N0}G（代金の {ProphetTuning.AutoUpMinStockValueRate:0.#} 倍以上）");

        long downMax = (long)Math.Floor(Math.Max(1, cost) * (double)ProphetTuning.AutoDownMaxStockValueRate);
        ProphetAutoCandidate? down = null;
        foreach (var c in candidates)
        {
            if (!c.WantToBuy || c.StockValue >= downMax || !canPlace(c.ItemId, ProphetDirection.Down)) continue;
            if (!down.HasValue || c.Price > down.Value.Price) down = c;
        }
        if (down.HasValue)
            return new ProphetAutoPick(down.Value.ItemId, ProphetDirection.Down,
                $"安く仕入れたい品（{down.Value.Price:N0}G・在庫 {down.Value.Stock}）を下げてから買う");

        return ProphetAutoPick.None("上げる品（新聞▲で在庫が多い）も下げる品（仕入れたい品）も無い");
    }
}

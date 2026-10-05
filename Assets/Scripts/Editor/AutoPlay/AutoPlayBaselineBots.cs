using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 貪欲ボット＝「おすすめ通りに押すだけのプレイヤー」。API キー不要の比較基準。
/// - 仕入れ: ゲーム内のおまかせ仕入れ（AutoPurchase / Recommend）。直近の返済額だけは残す
/// - 陳列: 期待収益（需要×価格×SalesRate）の高い順に枠いっぱい
/// - 配信: 期待収益の高い在庫を全部持ち込む
/// - 魔王軍支援・設備投資: しない / レリック: 先頭を取る
/// これが Jev より稼げるなら「おすすめを押すゲーム」に戻っている合図（Docs/News_Spec.md の診断）。
/// </summary>
public sealed class AutoPlayGreedyBot : IAutoPlayBot
{
    public string Name => "greedy";
    public int Requests => 0;
    public long InputTokens => 0;

    /// <summary>返済がこのターン数以内なら返済額を手元に残す。</summary>
    public int DebtReserveHorizon = 2;

    public Task<AutoPlayDayPlan> PlanDayAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        var plan = new AutoPlayDayPlan();
        int reserve = s.TurnsUntilDebt <= DebtReserveHorizon ? s.NextDebtAmount : 0;
        plan.AutoBuyBudget = Mathf.Max(0, s.Money - reserve);
        plan.AutoBuyStrategy = AutoBuyStrategy.Recommend;
        plan.DisplayPriority = s.Items
            .Where(i => i.Unlocked)
            .OrderByDescending(i => i.Demand * i.Price * i.SalesRate)
            .Select(i => i.Id)
            .ToList();
        plan.DisplayFraction = 1f;
        plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "budget", Chosen = plan.AutoBuyBudget.ToString() });
        return Task.FromResult(plan);
    }

    public Task<AutoPlayStreamPlan> PlanStreamAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        var plan = new AutoPlayStreamPlan();
        foreach (var i in s.Items.Where(i => i.Stock > 0).OrderByDescending(i => i.Demand * i.Price * i.SalesRate))
            plan.Items.Add(new AutoPlayStreamItem(i.Id, i.Stock));
        plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "stream", Question = "stream_kinds", Chosen = plan.Items.Count.ToString() });
        return Task.FromResult(plan);
    }

    public Task<AutoPlayRelicPlan> PlanRelicAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        var plan = new AutoPlayRelicPlan { ChoiceIndex = s.PendingRelicChoices.Count > 0 ? 0 : -1 };
        plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "relic", Question = "relic", Chosen = plan.ChoiceIndex.ToString() });
        return Task.FromResult(plan);
    }
}

/// <summary>ランダムボット。シード固定。バグ探索（変な手順でも壊れないか）と下限の基準。</summary>
public sealed class AutoPlayRandomBot : IAutoPlayBot
{
    private readonly System.Random _rng;

    public AutoPlayRandomBot(int seed) { _rng = new System.Random(seed * 31 + 7); }

    public string Name => "random";
    public int Requests => 0;
    public long InputTokens => 0;

    public Task<AutoPlayDayPlan> PlanDayAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        var plan = new AutoPlayDayPlan();
        float budgetFrac = (float)_rng.NextDouble();
        int budget = Mathf.RoundToInt(s.Money * budgetFrac);
        var unlocked = s.Items.Where(i => i.Unlocked).ToList();
        int picks = unlocked.Count == 0 ? 0 : _rng.Next(1, Mathf.Min(5, unlocked.Count) + 1);
        foreach (var item in unlocked.OrderBy(_ => _rng.Next()).Take(picks))
        {
            int qty = Mathf.Max(1, (budget / picks) / Mathf.Max(1, item.BuyUnitPrice));
            plan.Purchases.Add(new AutoPlayStreamItem(item.Id, qty));
        }
        plan.DisplayPriority = s.Items.OrderBy(_ => _rng.Next()).Select(i => i.Id).ToList();
        plan.DisplayFraction = (float)_rng.NextDouble();

        if (_rng.NextDouble() < 0.1)
        {
            var d = s.Dungeons.Where(x => x.SupportCost > 0).OrderBy(_ => _rng.Next()).FirstOrDefault();
            if (d != null) plan.SupportDungeon = d.Id;
        }
        double u = _rng.NextDouble();
        plan.Upgrade = u < 0.05 ? "blacksmith" : u < 0.10 ? "shop" : "none";

        plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "budget", Score = budgetFrac, Chosen = budget.ToString() });
        plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "support", Chosen = plan.SupportDungeon ?? "none" });
        plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "upgrade", Chosen = plan.Upgrade });
        return Task.FromResult(plan);
    }

    public Task<AutoPlayStreamPlan> PlanStreamAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        var plan = new AutoPlayStreamPlan();
        foreach (var i in s.Items.Where(i => i.Stock > 0).OrderBy(_ => _rng.Next()))
        {
            if (_rng.NextDouble() < 0.5) continue;
            plan.Items.Add(new AutoPlayStreamItem(i.Id, _rng.Next(1, i.Stock + 1)));
        }
        return Task.FromResult(plan);
    }

    public Task<AutoPlayRelicPlan> PlanRelicAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        int n = s.PendingRelicChoices.Count;
        var plan = new AutoPlayRelicPlan { ChoiceIndex = n == 0 ? -1 : _rng.Next(-1, n) };
        plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "relic", Question = "relic", Chosen = plan.ChoiceIndex.ToString() });
        return Task.FromResult(plan);
    }
}

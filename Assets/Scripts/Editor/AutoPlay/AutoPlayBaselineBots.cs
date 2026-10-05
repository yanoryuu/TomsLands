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
        var brought = s.Items.Where(i => i.Stock > 0).OrderByDescending(i => i.Demand * i.Price * i.SalesRate).Take(6).ToList();
        foreach (var i in brought)
            plan.Items.Add(new AutoPlayStreamItem(i.Id, i.Stock));
        plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "stream", Question = "stream_kinds", Chosen = plan.Items.Count.ToString() });

        // 介入: 期待値ベース（オラクル what-if を使う上限側の基準）。
        //   EV = 防衛報酬の増減（勝敗が反転するか）＋ 配信が延びる/縮むぶんの売上 − 価格。正で最大の1件だけ使う
        var info = s.Stream;
        string chosen = "none";
        if (info != null && info.InterventionsEnabled)
        {
            float perTurn = brought.Sum(i => Mathf.Min(i.Stock, Mathf.Clamp01(i.Demand) * i.SalesRate * Mathf.Max(1, i.DisplayStock)) * i.Price);
            float stockValue = brought.Sum(i => (float)i.Stock * i.Price);
            float SalesOver(int turns) => Mathf.Min(stockValue, perTurn * turns);

            AutoPlayInterventionOption best = null;
            float bestEv = 0f;
            foreach (var o in info.Options.Where(o => o.Affordable))
            {
                float reward = 0f;
                if (info.BaselineHeroWins && !o.WhatIfHeroWins) reward += info.DefeatReward;
                if (!info.BaselineHeroWins && o.WhatIfHeroWins) reward -= info.DefeatReward;
                float ev = reward + SalesOver(o.WhatIfTurns) - SalesOver(info.BaselineTurns) - o.Price;
                if (ev > bestEv) { bestEv = ev; best = o; }
            }
            if (best != null)
            {
                plan.Interventions.Add(new AutoPlayInterventionOrder(best.Kind, 0.5f));
                chosen = AutoPlayInterventionKinds.Key(best.Kind);
            }
        }
        plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "stream", Question = "stream_intervene", Chosen = chosen });
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

        // 介入: 30% で1件、さらに 10% で2件目（種類・タイミングとも一様）
        var info = s.Stream;
        if (info != null && info.InterventionsEnabled && info.Options.Count > 0)
        {
            int n = _rng.NextDouble() < 0.3 ? (_rng.NextDouble() < 0.33 ? 2 : 1) : 0;
            for (int k = 0; k < n; k++)
            {
                var o = info.Options[_rng.Next(info.Options.Count)];
                plan.Interventions.Add(new AutoPlayInterventionOrder(o.Kind, (float)_rng.NextDouble()));
            }
        }
        plan.Decisions.Add(new AutoPlayDecision
        {
            Turn = s.RunTurn, Phase = "stream", Question = "stream_intervene",
            Chosen = plan.Interventions.Count == 0 ? "none" : string.Join("+", plan.Interventions.Select(i => AutoPlayInterventionKinds.Key(i.Kind))),
        });
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

/// <summary>
/// ダンジョン側全振りボット（API 不要）。「防衛報酬狙いを本気でやったら勝敗をどこまで揺らせるか」の上限側の基準。
/// - 営業日: 貪欲と同じ。ただし翌日が配信なら、ダンジョン側の介入に使う額を手元に残す
/// - 配信: ダンジョン側の介入を重ねて予定に入れる（ボス強化→開幕 / 呪い→序盤 / 罠×3→序盤・中盤・終盤）
/// </summary>
public sealed class AutoPlayDungeonBot : IAutoPlayBot
{
    private readonly AutoPlayGreedyBot _shop = new AutoPlayGreedyBot();

    public string Name => "dungeon";
    public int Requests => 0;
    public long InputTokens => 0;

    /// <summary>配信前日に残す現金の上限（ダンジョン側4種の合計を目安にする）。</summary>
    public int StreamReserve = 190000;

    public async Task<AutoPlayDayPlan> PlanDayAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        var plan = await _shop.PlanDayAsync(s, ct);
        if (s.TurnsUntilStream >= 0 && s.TurnsUntilStream <= 1)
        {
            int reserve = Mathf.Min(StreamReserve, Mathf.RoundToInt(s.Money * 0.6f));
            plan.AutoBuyBudget = Mathf.Max(0, plan.AutoBuyBudget - reserve);
            plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "stream_reserve", Chosen = reserve.ToString() });
        }
        return plan;
    }

    public async Task<AutoPlayStreamPlan> PlanStreamAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        var plan = await _shop.PlanStreamAsync(s, ct);
        plan.Interventions.Clear();
        plan.Decisions.RemoveAll(d => d.Question == "stream_intervene");
        var info = s.Stream;
        if (info != null && info.InterventionsEnabled)
        {
            void Add(AutoPlayInterventionKind k, float timing)
            {
                if (info.Options.Any(o => o.Kind == k)) plan.Interventions.Add(new AutoPlayInterventionOrder(k, timing));
            }
            // 払えるかどうかは配信中の利用可能残高（売上込み）で判定されるので、ここでは全部予定に入れる
            // クールダウンの許す限り重ねる（罠は安いので3回）。増援は配信が延びて勇者に有利なこともあるので使わない
            Add(AutoPlayInterventionKind.BossBuff, 0f);
            Add(AutoPlayInterventionKind.Curse, 0.1f);
            Add(AutoPlayInterventionKind.Trap, 0.3f);
            Add(AutoPlayInterventionKind.Trap, 0.55f);
            Add(AutoPlayInterventionKind.Trap, 0.8f);
        }
        plan.Decisions.Add(new AutoPlayDecision
        {
            Turn = s.RunTurn, Phase = "stream", Question = "stream_intervene",
            Chosen = plan.Interventions.Count == 0 ? "none" : string.Join("+", plan.Interventions.Select(i => AutoPlayInterventionKinds.Key(i.Kind))),
        });
        return plan;
    }

    public Task<AutoPlayRelicPlan> PlanRelicAsync(AutoPlaySnapshot s, CancellationToken ct) => _shop.PlanRelicAsync(s, ct);
}

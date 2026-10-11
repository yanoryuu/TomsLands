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
        // 勇者には解放済みで最高ランクの武具を持たせる（装備は在庫を消費しない＝本体と同じ）
        (plan.HeroWeapon, plan.HeroArmor) = BestGear(s);
        PlanNewspaper(s, plan, reserve);
        plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "budget", Chosen = plan.AutoBuyBudget.ToString() });
        return Task.FromResult(plan);
    }

    /// <summary>
    /// 新聞の単純な基準: 枠が空いていれば「信頼 × 面の数 ÷ 料金」が最大の社を、返済額＋6万Gを残せる範囲で購読（貪欲は紙面を読まないので余裕のあるときだけ）。
    /// スクラップは空き枠に今日の一面（無ければ最初）の通常記事を貼り、決着・未確認が3件そろって所持金に余裕があればまとめて確認。
    /// </summary>
    public static void PlanNewspaper(AutoPlaySnapshot s, AutoPlayDayPlan plan, int reserve)
    {
        string chosen = "none";
        if (s.SubscriptionUsed < s.SubscriptionSlots)
        {
            var best = s.Newspapers
                .Where(c => c.CanSubscribe && s.Money - c.Price >= reserve + 60000)
                .OrderByDescending(c => c.Trust * c.Pages / (double)Mathf.Max(1, c.Price))
                .FirstOrDefault();
            if (best != null)
            {
                plan.SubscribeNewspapers.Add(best.Id);
                chosen = best.Id;
            }
            plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "subscribe", Chosen = chosen });
        }

        int free = s.ScrapCapacity - s.Scraps.Count;
        if (free > 0)
        {
            var pin = s.News.Where(n => n.CanPin && n.Kind == "Report").OrderBy(n => n.Page == "front" ? 0 : 1).FirstOrDefault();
            if (pin != null) plan.PinScraps.Add(pin.EntryKey);
        }
        int unconfirmed = s.Scraps.Count(x => x.State == "Unconfirmed");
        plan.ConfirmScraps = unconfirmed >= 3 && s.Money - NewsTuning.ConfirmBatchCost >= reserve + 20000 ? "batch" : "none";
        if (unconfirmed > 0)
            plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "scrap_confirm", Chosen = plan.ConfirmScraps });
    }

    /// <summary>解放済みの武器・防具のうち最高ランク（同ランクは基準価格の高い方）。今と同じなら null（変えない）。</summary>
    public static (string weapon, string armor) BestGear(AutoPlaySnapshot s)
    {
        string Best(string type) => s.Items.Where(i => i.Unlocked && i.Type == type)
            .OrderByDescending(i => i.Tier).ThenByDescending(i => i.BasePrice).Select(i => i.Id).FirstOrDefault();
        string w = Best("Weapon"), a = Best("Armor");
        return (w == s.HeroWeaponId ? null : w, a == s.HeroArmorId ? null : a);
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

        // 新聞: 枠が空いていれば 30% で払える社を1つ。スクラップは 50% で1本、確認は未確認があれば 30% で1件
        if (s.SubscriptionUsed < s.SubscriptionSlots && _rng.NextDouble() < 0.3)
        {
            var can = s.Newspapers.Where(c => c.CanSubscribe).ToList();
            if (can.Count > 0) plan.SubscribeNewspapers.Add(can[_rng.Next(can.Count)].Id);
            plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "subscribe", Chosen = plan.SubscribeNewspapers.FirstOrDefault() ?? "none" });
        }
        var pinnable = s.News.Where(n => n.CanPin).ToList();
        if (pinnable.Count > 0 && s.Scraps.Count < s.ScrapCapacity && _rng.NextDouble() < 0.5)
            plan.PinScraps.Add(pinnable[_rng.Next(pinnable.Count)].EntryKey);
        if (s.Scraps.Any(x => x.State == "Unconfirmed") && _rng.NextDouble() < 0.3) plan.ConfirmScraps = "one";

        if (_rng.NextDouble() < 0.2)
        {
            var ws = s.Items.Where(i => i.Unlocked && i.Type == "Weapon").ToList();
            var ars = s.Items.Where(i => i.Unlocked && i.Type == "Armor").ToList();
            plan.HeroWeapon = ws.Count == 0 || _rng.NextDouble() < 0.3 ? "" : ws[_rng.Next(ws.Count)].Id;
            plan.HeroArmor = ars.Count == 0 || _rng.NextDouble() < 0.3 ? "" : ars[_rng.Next(ars.Count)].Id;
        }

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
/// 防衛報酬狙いボット（API 不要）。「魔王軍支援でダンジョンを育てる × 配信でダンジョン側の介入を重ねる」の検証用。
/// variant の書式: "s{N}" = 次の配信ダンジョンを Lv N まで魔王軍支援 / "i" = 配信でダンジョン側の介入を重ねる / "t" = 安い介入だけ（呪い＋罠）。
///   例 "i"（介入のみ）・"s5"（支援のみ Lv5）・"s4i"（Lv4 まで支援＋介入）。
/// 店の営業は貪欲と同じ。配信前日は支援費・介入費のぶん仕入れを控える。
/// </summary>
public sealed class AutoPlayDungeonBot : IAutoPlayBot
{
    private readonly AutoPlayGreedyBot _shop = new AutoPlayGreedyBot();

    public readonly string Variant;
    /// <summary>魔王軍支援で上げる目標レベル（0 = 支援しない）。</summary>
    public readonly int SupportTarget;
    /// <summary>配信でダンジョン側の介入を使うか。</summary>
    public readonly bool UseInterventions;
    /// <summary>"t": 安い介入だけ（呪い＋罠×3。ボス強化は使わない）。</summary>
    public readonly bool CheapOnly;
    /// <summary>"e": 期待値で使う（勇者が勝ちそうなときだけ、1回で負けに変えられる最安の介入を防衛報酬より安ければ使う）。</summary>
    public readonly bool Smart;

    public AutoPlayDungeonBot(string variant = "i")
    {
        Variant = string.IsNullOrEmpty(variant) ? "i" : variant.ToLowerInvariant();
        UseInterventions = Variant.Contains("i") || Variant.Contains("t") || Variant.Contains("e");
        CheapOnly = Variant.Contains("t");
        Smart = Variant.Contains("e");
        var digits = new string(Variant.SkipWhile(c => c != 's').Skip(1).TakeWhile(char.IsDigit).ToArray());
        SupportTarget = int.TryParse(digits, out var n) ? n : 0;
    }

    public string Name => "dungeon_" + Variant;
    public int Requests => 0;
    public long InputTokens => 0;

    /// <summary>配信前日に介入用として残す現金の上限（ダンジョン側4種の合計が目安）。</summary>
    public int StreamReserve = 190000;
    /// <summary>配信の何日前から現金を貯める（支援もこの期間に払えた段から行う）。</summary>
    public int ReserveDays = 3;

    public async Task<AutoPlayDayPlan> PlanDayAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        var plan = await _shop.PlanDayAsync(s, ct);
        // 防衛報酬狙いなので勇者には装備を持たせない
        plan.HeroWeapon = string.IsNullOrEmpty(s.HeroWeaponId) ? null : "";
        plan.HeroArmor = string.IsNullOrEmpty(s.HeroArmorId) ? null : "";
        // 配信の ReserveDays 日前から仕入れを控えて現金を貯める（貪欲は毎日ほぼ全額仕入れるため、
        // 前日だけ控えても支援・介入に回す現金が残らない）
        if (s.TurnsUntilStream < 0 || s.TurnsUntilStream > ReserveDays) return plan;

        // 魔王軍支援: 次の配信ダンジョンを目標レベルまで（費用は1段ごとに本体側で判定される。払えた段まで上がる）
        int supportBudget = 0;
        var next = s.Dungeons.FirstOrDefault(d => d.IsNextStream);
        if (SupportTarget > 0 && next != null && next.Level < SupportTarget)
        {
            plan.SupportDungeon = next.Id;
            plan.SupportTimes = SupportTarget - next.Level;
            supportBudget = EstimateSupportCost(next, SupportTarget);
            plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "support",
                Chosen = $"{next.Id} Lv{next.Level}→{SupportTarget} (~{supportBudget})" });
        }

        int reserve = UseInterventions ? Mathf.Min(CheapOnly ? 60000 : Smart ? 100000 : StreamReserve, Mathf.RoundToInt(s.Money * 0.6f)) : 0;
        plan.AutoBuyBudget = Mathf.Max(0, plan.AutoBuyBudget - reserve - supportBudget);
        plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "stream_reserve", Chosen = (reserve + supportBudget).ToString() });
        return plan;
    }

    /// <summary>現在の段の費用から上の段を見積もる（本体の表は段ごとに倍。正確な額は実行時に本体が判定）。</summary>
    private static int EstimateSupportCost(AutoPlayDungeonView d, int target)
    {
        int total = 0, cost = Mathf.Max(0, d.SupportCost);
        for (int lv = d.Level; lv < target; lv++) { total += cost; cost *= 2; }
        return total;
    }

    public async Task<AutoPlayStreamPlan> PlanStreamAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        var plan = await _shop.PlanStreamAsync(s, ct);
        plan.Interventions.Clear();
        plan.Decisions.RemoveAll(d => d.Question == "stream_intervene");
        var info = s.Stream;
        if (Smart && info != null && info.InterventionsEnabled)
        {
            // "e": 勇者が放っておいても負けるなら何もしない。勝ちそうなら、1回で負けに変えられる最安のダンジョン側介入
            //      （オラクルの what-if）を、防衛報酬より安い場合だけ使う
            if (info.BaselineHeroWins)
            {
                var pick = info.Options
                    .Where(o => o.Affordable && !AutoPlayInterventionKinds.IsHeroSide(o.Kind) && !o.WhatIfHeroWins && o.Price < info.DefeatReward)
                    .OrderBy(o => o.Price).FirstOrDefault();
                if (pick != null) plan.Interventions.Add(new AutoPlayInterventionOrder(pick.Kind, 0.5f));
            }
        }
        else if (UseInterventions && info != null && info.InterventionsEnabled)
        {
            void Add(AutoPlayInterventionKind k, float timing)
            {
                if (info.Options.Any(o => o.Kind == k)) plan.Interventions.Add(new AutoPlayInterventionOrder(k, timing));
            }
            // クールダウンの許す限り重ねる（罠は安いので3回）。増援は配信が延びて勇者に有利なこともあるので使わない。
            // 払えるかどうかは配信中の利用可能残高（売上込み）で判定されるので、ここでは全部予定に入れる
            if (!CheapOnly) Add(AutoPlayInterventionKind.BossBuff, 0f);
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

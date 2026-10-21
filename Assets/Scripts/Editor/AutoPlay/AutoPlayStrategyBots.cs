using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// =====================================================================
// Jev AutoPlay — 「読まなくても勝てる道」を測る戦略ボット（H-1）
//
// どれも店の営業は貪欲（おすすめで全額仕入れ→全部陳列）と同じで、そこに1つの投資先を足す。
// 戦略ごとの最終純資産を並べ、どれかが貪欲や「読む」ボットより大きく勝つなら支配戦略の合図。
//   follower : SNS フォロワーキャンペーンを最上段（50,000人）まで買い込む
//   golem    : 店を上げてマシン枠を増やし、お金を産むマシン（貯金ゴーレム）で枠を埋める
//   bond     : 余った現金を債券（短期→長期）に入れ続ける
//   upgrade  : 店と鍛冶屋を払える限り上げる
//   max      : upgrade + golem + follower を全部
//   bargain  : 仕入れを割安度（適正値に対する安さ）で選ぶ。「読む」側の簡易な代理
// =====================================================================

/// <summary>戦略ボットの共通部分（貪欲の営業＋投資の判断）。</summary>
public abstract class AutoPlayStrategyBotBase : IAutoPlayBot
{
    protected readonly AutoPlayGreedyBot Shop = new AutoPlayGreedyBot();

    public abstract string Name { get; }
    public int Requests => 0;
    public long InputTokens => 0;

    /// <summary>投資に回してよい額（返済が近いときの返済額は残す）。</summary>
    protected static int Investable(AutoPlaySnapshot s, float share)
    {
        int reserve = s.TurnsUntilDebt <= 2 ? s.NextDebtAmount : 0;
        return Mathf.Max(0, Mathf.RoundToInt((s.Money - reserve) * share));
    }

    public async Task<AutoPlayDayPlan> PlanDayAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        var plan = await Shop.PlanDayAsync(s, ct);
        int spent = Invest(s, plan);
        plan.AutoBuyBudget = Mathf.Max(0, plan.AutoBuyBudget - spent);
        return plan;
    }

    /// <summary>投資の予定を plan に積み、使う見込み額を返す（仕入れ予算から引く）。</summary>
    protected abstract int Invest(AutoPlaySnapshot s, AutoPlayDayPlan plan);

    public Task<AutoPlayStreamPlan> PlanStreamAsync(AutoPlaySnapshot s, CancellationToken ct) => Shop.PlanStreamAsync(s, ct);
    public Task<AutoPlayRelicPlan> PlanRelicAsync(AutoPlaySnapshot s, CancellationToken ct) => Shop.PlanRelicAsync(s, ct);

    // ---------------- 共通の投資手 ----------------

    /// <summary>フォロワーキャンペーンを、上限 maxFollowers まで budget の範囲で積む。</summary>
    protected static int PlanFollowers(AutoPlaySnapshot s, AutoPlayDayPlan plan, int budget, int maxFollowers = 50000)
    {
        var ad = s.Ads.Where(a => a.FollowerGain > 0).OrderBy(a => a.Cost / (float)a.FollowerGain).FirstOrDefault();
        if (ad == null || ad.Cost <= 0) return 0;
        int spent = 0, followers = s.Followers;
        while (followers < maxFollowers && spent + ad.Cost <= budget && plan.Advertisements.Count < 60)
        {
            plan.Advertisements.Add(ad.Name);
            spent += ad.Cost;
            followers += ad.FollowerGain;
        }
        if (plan.Advertisements.Count > 0)
            plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "ads", Chosen = $"{ad.Name}x{plan.Advertisements.Count}" });
        return spent;
    }

    /// <summary>空き枠を「元が取れる日数が一番短い」お金のマシンで埋める。</summary>
    protected static int PlanGolems(AutoPlaySnapshot s, AutoPlayDayPlan plan, int budget)
    {
        int free = s.MachineSlots - s.PlacedMachines.Count;
        int spent = 0;
        var best = s.Machines
            .Where(m => m.DailyMoney > 0 && m.RequiredShopLevel <= s.ShopLevel)
            .OrderBy(m => m.Cost / (float)m.DailyMoney)
            .ThenByDescending(m => m.DailyMoney)
            .FirstOrDefault();
        if (best == null) return 0;
        for (int i = 0; i < free && spent + best.Cost <= budget; i++)
        {
            plan.PlaceMachines.Add(best.Id);
            spent += best.Cost;
        }
        if (plan.PlaceMachines.Count > 0)
            plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "machines", Chosen = $"{best.Id}x{plan.PlaceMachines.Count}" });
        return spent;
    }

    /// <summary>店→鍛冶屋の順に、budget の範囲で上げる（実際の費用は本体が判定）。</summary>
    protected static int PlanUpgrades(AutoPlaySnapshot s, AutoPlayDayPlan plan, int budget)
    {
        int spent = 0;
        bool shop = s.ShopUpgradeCost > 0 && s.ShopUpgradeCost <= budget;
        bool bs = s.BlacksmithUpgradeCost > 0 && s.BlacksmithUpgradeCost <= budget - (shop ? s.ShopUpgradeCost : 0);
        if (shop && bs) { plan.Upgrade = "both"; spent = s.ShopUpgradeCost + s.BlacksmithUpgradeCost; }
        else if (shop) { plan.Upgrade = "shop"; spent = s.ShopUpgradeCost; }
        else if (bs) { plan.Upgrade = "blacksmith"; spent = s.BlacksmithUpgradeCost; }
        plan.UpgradeTimes = 1;
        if (plan.Upgrade != "none")
            plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "upgrade", Chosen = plan.Upgrade });
        return spent;
    }
}

public sealed class AutoPlayFollowerBot : AutoPlayStrategyBotBase
{
    public override string Name => "follower";
    protected override int Invest(AutoPlaySnapshot s, AutoPlayDayPlan plan) => PlanFollowers(s, plan, Investable(s, 0.6f));
}

public sealed class AutoPlayGolemBot : AutoPlayStrategyBotBase
{
    public override string Name => "golem";
    protected override int Invest(AutoPlaySnapshot s, AutoPlayDayPlan plan)
    {
        int budget = Investable(s, 0.7f);
        // 枠が無ければ店を上げる（枠を増やすため）
        int spent = 0;
        if (s.PlacedMachines.Count >= s.MachineSlots && s.ShopUpgradeCost > 0 && s.ShopUpgradeCost <= budget)
        {
            plan.Upgrade = "shop";
            spent += s.ShopUpgradeCost;
            plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "upgrade", Chosen = "shop" });
            return spent; // 枠は翌日から（今日のスナップショットの枠数で計画しているため）
        }
        return spent + PlanGolems(s, plan, budget - spent);
    }
}

public sealed class AutoPlayBondBot : AutoPlayStrategyBotBase
{
    public override string Name => "bond";
    protected override int Invest(AutoPlaySnapshot s, AutoPlayDayPlan plan)
    {
        int budget = Investable(s, 0.5f);
        int spent = 0;
        foreach (var b in s.Bonds.Where(b => b.Unlocked).OrderBy(b => b.MaturityTurns))
        {
            // 配信・返済までに満期が来ない長期は避ける（返済日の現金を残す）
            if (s.TurnsUntilDebt > 0 && b.MaturityTurns > s.TurnsUntilDebt + 7) continue;
            int units = Mathf.Min(b.RemainingUnits, (budget - spent) / Mathf.Max(1, b.UnitPrice));
            if (units <= 0) continue;
            plan.Bonds.Add(new AutoPlayStreamItem(b.Id, units));
            spent += units * b.UnitPrice;
        }
        if (plan.Bonds.Count > 0)
            plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "bonds", Chosen = string.Join("+", plan.Bonds.Select(b => $"{b.ItemId}x{b.Quantity}")) });
        return spent;
    }
}

public sealed class AutoPlayUpgradeBot : AutoPlayStrategyBotBase
{
    private readonly string _name;
    private readonly float _share;
    /// <summary>share = 1日に設備投資へ回してよい割合（"upgrade" 0.8＝払える限り / "upgrade_lite" 0.25＝余裕のぶんだけ）。</summary>
    public AutoPlayUpgradeBot(string name = "upgrade", float share = 0.8f) { _name = name; _share = share; }
    public override string Name => _name;
    protected override int Invest(AutoPlaySnapshot s, AutoPlayDayPlan plan) => PlanUpgrades(s, plan, Investable(s, _share));
}

public sealed class AutoPlayMaxBot : AutoPlayStrategyBotBase
{
    public override string Name => "max";
    protected override int Invest(AutoPlaySnapshot s, AutoPlayDayPlan plan)
    {
        int budget = Investable(s, 0.8f);
        int spent = PlanUpgrades(s, plan, budget);
        spent += PlanGolems(s, plan, budget - spent);
        spent += PlanFollowers(s, plan, budget - spent);
        return spent;
    }
}

/// <summary>
/// 割安度で仕入れる（AutoBuyStrategy.Bargain）。陳列は「需要が高い順」。
/// 新聞を読まないが「今の値段が需要に対して安いか」は見る、という読む側の最小の代理。
/// </summary>
public sealed class AutoPlayBargainBot : AutoPlayStrategyBotBase
{
    public override string Name => "bargain";
    protected override int Invest(AutoPlaySnapshot s, AutoPlayDayPlan plan)
    {
        plan.AutoBuyStrategy = AutoBuyStrategy.Bargain;
        plan.DisplayPriority = s.Items.Where(i => i.Unlocked).OrderByDescending(i => i.Demand).Select(i => i.Id).ToList();
        return 0;
    }
}

/// <summary>
/// 売れる量を見て仕入れる（読まない・賢い）: おすすめ順に、1日の客の数 × DaysOfStock 日分まで買う。
/// 「全額仕入れ→全部陳列」が売れ残りで損をするかの対照。
/// </summary>
public sealed class AutoPlayCapAwareBot : AutoPlayStrategyBotBase
{
    public override string Name => "capaware";
    public float DaysOfStock = 2f;
    protected override int Invest(AutoPlaySnapshot s, AutoPlayDayPlan plan)
    {
        int budget = plan.AutoBuyBudget;
        int spent = BuyByCustomers(s, plan, s.Items.Where(i => i.Unlocked).OrderByDescending(i => i.RecommendScore), budget, DaysOfStock, s.MaxDisplayKinds);
        plan.AutoBuyBudget = 0;
        plan.DisplayPriority = s.Items.Where(i => i.Unlocked).OrderByDescending(i => i.Customers * (float)i.Price).Select(i => i.Id).ToList();
        return 0; // AutoBuyBudget は 0 にしたので引かない
    }

    /// <summary>候補の順に、客の数 × days 日分（今の在庫を引く）を予算の範囲で買う。最大 kinds 銘柄。</summary>
    public static int BuyByCustomers(AutoPlaySnapshot s, AutoPlayDayPlan plan, IEnumerable<AutoPlayItemView> order, int budget, float days, int kinds)
    {
        int spent = 0, n = 0;
        foreach (var i in order)
        {
            if (n >= kinds || budget - spent <= 0) break;
            int target = i.Customers == int.MaxValue ? i.MaxStock : Mathf.Min(i.MaxStock, Mathf.CeilToInt(i.Customers * days));
            int qty = Mathf.Min(target - i.Stock, (budget - spent) / Mathf.Max(1, i.BuyUnitPrice));
            if (qty <= 0) { if (i.Stock > 0) n++; continue; }
            plan.Purchases.Add(new AutoPlayStreamItem(i.Id, qty));
            spent += qty * i.BuyUnitPrice;
            n++;
        }
        return spent;
    }
}

/// <summary>
/// 【オラクル】新聞を正しく読めたプレイヤーの代理。この先3日の本当の需要の向き（OracleNewsBias）で売買する:
///   上がる品 … 客の数 × 3日分まで仕入れて、上がり切るまで並べずに持つ（安いうちに買う）
///   上がり切った品・下がる品 … 並べて売る（下がる品は仕入れない）
/// 残りの予算は「売れる量を見る」ボットと同じくおすすめ順。読みが当たったときの儲けの上限側の目安。
/// </summary>
public sealed class AutoPlayOracleBot : AutoPlayStrategyBotBase
{
    private readonly string _name;
    private readonly bool _hold;
    public float UpThreshold = 0.5f;
    /// <summary>hold = true: 上がる品は上がり切るまで並べずに持つ / false: 上がる品から並べて売る（需要が上がる＝客が増える）。</summary>
    public AutoPlayOracleBot(string name = "oracle", bool hold = false) { _name = name; _hold = hold; }
    public override string Name => _name;
    protected override int Invest(AutoPlaySnapshot s, AutoPlayDayPlan plan)
    {
        int budget = plan.AutoBuyBudget;
        var unlocked = s.Items.Where(i => i.Unlocked).ToList();
        var up = unlocked.Where(i => i.OracleNewsBias > UpThreshold).OrderByDescending(i => i.OracleNewsBias).ToList();
        int spent = AutoPlayCapAwareBot.BuyByCustomers(s, plan, up, budget, 3f, 99);
        // 売る側: 上がる品以外（下がる品を優先して売り切る）。残りの予算で、下がらない品を売る量だけ仕入れる
        var sell = unlocked.Where(i => !up.Contains(i)).ToList();
        AutoPlayCapAwareBot.BuyByCustomers(s, plan, sell.Where(i => i.OracleNewsBias >= 0f).OrderByDescending(i => i.RecommendScore),
            budget - spent, 1.5f, s.MaxDisplayKinds);
        plan.AutoBuyBudget = 0;
        var sellOrder = sell.OrderBy(i => i.OracleNewsBias < 0f ? 0 : 1).ThenByDescending(i => i.Customers * (float)i.Price).Select(i => i.Id);
        plan.DisplayPriority = _hold ? sellOrder.ToList() : up.Select(i => i.Id).Concat(sellOrder).ToList();
        plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "oracle_up", Chosen = string.Join("+", up.Take(3).Select(i => i.Id)) });
        return 0;
    }
}

/// <summary>
/// 属性で装備を選ぶ（Q7 の検証）: 売る側は capaware と同じ。勇者の武器は「次の配信先の弱点属性」の中で一番ランクが高いもの。
/// </summary>
public sealed class AutoPlayAttributeGearBot : AutoPlayStrategyBotBase
{
    private readonly AutoPlayCapAwareBot _cap = new AutoPlayCapAwareBot();
    public override string Name => "gear_attr";
    protected override int Invest(AutoPlaySnapshot s, AutoPlayDayPlan plan)
    {
        var w = s.Items.Where(i => i.Unlocked && i.Type == "Weapon" && i.Attribute == s.NextStreamWeakness)
            .OrderByDescending(i => i.Tier).ThenByDescending(i => i.BasePrice).FirstOrDefault();
        if (w != null) plan.HeroWeapon = w.Id == s.HeroWeaponId ? null : w.Id;
        return 0;
    }
}

public static class AutoPlayStrategyBots
{
    public static readonly string[] All = { "follower", "golem", "bond", "upgrade", "upgrade_lite", "max", "bargain", "capaware", "oracle", "oracle_hold", "gear_attr" };

    public static IAutoPlayBot Create(string name) => name switch
    {
        "follower" => new AutoPlayFollowerBot(),
        "golem" => new AutoPlayGolemBot(),
        "bond" => new AutoPlayBondBot(),
        "upgrade" => new AutoPlayUpgradeBot(),
        "upgrade_lite" => new AutoPlayUpgradeBot("upgrade_lite", 0.25f),
        "max" => new AutoPlayMaxBot(),
        "bargain" => new AutoPlayBargainBot(),
        "capaware" => new AutoPlayCapAwareBot(),
        "oracle" => new AutoPlayOracleBot(),
        "gear_attr" => new AutoPlayAttributeGearBot(),
        "oracle_hold" => new AutoPlayOracleBot("oracle_hold", true),
        _ => null,
    };
}

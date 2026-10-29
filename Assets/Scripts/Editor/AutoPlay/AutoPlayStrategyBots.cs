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

    /// <summary>
    /// 空き枠を「元が取れる日数が短い」順のお金のマシンで埋める（同じマシンは 1 台までなので、まだ置いていないものから）。
    /// 先に壊れたマシンの修理と、元が取れる強化（AutoPlayMachineRules）を積む。
    /// </summary>
    protected static int PlanGolems(AutoPlaySnapshot s, AutoPlayDayPlan plan, int budget)
    {
        int spent = PlanMachineCare(s, plan, budget);
        int free = s.MachineSlots - s.PlacedMachines.Count;
        var picks = s.Machines
            .Where(m => !m.Placed && m.DailyMoney > 0 && m.RequiredShopLevel <= s.ShopLevel)
            .OrderBy(m => m.Cost / (float)m.DailyMoney)
            .ThenByDescending(m => m.DailyMoney)
            .ToList();
        int placed = 0;
        foreach (var m in picks)
        {
            if (placed >= free) break;
            if (spent + m.Cost > budget) continue;
            plan.PlaceMachines.Add(m.Id);
            spent += m.Cost;
            placed++;
        }
        if (placed > 0)
            plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "machines", Chosen = string.Join("+", plan.PlaceMachines) });
        return spent;
    }

    /// <summary>このランで残っている朝の数（日送りの回数。イベントも含む目安）。</summary>
    protected static int RemainingMornings(AutoPlaySnapshot s) => Mathf.Max(0, s.FlowLength - s.FlowIndex - 1);

    /// <summary>
    /// 置いてあるマシンの修理と強化（AutoPlayMachineRules: 元が取れる日数 ≤ 残りの朝 ÷ 2）。修理を先に、強化は元が取れる日数の短い順。
    /// </summary>
    protected static int PlanMachineCare(AutoPlaySnapshot s, AutoPlayDayPlan plan, int budget)
    {
        int spent = 0;
        int left = RemainingMornings(s);
        foreach (var m in s.Machines.Where(m => m.Placed && m.Broken))
        {
            if (!System.Enum.TryParse<ShopMachineEffectType>(m.Type, out var type)) continue;
            float value = AutoPlayMachineRules.DailyValue(type, m.EffectValue);
            if (!AutoPlayMachineRules.ShouldRepair(type, m.RepairCost, value, left) || spent + m.RepairCost > budget) continue;
            plan.RepairMachines.Add(m.Id);
            spent += m.RepairCost;
        }
        var ups = s.Machines
            .Where(m => m.Placed && m.UpgradeCost > 0 && System.Enum.TryParse<ShopMachineEffectType>(m.Type, out _))
            .Select(m =>
            {
                var type = (ShopMachineEffectType)System.Enum.Parse(typeof(ShopMachineEffectType), m.Type);
                float added = AutoPlayMachineRules.DailyValue(type, m.NextEffectValue) - AutoPlayMachineRules.DailyValue(type, m.EffectValue);
                return (m, added);
            })
            .Where(x => AutoPlayMachineRules.ShouldUpgrade(x.m.UpgradeCost, x.added, left))
            .OrderBy(x => x.m.UpgradeCost / x.added)
            .ToList();
        foreach (var (m, _) in ups)
        {
            if (spent + m.UpgradeCost > budget) continue;
            plan.UpgradeMachines.Add(m.Id);
            spent += m.UpgradeCost;
        }
        if (plan.RepairMachines.Count > 0 || plan.UpgradeMachines.Count > 0)
            plan.Decisions.Add(new AutoPlayDecision
            {
                Turn = s.RunTurn, Phase = "day", Question = "machine_care",
                Chosen = $"repair={string.Join("+", plan.RepairMachines)};upgrade={string.Join("+", plan.UpgradeMachines)}",
            });
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
/// 【新聞を普通に読む】記事を信じて仕入れる（誤報・誇張も信じる）。プレイヤーが紙面から読み取れる範囲だけを使う:
///   購読 … 契約が切れていたら「信頼 × 面の数 ÷ 料金」が一番の社を、返済分＋ReserveForNews を残せる範囲で取る
///   読む … 載った報道の効き先（属性×種別・高額帯）と、掲載日＋社の早さから発効日（ReadNewsBias / ReadNewsDaysToEffect）
///   上がると読んだ品 … 効き始めるまで並べずに持ち、効いている間に並べて売る（客の数 × HoldDays 日分まで仕入れる）
///   下がると読んだ品 … 仕入れず、在庫は先に並べて売り切る
/// 残りの予算は capaware と同じ（おすすめ順・客の数 × 1.5 日分）。
/// fortune = true なら、占い師の見立て（FortuneDirection）が「下がる」品は記事を信じず外す（裏取り）。
/// </summary>
/// <summary>営業日の計画の前に占い師に占ってもらうか（AutoPlayRunner が先に占ってからスナップショットを取り直す）。</summary>
public interface IAutoPlayFortuneBot
{
    bool WantsFortune(AutoPlaySnapshot s);
}

public sealed class AutoPlayReaderBot : AutoPlayStrategyBotBase, IAutoPlayFortuneBot
{
    private readonly string _name;
    private readonly bool _fortune;
    private readonly bool _readNews;
    public float UpThreshold = 0.25f;
    public float HoldDays = 3f;
    public int ReserveForNews = 20000;
    /// <summary>占うのは所持金がこれ以上のときだけ（15,000G は元手が小さいうちは重い）。</summary>
    public int FortuneMinMoney = 150000;

    /// <param name="fortune">占い師を使う</param>
    /// <param name="readNews">新聞を読む（false なら記事を使わない＝占いだけで張る）</param>
    public AutoPlayReaderBot(string name = "reader", bool fortune = false, bool readNews = true)
    {
        _name = name; _fortune = fortune; _readNews = readNews;
    }
    public override string Name => _name;

    /// <summary>所持金に余裕がある日に占う（記事を読む側は、まだ効いていない「上がる」記事があるときだけ＝裏取り）。</summary>
    public bool WantsFortune(AutoPlaySnapshot s)
    {
        if (!_fortune) return false;
        int reserve = s.TurnsUntilDebt <= 2 ? s.NextDebtAmount : 0;
        if (s.Money - ProphetFortune.CurrentCost - reserve < FortuneMinMoney) return false;
        return !_readNews || s.Items.Any(i => i.Unlocked && i.ReadNewsBias >= UpThreshold && i.ReadNewsDaysToEffect > 0);
    }

    protected override int Invest(AutoPlaySnapshot s, AutoPlayDayPlan plan)
    {
        int reserve = s.TurnsUntilDebt <= 2 ? s.NextDebtAmount : 0;
        int spentNews = 0;
        // 購読（貪欲の基準は「6万G 余るときだけ」。読む側はもっと積極的に取る）
        if (_readNews && plan.SubscribeNewspapers.Count == 0 && s.SubscriptionUsed < s.SubscriptionSlots)
        {
            var best = s.Newspapers
                .Where(c => c.CanSubscribe && !c.Subscribed && s.Money - c.Price >= reserve + ReserveForNews)
                .OrderByDescending(c => c.Trust * c.Pages / (double)Mathf.Max(1, c.Price))
                .FirstOrDefault();
            if (best != null)
            {
                plan.SubscribeNewspapers.Add(best.Id);
                spentNews += best.Price;
                plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "subscribe_reader", Chosen = best.Id });
            }
        }

        int budget = Mathf.Max(0, plan.AutoBuyBudget - spentNews);
        var unlocked = s.Items.Where(i => i.Unlocked).ToList();
        // 占いに「上がる」と出なかった品は、記事を信じない（誤報の疑い）
        bool Doubted(AutoPlayItemView i) => _fortune && i.FortuneDirection.HasValue && i.FortuneDirection.Value <= 0;
        var up = !_readNews ? new List<AutoPlayItemView>() : unlocked.Where(i => i.ReadNewsBias >= UpThreshold && i.ReadNewsDaysToEffect >= 0 && !Doubted(i))
            .OrderBy(i => i.ReadNewsDaysToEffect).ThenByDescending(i => i.ReadNewsBias).ToList();
        // 占いで「上がる」と出た品も張る（効く日は分からないので、今日は並べずに持つ）
        if (_fortune)
            foreach (var i in unlocked.Where(i => i.FortuneDirection.HasValue && i.FortuneDirection.Value > 0 && !up.Contains(i)))
            {
                if (i.ReadNewsDaysToEffect < 0) i.ReadNewsDaysToEffect = 1;
                up.Add(i);
            }
        var down = unlocked.Where(i => (_readNews && i.ReadNewsBias <= -UpThreshold) || (i.FortuneDirection.HasValue && i.FortuneDirection.Value < 0)).Where(i => !up.Contains(i)).ToList();

        // 効き始める前の品は持つ。効いている品（0日）は売る
        int spent = AutoPlayCapAwareBot.BuyByCustomers(s, plan, up, budget, HoldDays, 99);
        var rest = unlocked.Where(i => !up.Contains(i) && !down.Contains(i)).OrderByDescending(i => i.RecommendScore);
        AutoPlayCapAwareBot.BuyByCustomers(s, plan, rest, budget - spent, 1.5f, s.MaxDisplayKinds);
        plan.AutoBuyBudget = 0;

        var sellNow = up.Where(i => i.ReadNewsDaysToEffect == 0).Select(i => i.Id)
            .Concat(down.Select(i => i.Id))
            .Concat(unlocked.Where(i => !up.Contains(i) && !down.Contains(i)).OrderByDescending(i => i.Customers * (float)i.Price).Select(i => i.Id));
        plan.DisplayPriority = sellNow.ToList();
        if (up.Count > 0)
            plan.Decisions.Add(new AutoPlayDecision { Turn = s.RunTurn, Phase = "day", Question = "reader_up", Chosen = string.Join("+", up.Take(3).Select(i => $"{i.Id}@{i.ReadNewsDaysToEffect}")) });
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
    public static readonly string[] All = { "follower", "golem", "bond", "upgrade", "upgrade_lite", "max", "bargain", "capaware", "oracle", "oracle_hold", "gear_attr", "reader", "reader_fortune", "fortune" };

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
        "reader" => new AutoPlayReaderBot(),
        "reader_fortune" => new AutoPlayReaderBot("reader_fortune", true),
        "fortune" => new AutoPlayReaderBot("fortune", true, readNews: false),
        _ => null,
    };
}

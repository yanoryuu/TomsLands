using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Jev に渡す店主の性格（instructions の頭に付ける英文）。</summary>
public sealed class AutoPlayPersona
{
    public string Id;
    public string LabelJa;
    public string Instruction;

    public static readonly AutoPlayPersona[] All =
    {
        new AutoPlayPersona
        {
            Id = "steady", LabelJa = "堅実",
            Instruction = "You are a cautious, steady shopkeeper. Never risk bankruptcy: always keep enough cash for the next debt payment. " +
                          "Prefer items with solid demand and calm prices, spread purchases over several items, and avoid rumors from unsigned sources. During streams you rarely pay for super chats unless the gain clearly exceeds the price.",
        },
        new AutoPlayPersona
        {
            Id = "speculator", LabelJa = "投機",
            Instruction = "You are an aggressive speculator. You read the newspaper for causes that will move demand, " +
                          "concentrate cash on the one or two items you expect to rise, and accept the risk of losses. During streams you make bold one-shot bets with super chats when they could swing the result.",
        },
        new AutoPlayPersona
        {
            Id = "streamer", LabelJa = "配信重視",
            Instruction = "You focus on the live streams. You stock up on goods to sell to stream viewers on stream days, " +
                          "prefer items with high demand, and treat daily shop sales as secondary. During streams you like hero-side super chats (heal, skill, special move) that excite viewers and keep the stream going longer.",
        },
        new AutoPlayPersona
        {
            Id = "demonlord", LabelJa = "魔王軍支援",
            Instruction = "You profit when the hero loses. You pay the demon army to strengthen the dungeon of the next stream " +
                          "when the defeat reward is worth more than the cost, and otherwise run the shop normally. During streams you prefer dungeon-side interventions (trap, curse, reinforcements, boss buff) that can make the hero lose.",
        },
    };

    public static AutoPlayPersona Find(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}

public enum AutoPlayJevDecisionMode
{
    /// <summary>1択の質問は確率分布から抽選（シード固定）。配分系は分布をそのまま使う。</summary>
    Sample,
    /// <summary>1択の質問は最尤の選択肢。配分系は分布をそのまま使う。</summary>
    Argmax,
}

/// <summary>
/// Jev で判断するボット。1回の判断（営業日／配信日／レリック）＝ 1リクエスト。
/// 同じ state に対する複数の質問を1リクエストに詰めて並列評価させる（コスト・遅延とも1問分とほぼ同じ）。
/// 失敗時は貪欲ボットの判断で代替し、jev_error として記録する（ランは止めない）。
/// </summary>
public sealed class AutoPlayJevBot : IAutoPlayBot
{
    private const string Context =
        "You are Tom, the owner of an item shop in a fantasy town (the game TomsLands). " +
        "Each shop day you may buy goods at today's market price and put goods on display. " +
        "Displayed goods are all sold as sell orders that settle the next day at the next day's market price " +
        "(limited to +/-20% of the order price), so you profit when you buy low and prices rise. " +
        "Prices drift toward a fair value set by demand; demand follows hidden trends, and newspaper articles describe " +
        "causes that may move demand of certain attributes or types a few days later (some articles are false or exaggerated; " +
        "confirmed sources and signed articles are more reliable). " +
        "On stream days the hero fights a dungeon live while you sell the goods you brought to viewers. " +
        "If the hero is defeated you receive the dungeon's defeat reward. Paying the demon army raises a dungeon's level: " +
        "stronger monsters, a lower hero clear chance and a larger defeat reward. " +
        "A debt payment is due every few days; failing to pay means bankruptcy and the run ends. " +
        "Your goal is to finish the run with as much money as possible without going bankrupt.";

    private readonly AutoPlayPersona _persona;
    private readonly AutoPlayJevDecisionMode _mode;
    private readonly System.Random _rng;
    private readonly AutoPlayGreedyBot _fallback = new AutoPlayGreedyBot();

    public int Requests { get; private set; }
    public long InputTokens { get; private set; }
    public int Errors { get; private set; }
    public string LastError { get; private set; }

    public AutoPlayJevBot(AutoPlayPersona persona, AutoPlayJevDecisionMode mode, int seed)
    {
        _persona = persona;
        _mode = mode;
        _rng = new System.Random(seed * 97 + StableHash(persona.Id));
    }

    public string Name => "jev_" + _persona.Id;

    // =================================================================
    // 営業日
    // =================================================================

    public async Task<AutoPlayDayPlan> PlanDayAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        var req = new JevRequest { State = BuildState(s) };
        string who = _persona.Instruction + " ";

        req.Questions["budget"] = JevQuestion.Score(
            who + "How much of your current cash should you spend on buying goods today? " +
            "Remember the next debt payment (amount and days left are in the state).",
            new[] { "Spend nothing today", "Spend about 20% of cash", "Spend about 40% of cash",
                    "Spend about 60% of cash", "Spend about 80% of cash", "Spend almost all cash" });

        var buyOptions = s.Items
            .Where(i => i.Unlocked && i.Stock < i.MaxStock && i.BuyUnitPrice <= Mathf.Max(1, s.Money))
            .ToDictionary(i => i.Id, DescribeItem);
        if (buyOptions.Count > 0)
            req.Questions["buy"] = JevQuestion.Choice(
                who + "Which single item is the best purchase today? Consider price vs. base price, demand trend, " +
                "heat, the newspaper and the next stream.", buyOptions);

        var displayOptions = s.Items
            .Where(i => i.Stock > 0 || buyOptions.ContainsKey(i.Id))
            .ToDictionary(i => i.Id, DescribeItem);
        if (displayOptions.Count > 0)
        {
            req.Questions["display"] = JevQuestion.Choice(
                who + $"You can display up to {s.MaxDisplayKinds} kinds today. Which item deserves shelf space most? " +
                "Displayed stock is sold tonight and paid at tomorrow's price.", displayOptions);
            req.Questions["display_amount"] = JevQuestion.Score(
                who + "How much of your stock should you put on display today instead of holding it for later (or for the stream)?",
                new[] { "Display nothing; hold all stock", "Display about a quarter", "Display about half",
                        "Display about three quarters", "Display as much as allowed" });
        }

        var supportOptions = new Dictionary<string, string> { ["none"] = "Do not pay the demon army today." };
        foreach (var d in s.Dungeons.Where(d => d.SupportCost > 0 && d.SupportCost <= s.Money))
        {
            supportOptions[d.Id] =
                $"Pay {d.SupportCost}G to raise {d.Id} from level {d.Level} to {d.Level + 1}. " +
                $"Current hero clear chance there {d.ClearChancePct:F0}%, current defeat reward {d.DefeatReward}G" +
                (d.IsNextStream ? ". This is the dungeon of the next stream." : ".");
        }
        if (supportOptions.Count > 1)
            req.Questions["support"] = JevQuestion.Choice(who + "Should you pay the demon army to strengthen a dungeon today?", supportOptions);

        var upgradeOptions = new Dictionary<string, string> { ["none"] = "Do not invest in the shop today." };
        if (s.BlacksmithUpgradeCost > 0 && s.BlacksmithUpgradeCost <= s.Money)
            upgradeOptions["blacksmith"] = $"Pay {s.BlacksmithUpgradeCost}G to raise the blacksmith level (unlocks higher-tier items).";
        if (s.ShopUpgradeCost > 0 && s.ShopUpgradeCost <= s.Money)
            upgradeOptions["shop"] = $"Pay {s.ShopUpgradeCost}G to raise the shop level (more display kinds and more stock per kind).";
        if (upgradeOptions.Count > 1)
            req.Questions["upgrade"] = JevQuestion.Choice(who + "Should you invest in the shop today?", upgradeOptions);

        // 勇者の装備（無料で付け外しできる）。強くすれば勝ちやすく、外せば負けやすく＝防衛報酬が狙える
        req.Questions["hero_gear"] = JevQuestion.Choice(
            who + "You can equip the hero for free with any unlocked weapon and armor. What should the hero carry into the next stream?",
            new Dictionary<string, string>
            {
                ["best"] = "Equip the strongest unlocked weapon and armor: the hero gets stronger and is more likely to win.",
                ["keep"] = "Keep the current equipment.",
                ["none"] = "Remove all equipment: the hero is weaker and more likely to lose (you earn the defeat reward if the hero loses).",
            });

        // 新聞の購読（枠が空いているときだけ）。購読した社の紙面は翌日から読める
        var subOptions = new Dictionary<string, string> { ["none"] = "Do not subscribe to a newspaper today." };
        if (s.SubscriptionUsed < s.SubscriptionSlots)
            foreach (var c in s.Newspapers.Where(c => c.CanSubscribe))
                subOptions[c.Id] = $"Subscribe to {c.Id} for {c.Price}G ({c.ContractTurns} days): trust {c.Trust}/5, speed {c.Speed}/5, " +
                                   $"{c.Pages} pages, covers {c.Categories}" + (c.MeasuredAccuracy >= 0 ? $", your measured accuracy {c.MeasuredAccuracy:P0}" : "");
        if (subOptions.Count > 1)
            req.Questions["subscribe"] = JevQuestion.Choice(who +
                $"Newspaper slots used {s.SubscriptionUsed}/{s.SubscriptionSlots}" + (s.WallPaperOnly ? " (you only see the free wall paper now)" : "") +
                ". Which newspaper is worth subscribing to?", subOptions);

        // スクラップ（無料・枠3）と確認（1,500G / 3件 4,000G）
        var pinnable = s.News.Where(n => n.CanPin).ToList();
        if (pinnable.Count > 0 && s.Scraps.Count < s.ScrapCapacity)
        {
            var pinOptions = new Dictionary<string, string> { ["none"] = "Do not clip any article today." };
            for (int k = 0; k < pinnable.Count; k++)
                pinOptions["a" + k] = $"{pinnable[k].Company}/{pinnable[k].Page}: {(string.IsNullOrEmpty(pinnable[k].SummaryEn) ? pinnable[k].HeadlineJa : pinnable[k].SummaryEn)}";
            req.Questions["scrap_pin"] = JevQuestion.Choice(who +
                "You may clip one report to your scrapbook (free, 3 slots, cannot be removed). After it settles you can pay to learn " +
                "whether it was true, which builds each paper's measured accuracy. Which report is worth clipping?", pinOptions);
        }
        int unconfirmed = s.Scraps.Count(x => x.State == "Unconfirmed");
        if (unconfirmed > 0)
        {
            var confOptions = new Dictionary<string, string>
            {
                ["none"] = "Do not pay to check clipped reports today.",
                ["one"] = $"Pay {NewsTuning.ConfirmCost}G to check one settled clipped report (frees a slot, shows if the paper was right).",
            };
            if (unconfirmed >= NewsTuning.ConfirmBatchCount)
                confOptions["batch"] = $"Pay {NewsTuning.ConfirmBatchCost}G to check {NewsTuning.ConfirmBatchCount} settled clipped reports at once.";
            req.Questions["scrap_confirm"] = JevQuestion.Choice(who + "Is it worth paying to check your settled clipped reports?", confOptions);
        }

        var resp = await SendAsync(req, ct);
        if (resp == null)
        {
            var fb = await _fallback.PlanDayAsync(s, ct);
            fb.Decisions.Add(Err(s, "day"));
            return fb;
        }

        var plan = new AutoPlayDayPlan();
        float budgetFrac = Fraction(resp, "budget", 6, 0.5f);
        // 返済の手当ては Jev 自身に判断させる（state に返済額と残り日数を載せてある）
        int budget = Mathf.Max(0, Mathf.RoundToInt(s.Money * budgetFrac));
        Record(plan.Decisions, s, "day", "budget", resp, budget.ToString());

        // 仕入れ: argmax ではなく確率分布で予算を配分する（Docs/Jev_AutoPlay_Design.md §3.2）
        if (resp.Answers.TryGetValue("buy", out var buy) && buy.Probabilities != null && budget > 0)
        {
            var alloc = TopShares(buy.Probabilities, minShare: 0.08f, maxKinds: 4);
            foreach (var kv in alloc)
            {
                var item = s.Items.First(i => i.Id == kv.Key);
                int qty = Mathf.FloorToInt(budget * kv.Value / Mathf.Max(1, item.BuyUnitPrice));
                if (qty > 0) plan.Purchases.Add(new AutoPlayStreamItem(item.Id, qty));
            }
            Record(plan.Decisions, s, "day", "buy", resp, buy.Choice);
        }

        if (resp.Answers.TryGetValue("display", out var disp) && disp.Probabilities != null)
        {
            plan.DisplayPriority = disp.Probabilities.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
            Record(plan.Decisions, s, "day", "display", resp, disp.Choice);
        }
        plan.DisplayFraction = Fraction(resp, "display_amount", 5, 1f);
        if (resp.Answers.ContainsKey("display_amount"))
            Record(plan.Decisions, s, "day", "display_amount", resp, plan.DisplayFraction.ToString("F2"));

        if (resp.Answers.TryGetValue("support", out var sup))
        {
            string pick = Pick(sup);
            plan.SupportDungeon = pick == "none" ? null : pick;
            Record(plan.Decisions, s, "day", "support", resp, pick);
        }

        if (resp.Answers.TryGetValue("upgrade", out var upg))
        {
            plan.Upgrade = Pick(upg) ?? "none";
            Record(plan.Decisions, s, "day", "upgrade", resp, plan.Upgrade);
        }

        if (resp.Answers.TryGetValue("hero_gear", out var gear))
        {
            string g = Pick(gear) ?? "keep";
            if (g == "best") (plan.HeroWeapon, plan.HeroArmor) = AutoPlayGreedyBot.BestGear(s);
            else if (g == "none") { plan.HeroWeapon = ""; plan.HeroArmor = ""; }
            Record(plan.Decisions, s, "day", "hero_gear", resp, g);
        }

        if (resp.Answers.TryGetValue("subscribe", out var sub))
        {
            string pick = Pick(sub) ?? "none";
            if (pick != "none") plan.SubscribeNewspapers.Add(pick);
            Record(plan.Decisions, s, "day", "subscribe", resp, pick);
        }
        if (resp.Answers.TryGetValue("scrap_pin", out var pinA))
        {
            string pick = Pick(pinA) ?? "none";
            var pinList = s.News.Where(n => n.CanPin).ToList();
            if (pick.StartsWith("a") && int.TryParse(pick.Substring(1), out var idx) && idx < pinList.Count)
                plan.PinScraps.Add(pinList[idx].EntryKey);
            Record(plan.Decisions, s, "day", "scrap_pin", resp, pick == "none" ? "none" : "clip");
        }
        if (resp.Answers.TryGetValue("scrap_confirm", out var conf))
        {
            plan.ConfirmScraps = Pick(conf) ?? "none";
            Record(plan.Decisions, s, "day", "scrap_confirm", resp, plan.ConfirmScraps);
        }

        return plan;
    }

    // =================================================================
    // 配信日
    // =================================================================

    public async Task<AutoPlayStreamPlan> PlanStreamAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        var stocked = s.Items.Where(i => i.Stock > 0).ToList();
        var info = s.Stream;
        var interveneOptions = new Dictionary<string, string>();
        if (info != null && info.InterventionsEnabled)
        {
            interveneOptions["none"] = "Do not pay for any super chat or intervention in this stream.";
            foreach (var o in info.Options.Where(o => o.Affordable))
                interveneOptions[AutoPlayInterventionKinds.Key(o.Kind)] = $"Pay {o.Price}G. {o.DescriptionEn}";
            if (interveneOptions.Count == 1) interveneOptions.Clear();
        }
        if (stocked.Count == 0 && interveneOptions.Count == 0) return new AutoPlayStreamPlan();

        var req = new JevRequest { State = BuildState(s) };
        string who = _persona.Instruction + " ";
        if (stocked.Count > 0)
        {
            req.Questions["stream_pick"] = JevQuestion.Choice(
                who + $"Today is a stream day ({s.NextStreamDungeon}, hero clear chance {s.NextStreamClearChancePct:F0}%). " +
                "Which item should you bring to sell to the stream viewers? Items sell at today's price, faster when demand is high.",
                stocked.ToDictionary(i => i.Id, DescribeItem));
            req.Questions["stream_amount"] = JevQuestion.Score(
                who + "How much of the stock of the chosen items should you bring to the stream?",
                new[] { "Bring almost nothing", "Bring about a quarter", "Bring about half", "Bring about three quarters", "Bring all of it" });
        }
        if (interveneOptions.Count > 0)
        {
            // 側（勇者/ダンジョン）と種類を1問で聞く。払うお金は所持金から（視聴者のスパチャは収入にならない）
            req.Questions["stream_intervene"] = JevQuestion.Choice(
                who + "During the stream you may pay money for one intervention: hero-side super chats help the hero win and " +
                "make the stream more exciting; dungeon-side interventions help the monsters, and if the hero loses you earn " +
                "the defeat reward (see 'stream' in the state). The price is paid from your cash and is not returned. " +
                "Which intervention is worth it in this stream, if any?", interveneOptions);
            req.Questions["intervene_timing"] = JevQuestion.Score(
                who + "If you intervene, at which point of the battle should you do it?",
                new[] { "Right at the start", "Early", "In the middle", "Late", "Near the end (e.g. at the boss)" });
        }

        var resp = await SendAsync(req, ct);
        if (resp == null)
        {
            var fb = await _fallback.PlanStreamAsync(s, ct);
            fb.Decisions.Add(Err(s, "stream"));
            return fb;
        }

        var plan = new AutoPlayStreamPlan();
        if (resp.Answers.TryGetValue("stream_pick", out var pick) && pick.Probabilities != null)
        {
            float frac = Fraction(resp, "stream_amount", 5, 1f);
            foreach (var kv in TopShares(pick.Probabilities, minShare: 0.05f, maxKinds: 6))
            {
                var item = stocked.FirstOrDefault(i => i.Id == kv.Key);
                if (item == null) continue;
                int qty = Mathf.Clamp(Mathf.RoundToInt(item.Stock * frac), 0, item.Stock);
                if (qty > 0) plan.Items.Add(new AutoPlayStreamItem(item.Id, qty));
            }
            Record(plan.Decisions, s, "stream", "stream_pick", resp, pick.Choice);
            Record(plan.Decisions, s, "stream", "stream_amount", resp, frac.ToString("F2"));
        }

        if (resp.Answers.TryGetValue("stream_intervene", out var iv))
        {
            float timing = Fraction(resp, "intervene_timing", 5, 0.5f);
            string first = Pick(iv) ?? "none";
            if (AutoPlayInterventionKinds.TryParse(first, out var k1))
            {
                plan.Interventions.Add(new AutoPlayInterventionOrder(k1, timing));
                // 2番手も強く推されていれば（25%以上）、少し後に重ねる（同時に1件・クールダウンはサロゲートが守る）
                var second = iv.Probabilities?
                    .Where(kv => kv.Key != first && kv.Key != "none" && kv.Value >= 0.25f)
                    .OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
                if (second != null && AutoPlayInterventionKinds.TryParse(second, out var k2))
                    plan.Interventions.Add(new AutoPlayInterventionOrder(k2, Mathf.Clamp01(timing + 0.3f)));
            }
            Record(plan.Decisions, s, "stream", "stream_intervene", resp,
                plan.Interventions.Count == 0 ? "none" : string.Join("+", plan.Interventions.Select(i => AutoPlayInterventionKinds.Key(i.Kind))));
            Record(plan.Decisions, s, "stream", "intervene_timing", resp, timing.ToString("F2"));
        }
        return plan;
    }

    // =================================================================
    // レリック3択
    // =================================================================

    public async Task<AutoPlayRelicPlan> PlanRelicAsync(AutoPlaySnapshot s, CancellationToken ct)
    {
        if (s.PendingRelicChoices.Count == 0) return new AutoPlayRelicPlan();
        var options = new Dictionary<string, string>
        {
            ["decline"] = $"Decline all and take {s.RelicDeclineGold}G instead.",
        };
        for (int i = 0; i < s.PendingRelicChoices.Count; i++)
        {
            var c = s.PendingRelicChoices[i];
            // 説明は日本語のみ（英訳列なし）。レア度と ID で判断させる
            options[i.ToString()] = $"Relic '{c.RelicId}' ({c.Rarity}). Effect (Japanese): {c.Description}";
        }

        var req = new JevRequest { State = BuildState(s) };
        req.Questions["relic"] = JevQuestion.Choice(_persona.Instruction + " You may take one relic (a permanent passive effect) or decline it for gold.", options);
        var resp = await SendAsync(req, ct);
        if (resp == null)
        {
            var fb = await _fallback.PlanRelicAsync(s, ct);
            fb.Decisions.Add(Err(s, "relic"));
            return fb;
        }

        var plan = new AutoPlayRelicPlan();
        string choice = resp.Answers.TryGetValue("relic", out var a) ? Pick(a) : "decline";
        plan.ChoiceIndex = int.TryParse(choice, out var idx) ? idx : -1;
        Record(plan.Decisions, s, "relic", "relic", resp, choice);
        return plan;
    }

    // =================================================================
    // 共通
    // =================================================================

    private async Task<JevResponse> SendAsync(JevRequest req, CancellationToken ct)
    {
        try
        {
            // JevApi.SendAsync は内部で ConfigureAwait(false)。ここでは await の継続を Unity メインスレッドに戻す
            // （同期待ちは絶対にしない：メインスレッドからの .Result はエディタをデッドロックさせる）。
            var resp = await JevApi.SendAsync(req, ct);
            Requests++;
            if (resp?.Usage != null) InputTokens += resp.Usage.InputTokens;
            if (resp?.Answers == null) throw new InvalidOperationException("answers が空");
            return resp;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Errors++;
            LastError = e.Message;
            Debug.LogWarning($"[AutoPlay][Jev] {Name}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Jev に見せる盤面。プレイヤーが画面で見られる情報だけを英語で載せる（未来の Trend・弱点などは載せない）。
    /// </summary>
    private static object BuildState(AutoPlaySnapshot s)
    {
        return new Dictionary<string, object>
        {
            ["context"] = Context,
            ["day"] = s.RunTurn,
            ["runProgress"] = s.FlowLength > 0 ? $"{s.FlowIndex}/{s.FlowLength} days" : "unknown",
            ["cash"] = s.Money,
            ["pendingSellOrdersValue"] = s.PendingSellOrderEstimate,
            ["nextDebt"] = new { amount = s.NextDebtAmount, daysLeft = s.TurnsUntilDebt },
            ["shop"] = new
            {
                level = s.ShopLevel,
                maxDisplayKinds = s.MaxDisplayKinds,
                maxDisplayPerItem = s.MaxDisplayPerItem,
                blacksmithLevel = s.BlacksmithLevel,
                buzz = s.BuzzActive ? s.BuzzType : "none",
            },
            ["hero"] = new
            {
                level = s.HeroLevel, hp = s.HeroHp, attack = s.HeroAttack, defense = s.HeroDefense,
                weapon = string.IsNullOrEmpty(s.HeroWeaponId) ? "none" : s.HeroWeaponId,
                armor = string.IsNullOrEmpty(s.HeroArmorId) ? "none" : s.HeroArmorId,
            },
            ["nextStream"] = s.NextStreamDungeon == null ? null : new
            {
                dungeon = s.NextStreamDungeon,
                dungeonLevel = s.NextStreamDungeonLevel,
                daysUntil = s.TurnsUntilStream,
                heroClearChancePct = Mathf.RoundToInt(s.NextStreamClearChancePct),
            },
            ["items"] = s.Items.Where(i => i.Unlocked || i.Stock > 0).Select(i => new
            {
                id = i.Id,
                type = i.Type,
                attribute = i.Attribute,
                tier = i.Tier,
                price = i.Price,
                basePrice = i.BasePrice,
                change1d = $"{i.PriceChange1d * 100f:+0.0;-0.0;0}%",
                demand = Math.Round(i.Demand, 2),
                demandChange1d = Math.Round(i.DemandChange1d, 2),
                heat = i.Heat,
                stock = i.Stock,
                maxStock = i.MaxStock,
                displayed = i.Displayed ? i.DisplayStock : 0,
                dividendPerDay = i.DividendPerTurn,
            }).ToList(),
            ["dungeons"] = s.Dungeons.Select(d => new
            {
                id = d.Id,
                level = d.Level,
                heroClearChancePct = Mathf.RoundToInt(d.ClearChancePct),
                defeatReward = d.DefeatReward,
                supportCost = d.SupportCost,
            }).ToList(),
            // 配信日だけ: 配信画面で見える情報（勇者の強さ・クリア確率・ボスの有無・防衛報酬）。オラクルの what-if は載せない
            ["stream"] = s.Stream == null ? null : new
            {
                dungeon = s.Stream.Dungeon,
                dungeonLevel = s.Stream.Level,
                heroClearChancePct = Mathf.RoundToInt(s.Stream.DisplayedClearPct),
                heroMaxHp = s.Stream.HeroMaxHp,
                heroAttack = s.Stream.HeroAttack,
                heroDefense = s.Stream.HeroDefense,
                monsters = s.Stream.EnemyCount,
                bossPresent = s.Stream.HasBoss,
                defeatRewardIfHeroLoses = s.Stream.DefeatReward,
                cashAvailableForSuperChats = s.Money,
                interventionsEnabled = s.Stream.InterventionsEnabled,
            },
            ["newspaperSubscriptions"] = new
            {
                slotsUsed = s.SubscriptionUsed,
                slots = s.SubscriptionSlots,
                wallPaperOnly = s.WallPaperOnly,
                papers = s.Newspapers.Select(c => new
                {
                    id = c.Id, price = c.Price, trust = c.Trust, speed = c.Speed, pages = c.Pages, covers = c.Categories,
                    subscribed = c.Subscribed, daysLeft = c.TurnsLeft,
                    measuredAccuracy = c.MeasuredAccuracy >= 0 ? (object)Math.Round(c.MeasuredAccuracy, 2) : "unknown",
                }).ToList(),
                scrapbook = s.Scraps.Select(x => new { paper = x.Company, day = x.PublishTurn, state = x.State }).ToList(),
            },
            ["newspaper"] = s.News.Select(n => new
            {
                paper = n.Company,
                page = n.Page,
                source = n.SourceClarity,
                byline = n.Byline,
                kind = n.Kind,
                // summaryEn が空の記事は日本語見出しで代用（精度は落ちる）
                text = string.IsNullOrEmpty(n.SummaryEn) ? "(JA) " + n.HeadlineJa : n.SummaryEn,
            }).ToList(),
        };
    }

    private static string DescribeItem(AutoPlayItemView i) =>
        $"{i.Attribute} {i.Type} tier {i.Tier}: price {i.Price} (base {i.BasePrice}, {i.PriceChange1d * 100f:+0.0;-0.0;0}% 1d), " +
        $"demand {i.Demand:F2}, heat {i.Heat}, stock {i.Stock}/{i.MaxStock}";

    /// <summary>score の答えを 0〜1 の割合へ。無ければ既定値。</summary>
    private static float Fraction(JevResponse resp, string key, int levels, float fallback)
    {
        if (!resp.Answers.TryGetValue(key, out var a) || !a.Score.HasValue) return fallback;
        return Mathf.Clamp01(a.Score.Value / Mathf.Max(1, levels - 1));
    }

    /// <summary>確率分布の上位を、最低シェア未満を捨てて正規化する（配分用）。</summary>
    private static List<KeyValuePair<string, float>> TopShares(Dictionary<string, float> probs, float minShare, int maxKinds)
    {
        var top = probs.OrderByDescending(kv => kv.Value).Take(maxKinds).Where(kv => kv.Value >= minShare).ToList();
        if (top.Count == 0) top = probs.OrderByDescending(kv => kv.Value).Take(1).ToList();
        float sum = top.Sum(kv => kv.Value);
        return top.Select(kv => new KeyValuePair<string, float>(kv.Key, sum > 0 ? kv.Value / sum : 1f / top.Count)).ToList();
    }

    /// <summary>1択の質問の答え。Sample なら分布から抽選、Argmax なら最尤。</summary>
    private string Pick(JevAnswer a)
    {
        if (a == null) return null;
        if (_mode == AutoPlayJevDecisionMode.Argmax || a.Probabilities == null || a.Probabilities.Count == 0) return a.Choice;
        double r = _rng.NextDouble() * a.Probabilities.Values.Sum();
        foreach (var kv in a.Probabilities.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            r -= kv.Value;
            if (r <= 0) return kv.Key;
        }
        return a.Choice;
    }

    private static void Record(List<AutoPlayDecision> list, AutoPlaySnapshot s, string phase, string question, JevResponse resp, string chosen)
    {
        resp.Answers.TryGetValue(question, out var a);
        list.Add(new AutoPlayDecision
        {
            Turn = s.RunTurn,
            Phase = phase,
            Question = question,
            Chosen = chosen,
            Score = a?.Score,
            Confidence = a?.Confidence,
            Probabilities = a?.Probabilities,
        });
    }

    /// <summary>プロセスをまたいでも変わらない文字列ハッシュ（シード再現用）。</summary>
    private static int StableHash(string text)
    {
        unchecked
        {
            int h = 23;
            foreach (char c in text ?? "") h = h * 31 + c;
            return h;
        }
    }

    private AutoPlayDecision Err(AutoPlaySnapshot s, string phase) => new AutoPlayDecision
    {
        Turn = s.RunTurn, Phase = phase, Question = "jev_error", Chosen = "fallback_greedy:" + LastError,
    };
}

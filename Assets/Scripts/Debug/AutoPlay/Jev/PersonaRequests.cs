#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
// 人格プレイで Jev 互換モデルへ送る state と質問を組み立てる（純粋関数。ゲームのモデルには触らない）。
// 画面で見える情報だけを英語で載せる（未来の Trend・未購入の弱点は載せない）。
// tools/persona_play/test_request.py はこのファイルと同じ JSON を作る。文言を変えたらそちらも直す。
// =====================================================================

/// <summary>商品1件（state 用）。</summary>
public sealed class PersonaItemView
{
    public string Id;
    public string Type;        // "Weapon" / "Armor"
    public string Attribute;   // "Fire" など
    public int Price;
    public int PreviousPrice;
    public float Demand;
    public float PreviousDemand;
    public int Stock;
    public int MaxStock;
    public int Displayed;
    public int DividendPerDay;
    public bool Unlocked;
    public float ExpectedRevenue;
}

/// <summary>次の配信（state 用）。</summary>
public sealed class PersonaStreamView
{
    public string Dungeon;
    public int DaysUntil;          // 0 = 次の行き先が配信（前日の仕入れ・配信日の朝）
    public int Level;
    public bool WeaknessKnown;
    public string Weakness;        // 知らなければ null
    public int IntelCost = -1;     // 情報屋の情報料（買えない・買った後は -1）
    public int DefeatReward;       // 勇者が負けたときの防衛報酬（今のレベル）
    public int SupportCost;        // 魔王軍支援 1 レベルぶん（上限なら 0）
    public int RewardAfterSupport; // 1 レベル上げた後の防衛報酬
}

/// <summary>ダンジョン向けに買える武器（state・質問用）。</summary>
public sealed class PersonaWeaponView
{
    public string Id;
    public string Attribute;
    public int UnitPrice;
    public int RemainToMax;
    public bool WeakAttribute;     // 弱点属性そのもの
}

/// <summary>村の施設 1 つ（state・質問用）。</summary>
public sealed class PersonaFacilityView
{
    public string Id;
    public string Name;
    public int Level;
    public int MaxLevel;
    public int NextCost = -1;      // 最大レベルなら -1
    public string NextEffect;      // 次のレベルの効果（ゲームの表記のまま）
    public bool CanInvest;
    public string BlockedReason;   // 投資できない理由（ゲームの表記のまま）
}

/// <summary>村に着いたときの判断材料（メタ進行。ランをまたいで残る）。</summary>
public sealed class PersonaVillageSnapshot
{
    public int Runs;               // このセーブで終えたランの数
    public int Funds;              // 村資金
    public int VillageLevel;       // 全施設レベルの合計
    public int MaxVillageLevel;
    public int HallLevel;
    public int Rank;
    public int TopRank;
    public string RankName;
    public int LastRunProfit;
    public bool LastRunCleared = true;
    public readonly List<PersonaFacilityView> Facilities = new();

    public float LevelShare => MaxVillageLevel > 0 ? VillageLevel / (float)MaxVillageLevel : 0f;
}

/// <summary>勇者に持たせられる装備 1 つ（state・質問用。勇者の画面の候補と同じ: 鍛冶屋Lv以下の武器 / 防具）。</summary>
public sealed class PersonaGearView
{
    public string Id;
    public string Name;
    public string Attribute;
    public int Tier;               // ランク（品の必要鍛冶屋Lv）。強さの上乗せはこれで決まる
    public int BonusPct;           // 武器 = 攻撃 +%、防具 = 防御 +%
    public int Stock;
    public int Price;
    public bool Equipped;
    public bool WeakAttribute;     // 次の配信先の弱点（知っているときだけ true）
}

/// <summary>置けるマシン 1 つ（state・質問用）。</summary>
public sealed class PersonaMachineView
{
    public string Id;
    public string Name;
    public string Type;            // ShopMachineEffectType の名前
    public int Cost;
    public string Effect;          // ゲームの表記（毎朝 +800G など）
    public string EffectEn;
    public int DailyGold;          // 毎朝のお金（お金の設備だけ。それ以外は 0）
    public bool Flashy;            // 目立つ（ずんだもんが飛びつく）
    public int Placed;             // 置いているか（同じ機種は 1 台まで。0 / 1）
    // --- 置いてあるマシンのレベル・壊れやすさ（強化・修理の質問用。2026-10-29） ---
    public int Level, MaxLevel;
    public string NextEffect, NextEffectEn;   // 次のレベルの効果（最大なら null）
    public int UpgradeCost = -1;   // 割引込み。-1 = 最大
    public float DailyValue;       // 今の 1 日の価値（G の目安。AutoPlayMachineRules.DailyValue）
    public float AddedDailyValue;  // 強化で増える 1 日の価値
    public bool Broken;
    public bool Repairable;        // 故障中か、明朝から壊れる恐れがある
    public int RepairCost;
    public int SafeLeft = int.MaxValue;   // あと何回の朝は壊れないか（壊れないマシンは int.MaxValue）
    public int NextBreakPct;       // 明朝の故障率 %
}

/// <summary>取引所の商品 1 つ（state・質問用。情報屋Lvで解禁済みのものだけ）。</summary>
public sealed class PersonaFinanceView
{
    public string Id;
    public string Name;
    public bool Bond;
    public int UnitPrice;          // 1 口（ファンドは今の基準価額）
    public int RatePct;            // 債券の満期の利率 %
    public int MaturityDays;
    public int RemainingUnits = -1;// 債券のあと買える口数（-1 = 上限なし）
    public string Tracks;          // ファンドの連動先（"all goods" / "Fire goods" …）
    public float Change1d;         // ファンドの前日比（0.012 = +1.2%）
    public int FeePct;             // ファンドの売買手数料 %
    public int Held;
    public int HeldValue;          // 今の評価額（債券は元本）
    public int HeldCost;           // 買ったときの額（手数料抜き）
}

/// <summary>店への投資の判断材料（勇者の装備・店の改装・鍛冶屋 / 情報屋の開発・設備・取引所）。</summary>
public sealed class PersonaShopView
{
    // 勇者
    public bool HeroAvailable;     // 店の「勇者」ボタンが押せる
    public string HeroWeaponId, HeroArmorId, HeroTactics;
    public readonly List<PersonaGearView> Weapons = new();
    public readonly List<PersonaGearView> Armors = new();
    // 店の改装
    public bool ShopAvailable;     // 店の「店」ボタンが押せる
    public int ShopLevel, ShopMaxLevel, ShopUpgradeCost = -1;
    public int KindsNow, KindsNext, SlotsNow, SlotsNext;
    // 鍛冶屋の開発
    public int SmithLevel, SmithMaxLevel, SmithUpgradeCost = -1, SmithUnlocks;
    public int SmithUnlockMinPrice, SmithUnlockMaxPrice;
    // 設備
    public int SlotsUsed, SlotsMax;
    public readonly List<PersonaMachineView> Machines = new();     // いま買って置けるもの
    public readonly List<string> PlacedNames = new();
    public readonly List<PersonaMachineView> PlacedMachines = new();   // 置いてあるもの（強化・修理）
    /// <summary>強化を聞くマシン（元が取れる日数が一番短いもの。無ければ null）と、修理を聞くマシン（壊れたお金・製造の設備）。</summary>
    public string UpgradeTargetId, RepairTargetId;
    /// <summary>このランで残っている朝の数（日送りの回数）。</summary>
    public int RemainingMornings;
    // 情報屋・取引所
    public bool BrokerAvailable;   // 店の「情報屋」ボタンが押せる
    public int BrokerLevel, BrokerMaxLevel, BrokerUpgradeCost = -1, BrokerUnlocks;
    public readonly List<PersonaFinanceView> Products = new();
    public int PortfolioValue;
}

/// <summary>営業日1日ぶんの判断材料。</summary>
public sealed class PersonaDaySnapshot
{
    public int Day;
    public int VillageLevel;
    public int MaxVillageLevel;
    public int Cash;
    public int DebtAmount;
    public int DebtDaysLeft = -1;
    public int ShopLevel;
    public int BlacksmithLevel;
    public readonly List<PersonaItemView> Items = new();
    public PersonaStreamView NextStream;
    public readonly List<PersonaWeaponView> Weapons = new();
    /// <summary>店への投資の材料（取れなければ null。その日は聞かない）。</summary>
    public PersonaShopView Shop;
}

/// <summary>配信開始時の判断材料。</summary>
public sealed class PersonaStreamSnapshot
{
    public int Day;
    public int Balance;
    public int SpendBudget;
    public int HeroHpPct = -1;
    public int Monsters;
    public bool BossAlive;
    /// <summary>介入キー（heal/skill/special/trap/curse/reinforce/bossbuff）→ 価格。</summary>
    public readonly Dictionary<string, int> Interventions = new();
}

public static class PersonaRequests
{
    public const string Context =
        "You run an item shop in a fantasy town (the game TomsLands). Each shop day you may buy weapons and armor at today's price " +
        "and display them; displayed goods sell to customers and are paid at the next day's price, so you profit when you buy low and prices rise. " +
        "Prices drift toward a fair value set by demand; newspaper articles describe causes that may move demand a few days later. " +
        "On stream days the hero fights a dungeon live while viewers buy your goods. If the hero loses, you receive the dungeon's defeat reward. " +
        "Paying the demon army raises a dungeon's level: stronger monsters, lower hero clear chance, larger defeat reward. " +
        "During a stream you may pay for super chats: hero-side (heal, skill, special) or dungeon-side (trap, curse, reinforce, boss buff). " +
        "Each dungeon's weak attribute is reshuffled every run, so it cannot be guessed from the dungeon's name or looks; only the information broker reveals it. " +
        "Viewer excitement rises on highlights: the boss appearing, the hero falling into a pinch, a comeback, the hero falling; " +
        "any super chat you pay for also counts as a highlight. It cools down when nothing happens for a few turns. " +
        "Before a stream you choose the hero's weapon, armor and tactics; what he carries rises in price when he wins. " +
        "You can also remodel the shop (more kinds on display, more machine slots), develop the blacksmith (stronger goods) and the information broker, " +
        "place machines that work every day, and buy bonds or funds at the broker's exchange; all of these last until the end of the run. " +
        "A debt payment is due every few days; failing to pay means bankruptcy. Goal: finish the run with as much money as possible.";

    /// <summary>state に載せる商品の上限（在庫あり → 期待収益の高い順）。</summary>
    public const int MaxItemsInState = 20;
    /// <summary>ダンジョン向け武器の選択肢の上限（Ollama の choice は 26 件まで。弱点属性 → 安い順）。</summary>
    public const int MaxWeaponOptions = PersonaJevQuestion.MaxChoiceOptions;

    public static readonly string[] BudgetLevels =
    {
        "Spend nothing today", "Spend about 20% of spendable cash", "Spend about 40% of spendable cash",
        "Spend about 60% of spendable cash", "Spend about 80% of spendable cash", "Spend almost all spendable cash",
    };

    public static readonly string[] SupportLevels =
    {
        "Do not pay the demon army", "Raise the dungeon by 1 level", "Raise the dungeon by 2 levels", "Raise it as far as cash allows",
    };

    public static readonly string[] ShareLevels =
    {
        "None of the budget", "About 25% of the budget", "About half of the budget", "About 75% of the budget", "All of the budget",
    };

    public static readonly string[] VillageBudgetLevels =
    {
        "Invest nothing on this visit", "Spend about 25% of the village funds", "Spend about half of the village funds",
        "Spend about 75% of the village funds", "Spend all the village funds you allow yourself",
    };

    /// <summary>目立つ・派手な施設（ずんだもんが勘で飛びつく）。</summary>
    public static readonly HashSet<string> FlashyFacilities = new() { "plaza", "guild", "artisan" };

    public const string VillageContext =
        "Between runs you return to your home village. The profit of each finished run is converted into village funds. " +
        "Investing village funds raises a facility by one level; facility effects apply from the next run on and stay forever. " +
        "The hall's level caps the other facilities (hall Lv N allows the others up to Lv N+1). Money spent here is not available to carry into the next run. " +
        "Goal: grow the village so that later runs earn more.";

    public static readonly string[] IntensityLevels =
    {
        "Never pay for super chats", "Rarely", "Sometimes", "Often", "At every chance",
    };

    public static object BuildDayState(PersonaProfile persona, IList<string> lessons, PersonaDaySnapshot s)
    {
        var items = s.Items
            .Where(i => i.Unlocked || i.Stock > 0)
            .OrderByDescending(i => i.Stock > 0 ? 1 : 0)
            .ThenByDescending(i => i.ExpectedRevenue)
            .Take(MaxItemsInState)
            .Select(i => new Dictionary<string, object>
            {
                ["id"] = i.Id,
                ["type"] = i.Type,
                ["attribute"] = i.Attribute,
                ["price"] = i.Price,
                ["change1d"] = PctChange(i.Price, i.PreviousPrice),
                ["demand"] = Round2(i.Demand),
                ["demandChange1d"] = Round2(i.Demand - i.PreviousDemand),
                ["stock"] = i.Stock,
                ["maxStock"] = i.MaxStock,
                ["displayed"] = i.Displayed,
                ["dividendPerDay"] = i.DividendPerDay,
            })
            .ToList();

        var state = new Dictionary<string, object>
        {
            ["persona"] = persona.Preamble,
            ["lessonsFromPastRuns"] = lessons ?? new List<string>(),
            ["context"] = Context,
            ["day"] = s.Day,
            ["cash"] = s.Cash,
            ["nextDebt"] = new Dictionary<string, object> { ["amount"] = s.DebtAmount, ["daysLeft"] = s.DebtDaysLeft },
            ["shop"] = new Dictionary<string, object> { ["level"] = s.ShopLevel, ["blacksmithLevel"] = s.BlacksmithLevel },
            ["items"] = items,
        };
        if (s.MaxVillageLevel > 0)
            state["village"] = new Dictionary<string, object> { ["totalFacilityLevel"] = s.VillageLevel, ["maxTotalFacilityLevel"] = s.MaxVillageLevel };
        if (persona.CashFloor > 0) state["cashToKeep"] = persona.CashFloor;
        if (s.Shop != null) AddShopState(state, s.Shop);
        if (s.NextStream != null)
        {
            var n = s.NextStream;
            state["nextStream"] = new Dictionary<string, object>
            {
                ["dungeon"] = n.Dungeon,
                ["daysUntil"] = n.DaysUntil,
                ["dungeonLevel"] = n.Level,
                ["weakAttribute"] = n.WeaknessKnown ? n.Weakness : "unknown (buy information to learn it)",
                ["defeatRewardIfHeroLoses"] = n.DefeatReward,
            };
        }
        return state;
    }

    public static PersonaJevRequest BuildDayRequest(PersonaProfile persona, string model, IList<string> lessons, PersonaDaySnapshot s)
    {
        var req = new PersonaJevRequest { Model = model, State = BuildDayState(persona, lessons, s) };
        var n = s.NextStream;
        bool prepDay = n != null && n.DaysUntil <= 0;

        req.Questions["budget"] = PersonaJevQuestion.Score(
            "How much of your spendable cash should you spend on stock today? Remember the next debt payment.", BudgetLevels);

        var styles = new List<KeyValuePair<string, string>>
        {
            new("recommend", "Recommended order: items with the best expected revenue first."),
            new("bargain", "Bargain: items whose price is lowest compared with their usual price."),
            new("dividend", "Dividend: items that pay the best dividend per day for their price."),
        };
        if (prepDay && n.WeaknessKnown)
            styles.Add(new("dungeon_focus", $"Dungeon focus: weapons and armor of {n.Weakness}, the weak attribute of {n.Dungeon}, the next stream."));
        req.Questions["buy_style"] = PersonaJevQuestion.Choice("How should the automatic purchase choose items today?", styles);

        req.Questions["newspaper"] = PersonaJevQuestion.Noul(
            "Should you read the newspaper this morning before buying?",
            "Read it first: the articles hint at demand changes.", "Skip it and buy right away.");

        if (n != null && !n.WeaknessKnown && n.IntelCost > 0 && n.IntelCost <= s.Cash)
            req.Questions["intel"] = PersonaJevQuestion.Noul(
                $"Should you pay {n.IntelCost}G to the information broker to learn the weak attribute of {n.Dungeon}, " +
                $"the next stream dungeon ({n.DaysUntil} days away)? Knowing it lets you stock goods that sell well in that stream.",
                "Buy the information.", "Do not buy it; save the money.");

        if (prepDay && n.SupportCost > 0 && n.SupportCost <= s.Cash)
            req.Questions["support"] = PersonaJevQuestion.Score(
                $"Should you pay the demon army to strengthen {n.Dungeon} before the stream? One level costs {n.SupportCost}G " +
                $"and raises the defeat reward from {n.DefeatReward}G to {n.RewardAfterSupport}G, but the money is gone even if the hero wins.",
                SupportLevels);

        if (prepDay && n.WeaknessKnown)
        {
            var weapons = s.Weapons
                .Where(w => w.UnitPrice > 0 && w.UnitPrice <= s.Cash && w.RemainToMax > 0)
                .OrderByDescending(w => w.WeakAttribute)
                .ThenBy(w => w.UnitPrice)
                .Take(MaxWeaponOptions)
                .Select(w => new KeyValuePair<string, string>(w.Id,
                    $"{w.Attribute} weapon, {w.UnitPrice}G each, room for {w.RemainToMax}" + (w.WeakAttribute ? ", matches the weak attribute" : ", effective attribute")))
                .ToList();
            if (weapons.Count > 0)
            {
                req.Questions["focus_share"] = PersonaJevQuestion.Score(
                    $"How much of today's budget should go to goods for the {n.Dungeon} stream (weak attribute {n.Weakness})?", ShareLevels);
                req.Questions["weapon"] = PersonaJevQuestion.Choice(
                    $"Which weapon should you stock for the {n.Dungeon} stream?", weapons);
            }
        }
        if (s.Shop != null) AddShopQuestions(req, persona, s);
        return req;
    }

    // =====================================================
    // 店への投資（勇者の装備・店の改装・鍛冶屋 / 情報屋の開発・設備・取引所）
    // =====================================================

    public static readonly string[] FinanceShareLevels =
    {
        "About 10% of spendable cash", "About 25% of spendable cash", "About half of spendable cash", "About 75% of spendable cash",
    };

    /// <summary>勇者の作戦（HeroTactics の名前 → 説明。効くのは配信中の相場だけ。勝ち負けは変わらない）。</summary>
    public static readonly KeyValuePair<string, string>[] TacticsOptions =
    {
        new("Balanced", "Balanced: no change to how the stream moves prices."),
        new("Aggressive", "Aggressive: the weapon's price reacts 1.2x and stream excitement 1.15x, the armor's price only 0.9x."),
        new("Defensive", "Defensive: the armor's price reacts 1.2x, the weapon's 0.9x, excitement 0.95x."),
        new("Stealth", "Cautious: the armor's price 1.1x, the weapon's 0.9x, excitement 0.85x."),
        new("MagicFocused", "Attribute focus: effective / weak attribute hits move prices 1.2x, weapon and armor 0.95x."),
    };

    /// <summary>目立つ設備（ずんだもんが飛びつく）。</summary>
    public static readonly HashSet<string> FlashyMachines = new() { "machine_attract", "machine_attract_l", "machine_money_l", "machine_maker_l" };

    private static void AddShopState(Dictionary<string, object> state, PersonaShopView v)
    {
        var shop = state.TryGetValue("shop", out var o) && o is Dictionary<string, object> d ? d : new Dictionary<string, object>();
        shop["machineSlots"] = $"{v.SlotsUsed}/{v.SlotsMax}";
        if (v.PlacedMachines.Count > 0)
            shop["machines"] = v.PlacedMachines.Select(m => m.Broken ? $"{m.Name} Lv{m.Level} (broken)" : $"{m.Name} Lv{m.Level}").ToList();
        else if (v.PlacedNames.Count > 0) shop["machines"] = v.PlacedNames;
        shop["infoBrokerLevel"] = v.BrokerLevel;
        state["shop"] = shop;
        if (!string.IsNullOrEmpty(v.HeroWeaponId) || !string.IsNullOrEmpty(v.HeroArmorId))
            state["hero"] = new Dictionary<string, object>
            {
                ["weapon"] = v.HeroWeaponId ?? "none", ["armor"] = v.HeroArmorId ?? "none", ["tactics"] = v.HeroTactics ?? "Balanced",
            };
        var held = v.Products.Where(p => p.Held > 0).Select(p => new Dictionary<string, object>
        {
            ["id"] = p.Id, ["units"] = p.Held, ["value"] = p.HeldValue, ["boughtFor"] = p.HeldCost,
        }).ToList();
        if (held.Count > 0) state["portfolio"] = held;
    }

    private static string GearText(PersonaGearView g, bool weapon) =>
        $"{g.Name} ({g.Attribute}, rank {g.Tier}, {(weapon ? "attack" : "defense")} +{g.BonusPct}%), you have {g.Stock} in stock at {g.Price}G each" +
        (g.WeakAttribute ? ", matches the weak attribute of the next stream" : "") + (g.Equipped ? " (equipped now)" : "");

    private static void AddShopQuestions(PersonaJevRequest req, PersonaProfile persona, PersonaDaySnapshot s)
    {
        var v = s.Shop;
        var n = s.NextStream;
        bool streamSoon = n != null && n.DaysUntil <= 0;

        // 勇者の装備: 配信日は装備を変えられないので、配信の前の営業日（次の行き先が配信）に聞く
        if (v.HeroAvailable && streamSoon)
        {
            static IEnumerable<PersonaGearView> Pick(List<PersonaGearView> list) => list
                .OrderByDescending(g => g.Equipped)
                .ThenByDescending(g => g.Stock > 0)
                .ThenByDescending(g => g.WeakAttribute)
                .ThenByDescending(g => g.Tier)
                .ThenByDescending(g => g.Stock)
                .Take(8);
            var weapons = Pick(v.Weapons).Select(g => new KeyValuePair<string, string>(g.Id, GearText(g, true))).ToList();
            if (weapons.Count >= 2)
                req.Questions["hero_weapon"] = PersonaJevQuestion.Choice(
                    $"Which weapon should the hero carry into the {n.Dungeon} stream? A stronger rank helps him win; a weapon of the weak attribute hits " +
                    "twice as hard. When the hero wins, the price of what he carries rises (about 1.8x), so carrying goods you hold a lot of pays more; " +
                    "if he loses, their price and demand fall by half.", weapons);
            var armors = Pick(v.Armors).Select(g => new KeyValuePair<string, string>(g.Id, GearText(g, false))).ToList();
            if (armors.Count >= 2)
                req.Questions["hero_armor"] = PersonaJevQuestion.Choice(
                    $"Which armor should the hero wear for the {n.Dungeon} stream? A higher rank gives more defense and HP. " +
                    "Its price also rises when he wins and falls when he loses.", armors);
            req.Questions["hero_tactics"] = PersonaJevQuestion.Choice(
                "Which tactics should the hero use? Tactics only change how the stream moves the prices of his weapon and armor, not whether he wins.",
                TacticsOptions);
        }

        // 店の改装
        if (v.ShopAvailable && v.ShopUpgradeCost > 0 && v.ShopLevel < v.ShopMaxLevel && v.ShopUpgradeCost <= s.Cash)
            req.Questions["shop_upgrade"] = PersonaJevQuestion.Noul(
                $"Should you pay {v.ShopUpgradeCost}G to remodel the shop from Lv{v.ShopLevel} to Lv{v.ShopLevel + 1}? " +
                $"You could display {v.KindsNow} -> {v.KindsNext} kinds of goods at once and place {v.SlotsNow} -> {v.SlotsNext} machines. " +
                "The remodel lasts until the end of this run.",
                "Remodel the shop now.", "Not now; keep the money.");

        // 鍛冶屋の開発
        if (v.SmithUpgradeCost > 0 && v.SmithLevel < v.SmithMaxLevel && v.SmithUpgradeCost <= s.Cash)
            req.Questions["smith_upgrade"] = PersonaJevQuestion.Noul(
                $"Should you pay {v.SmithUpgradeCost}G to develop the blacksmith from Lv{v.SmithLevel} to Lv{v.SmithLevel + 1}? " +
                $"{v.SmithUnlocks} new weapons and armor become available ({v.SmithUnlockMinPrice}-{v.SmithUnlockMaxPrice}G each now); " +
                "they can be stocked and also given to the hero. It lasts until the end of this run.",
                "Develop the blacksmith now.", "Not now; keep the money.");

        // 設備（マシン）
        if (v.ShopAvailable && v.Machines.Count > 0 && v.SlotsUsed < v.SlotsMax)
        {
            var opts = new List<KeyValuePair<string, string>> { new("none", "Place no machine today.") };
            foreach (var m in v.Machines.Where(m => m.Placed == 0 && m.Cost <= s.Cash).OrderBy(m => m.Cost))
                opts.Add(new(m.Id, $"{m.Name}: {m.EffectEn}, costs {m.Cost}G" +
                                   (m.DailyGold > 0 ? $" (pays back in about {(m.Cost + m.DailyGold - 1) / m.DailyGold} days)" : "")));
            if (opts.Count >= 2)
                req.Questions["machine"] = PersonaJevQuestion.Choice(
                    $"Should you buy and place a machine in the shop ({v.SlotsUsed}/{v.SlotsMax} slots used)? You can own one of each machine. " +
                    "Machines work every morning until the end of this run, can be upgraded later, and money and maker machines may break after a few days; " +
                    "removing one refunds half of what you paid.", opts);
        }

        // 設備の強化（置いてあるマシン 1 台。元が取れる日数が一番短いもの）
        var up = v.PlacedMachines.FirstOrDefault(m => m.Id == v.UpgradeTargetId);
        if (v.ShopAvailable && up != null && up.UpgradeCost > 0 && up.UpgradeCost <= s.Cash)
            req.Questions["machine_upgrade"] = PersonaJevQuestion.Noul(
                $"Should you pay {up.UpgradeCost}G to upgrade {up.Name} from Lv{up.Level} to Lv{up.Level + 1}? " +
                $"Its effect goes from {up.EffectEn} to {up.NextEffectEn}" +
                (up.AddedDailyValue > 0 ? $" (about +{Mathf.RoundToInt(up.AddedDailyValue)}G a day, pays back in about {Mathf.CeilToInt(up.UpgradeCost / up.AddedDailyValue)} days)" : "") +
                $". {v.RemainingMornings} mornings are left in this run. Upgrading also resets its wear, so it will not break for a while.",
                "Upgrade it now.", "Not now; keep the money.");

        // 設備の修理（壊れたお金・製造の設備。直すまで何も産まない）
        var rep = v.PlacedMachines.FirstOrDefault(m => m.Id == v.RepairTargetId);
        if (v.ShopAvailable && rep != null && rep.RepairCost <= s.Cash)
            req.Questions["machine_repair"] = PersonaJevQuestion.Noul(
                $"{rep.Name} (Lv{rep.Level}) is broken and makes nothing until repaired. Should you pay {rep.RepairCost}G to repair it? " +
                $"Working, it is worth about {Mathf.RoundToInt(rep.DailyValue)}G a day; {v.RemainingMornings} mornings are left in this run.",
                "Repair it now.", "Leave it broken.");

        // 情報屋の開発（取引所の商品が増え、債券を持てる口数が増える）
        if (v.BrokerAvailable && v.BrokerUpgradeCost > 0 && v.BrokerLevel < v.BrokerMaxLevel && v.BrokerUpgradeCost <= s.Cash)
            req.Questions["broker_upgrade"] = PersonaJevQuestion.Noul(
                $"Should you pay {v.BrokerUpgradeCost}G to develop the information broker from Lv{v.BrokerLevel} to Lv{v.BrokerLevel + 1}? " +
                (v.BrokerUnlocks > 0 ? $"{v.BrokerUnlocks} more funds or bonds become tradable at the exchange, and " : "") +
                "you may hold more units of each bond.",
                "Develop the information broker now.", "Not now; keep the money.");

        // 取引所（債券・投資信託）
        if (v.BrokerAvailable && v.Products.Count > 0)
        {
            var opts = new List<KeyValuePair<string, string>> { new("none", "Do not trade at the exchange today.") };
            foreach (var p in v.Products.Where(p => p.UnitPrice > 0 && p.UnitPrice <= s.Cash))
            {
                if (p.Bond)
                {
                    if (p.RemainingUnits == 0) continue;
                    opts.Add(new(p.Id, $"Buy {p.Name}: a bond, {p.UnitPrice}G per unit, pays +{p.RatePct}% at maturity in {p.MaturityDays} days; " +
                                       "the money is locked until then" + (p.RemainingUnits > 0 ? $", up to {p.RemainingUnits} more units" : "") + "."));
                }
                else
                {
                    opts.Add(new(p.Id, $"Buy {p.Name}: a fund tracking the prices of {p.Tracks}, now {p.UnitPrice}G per unit " +
                                       $"({p.Change1d * 100f:+0.0;-0.0;0}% since yesterday), {p.FeePct}% fee when buying and selling, sell any day."));
                }
            }
            foreach (var p in v.Products.Where(p => !p.Bond && p.Held > 0))
                opts.Add(new("sell_" + p.Id, $"Sell all {p.Held} units of {p.Name} (worth about {p.HeldValue}G now, bought for about {p.HeldCost}G)."));
            if (opts.Count >= 2)
            {
                req.Questions["finance"] = PersonaJevQuestion.Choice(
                    "Should you put money into the exchange today? Bonds are safe and pay a fixed interest at maturity; " +
                    "funds follow the market prices of goods and can rise or fall. The money is not available for stock while invested.", opts);
                req.Questions["finance_share"] = PersonaJevQuestion.Score(
                    "If you buy a bond or a fund today, how much of your spendable cash should go into it?", FinanceShareLevels);
            }
        }
    }

    public static object BuildStreamState(PersonaProfile persona, IList<string> lessons, PersonaStreamSnapshot s) =>
        new Dictionary<string, object>
        {
            ["persona"] = persona.Preamble,
            ["lessonsFromPastRuns"] = lessons ?? new List<string>(),
            ["context"] = Context,
            ["day"] = s.Day,
            ["stream"] = new Dictionary<string, object>
            {
                ["cashAvailable"] = s.Balance,
                ["budgetForSuperChats"] = s.SpendBudget,
                ["heroHpPct"] = s.HeroHpPct,
                ["monsters"] = s.Monsters,
                ["bossAlive"] = s.BossAlive,
            },
            ["superChatPrices"] = s.Interventions,
        };

    private static readonly Dictionary<string, string> InterventionText = new()
    {
        ["heal"] = "Heal the hero (hero side).",
        ["skill"] = "Hero skill attack on one monster (hero side).",
        ["special"] = "Hero special move, strong attack (hero side).",
        ["trap"] = "Trap: damage the hero by 30% of max HP, ignores defense (dungeon side).",
        ["curse"] = "Curse: halve the hero's defense for several hits (dungeon side).",
        ["reinforce"] = "Call monster reinforcements (dungeon side).",
        ["bossbuff"] = "Boss buff: the boss hits harder and takes less damage, once per stream (dungeon side).",
    };

    public static PersonaJevRequest BuildStreamRequest(PersonaProfile persona, string model, IList<string> lessons, PersonaStreamSnapshot s)
    {
        var req = new PersonaJevRequest { Model = model, State = BuildStreamState(persona, lessons, s) };
        req.Questions["stream_side"] = PersonaJevQuestion.Choice(
            "Which side do you back in this stream?",
            new List<KeyValuePair<string, string>>
            {
                new("hero", "Back the hero: you want the hero to clear the dungeon."),
                new("demon", "Back the dungeon: if the hero loses you earn the defeat reward."),
                new("none", "Back nobody: just sell goods and keep the cash."),
            });

        var options = new List<KeyValuePair<string, string>> { new("none", "Do not pay for any super chat.") };
        foreach (var kv in s.Interventions.Where(kv => kv.Value > 0 && kv.Value <= s.SpendBudget).OrderBy(kv => kv.Value))
            options.Add(new(kv.Key, $"Pay {kv.Value}G. " + (InterventionText.TryGetValue(kv.Key, out var t) ? t : kv.Key)));
        req.Questions["intervene"] = PersonaJevQuestion.Choice("Which super chat is most worth paying for in this stream?", options);

        req.Questions["intensity"] = PersonaJevQuestion.Score("How often should you pay for super chats during this stream?", IntensityLevels);
        return req;
    }

    public static object BuildVillageState(PersonaProfile persona, IList<string> lessons, PersonaVillageSnapshot v)
    {
        var state = new Dictionary<string, object>
        {
            ["persona"] = string.IsNullOrEmpty(persona.VillagePreamble) ? persona.Preamble : persona.Preamble + " " + persona.VillagePreamble,
            ["lessonsFromPastRuns"] = lessons ?? new List<string>(),
            ["context"] = VillageContext,
            ["runsFinished"] = v.Runs,
            ["villageFunds"] = v.Funds,
            ["villageRank"] = new Dictionary<string, object> { ["rank"] = v.Rank, ["top"] = v.TopRank, ["name"] = v.RankName ?? "" },
            ["totalFacilityLevel"] = v.VillageLevel,
            ["maxTotalFacilityLevel"] = v.MaxVillageLevel,
            ["lastRun"] = new Dictionary<string, object> { ["profit"] = v.LastRunProfit, ["cleared"] = v.LastRunCleared },
            ["facilities"] = v.Facilities.Select(f => new Dictionary<string, object>
            {
                ["id"] = f.Id,
                ["name"] = f.Name,
                ["level"] = f.Level,
                ["maxLevel"] = f.MaxLevel,
                ["nextLevelCost"] = f.NextCost,
                ["nextLevelEffect"] = f.NextEffect ?? "",
                ["canInvestNow"] = f.CanInvest,
                ["blockedBecause"] = f.CanInvest ? "" : f.BlockedReason ?? "",
            }).ToList(),
        };
        int keep = persona.VillageReserve(v.Funds);
        if (keep > 0) state["villageFundsToKeep"] = keep;
        return state;
    }

    /// <summary>村での判断。予算（score）と、いま投資できる施設ごとの noul（質問キー invest_&lt;施設ID&gt;）。投資できる施設が無ければ null。</summary>
    public static PersonaJevRequest BuildVillageRequest(PersonaProfile persona, string model, IList<string> lessons, PersonaVillageSnapshot v)
    {
        var investable = v.Facilities.Where(f => f.CanInvest && f.NextCost > 0).ToList();
        if (investable.Count == 0) return null;
        var req = new PersonaJevRequest { Model = model, State = BuildVillageState(persona, lessons, v) };
        req.Questions["village_budget"] = PersonaJevQuestion.Score(
            $"How much of the {v.Funds}G village funds should you invest on this visit?", VillageBudgetLevels);
        foreach (var f in investable)
            req.Questions["invest_" + f.Id] = PersonaJevQuestion.Noul(
                $"Should you invest {f.NextCost}G to raise the {f.Name} ({f.Id}) from Lv{f.Level} to Lv{f.Level + 1}? Next level: {f.NextEffect}",
                "Invest in it now.", "Do not invest in it on this visit.");
        return req;
    }

    // =====================================================
    // 出店準備（スターターレリック）
    // =====================================================

    /// <summary>派手・当たり外れの大きいレリック（バズ・イベント・値動きの幅・防衛報酬。ずんだもんが飛びつく）。</summary>
    public static readonly HashSet<string> FlashyRelics = new()
    {
        "relic_gossip_notes", "relic_fairy_coin", "relic_trader_creed", "relic_demon_pact", "relic_charisma", "relic_demon_banner", "relic_star_chart",
    };

    /// <summary>手堅いレリック（毎朝のお金・仕入れ値・売上・需要の底・納付金・利息と配当。めたんの好み）。</summary>
    public static readonly HashSet<string> SteadyRelics = new()
    {
        "relic_piggy", "relic_wholesaler_pass", "relic_signboard", "relic_charm", "relic_collector_secret", "relic_seal", "relic_guild_license",
        "relic_compound_tome", "relic_dividend_book",
    };

    /// <summary>
    /// レリックの効果の英語（Jev は英語の方が精度が高い）。ゲームの数字（RelicDefinition の modifiers）をスプレッドシートで変えたら、ここも直す。
    /// 載っていないレリックはゲームの表記（日本語）のまま送る。
    /// </summary>
    public static readonly Dictionary<string, string> RelicEffectEn = new()
    {
        ["relic_piggy"] = "gives 3,000G every morning",
        ["relic_fairy_coin"] = "+50% gold from events",
        ["relic_gossip_notes"] = "+5% chance that a stream goes viral",
        ["relic_showcase"] = "+1 kind of goods on display at once",
        ["relic_signboard"] = "+10% shop sales",
        ["relic_wholesaler_pass"] = "-5% purchase prices",
        ["relic_blueprint"] = "+500G daily production budget for crafting machines",
        ["relic_charm"] = "+8% demand floor for all goods",
        ["relic_collector_secret"] = "-10% debt payments",
        ["relic_compound_tome"] = "+50% bond interest",
        ["relic_display_manual"] = "+2 kinds of goods on display at once",
        ["relic_dividend_book"] = "+50% dividend income",
        ["relic_trader_creed"] = "selling prices swing 15% wider, both up and down",
        ["relic_demon_pact"] = "+50% defeat reward when the hero loses",
        ["relic_charisma"] = "+12% chance that a stream goes viral and +5% shop sales",
        ["relic_demon_banner"] = "+30% defeat reward when the hero loses, but the hero is 15% weaker",
        ["relic_dept_creed"] = "+3 goods on display at once and +5% shop sales",
        ["relic_furnace_core"] = "+1,200G daily production budget for crafting machines",
        ["relic_guild_license"] = "-15% purchase prices",
        ["relic_seal"] = "-20% debt payments",
        ["relic_star_chart"] = "doubles the gold from events",
    };

    public const string StarterContext =
        "Before each run you pick one starter relic in the preparation screen. It works from the first shop day until the end of the run, " +
        "and you cannot change it during the run. Goal: finish the run with as much money as possible.";

    /// <summary>
    /// 出店準備での判断。スターターレリックの choice（質問キー starter_relic、選択肢キーはレリック ID。画面の並び順）。
    /// 「選ばない」はゲームでは選べるが、レリックはただでもらえるので選択肢に入れない（勇者の装備と同じ）。候補が 2 つ未満なら null。
    /// </summary>
    public static PersonaJevRequest BuildStarterRelicRequest(PersonaProfile persona, string model, IList<string> lessons, PersonaVillageSnapshot v,
        IList<AutoPlayStarterRelicChoice> choices)
    {
        var opts = (choices ?? new List<AutoPlayStarterRelicChoice>())
            .Where(c => !string.IsNullOrEmpty(c.Id))
            .Select(c => new KeyValuePair<string, string>(c.Id, StarterRelicText(c)))
            .ToList();
        if (opts.Count < 2) return null;
        var state = new Dictionary<string, object>
        {
            ["persona"] = persona.Preamble,
            ["lessonsFromPastRuns"] = lessons ?? new List<string>(),
            ["context"] = Context + " " + StarterContext,
            ["runsFinished"] = v?.Runs ?? 0,
        };
        if (v != null)
        {
            state["lastRun"] = new Dictionary<string, object> { ["profit"] = v.LastRunProfit, ["cleared"] = v.LastRunCleared };
            if (v.MaxVillageLevel > 0)
                state["village"] = new Dictionary<string, object> { ["totalFacilityLevel"] = v.VillageLevel, ["maxTotalFacilityLevel"] = v.MaxVillageLevel };
        }
        var req = new PersonaJevRequest { Model = model, State = state };
        req.Questions["starter_relic"] = PersonaJevQuestion.Choice(
            "Which starter relic do you take into this run?", opts);
        return req;
    }

    private static string StarterRelicText(AutoPlayStarterRelicChoice c)
    {
        string effect = RelicEffectEn.TryGetValue(c.Id, out var en) ? en : c.Description ?? "";
        return $"{c.Name} ({c.Rarity.ToString().ToLowerInvariant()}): {effect}";
    }

    /// <summary>
    /// 似た場面の想起（PersonaRecallStore）を state.similarPastSituations に載せる（空なら載せない）。
    /// 質問が決まってから選ぶので、組み立てた後に足す。
    /// </summary>
    public static void AttachSimilarPast(PersonaJevRequest req, IList<string> lines)
    {
        if (req?.State is not IDictionary<string, object> state || lines == null || lines.Count == 0) return;
        state["similarPastSituations"] = new List<string>(lines);
    }

    private static string PctChange(int price, int previous) =>
        previous > 0 ? $"{(price - previous) * 100f / previous:+0.0;-0.0;0}%" : "0%";

    private static double Round2(float v) => System.Math.Round(v, 2);
}
#endif

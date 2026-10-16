#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>配信中の棚（売り場 / 在庫）1スロットの情報。</summary>
public readonly struct AutoPlayShelfSlot
{
    /// <summary>売り場なら売り場の番号、在庫なら在庫の番号（0 始まり）。</summary>
    public readonly int Index;
    public readonly string ItemId;   // 空スロットは null
    public readonly string ItemName;
    public readonly int Stock;
    public readonly int Price;
    public readonly float Demand;

    public AutoPlayShelfSlot(int index, string itemId, string itemName, int stock, int price, float demand)
    {
        Index = index;
        ItemId = itemId;
        ItemName = itemName;
        Stock = stock;
        Price = price;
        Demand = demand;
    }

    public bool IsEmpty => string.IsNullOrEmpty(ItemId);
    public bool HasStock => !IsEmpty && Stock > 0;
}

/// <summary>配信中の敵1体の情報。</summary>
public readonly struct AutoPlayEnemyInfo
{
    public readonly int Index;
    public readonly float Hp01;
    public readonly bool IsBoss;

    public AutoPlayEnemyInfo(int index, float hp01, bool isBoss)
    {
        Index = index;
        Hp01 = hp01;
        IsBoss = isBoss;
    }
}

/// <summary>配信中の介入（スパチャ指示）1回分。None は何もしない。</summary>
public readonly struct AutoPlayStreamIntervention
{
    public readonly bool HasValue;
    public readonly bool IsHeroSide;
    public readonly HeroInterventionType Hero;
    public readonly DungeonInterventionType Dungeon;

    private AutoPlayStreamIntervention(bool isHero, HeroInterventionType hero, DungeonInterventionType dungeon)
    {
        HasValue = true;
        IsHeroSide = isHero;
        Hero = hero;
        Dungeon = dungeon;
    }

    public static AutoPlayStreamIntervention None => default;
    public static AutoPlayStreamIntervention Of(HeroInterventionType t) => new(true, t, default);
    public static AutoPlayStreamIntervention Of(DungeonInterventionType t) => new(false, default, t);

    public override string ToString() => !HasValue ? "なし" : IsHeroSide ? Hero.ToString() : Dungeon.ToString();
}

/// <summary>配信中（FightScene の配信パネル）の判断材料。</summary>
public sealed class AutoPlayStreamContext
{
    /// <summary>介入・補充に使える残高（所持金＋配信中売上−補充費−介入支出。InterventionPresenter.AvailableBalance）。</summary>
    public int AvailableBalance;
    /// <summary>この配信を自動操作し始めたときの残高。</summary>
    public int StartBalance;
    /// <summary>この配信で AutoPlay が介入に使った額（目安）。</summary>
    public int SpentThisStream;
    /// <summary>介入に使ってよい残り額（StartBalance × AutoPlaySettings.StreamSpendRatio − SpentThisStream）。</summary>
    public int SpendBudgetLeft;
    /// <summary>勇者の HP 割合（不明なら -1）。</summary>
    public float HeroHp01 = -1f;
    /// <summary>生きている敵。</summary>
    public readonly List<AutoPlayEnemyInfo> Enemies = new();
    /// <summary>介入ボタンの状態（取れなかった種類は入らない）。</summary>
    public readonly Dictionary<HeroInterventionType, InterventionButtonState> HeroButtons = new();
    public readonly Dictionary<DungeonInterventionType, InterventionButtonState> DungeonButtons = new();
    /// <summary>売り場・在庫。</summary>
    public readonly List<AutoPlayShelfSlot> SellSlots = new();
    public readonly List<AutoPlayShelfSlot> InventorySlots = new();
    /// <summary>この配信で AutoPlay が行った操作の回数。</summary>
    public int ActionCount;
    public System.Random Random;

    public bool BossAlive => Enemies.Exists(e => e.IsBoss);

    /// <summary>押せて、かつ使ってよい残り額に収まるか。</summary>
    public bool CanUse(HeroInterventionType t) =>
        HeroButtons.TryGetValue(t, out var s) && s.Interactable && s.Price <= SpendBudgetLeft;

    public bool CanUse(DungeonInterventionType t) =>
        DungeonButtons.TryGetValue(t, out var s) && s.Interactable && s.Price <= SpendBudgetLeft;

    public int PriceOf(HeroInterventionType t) => HeroButtons.TryGetValue(t, out var s) ? s.Price : int.MaxValue;
    public int PriceOf(DungeonInterventionType t) => DungeonButtons.TryGetValue(t, out var s) ? s.Price : int.MaxValue;

    public float NextFloat() => Random != null ? (float)Random.NextDouble() : UnityEngine.Random.value;
    public int Next(int min, int maxExclusive) => Random != null ? Random.Next(min, maxExclusive) : UnityEngine.Random.Range(min, maxExclusive);
}

/// <summary>戦略に渡す判断材料（取れないものは 0 / -1）。</summary>
public sealed class AutoPlayContext
{
    /// <summary>現在のターン（不明なら -1）。</summary>
    public int Turn = -1;
    /// <summary>所持金。</summary>
    public int Money;
    /// <summary>2ターン以内に迫った納税額（残しておくべき額）。無ければ 0。</summary>
    public int TaxReserve;
    public int BlacksmithLevel;
    public int ShopLevel;
    /// <summary>配信日の朝（配信前の寄り道中）か。</summary>
    public bool AwaitingStream;
    /// <summary>戦略用の乱数（AutoPlaySettings.RandomSeed で初期化）。</summary>
    public System.Random Random;

    /// <summary>納税準備金を除いた、使ってよい所持金。</summary>
    public int Spendable => Mathf.Max(0, Money - TaxReserve);
}

/// <summary>
/// オートプレイの判断（何を買う・並べる・選ぶか）。差し替えは AutoPlay.Strategy に代入する。
/// 「どのボタンをどう押すか」は AutoPlayController / DebugAutoPlayer 側が受け持ち、ここは判断だけを返す。
/// </summary>
public interface IAutoPlayStrategy
{
    /// <summary>表示名（デバッグメニュー・ログ用）。</summary>
    string Name { get; }

    /// <summary>仕入れフェーズで鍛冶屋のお任せ仕入れをするか。</summary>
    bool ShouldProcure(AutoPlayContext ctx);

    /// <summary>お任せ仕入れの予算（G）。0〜所持金にクランプされる。</summary>
    int ChooseProcurementBudget(AutoPlayContext ctx);

    /// <summary>お任せ仕入れの方針。</summary>
    AutoBuyStrategy ChooseAutoBuyStrategy(AutoPlayContext ctx);

    /// <summary>陳列フェーズでオート陳列をするか。</summary>
    bool ShouldDisplay(AutoPlayContext ctx);

    /// <summary>配信日の朝に「鍛冶屋へ寄る」か（false なら「このまま配信へ」）。</summary>
    bool ShouldVisitBlacksmithBeforeStream(AutoPlayContext ctx);

    /// <summary>レリック3択で選ぶ番号（0 始まり）。</summary>
    int ChooseRelic(AutoPlayContext ctx, int choiceCount);

    /// <summary>会話の選択肢で選ぶ番号（0 始まり）。</summary>
    int ChooseScenarioSelection(AutoPlayContext ctx, int choiceCount);

    /// <summary>出店準備で村資金を上限まで持ち込むか。</summary>
    bool ShouldCarryMaxFunds(AutoPlayContext ctx);

    /// <summary>出店準備で選ぶスターターレリックの番号（-1 = 選ばない）。</summary>
    int ChooseStarterRelic(AutoPlayContext ctx, int choiceCount);

    // ---- 配信中（StreamActionInterval ごとに 1 つ聞かれる） ----

    /// <summary>介入（勇者側 回復/スキル/必殺、ダンジョン側 罠/呪い/増援/ボス強化）をするか。None で何もしない。</summary>
    AutoPlayStreamIntervention ChooseStreamIntervention(AutoPlayStreamContext ctx);

    /// <summary>スキル・必殺の前にタップして狙う敵（ctx.Enemies の番号。-1 = 指定しない）。</summary>
    int ChooseEnemyTarget(AutoPlayStreamContext ctx, AutoPlayStreamIntervention intervention);

    /// <summary>在庫切れの補充数（0 = 補充しない）。maxQuantity は残高・在庫枠から買える上限、defaultQuantity はポップアップの既定値。</summary>
    int ChooseRestockQuantity(AutoPlayStreamContext ctx, string itemName, int maxQuantity, int defaultQuantity);

    /// <summary>在庫から売り場へ入れ替えるか（ドラッグ操作）。true なら inventoryIndex → sellIndex。</summary>
    bool ChooseShelfSwap(AutoPlayStreamContext ctx, out int inventoryIndex, out int sellIndex);

    /// <summary>売り場の商品の値動きグラフを見るか（見た目用。true なら sellIndex を開いて少しして閉じる）。</summary>
    bool ChoosePriceGraphPeek(AutoPlayStreamContext ctx, out int sellIndex);

    // ---- 配信前（次の配信が近い日の仕入れ） ----

    /// <summary>次の配信先の弱点が分からないとき、情報屋でダンジョン情報を買うか（ctx.IntelCost を払う）。</summary>
    bool ShouldBuyDungeonIntel(AutoPlayStreamPrepContext ctx);

    /// <summary>配信前の仕入れの配分（ダンジョン特化で買う額と、残りを従来どおり買う額）。</summary>
    AutoPlayStreamPurchasePlan ChooseStreamPurchase(AutoPlayStreamPrepContext ctx);
}

/// <summary>配信前の仕入れ判断の材料。</summary>
public sealed class AutoPlayStreamPrepContext
{
    public DungeonName Dungeon;
    public string DungeonLabel;
    /// <summary>次の配信までの営業日数（0 = 次の行き先が配信。配信日の朝の寄り道中も 0）。</summary>
    public int TurnsUntilStream;
    public bool AwaitingStream;
    /// <summary>弱点属性を知っているか（情報屋で情報を買った・デバッグで全開示）。</summary>
    public bool WeaknessKnown;
    /// <summary>弱点属性（知らなければ null）。ここに一致する属性の武具が配信中に値上がり・需要増しやすい。</summary>
    public ItemTypeData.ItemAttribute? Weakness;
    /// <summary>情報屋の情報料（売っていない・購入済みなら -1）。</summary>
    public int IntelCost = -1;
    public int Money;
    /// <summary>この仕入れに使う予算（戦略の ChooseProcurementBudget の結果）。</summary>
    public int Budget;
    /// <summary>
    /// 配信先に出る魔物の属性（現在のダンジョンLvの出現リスト。重複あり＝出現数の重み）。
    /// ゲーム側（品出しのオート選択）と同じく、ダンジョン情報を買うまでは空（使わない）。
    /// </summary>
    public readonly List<ElementType> EnemyElements = new();
    /// <summary>
    /// 有利な属性（弱点属性が先頭、続いて魔物に対して ElementAttributeMapper.IsEffective になる属性。有利が不利より多いもの）。
    /// 配信中に魔物を倒すたび、この属性の商品が値上がり・需要増になる。
    /// </summary>
    public readonly List<ItemTypeData.ItemAttribute> EffectiveAttributes = new();
    /// <summary>不利な属性（魔物に対して IsWeak が IsEffective より多い）。値下がりするので避ける。</summary>
    public readonly List<ItemTypeData.ItemAttribute> WeakAttributes = new();
    /// <summary>有利な属性（EffectiveAttributes）で、いま買える武具。弱点属性 → ほかの有利属性、それぞれ期待収益の高い順。</summary>
    public readonly List<AutoPlayShopCandidate> Matching = new();
    public System.Random Random;

    public float NextFloat() => Random != null ? (float)Random.NextDouble() : UnityEngine.Random.value;
    public int MatchingWeaponCount => Matching.FindAll(c => c.IsWeapon).Count;
    public bool IsWeakness(ItemTypeData.ItemAttribute a) => Weakness.HasValue && Weakness.Value == a;
}

/// <summary>鍛冶屋で買える商品 1 つ。</summary>
public readonly struct AutoPlayShopCandidate
{
    public readonly string ItemId;
    public readonly string ItemName;
    public readonly bool IsWeapon;
    public readonly ItemTypeData.ItemAttribute Attribute;
    public readonly int UnitPrice;
    /// <summary>在庫の空き（これ以上は買えない）。</summary>
    public readonly int RemainToMax;
    public readonly float ExpectedRevenue;

    public AutoPlayShopCandidate(string itemId, string itemName, bool isWeapon, ItemTypeData.ItemAttribute attribute,
        int unitPrice, int remainToMax, float expectedRevenue)
    {
        ItemId = itemId;
        ItemName = itemName;
        IsWeapon = isWeapon;
        Attribute = attribute;
        UnitPrice = unitPrice;
        RemainToMax = remainToMax;
        ExpectedRevenue = expectedRevenue;
    }
}

/// <summary>
/// 配信前の仕入れ配分。
/// 1. WeaponOrders を鍛冶屋の商品一覧から個別に買う（行を選ぶ → 数量 → 購入）。
/// 2. FocusBudget の残りを「次ダンジョン特化」のお任せ購入（弱点属性の武具を優先）。
/// 3. 予算の残りを RegularStrategy のお任せ購入。
/// </summary>
public struct AutoPlayStreamPurchasePlan
{
    public List<(string itemId, int quantity)> WeaponOrders;
    public int FocusBudget;
    public AutoBuyStrategy RegularStrategy;
    /// <summary>ログに出す理由（例:「火山 → 弱点:水 の武器を優先」）。</summary>
    public string Reason;
}

/// <summary>
/// 標準: お任せ仕入れ（おすすめ順・納税準備金を残して全額）＋オート陳列。選択肢はすべて先頭。
/// 既存のデバッグメニュー「オート」タブの「標準」戦略と同じ動き。
/// </summary>
public class DefaultAutoPlayStrategy : IAutoPlayStrategy
{
    public virtual string Name => "標準（お任せ仕入れ＋オート陳列）";

    /// <summary>予算＝使ってよい所持金に対する割合（0〜100）。</summary>
    public int BudgetPercent = 100;

    public virtual bool ShouldProcure(AutoPlayContext ctx) => true;

    /// <summary>
    /// 配信日の朝（配信前に鍛冶屋へ寄る時）に残しておく額。配信中の介入（回復・罠などは 1 万G〜）に使う。
    /// 使ってよい所持金の半分が上限。
    /// </summary>
    public int StreamReserveGold = 40000;

    public virtual int ChooseProcurementBudget(AutoPlayContext ctx) =>
        (int)((long)SpendableAfterStreamReserve(ctx) * Mathf.Clamp(BudgetPercent, 0, 100) / 100);

    /// <summary>配信日の朝なら StreamReserveGold（最大で半分）を残した額。</summary>
    protected int SpendableAfterStreamReserve(AutoPlayContext ctx)
    {
        int spendable = ctx.Spendable;
        if (!ctx.AwaitingStream) return spendable;
        return spendable - Mathf.Min(Mathf.Max(0, StreamReserveGold), spendable / 2);
    }

    public virtual AutoBuyStrategy ChooseAutoBuyStrategy(AutoPlayContext ctx) => AutoBuyStrategy.Recommend;

    public virtual bool ShouldDisplay(AutoPlayContext ctx) => true;

    public virtual bool ShouldVisitBlacksmithBeforeStream(AutoPlayContext ctx) => true;

    public virtual int ChooseRelic(AutoPlayContext ctx, int choiceCount) => 0;

    public virtual int ChooseScenarioSelection(AutoPlayContext ctx, int choiceCount) => 0;

    public virtual bool ShouldCarryMaxFunds(AutoPlayContext ctx) => true;

    public virtual int ChooseStarterRelic(AutoPlayContext ctx, int choiceCount) => choiceCount > 0 ? 0 : -1;

    // ---- 配信中 ----

    /// <summary>
    /// 標準の介入: 勇者が危ない（HP35%未満）なら回復、ボスがいて余裕があればスキル、
    /// 勇者に余裕がある（HP75%超）ときは安い罠でたまに配信を延ばす（販売機会↑）。
    /// どれも「使ってよい残り額」（StreamSpendRatio）の範囲だけ。
    /// </summary>
    public virtual AutoPlayStreamIntervention ChooseStreamIntervention(AutoPlayStreamContext ctx)
    {
        if (ctx.Enemies.Count == 0) return AutoPlayStreamIntervention.None;

        if (ctx.HeroHp01 >= 0f && ctx.HeroHp01 < 0.35f && ctx.CanUse(HeroInterventionType.Heal))
            return AutoPlayStreamIntervention.Of(HeroInterventionType.Heal);

        if (ctx.BossAlive && ctx.CanUse(HeroInterventionType.Skill)
            && ctx.SpendBudgetLeft >= ctx.PriceOf(HeroInterventionType.Skill) * 2)
            return AutoPlayStreamIntervention.Of(HeroInterventionType.Skill);

        if (ctx.HeroHp01 > 0.75f && ctx.CanUse(DungeonInterventionType.Trap) && ctx.NextFloat() < 0.35f)
            return AutoPlayStreamIntervention.Of(DungeonInterventionType.Trap);

        return AutoPlayStreamIntervention.None;
    }

    /// <summary>ボスがいればボス、いなければ一番弱っている敵。</summary>
    public virtual int ChooseEnemyTarget(AutoPlayStreamContext ctx, AutoPlayStreamIntervention intervention)
    {
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < ctx.Enemies.Count; i++)
        {
            var e = ctx.Enemies[i];
            float score = e.IsBoss ? -1f : e.Hp01;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    /// <summary>補充は売れ残りが返金されるので、既定数（10個・買える範囲）を補充する。</summary>
    public virtual int ChooseRestockQuantity(AutoPlayStreamContext ctx, string itemName, int maxQuantity, int defaultQuantity) =>
        Mathf.Clamp(defaultQuantity, 0, Mathf.Max(0, maxQuantity));

    /// <summary>売り場に空き・売り切れがあれば、在庫の中で在庫×価格が一番大きい商品を入れる。</summary>
    public virtual bool ChooseShelfSwap(AutoPlayStreamContext ctx, out int inventoryIndex, out int sellIndex)
    {
        inventoryIndex = -1;
        sellIndex = -1;
        foreach (var s in ctx.SellSlots)
        {
            if (s.HasStock) continue;
            sellIndex = s.Index;
            break;
        }
        if (sellIndex < 0) return false;

        long bestValue = 0;
        foreach (var inv in ctx.InventorySlots)
        {
            if (!inv.HasStock) continue;
            long value = (long)inv.Stock * Mathf.Max(1, inv.Price);
            if (value > bestValue) { bestValue = value; inventoryIndex = inv.Index; }
        }
        return inventoryIndex >= 0;
    }

    /// <summary>手が空いたときにたまに値動きを確認する（15%）。</summary>
    public virtual bool ChoosePriceGraphPeek(AutoPlayStreamContext ctx, out int sellIndex)
    {
        sellIndex = -1;
        if (ctx.NextFloat() >= PriceGraphPeekChance) return false;
        var candidates = ctx.SellSlots.FindAll(s => s.HasStock);
        if (candidates.Count == 0) return false;
        sellIndex = candidates[ctx.Next(0, candidates.Count)].Index;
        return true;
    }

    protected virtual float PriceGraphPeekChance => 0.15f;

    // ---- 配信前 ----

    /// <summary>次の配信先の弱点が分からず、情報料が所持金の 25% 以内なら情報屋で買う。</summary>
    public virtual bool ShouldBuyDungeonIntel(AutoPlayStreamPrepContext ctx) =>
        !ctx.WeaknessKnown && ctx.IntelCost > 0 && ctx.IntelCost <= ctx.Money / 4;

    /// <summary>ダンジョン特化に予算の何割を回すか（弱点が分かっていて対象の武具があるときだけ）。</summary>
    public float StreamFocusShare = 0.6f;

    /// <summary>弱点が分かっていて対象の武具があれば、予算の StreamFocusShare を「次ダンジョン特化」、残りをおすすめ順で買う。</summary>
    public virtual AutoPlayStreamPurchasePlan ChooseStreamPurchase(AutoPlayStreamPrepContext ctx) =>
        PlanWithShare(ctx, StreamFocusShare);

    protected static AutoPlayStreamPurchasePlan PlanWithShare(AutoPlayStreamPrepContext ctx, float share)
    {
        var plan = new AutoPlayStreamPurchasePlan { RegularStrategy = AutoBuyStrategy.Recommend };
        if (!ctx.WeaknessKnown || !ctx.Weakness.HasValue)
        {
            plan.Reason = $"{ctx.DungeonLabel}: 弱点が不明なのでいつもどおり仕入れ";
            return plan;
        }
        string attr = AutoPlayController.AttributeLabel(ctx.Weakness.Value);
        string affinity = DescribeAffinity(ctx);
        if (ctx.Matching.Count == 0)
        {
            plan.Reason = $"{affinity}: 有利な属性の武具が買えないのでいつもどおり仕入れ";
            return plan;
        }
        plan.FocusBudget = Mathf.Clamp(Mathf.RoundToInt(ctx.Budget * Mathf.Clamp01(share)), 0, ctx.Budget);

        // 武器の選び方: 弱点属性の武器を最大 MaxPrimaryWeapons 種 → ほかの有利属性の武器を最大 MaxSecondaryWeapons 種
        // （片方が足りなければもう片方で埋めて合計 MaxFocusWeapons 種まで）。不利属性は Matching に入っていない。
        // ダンジョン向け予算を種類数で均等に割って買う。
        var weapons = ctx.Matching.FindAll(c => c.IsWeapon && c.UnitPrice > 0 && c.RemainToMax > 0);
        var primary = weapons.FindAll(c => ctx.IsWeakness(c.Attribute));
        var secondary = weapons.FindAll(c => !ctx.IsWeakness(c.Attribute));
        var picks = new List<AutoPlayShopCandidate>();
        picks.AddRange(primary.GetRange(0, Mathf.Min(MaxPrimaryWeapons, primary.Count)));
        picks.AddRange(secondary.GetRange(0, Mathf.Min(MaxSecondaryWeapons, secondary.Count)));
        foreach (var c in primary) { if (picks.Count >= MaxFocusWeapons) break; if (!picks.Contains(c)) picks.Add(c); }
        foreach (var c in secondary) { if (picks.Count >= MaxFocusWeapons) break; if (!picks.Contains(c)) picks.Add(c); }

        plan.WeaponOrders = new List<(string, int)>();
        var names = new List<string>();
        if (picks.Count > 0)
        {
            int per = plan.FocusBudget / picks.Count;
            foreach (var w in picks)
            {
                int qty = Mathf.Min(w.RemainToMax, per / w.UnitPrice);
                if (qty <= 0) continue;
                plan.WeaponOrders.Add((w.ItemId, qty));
                names.Add($"{w.ItemName}({AutoPlayController.AttributeLabel(w.Attribute)})×{qty}");
            }
        }

        plan.Reason = names.Count > 0
            ? $"{affinity} → 有利属性の武器を優先: {string.Join("・", names)}（ダンジョン向け予算 {plan.FocusBudget:N0}G、余りは{attr}属性の防具など）"
            : $"{affinity} → 有利属性の武器が無い/買えないので{attr}属性の武具を優先（予算 {plan.FocusBudget:N0}G）";
        return plan;
    }

    /// <summary>「深緑の獣林（木）: 有利=火 / 不利=水」の形。</summary>
    protected static string DescribeAffinity(AutoPlayStreamPrepContext ctx)
    {
        var enemies = new List<string>();
        foreach (var e in ctx.EnemyElements)
        {
            string label = AutoPlayController.ElementLabel(e);
            if (!enemies.Contains(label)) enemies.Add(label);
        }
        string head = enemies.Count > 0 ? $"{ctx.DungeonLabel}（{string.Join("・", enemies)}）" : ctx.DungeonLabel;
        var eff = ctx.EffectiveAttributes.ConvertAll(AutoPlayController.AttributeLabel);
        var weak = ctx.WeakAttributes.ConvertAll(AutoPlayController.AttributeLabel);
        string s = $"{head}: 有利={string.Join(",", eff)}";
        if (weak.Count > 0) s += $" / 不利={string.Join(",", weak)}";
        return s;
    }

    /// <summary>ダンジョン向けに個別に買う武器の種類数の上限（弱点属性 / ほかの有利属性 / 合計）。</summary>
    protected const int MaxPrimaryWeapons = 2;
    protected const int MaxSecondaryWeapons = 2;
    protected const int MaxFocusWeapons = 4;
}

/// <summary>
/// ランダム: 予算 50〜100%・仕入れ方針・レリック・会話の選択肢・スターターレリックを乱数で選ぶ。
/// 動画のバリエーション作りや、いろいろな分岐を踏む動作確認向け。
/// </summary>
public class RandomAutoPlayStrategy : DefaultAutoPlayStrategy
{
    public override string Name => "ランダム";

    private static readonly AutoBuyStrategy[] BuyStrategies =
        (AutoBuyStrategy[])Enum.GetValues(typeof(AutoBuyStrategy));

    public override int ChooseProcurementBudget(AutoPlayContext ctx)
    {
        int percent = Next(ctx, 50, 101);
        return (int)((long)SpendableAfterStreamReserve(ctx) * percent / 100);
    }

    public override AutoBuyStrategy ChooseAutoBuyStrategy(AutoPlayContext ctx) =>
        BuyStrategies.Length > 0 ? BuyStrategies[Next(ctx, 0, BuyStrategies.Length)] : AutoBuyStrategy.Recommend;

    public override int ChooseRelic(AutoPlayContext ctx, int choiceCount) =>
        choiceCount > 0 ? Next(ctx, 0, choiceCount) : 0;

    public override int ChooseScenarioSelection(AutoPlayContext ctx, int choiceCount) =>
        choiceCount > 0 ? Next(ctx, 0, choiceCount) : 0;

    public override int ChooseStarterRelic(AutoPlayContext ctx, int choiceCount) =>
        choiceCount > 0 ? Next(ctx, 0, choiceCount) : -1;

    /// <summary>使えるものから等確率（何もしない 40%）。</summary>
    public override AutoPlayStreamIntervention ChooseStreamIntervention(AutoPlayStreamContext ctx)
    {
        if (ctx.Enemies.Count == 0 || ctx.NextFloat() < 0.4f) return AutoPlayStreamIntervention.None;
        var options = new List<AutoPlayStreamIntervention>();
        foreach (HeroInterventionType t in Enum.GetValues(typeof(HeroInterventionType)))
            if (ctx.CanUse(t)) options.Add(AutoPlayStreamIntervention.Of(t));
        foreach (DungeonInterventionType t in Enum.GetValues(typeof(DungeonInterventionType)))
            if (ctx.CanUse(t)) options.Add(AutoPlayStreamIntervention.Of(t));
        return options.Count > 0 ? options[ctx.Next(0, options.Count)] : AutoPlayStreamIntervention.None;
    }

    public override int ChooseEnemyTarget(AutoPlayStreamContext ctx, AutoPlayStreamIntervention intervention) =>
        ctx.Enemies.Count > 0 ? ctx.Next(0, ctx.Enemies.Count) : -1;

    public override int ChooseRestockQuantity(AutoPlayStreamContext ctx, string itemName, int maxQuantity, int defaultQuantity) =>
        maxQuantity > 0 && ctx.NextFloat() < 0.7f ? ctx.Next(1, maxQuantity + 1) : 0;

    protected override float PriceGraphPeekChance => 0.3f;

    /// <summary>買える情報なら半々で買う。</summary>
    public override bool ShouldBuyDungeonIntel(AutoPlayStreamPrepContext ctx) =>
        !ctx.WeaknessKnown && ctx.IntelCost > 0 && ctx.IntelCost <= ctx.Money / 2 && ctx.NextFloat() < 0.5f;

    /// <summary>特化の割合を 30〜90% でランダムに。</summary>
    public override AutoPlayStreamPurchasePlan ChooseStreamPurchase(AutoPlayStreamPrepContext ctx) =>
        PlanWithShare(ctx, 0.3f + 0.6f * ctx.NextFloat());

    private static int Next(AutoPlayContext ctx, int min, int maxExclusive) =>
        ctx?.Random != null ? ctx.Random.Next(min, maxExclusive) : UnityEngine.Random.Range(min, maxExclusive);
}

/// <summary>素通し: 何も買わず・並べずにフェーズだけ進める（進行・演出の確認用）。</summary>
public class PassThroughAutoPlayStrategy : DefaultAutoPlayStrategy
{
    public override string Name => "素通し（買わない・並べない）";
    public override bool ShouldProcure(AutoPlayContext ctx) => false;
    public override bool ShouldDisplay(AutoPlayContext ctx) => false;
    public override bool ShouldVisitBlacksmithBeforeStream(AutoPlayContext ctx) => false;
    public override bool ShouldCarryMaxFunds(AutoPlayContext ctx) => false;
    public override int ChooseStarterRelic(AutoPlayContext ctx, int choiceCount) => -1;
    public override AutoPlayStreamIntervention ChooseStreamIntervention(AutoPlayStreamContext ctx) => AutoPlayStreamIntervention.None;
    public override int ChooseRestockQuantity(AutoPlayStreamContext ctx, string itemName, int maxQuantity, int defaultQuantity) => 0;
    public override bool ChooseShelfSwap(AutoPlayStreamContext ctx, out int inventoryIndex, out int sellIndex)
    {
        inventoryIndex = -1;
        sellIndex = -1;
        return false;
    }
    protected override float PriceGraphPeekChance => 0f;
    public override bool ShouldBuyDungeonIntel(AutoPlayStreamPrepContext ctx) => false;
    public override AutoPlayStreamPurchasePlan ChooseStreamPurchase(AutoPlayStreamPrepContext ctx) =>
        new() { RegularStrategy = AutoBuyStrategy.Recommend, Reason = "素通し" };
}
#endif

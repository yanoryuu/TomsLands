#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 店への投資の予定（その日の仕入れの前に 1 回。AutoPlayController が実画面を押す）。
/// 店の改装（店Lv）・鍛冶屋の開発（鍛冶屋Lv。鍛冶屋の画面でお任せ購入の前に押す）・店の設備（マシン 1 台）・取引所（債券 / ファンドの売買 1 件）。
/// </summary>
public struct AutoPlayShopInvestPlan
{
    public bool UpgradeShop;
    public bool UpgradeSmith;
    /// <summary>情報屋の開発（レベルアップ。取引所の商品・債券の口数が増える）。取引所の前に押す。</summary>
    public bool UpgradeBroker;
    /// <summary>買って置くマシンの ID（null = 置かない）。</summary>
    public string MachineId;
    /// <summary>取引所で売買する商品の ID（null = しない）。</summary>
    public string FinanceProductId;
    /// <summary>買う口数（正）/ 売る口数（負。ファンドだけ）。</summary>
    public int FinanceUnits;
    /// <summary>勇者に持たせる武器 / 防具の itemId（null = 替えない）。勇者の画面で装備する。</summary>
    public string HeroWeaponId;
    public string HeroArmorId;
    /// <summary>勇者の作戦（HeroTactics の名前。null = 替えない）。</summary>
    public string HeroTactics;
    public string Reason;

    public bool Any => UpgradeShop || UpgradeSmith || !string.IsNullOrEmpty(MachineId) || (!string.IsNullOrEmpty(FinanceProductId) && FinanceUnits != 0)
                       || HeroWeaponId != null || HeroArmorId != null || HeroTactics != null || UpgradeBroker;
}

/// <summary>店への投資を実際に押した結果（押せなかったものは false / 0）。</summary>
public sealed class AutoPlayShopInvestResult
{
    public int Turn;
    public bool ShopUpgraded, SmithUpgraded, BrokerUpgraded;
    public int ShopLevelAfter, SmithLevelAfter, BrokerLevelAfter;
    public string MachinePlaced;
    public string FinanceProductId;
    /// <summary>実際に動いた口数（買い = 正、売り = 負）。</summary>
    public int FinanceUnits;
    /// <summary>勇者に装備できた武器 / 防具の itemId（替えなかった・替えられなかったら null）。</summary>
    public string HeroWeaponEquipped, HeroArmorEquipped, HeroTacticsSet;
    /// <summary>このまとまりで動いたお金（支出 = 正、売りの入金 = 負）。</summary>
    public int Spent;
    public readonly List<string> Notes = new();
}

/// <summary>
/// 店への投資を判断する戦略（人格プレイ）。実装していれば AutoPlayController が仕入れの前に改装・設備・取引所へ寄り道し、
/// 鍛冶屋の画面でお任せ購入の前に開発（レベルアップ）を押す。
/// </summary>
public interface IAutoPlayShopInvestor
{
    /// <summary>その日の予定（ctx の所持金は寄り道の直前の値）。何もしないなら default。</summary>
    AutoPlayShopInvestPlan ChooseShopInvestments(AutoPlayContext ctx);

    /// <summary>寄り道（と鍛冶屋の開発）を終えたとき。押せなかったもの・押せたものをログと学習に残す。</summary>
    void OnShopInvestmentsDone(AutoPlayShopInvestResult result);
}

/// <summary>
/// 実況動画の撮影用に、通常の戦略に足す判断（新聞を読む・地図を見る・魔王軍支援）。
/// 戦略がこれを実装していれば AutoPlayController が仕入れの前に寄り道する。
/// </summary>
public interface IAutoPlayJikkyoExtras
{
    /// <summary>その日の朝、新聞を開いて眺めるか（何秒眺めるかは戻り値。0 以下なら開かない）。</summary>
    float NewspaperReadSeconds(AutoPlayContext ctx);

    /// <summary>その日、地図を開いて眺めるか（秒。0 以下なら開かない）。</summary>
    float MapPeekSeconds(AutoPlayContext ctx);

    /// <summary>配信前に魔王軍支援（次の配信先のレベルアップ）を何回するか（0 = しない）。所持金の範囲で押せた回数だけ上がる。</summary>
    int ChooseDemonSupportLevels(AutoPlayStreamPrepContext prep);

    /// <summary>村で各施設を調べて眺める秒数（0 以下なら調べずに出店準備へ）。</summary>
    float VillageLookSeconds { get; }

    /// <summary>村で、この施設に投資（建設・拡張）するか。押せるときだけ聞かれる。</summary>
    bool ShouldInvestFacility(string facilityId, int index);
}

/// <summary>
/// 実況A（淡々・最適解寄り）: 標準の判断に加えて、毎朝新聞を読み、配信前に地図を見る。情報屋は買う。魔王軍支援はしない。
/// </summary>
public class JikkyoOptimalStrategy : DefaultAutoPlayStrategy, IAutoPlayJikkyoExtras
{
    public override string Name => "実況A 最適解（新聞を読む・情報を買う）";

    private readonly HashSet<int> _newspaperDays = new();
    private readonly HashSet<int> _mapDays = new();

    public float NewspaperReadSeconds(AutoPlayContext ctx)
    {
        if (ctx.Turn < 2 || !_newspaperDays.Add(ctx.Turn)) return 0f;   // 初日はチュートリアルで鍛冶屋だけ
        return 7f;
    }

    public float MapPeekSeconds(AutoPlayContext ctx)
    {
        if (ctx.Turn < 2 || (ctx.Turn % 2 != 0) || !_mapDays.Add(ctx.Turn)) return 0f;
        return 4f;
    }

    public virtual int ChooseDemonSupportLevels(AutoPlayStreamPrepContext prep) => 0;

    public virtual float VillageLookSeconds => 3.5f;

    private int _invested;
    /// <summary>村資金が許す限り、最初に押せた施設から順に 2 つまで投資する。</summary>
    public virtual bool ShouldInvestFacility(string facilityId, int index) => _invested++ < 2;

    /// <summary>情報は買えるなら買う（所持金の 40% まで）。</summary>
    public override bool ShouldBuyDungeonIntel(AutoPlayStreamPrepContext ctx) =>
        !ctx.WeaknessKnown && ctx.IntelCost > 0 && ctx.IntelCost <= ctx.Money * 2 / 5;
}

/// <summary>
/// 実況C（初見・ミスあり）:
/// - 予算は毎回ほぼ全額（配信用の手持ちも残さない）。
/// - 1回目の配信は情報を買って勇者側で張る（スパチャも多め）。
/// - 2回目の配信は情報を買わず、手持ちの大半を魔王軍支援につぎ込む。配信中は勇者に介入しない（回復を送らない）。
/// - レリックと会話の選択肢は乱数。補充はつい多め。
/// </summary>
public class JikkyoNoviceStrategy : DefaultAutoPlayStrategy, IAutoPlayJikkyoExtras
{
    public override string Name => "実況C 初見（情報を買わない・魔王に張る）";

    private int _intelBought;
    private bool _demonSupported;
    private readonly HashSet<int> _newspaperDays = new();
    private readonly HashSet<int> _mapDays = new();

    public JikkyoNoviceStrategy()
    {
        StreamReserveGold = 0;      // 配信用の手持ちを残さない（あとで困る）
        BudgetPercent = 100;
    }

    public float NewspaperReadSeconds(AutoPlayContext ctx)
    {
        if (ctx.Turn < 2 || !_newspaperDays.Add(ctx.Turn)) return 0f;
        return ctx.Turn == 2 ? 8f : 4f;   // 2日目はじっくり、あとは流し読み
    }

    public float MapPeekSeconds(AutoPlayContext ctx)
    {
        if (ctx.Turn < 3 || !_mapDays.Add(ctx.Turn)) return 0f;
        return ctx.Turn == 3 ? 4f : 0f;
    }

    /// <summary>2回目の配信の前（情報を買わなかった配信）に、手持ちの 7 割が尽きるまでレベルアップ。</summary>
    public int ChooseDemonSupportLevels(AutoPlayStreamPrepContext prep)
    {
        if (_demonSupported || prep.WeaknessKnown || _intelBought == 0) return 0;
        if (prep.TurnsUntilStream > 1) return 0;
        _demonSupported = true;
        return 99;   // 押せるだけ（コントローラ側が所持金の 70% を上限にする）
    }

    /// <summary>1回目だけ情報を買う（半分までなら払う）。</summary>
    public override bool ShouldBuyDungeonIntel(AutoPlayStreamPrepContext ctx)
    {
        if (ctx.WeaknessKnown || ctx.IntelCost <= 0) return false;
        if (_intelBought >= 1) return false;
        if (ctx.IntelCost > ctx.Money / 2) return false;
        _intelBought++;
        return true;
    }

    /// <summary>弱点が分かっていれば 8 割を特化（買いすぎ）。分からなければおすすめ順で全額。</summary>
    public override AutoPlayStreamPurchasePlan ChooseStreamPurchase(AutoPlayStreamPrepContext ctx) =>
        PlanWithShare(ctx, 0.8f);

    public override int ChooseRelic(AutoPlayContext ctx, int choiceCount) =>
        choiceCount > 0 ? Next(ctx, 0, choiceCount) : 0;

    public override int ChooseScenarioSelection(AutoPlayContext ctx, int choiceCount) =>
        choiceCount > 0 ? Next(ctx, 0, choiceCount) : 0;

    /// <summary>
    /// 魔王に張った配信（2回目）では勇者に介入しない。それ以外は回復・必殺を惜しまない（予算の範囲）。
    /// </summary>
    public override AutoPlayStreamIntervention ChooseStreamIntervention(AutoPlayStreamContext ctx)
    {
        if (ctx.Enemies.Count == 0) return AutoPlayStreamIntervention.None;
        if (_demonSupported)
        {
            // 魔王側: たまにボス強化や罠で盛り上げる
            if (ctx.BossAlive && ctx.CanUse(DungeonInterventionType.BossBuff) && ctx.NextFloat() < 0.5f)
                return AutoPlayStreamIntervention.Of(DungeonInterventionType.BossBuff);
            if (ctx.CanUse(DungeonInterventionType.Trap) && ctx.NextFloat() < 0.3f)
                return AutoPlayStreamIntervention.Of(DungeonInterventionType.Trap);
            return AutoPlayStreamIntervention.None;
        }
        if (ctx.HeroHp01 >= 0f && ctx.HeroHp01 < 0.5f && ctx.CanUse(HeroInterventionType.Heal))
            return AutoPlayStreamIntervention.Of(HeroInterventionType.Heal);
        if (ctx.BossAlive && ctx.CanUse(HeroInterventionType.Special) && ctx.NextFloat() < 0.6f)
            return AutoPlayStreamIntervention.Of(HeroInterventionType.Special);
        if (ctx.BossAlive && ctx.CanUse(HeroInterventionType.Skill))
            return AutoPlayStreamIntervention.Of(HeroInterventionType.Skill);
        if (ctx.CanUse(HeroInterventionType.Skill) && ctx.NextFloat() < 0.25f)
            return AutoPlayStreamIntervention.Of(HeroInterventionType.Skill);
        return AutoPlayStreamIntervention.None;
    }

    /// <summary>補充はつい多め（買える上限の 8 割〜全部）。</summary>
    public override int ChooseRestockQuantity(AutoPlayStreamContext ctx, string itemName, int maxQuantity, int defaultQuantity) =>
        maxQuantity <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(maxQuantity * (0.8f + 0.2f * ctx.NextFloat())), 1, maxQuantity);

    protected override float PriceGraphPeekChance => 0.3f;

    public float VillageLookSeconds => 2.5f;

    /// <summary>目についた施設に深く考えず投資する（押せるものは全部）。</summary>
    public bool ShouldInvestFacility(string facilityId, int index) => true;

    private static int Next(AutoPlayContext ctx, int min, int maxExclusive) =>
        ctx?.Random != null ? ctx.Random.Next(min, maxExclusive) : UnityEngine.Random.Range(min, maxExclusive);
}
/// <summary>
/// 実況B（解説）: 1回目の配信までは A と同じ最適解（新聞・情報屋・勇者側）。
/// 2回目の配信は「実験」として情報を買わず、魔王軍支援に出資して魔王側に張る（台本の demo_demon / demo_stream2）。
/// </summary>
public class JikkyoKaisetsuStrategy : JikkyoOptimalStrategy
{
    public override string Name => "実況B 解説（1回目は最適解・2回目は魔王軍支援の実験）";

    public override float VillageLookSeconds => 5f;

    private int _intelBought;
    private bool _demonSupported;

    public override bool ShouldBuyDungeonIntel(AutoPlayStreamPrepContext ctx)
    {
        if (_intelBought >= 1) return false;          // 2回目は買わない（実験）
        bool buy = base.ShouldBuyDungeonIntel(ctx);
        if (buy) _intelBought++;
        return buy;
    }

    public override int ChooseDemonSupportLevels(AutoPlayStreamPrepContext prep)
    {
        if (_demonSupported || _intelBought == 0 || prep.WeaknessKnown) return 0;
        if (prep.TurnsUntilStream > 1) return 0;
        _demonSupported = true;
        return 99;
    }

    /// <summary>魔王に張った配信では勇者を助けない。たまに罠・ボス強化で盛り上げる。</summary>
    public override AutoPlayStreamIntervention ChooseStreamIntervention(AutoPlayStreamContext ctx)
    {
        if (!_demonSupported) return base.ChooseStreamIntervention(ctx);
        if (ctx.Enemies.Count == 0) return AutoPlayStreamIntervention.None;
        if (ctx.BossAlive && ctx.CanUse(DungeonInterventionType.BossBuff) && ctx.NextFloat() < 0.5f)
            return AutoPlayStreamIntervention.Of(DungeonInterventionType.BossBuff);
        if (ctx.CanUse(DungeonInterventionType.Trap) && ctx.NextFloat() < 0.3f)
            return AutoPlayStreamIntervention.Of(DungeonInterventionType.Trap);
        return AutoPlayStreamIntervention.None;
    }
}
/// <summary>
/// 実況D（魔王軍に融資する店）: 勇者を裏切って毎回魔王側に張る。
/// - 情報屋では弱点を買わない（勇者を勝たせる気がないので要らない）。
/// - 毎回、配信前日の朝に手持ちの大半（コントローラ側の上限 70%）を魔王軍支援に出資し、次の配信先のレベルを上げる。
/// - 仕入れは割安買い（Bargain）に寄せる。勇者が負けても配当（防衛報酬）と安い在庫の売上で回収する作戦。
/// - 配信中は勇者を助けない。ボス強化・罠を積極的に使って勇者を負けさせる。補充は控えめ。
/// - 村では見つけた施設に順に投資する。
/// </summary>
public class JikkyoMaouStrategy : DefaultAutoPlayStrategy, IAutoPlayJikkyoExtras
{
    public override string Name => "実況D 魔王軍に融資（情報を買わない・毎回魔王側に張る）";

    private readonly HashSet<int> _newspaperDays = new();
    private readonly HashSet<int> _mapDays = new();
    /// <summary>次の配信にまだ出資していないか。配信から遠い日（TurnsUntilStream &gt; 1）を見たら次の配信ぶんが解禁される。</summary>
    private bool _supportArmed = true;
    public int SupportedStreams { get; private set; }

    public JikkyoMaouStrategy()
    {
        BudgetPercent = 100;
        StreamReserveGold = 30000;   // 配信中のボス強化・罠に使う分だけ残す（使ってよい額の半分が上限）
    }

    public float NewspaperReadSeconds(AutoPlayContext ctx)
    {
        if (ctx.Turn < 2 || !_newspaperDays.Add(ctx.Turn)) return 0f;
        return ctx.Turn == 2 ? 6f : 0f;   // 2日目に一度だけ眺める（魔王に張るので相場の記事は見ない主義）
    }

    public float MapPeekSeconds(AutoPlayContext ctx)
    {
        if (ctx.Turn < 3 || !_mapDays.Add(ctx.Turn)) return 0f;
        return ctx.Turn == 3 ? 4f : 0f;
    }

    /// <summary>配信の前日（または配信日の朝）に、押せるだけ出資する。1 回の配信につき 1 度だけ。</summary>
    public int ChooseDemonSupportLevels(AutoPlayStreamPrepContext prep)
    {
        if (prep.TurnsUntilStream > 1) { _supportArmed = true; return 0; }
        if (!_supportArmed) return 0;
        _supportArmed = false;
        SupportedStreams++;
        return 99;   // 押せるだけ（コントローラ側が所持金の 70% を上限にする）
    }

    /// <summary>情報は買わない。勇者を勝たせるための情報に払う金は無い。</summary>
    public override bool ShouldBuyDungeonIntel(AutoPlayStreamPrepContext ctx) => false;

    /// <summary>ふだんの仕入れは割安買い。勇者が負けても配当で回収するので、仕入れは安く広く。</summary>
    public override AutoBuyStrategy ChooseAutoBuyStrategy(AutoPlayContext ctx) => AutoBuyStrategy.Bargain;

    /// <summary>配信前の仕入れも割安買い（特化はしない。弱点は知らない）。</summary>
    public override AutoPlayStreamPurchasePlan ChooseStreamPurchase(AutoPlayStreamPrepContext ctx) =>
        new AutoPlayStreamPurchasePlan
        {
            RegularStrategy = AutoBuyStrategy.Bargain,
            FocusBudget = 0,
            Reason = $"{ctx.DungeonLabel}: 魔王側に張るので弱点は見ず、割安な品を広く仕入れ",
        };

    /// <summary>勇者は助けない。ボスが出たら強化、道中は罠で削る。</summary>
    public override AutoPlayStreamIntervention ChooseStreamIntervention(AutoPlayStreamContext ctx)
    {
        if (ctx.Enemies.Count == 0) return AutoPlayStreamIntervention.None;
        if (ctx.BossAlive && ctx.CanUse(DungeonInterventionType.BossBuff) && ctx.NextFloat() < 0.8f)
            return AutoPlayStreamIntervention.Of(DungeonInterventionType.BossBuff);
        if (ctx.CanUse(DungeonInterventionType.Trap) && ctx.NextFloat() < 0.5f)
            return AutoPlayStreamIntervention.Of(DungeonInterventionType.Trap);
        return AutoPlayStreamIntervention.None;
    }

    /// <summary>補充は控えめ（買える上限の 3 割まで。勇者が負ける前提なので高い武器を積まない）。</summary>
    public override int ChooseRestockQuantity(AutoPlayStreamContext ctx, string itemName, int maxQuantity, int defaultQuantity) =>
        maxQuantity <= 0 ? 0 : Mathf.Clamp(Mathf.Min(defaultQuantity, Mathf.CeilToInt(maxQuantity * 0.3f)), 1, maxQuantity);

    protected override float PriceGraphPeekChance => 0.2f;

    public float VillageLookSeconds => 3f;

    private int _invested;
    /// <summary>村資金が許す限り、押せた施設から順に 2 つまで投資する。</summary>
    public bool ShouldInvestFacility(string facilityId, int index) => _invested++ < 2;
}
#endif

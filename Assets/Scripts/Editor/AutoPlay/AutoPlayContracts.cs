using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// =====================================================================
// Jev AutoPlay — 契約（プレイヤー操作 API 層・状態スナップショット・ボット）
// 設計: Docs/Jev_AutoPlay_Design.md
// エディタ専用（Assets/Scripts/Editor 配下なのでビルドには含まれない）。
// =====================================================================

/// <summary>
/// プレイヤー操作 API。UI のボタン操作と同じことを関数で呼べる層。
/// 実装はヘッドレス（<see cref="AutoPlayHeadlessGame"/>）。将来 Play モード版も同じ契約で実装する。
/// すべてメインスレッドから同期で呼ぶこと（UnityEngine.Random を使うモデルがあるため）。
/// </summary>
public interface IPlayerActions
{
    /// <summary>現在の盤面（プレイヤーが画面で見られる情報だけ）。</summary>
    AutoPlaySnapshot Snapshot();

    // --- 仕入れフェーズ ---
    /// <summary>鍛冶屋で購入（BlackSmithPresenter.HandlePurchase と同等）。</summary>
    AutoPlayActionResult Buy(string itemId, int quantity);
    /// <summary>おまかせ仕入れ（ItemModel.AutoPurchase。ゲーム内「おすすめ」ボタンと同等）。</summary>
    AutoPlayActionResult AutoBuy(int budget, AutoBuyStrategy strategy);
    /// <summary>魔王軍支援＝ダンジョンのレベルアップ（DungeonLevelUpPresenter.HandleLevelUp と同等）。</summary>
    AutoPlayActionResult SupportDungeon(DungeonName dungeon);
    /// <summary>鍛冶屋レベルアップ（扱える銘柄ランクが増える）。</summary>
    AutoPlayActionResult UpgradeBlacksmith();
    /// <summary>店レベルアップ（陳列枠が増える）。</summary>
    AutoPlayActionResult UpgradeShop();

    // --- 陳列フェーズ ---
    /// <summary>陳列を全部外す。</summary>
    AutoPlayActionResult ClearDisplay();
    /// <summary>陳列設定（qty=0 で外す）。陳列種類数の上限・1銘柄あたりの上限を守る。</summary>
    AutoPlayActionResult SetDisplay(string itemId, int quantity);

    // --- ポップアップ系 ---
    AutoPlayActionResult ConfirmPendingEvent();
    AutoPlayActionResult ChooseRelic(int index);
    AutoPlayActionResult DeclineRelic();

    // --- 進行 ---
    /// <summary>営業開始 → 営業サマリー（売り注文化・約定入金）。TurnEndSummaryPresenter.Entry と同等。</summary>
    AutoPlayActionResult StartSales();
    /// <summary>サマリーの「確認」= 日送り（GameFlowManager.NextTurn）。</summary>
    AutoPlayActionResult EndDay();
    /// <summary>配信を開始する（品出し内容を渡す）。戦闘と配信販売はサロゲートで解決する。</summary>
    /// <remarks>interventions = 配信中の介入（スパチャ）の予定。戦闘は一括で解決するため、
    /// 「配信のどのあたりで何を使うか」を先に渡す離散モデル（Docs/Jev_AutoPlay_Design.md §2.5）。</remarks>
    AutoPlayActionResult StartStream(IReadOnlyList<AutoPlayStreamItem> items, IReadOnlyList<AutoPlayInterventionOrder> interventions = null);
}

/// <summary>操作の結果。失敗しても例外は投げず ok=false と理由を返す（ボットの不正手を数えるため）。</summary>
public sealed class AutoPlayActionResult
{
    public bool Ok;
    public string Message;

    public static AutoPlayActionResult Success(string message = null) => new AutoPlayActionResult { Ok = true, Message = message };
    public static AutoPlayActionResult Fail(string message) => new AutoPlayActionResult { Ok = false, Message = message };
}

public enum AutoPlayStage
{
    /// <summary>通常営業日（仕入れ→陳列→営業）。</summary>
    ShopDay,
    /// <summary>配信日（品出し→配信）。</summary>
    StreamDay,
    /// <summary>フロー完走（リザルトへ）。</summary>
    Completed,
    /// <summary>借金を払えず破産（ゲームオーバー）。</summary>
    Bankrupt,
}

/// <summary>盤面スナップショット。ボットはこれだけを見て判断する（未来情報・非公開情報は載せない）。</summary>
public sealed class AutoPlaySnapshot
{
    public int RunTurn;
    public int FlowIndex;
    public int FlowLength;
    public AutoPlayStage Stage;
    public string GameMode;

    public int Money;
    public int PendingSellOrderEstimate;
    public int NextDebtAmount;
    public int TurnsUntilDebt;

    public int ShopLevel;
    public int MaxDisplayKinds;
    public int MaxDisplayPerItem;
    public int ShopUpgradeCost;      // -1 = 最大
    public int BlacksmithLevel;
    public int BlacksmithUpgradeCost; // -1 = 最大

    public int TurnsUntilStream;      // -1 = もう配信なし
    public string NextStreamDungeon;  // 次（配信日なら今日）の配信ダンジョン
    public int NextStreamDungeonLevel;
    public float NextStreamClearChancePct;

    public int HeroLevel;
    public int HeroHp;
    public int HeroAttack;
    public int HeroDefense;

    public bool BuzzActive;
    public string BuzzType;

    public List<AutoPlayItemView> Items = new List<AutoPlayItemView>();
    public List<AutoPlayDungeonView> Dungeons = new List<AutoPlayDungeonView>();
    public List<AutoPlayNewsView> News = new List<AutoPlayNewsView>();
    public List<AutoPlayRelicChoiceView> PendingRelicChoices = new List<AutoPlayRelicChoiceView>();
    public int RelicDeclineGold;

    // --- 配信日の介入（StreamDay のときだけ埋まる） ---
    public AutoPlayStreamInfo Stream;
}

public sealed class AutoPlayItemView
{
    public string Id;
    public string Name;
    public string Type;
    public string Attribute;
    public int Tier;              // requiredLevel
    public bool Unlocked;         // 鍛冶屋レベルで買える/売れる
    public int Price;
    public int BuyUnitPrice;      // レリック補正後の仕入れ単価
    public int BasePrice;
    public float PriceChange1d;   // 前日比（-0.05 = -5%）
    public float Demand;
    public float DemandChange1d;
    public string Heat;           // MarketHeat.Describe
    public int Stock;
    public int MaxStock;
    public int DisplayStock;
    public bool Displayed;
    public float SalesRate;
    public int DividendPerTurn;
}

public sealed class AutoPlayDungeonView
{
    public string Id;
    public string Name;
    public int Level;
    public int MaxLevel;
    public int SupportCost;          // 0 = 最大
    public int DefeatReward;         // 勇者敗北（防衛成功）時の報酬
    public float ClearChancePct;     // 画面に出ているクリア確率
    public bool IsNextStream;
}

public sealed class AutoPlayNewsView
{
    public string ArticleId;
    public string Company;
    public string Page;
    public string SourceClarity;
    public string Byline;
    public string Kind;
    public string HeadlineJa;
    public string SummaryEn;   // Docs/News_Spec.md §13（Jev 検証専用列）
}

public sealed class AutoPlayRelicChoiceView
{
    public string RelicId;
    public string Name;
    public string Rarity;
    public string Description;
}

public sealed class AutoPlayStreamItem
{
    public string ItemId;
    public int Quantity;

    public AutoPlayStreamItem(string itemId, int quantity)
    {
        ItemId = itemId;
        Quantity = quantity;
    }
}

// ---------------------------------------------------------------------
// ボット
// ---------------------------------------------------------------------

/// <summary>1回の判断の記録（判断分布のレポート用）。</summary>
public sealed class AutoPlayDecision
{
    public int Turn;
    public string Phase;        // day / stream / relic
    public string Question;     // budget / buy / display / support / upgrade ...
    public string Chosen;
    public float? Score;
    public float? Confidence;
    public Dictionary<string, float> Probabilities;
}

/// <summary>営業日の行動計画。ランナーが IPlayerActions を通して順に実行する。</summary>
public sealed class AutoPlayDayPlan
{
    /// <summary>手動の購入（銘柄・数量）。上から順に実行し、資金不足の分は買える数に切り詰める。</summary>
    public List<AutoPlayStreamItem> Purchases = new List<AutoPlayStreamItem>();
    /// <summary>おまかせ仕入れを使う場合の予算（0 = 使わない）。</summary>
    public int AutoBuyBudget;
    public AutoBuyStrategy AutoBuyStrategy = AutoBuyStrategy.Recommend;
    /// <summary>陳列の優先順（銘柄ID）。上から陳列枠まで詰める。</summary>
    public List<string> DisplayPriority = new List<string>();
    /// <summary>陳列する量の割合（0〜1。1 = 上限いっぱい）。</summary>
    public float DisplayFraction = 1f;
    /// <summary>魔王軍支援するダンジョン（null = しない）。</summary>
    public string SupportDungeon;
    /// <summary>none / blacksmith / shop</summary>
    public string Upgrade = "none";

    public List<AutoPlayDecision> Decisions = new List<AutoPlayDecision>();
}

public sealed class AutoPlayStreamPlan
{
    public List<AutoPlayStreamItem> Items = new List<AutoPlayStreamItem>();
    /// <summary>配信中の介入の予定（上から順に、timing の位置で出せるようになったら出す）。</summary>
    public List<AutoPlayInterventionOrder> Interventions = new List<AutoPlayInterventionOrder>();
    public List<AutoPlayDecision> Decisions = new List<AutoPlayDecision>();
}

public sealed class AutoPlayRelicPlan
{
    /// <summary>選ぶ候補の番号。-1 = 辞退してゴールド。</summary>
    public int ChoiceIndex = -1;
    public List<AutoPlayDecision> Decisions = new List<AutoPlayDecision>();
}

/// <summary>判断を下すボット。Jev / ランダム / 貪欲 を差し替える。</summary>
public interface IAutoPlayBot
{
    string Name { get; }
    Task<AutoPlayDayPlan> PlanDayAsync(AutoPlaySnapshot s, CancellationToken ct);
    Task<AutoPlayStreamPlan> PlanStreamAsync(AutoPlaySnapshot s, CancellationToken ct);
    Task<AutoPlayRelicPlan> PlanRelicAsync(AutoPlaySnapshot s, CancellationToken ct);

    /// <summary>外部 API の使用量（Jev 以外は 0）。</summary>
    int Requests { get; }
    long InputTokens { get; }
}

// ---------------------------------------------------------------------
// 配信中の介入（スパチャ）— Docs/Streaming_Redesign.md §4・§5
// ---------------------------------------------------------------------

/// <summary>介入の種類（勇者側3種＋ダンジョン側4種）。</summary>
public enum AutoPlayInterventionKind
{
    Heal,
    Skill,
    Special,
    Trap,
    Curse,
    Reinforce,
    BossBuff,
}

public static class AutoPlayInterventionKinds
{
    public static bool IsHeroSide(AutoPlayInterventionKind k) =>
        k == AutoPlayInterventionKind.Heal || k == AutoPlayInterventionKind.Skill || k == AutoPlayInterventionKind.Special;

    public static string Key(AutoPlayInterventionKind k) => k.ToString().ToLowerInvariant();

    public static bool TryParse(string key, out AutoPlayInterventionKind kind)
    {
        foreach (AutoPlayInterventionKind k in System.Enum.GetValues(typeof(AutoPlayInterventionKind)))
        {
            if (Key(k) == key) { kind = k; return true; }
        }
        kind = default;
        return false;
    }

    public static int Price(AutoPlayInterventionKind k, StreamingInteractionSettings s) => k switch
    {
        AutoPlayInterventionKind.Heal => s.GetPrice(HeroInterventionType.Heal),
        AutoPlayInterventionKind.Skill => s.GetPrice(HeroInterventionType.Skill),
        AutoPlayInterventionKind.Special => s.GetPrice(HeroInterventionType.Special),
        AutoPlayInterventionKind.Trap => s.GetPrice(DungeonInterventionType.Trap),
        AutoPlayInterventionKind.Curse => s.GetPrice(DungeonInterventionType.Curse),
        AutoPlayInterventionKind.Reinforce => s.GetPrice(DungeonInterventionType.Reinforce),
        _ => s.GetPrice(DungeonInterventionType.BossBuff),
    };

    /// <summary>Jev に見せる英語の効果説明（数値は SO から）。</summary>
    public static string DescribeEn(AutoPlayInterventionKind k, StreamingInteractionSettings s) => k switch
    {
        AutoPlayInterventionKind.Heal => $"Hero side (green super chat): heal the hero by {s.healRatio:P0} of max HP. Longer battle = more selling time.",
        AutoPlayInterventionKind.Skill => $"Hero side (blue super chat): the hero's next attack deals x{s.skillMultiplier:0.##}.",
        AutoPlayInterventionKind.Special => $"Hero side (red super chat): special move, the hero's next attack deals x{s.specialMultiplier:0.##} (max {s.specialMaxPerStream} per stream incl. viewers).",
        AutoPlayInterventionKind.Trap => $"Dungeon side: trap, the hero's next hit taken deals extra {s.trapBonusDamageRatio:P0} of max HP ignoring defense.",
        AutoPlayInterventionKind.Curse => $"Dungeon side: curse, the hero's defense x{s.curseDefenseMul:0.##} for the next {s.curseHeroHits} hits taken (monsters deal attack minus defense).",
        AutoPlayInterventionKind.Reinforce => $"Dungeon side: reinforcements, {s.reinforceCount} extra monster(s) join the current wave. Longer battle, hero slightly weaker.",
        _ => $"Dungeon side: boss buff for this stream, boss attack x{s.bossBuffAttackMul:0.##} and damage taken x{s.bossBuffDamageTakenMul:0.##}, boss ignores {s.bossBuffDefensePierce:P0} of the hero's defense (once per stream).",
    };
}

/// <summary>介入の予定1件。timing は配信の進み具合（0=開幕・1=終盤）。</summary>
public sealed class AutoPlayInterventionOrder
{
    public AutoPlayInterventionKind Kind;
    public float Timing;

    public AutoPlayInterventionOrder(AutoPlayInterventionKind kind, float timing)
    {
        Kind = kind;
        Timing = timing;
    }
}

/// <summary>介入メニュー1行（配信日のスナップショット）。</summary>
public sealed class AutoPlayInterventionOption
{
    public AutoPlayInterventionKind Kind;
    public int Price;
    public bool Affordable;
    public string DescriptionEn;
    /// <summary>【オラクル】この介入を中盤に1回だけ使った場合のサロゲート勝敗。貪欲ボット専用（Jev には見せない）。</summary>
    public bool WhatIfHeroWins;
    public int WhatIfTurns;
}

/// <summary>配信日の情報（プレイヤーが配信画面で見られるもの＋オラクル）。</summary>
public sealed class AutoPlayStreamInfo
{
    public string Dungeon;
    public int Level;
    public float DisplayedClearPct;
    public bool HasBoss;
    public int EnemyCount;
    /// <summary>勇者が負けたときの防衛報酬（レリック補正込み）。</summary>
    public int DefeatReward;
    public int HeroMaxHp;
    public int HeroAttack;
    public int HeroDefense;
    public bool InterventionsEnabled;
    public int CooldownTurns;
    public List<AutoPlayInterventionOption> Options = new List<AutoPlayInterventionOption>();
    /// <summary>【オラクル】介入なしのサロゲート勝敗とターン数。</summary>
    public bool BaselineHeroWins;
    public int BaselineTurns;
}

/// <summary>視聴者の赤スパ（必殺技の自動発動）の扱い。</summary>
public enum AutoPlayViewerSuperChatMode
{
    /// <summary>StreamingInteractionSettings の viewerSuperChatEnabled / viewerRedTriggersSpecial に従う。</summary>
    FollowSettings,
    Off,
    On,
}

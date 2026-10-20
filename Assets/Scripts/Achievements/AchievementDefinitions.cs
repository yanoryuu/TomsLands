using System.Collections.Generic;

/// <summary>
/// Steam 実績の一覧（22個）。API 名は Steamworks の管理画面に登録する名前と完全に一致させること。
/// 表示名・説明・隠すかどうか・Stats は Docs/SteamAchievements.md が正。ここはゲーム内（デバッグメニュー）用の写し。
/// </summary>
public enum AchievementId
{
    FirstIncome,
    FirstStream,
    ScrapPrediction,
    EquipAhead,
    DemonDividend,
    BothSides,
    LongShot,
    NotFooled,
    Daily100k,
    Millionaire,
    TenMillionaire,
    BuzzWave,
    PopularShop,
    GolemWorkshop,
    AllRepaid,
    ByAHair,
    AllPapers,
    Collector,
    FirstClear,
    LongTrader,
    RankS,
    VillageRevival,
}

/// <summary>進捗を数値で持つもの（Steam の Stats。すべて INT）。</summary>
public enum AchievementStat
{
    MaxMoney,
    MaxDailyIncome,
    BestBuzzStreak,
    MaxFollowers,
    MaxGolems,
    MaxRelicsInRun,
    PapersSubscribed,
    ClearsShort,
    ClearsMedium,
    ClearsLong,
}

public sealed class AchievementDefinition
{
    public readonly AchievementId Id;
    public readonly string ApiName;
    public readonly string DisplayName;
    public readonly string Description;
    /// <summary>進捗の Stat（無ければ null）と、解除に要る値。</summary>
    public readonly AchievementStat? ProgressStat;
    public readonly int ProgressTarget;

    public AchievementDefinition(AchievementId id, string apiName, string displayName, string description,
        AchievementStat? progressStat = null, int progressTarget = 0)
    {
        Id = id;
        ApiName = apiName;
        DisplayName = displayName;
        Description = description;
        ProgressStat = progressStat;
        ProgressTarget = progressTarget;
    }
}

public static class AchievementDefinitions
{
    // しきい値（Docs/SteamAchievements.md の条件と揃える）
    public const int DailyIncomeThreshold = 100_000;      // 「超」なので > で判定
    public const int MillionaireMoney = 1_000_000;
    public const int TenMillionaireMoney = 10_000_000;
    public const int BuzzStreakDays = 3;
    public const int PopularFollowers = 10_000;
    public const int GolemCount = 3;
    public const int ByAHairMoney = 1_000;               // 未満
    public const int CollectorRelics = 10;
    /// <summary>新聞社の数（実際の判定は NewsMasterLoader の社数。表示・Steam の進捗上限用）。</summary>
    public const int PaperCompanies = 5;
    /// <summary>
    /// 「不利」以下とみなすクリア見込み(%)。PR #105 のクリア見込み5段階が main に入ったら、その段階判定に差し替える。
    /// </summary>
    public const float LongShotClearPct = 35f;

    public static readonly IReadOnlyList<AchievementDefinition> All = new[]
    {
        new AchievementDefinition(AchievementId.FirstIncome, "FIRST_INCOME", "はじめての入金", "初めて商品が売れて入金された"),
        new AchievementDefinition(AchievementId.FirstStream, "FIRST_STREAM", "初配信", "配信を最後まで見届けた"),
        new AchievementDefinition(AchievementId.ScrapPrediction, "SCRAP_PREDICTION", "読み的中", "スクラップした記事の効き先が実際に値上がりした"),
        new AchievementDefinition(AchievementId.EquipAhead, "EQUIP_AHEAD", "先回り", "装備させた品を抱えたまま勇者が勝ち、値上がりした"),
        new AchievementDefinition(AchievementId.DemonDividend, "DEMON_DIVIDEND", "配当確定", "魔王軍に出資して配当を受け取った"),
        new AchievementDefinition(AchievementId.BothSides, "BOTH_SIDES", "二枚舌", "1回の出店で勇者側でも魔王側でも儲けた"),
        new AchievementDefinition(AchievementId.LongShot, "LONG_SHOT", "大穴", "クリア見込みが不利以下の勇者が勝ち、装備で儲けた"),
        new AchievementDefinition(AchievementId.NotFooled, "NOT_FOOLED", "誤報には乗らない", "スクラップした誤報を確認して見抜いた"),
        new AchievementDefinition(AchievementId.Daily100k, "DAILY_100K", "一日十万", "1日の入金が 100,000G を超えた", AchievementStat.MaxDailyIncome, DailyIncomeThreshold + 1),
        new AchievementDefinition(AchievementId.Millionaire, "MILLIONAIRE", "百万長者", "所持金が 1,000,000G に届いた", AchievementStat.MaxMoney, MillionaireMoney),
        new AchievementDefinition(AchievementId.TenMillionaire, "TEN_MILLIONAIRE", "千万長者", "所持金が 10,000,000G に届いた", AchievementStat.MaxMoney, TenMillionaireMoney),
        new AchievementDefinition(AchievementId.BuzzWave, "BUZZ_WAVE", "バズの波", "バズを3日続けた", AchievementStat.BestBuzzStreak, BuzzStreakDays),
        new AchievementDefinition(AchievementId.PopularShop, "POPULAR_SHOP", "人気店", "フォロワーが 10,000人に届いた", AchievementStat.MaxFollowers, PopularFollowers),
        new AchievementDefinition(AchievementId.GolemWorkshop, "GOLEM_WORKSHOP", "ゴーレム工房", "ゴーレムを3台置いた", AchievementStat.MaxGolems, GolemCount),
        new AchievementDefinition(AchievementId.AllRepaid, "ALL_REPAID", "皆済", "1回の出店で返済をすべて期日に払い、出店を終えた"),
        new AchievementDefinition(AchievementId.ByAHair, "BY_A_HAIR", "首の皮一枚", "所持金 1,000G 未満で返済日を越えた"),
        new AchievementDefinition(AchievementId.AllPapers, "ALL_PAPERS", "全紙購読", "新聞5社をすべて購読した", AchievementStat.PapersSubscribed, PaperCompanies),
        new AchievementDefinition(AchievementId.Collector, "COLLECTOR", "収集家", "1回の出店でレリックを10個集めた", AchievementStat.MaxRelicsInRun, CollectorRelics),
        new AchievementDefinition(AchievementId.FirstClear, "FIRST_CLEAR", "初クリア", "かんたん（Short）をクリアした", AchievementStat.ClearsShort, 1),
        new AchievementDefinition(AchievementId.LongTrader, "LONG_TRADER", "長い商い", "ふつう（Normal）とむずかしい（Long）をクリアした"),
        new AchievementDefinition(AchievementId.RankS, "RANK_S", "ランク S", "ランク S でクリアした"),
        new AchievementDefinition(AchievementId.VillageRevival, "VILLAGE_REVIVAL", "村おこし", "村の施設をすべて建てた"),
    };

    public static AchievementDefinition Get(AchievementId id) => All[(int)id];

    public static string StatApiName(AchievementStat stat) => stat switch
    {
        AchievementStat.MaxMoney => "MAX_MONEY",
        AchievementStat.MaxDailyIncome => "MAX_DAILY_INCOME",
        AchievementStat.BestBuzzStreak => "BEST_BUZZ_STREAK",
        AchievementStat.MaxFollowers => "MAX_FOLLOWERS",
        AchievementStat.MaxGolems => "MAX_GOLEMS",
        AchievementStat.MaxRelicsInRun => "MAX_RELICS_IN_RUN",
        AchievementStat.PapersSubscribed => "PAPERS_SUBSCRIBED",
        AchievementStat.ClearsShort => "CLEARS_SHORT",
        AchievementStat.ClearsMedium => "CLEARS_NORMAL",
        AchievementStat.ClearsLong => "CLEARS_LONG",
        _ => stat.ToString().ToUpperInvariant(),
    };
}

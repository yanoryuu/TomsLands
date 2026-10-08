using UnityEngine;

/// <summary>
/// ニュース効果の目盛り（Docs/News_Spec.md §7.1・§7.1.1）。
///
/// 記事マスターで空欄にした効果量は、ここの既定値で埋める。100本を手で書くとき
/// 数値がばらつくと「同じ規模の事件なのに効きが違う」になり推論が当てずっぽうに戻るので、
/// 原則は trendDelta だけ書き、残りはここから引く。
/// 調整が固まったら GameConstSettings へ移して RemoteBalance で配信する想定（現状はコード定数）。
/// </summary>
public static class NewsTuning
{
    // ---------------------------------------------------------------
    // 1段目: 掲載ターンの跳ね（面に比例。一面ほど高く買わされる）
    // ---------------------------------------------------------------

    public const float HypeFront = 1.06f;
    public const float HypeSecond = 1.04f;
    public const float HypeMarket = 1.03f;
    public const float HypeRumor = 1.02f;

    /// <summary>同じ銘柄に跳ねが重なったときの上限。ストップ高に張り付かせないため。</summary>
    public const float HypeMax = 1.12f;
    public const float HypeMin = 1f / HypeMax;

    /// <summary>
    /// 跳ねが剥がれるまでのターン数。<b>発効ターンから</b>この期間で元へ戻る。
    /// 本物なら同じターンに2段目（需要）が来て下支えし、誤報なら何も来ずに値だけ落ちる
    /// ＝「本来なら来るはずだった日」に高値掴みが露見する。
    /// </summary>
    public const int HypeUnwindTurns = 3;

    /// <summary>面の既定の跳ね。悪材料（trendDelta が負）は売られる側へ同じ幅で跳ねる。</summary>
    public static float DefaultHypeRate(string page, float trendDelta)
    {
        float rate = page switch
        {
            "front" => HypeFront,
            "second" => HypeSecond,
            "market" => HypeMarket,
            "rumor" => HypeRumor,
            _ => 1f,
        };
        return trendDelta < 0f ? 1f / rate : rate;
    }

    // ---------------------------------------------------------------
    // 2段目: 発効後の実需（小 / 中 / 大）
    // ---------------------------------------------------------------

    public const float TrendSmall = 0.3f, KickSmall = 0.03f; public const int DurationSmall = 3;
    public const float TrendMedium = 0.6f, KickMedium = 0.06f; public const int DurationMedium = 4;
    public const float TrendLarge = 1.0f, KickLarge = 0.10f; public const int DurationLarge = 5;

    /// <summary>
    /// 誇張記事（truth = exaggerated）の2段目に掛ける倍率。
    /// 事件そのものは本当だが、紙面ほどの規模ではない。跳ね（1段目）は満額乗るので、
    /// 一面の誇張記事を掴むと「当たったのに儲からない」になる。
    /// </summary>
    public const float ExaggeratedScale = 0.5f;

    /// <summary>trendDelta の大きさから一番近い規模の demandKick を返す（符号は trendDelta に揃える）。</summary>
    public static float DefaultDemandKick(float trendDelta)
    {
        float abs = Mathf.Abs(trendDelta);
        if (abs <= 0f) return 0f;
        float kick = abs < (TrendSmall + TrendMedium) * 0.5f ? KickSmall
                   : abs < (TrendMedium + TrendLarge) * 0.5f ? KickMedium
                   : KickLarge;
        return Mathf.Sign(trendDelta) * kick;
    }

    /// <summary>trendDelta の大きさから一番近い規模の duration を返す。</summary>
    public static int DefaultDuration(float trendDelta)
    {
        float abs = Mathf.Abs(trendDelta);
        return abs < (TrendSmall + TrendMedium) * 0.5f ? DurationSmall
             : abs < (TrendMedium + TrendLarge) * 0.5f ? DurationMedium
             : DurationLarge;
    }

    // ---------------------------------------------------------------
    // 結果記事・訂正記事
    // ---------------------------------------------------------------

    /// <summary>決着（発効 + duration）から結果記事が出るまでの遅れ。決着ターンの朝刊に載せる。</summary>
    public const int ResultDelay = 0;

    /// <summary>
    /// 決着から訂正記事が出るまでの遅れ。新聞は誤りを認めるのが遅い。
    /// 有料の「確認」（§9.1）が売っているのはこの遅れ（時間と確実性）。
    /// </summary>
    public const int CorrectionDelay = 1;

    // ---------------------------------------------------------------
    // 購読（§8）
    // ---------------------------------------------------------------

    /// <summary>購読枠の上限。5社の全購読はできない（情報は完全にならない）。</summary>
    public const int MaxSubscriptionSlots = 3;

    /// <summary>
    /// 店レベル → 購読枠。<b>枠の解放は店レベルのみ</b>（村メタでは増やさない）。
    /// 確定（§14 T4・2026-10-08）: Lv1=1枠 / Lv3=2枠 / Lv5=3枠。
    /// </summary>
    public static int SubscriptionSlotsFor(int shopLevel)
    {
        int slots = shopLevel >= 5 ? 3 : shopLevel >= 3 ? 2 : 1;
        return Mathf.Clamp(slots, 1, MaxSubscriptionSlots);
    }

    /// <summary>未購読（契約0件）のときに一面へ出す無料の壁新聞の本数。</summary>
    public const int WallPaperArticles = 2;

    // ---------------------------------------------------------------
    // スクラップと確認（§9）
    // ---------------------------------------------------------------

    /// <summary>スクラップ枠。確認すると名鑑へ移って空く。</summary>
    public const int ScrapCapacity = 3;

    /// <summary>決着した記事1件の真偽を確認する費用。</summary>
    public const int ConfirmCost = 1500;

    /// <summary>まとめて確認の件数と費用（3件 4,000G）。</summary>
    public const int ConfirmBatchCount = 3;
    public const int ConfirmBatchCost = 4000;

    // ---------------------------------------------------------------
    // フェーズ3: 事象＋社別テンプレート＋埋め草（Docs/News_Phase3_Spec.md）
    // ---------------------------------------------------------------

    /// <summary>
    /// draft / reviewed のテンプレート・埋め草も読み込むか（§12 U6）。
    /// エディタと開発ビルドでは読む。製品は approved（と状態の空欄）だけ。
    /// </summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool IncludeDraftTemplates = true;
#else
    public static bool IncludeDraftTemplates = false;
#endif

    /// <summary>1ターンに置く（発効させる）事象の数の確率。0件 / 1件 / 2件。</summary>
    public const float EventsPerTurnZero = 0.2f;
    public const float EventsPerTurnTwo = 0.2f;

    /// <summary>1周に置く「大」の事象の下限（Short は1）。</summary>
    public const int MinLargeEventsPerRun = 2;

    /// <summary>埋め草の既定の再登場間隔（同じ社で何ターン空けるか）。</summary>
    public const int FillerDefaultCooldown = 8;

    /// <summary>L3（客の法則）で対象にする最低の requiredLevel。高額帯の銘柄に効く。</summary>
    public const int L3MinRequiredLevel = 3;

    /// <summary>カテゴリから面を決める（§4 の規則3）。社が持たない面なら一面。</summary>
    public static string PageOfCategory(string category) => category switch
    {
        "dungeon" or "hero" => "second",
        "rival" or "supply" or "economy" => "market",
        "culture" or "village" => "rumor",
        _ => "front",
    };

    /// <summary>差し込み枠の候補（{region} {village} {season} {monster}）。事象・埋め草の binds に指定が無いときに使う。</summary>
    public static readonly string[] Regions = { "北の街道", "南の港町", "東の丘陵", "西の宿場", "王都の下町", "川沿いの集落" };
    public static readonly string[] Villages = { "ハルム村", "ロウエン村", "ミルデ村", "カスト村", "ベルン村" };
    public static readonly string[] Seasons = { "春", "夏", "秋", "冬" };
    public static readonly string[] Monsters = { "甲殻種", "群狼", "大蜘蛛", "岩喰い", "影の獣" };

    /// <summary>本物でも1社しか報じない事象の割合（Docs/News_Phase3_Spec.md §12 U2）。</summary>
    public const float TrueSingleReportRate = 0.25f;

    /// <summary>誤報の報道に署名が付く確率の倍率（社の署名率 × これ）。誤報は無署名に寄せる。</summary>
    public const float FalseReportBylineFactor = 0.7f;

    /// <summary>誤報の報道を一面以外（うわさ欄など）へ回す割合。残りは本物と同じく一面に載る。</summary>
    public const float FalseReportHideRate = 0.4f;
}

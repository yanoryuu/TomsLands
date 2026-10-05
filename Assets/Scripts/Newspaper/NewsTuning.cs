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
}

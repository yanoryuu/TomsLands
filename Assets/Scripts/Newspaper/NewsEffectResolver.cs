using UnityEngine;

/// <summary>
/// その時点で効いているニュースを集計し、経済計算に渡す係数を返す。
///
/// ニュースの効果は<b>2成分</b>に分かれる（Docs/News_Spec.md §7）。
///   ・TrendBias  … 遅効・持続。適正値（需要が向かう均衡点）を動かす本体
///   ・DemandKick … 発効ターンに1回だけ需要へ直撃。lead 1〜3 の短い窓でも体感が出るように
/// 需要の収束は 15%/ターンなので、Trend を動かすだけでは数ターンでは効きが見えない。
///
/// ・HypeRate  … 掲載ターンの価格倍率。<b>真偽に関わらず起きる</b>。
///   皆が同じ紙面を読んで飛びつくため。誤報はこれだけ起きて実体が来ない＝高値掴みになる。
/// </summary>
public class NewsEffectResolver
{
    private readonly NewsModel newsModel;

    public NewsEffectResolver(NewsModel newsModel)
    {
        this.newsModel = newsModel;
    }

    /// <summary>
    /// 適正値へのバイアス。
    /// <b>RuntimeItemData.Trend には加算しない。</b>Trend 自身はランダムウォークと減衰を
    /// 続けており、そこへ足すと期間終了時に剥がせなくなるため、別枠で保持して
    /// naturalDemand を求めるときにだけ足す。
    /// </summary>
    public float TrendBias(RuntimeItemData item, int turn)
    {
        if (newsModel == null || item == null) return 0f;

        float sum = 0f;
        foreach (var e in newsModel.ActiveEffectsOn(turn))
        {
            var a = e.Article;
            if (a != null && a.Matches(item)) sum += a.trendDelta;
        }
        return Mathf.Clamp(sum, -1f, 1f);
    }

    /// <summary>発効ターンに1回だけ需要へ乗る直撃分。</summary>
    public float DemandKick(RuntimeItemData item, int turn)
    {
        if (newsModel == null || item == null) return 0f;

        float sum = 0f;
        foreach (var e in newsModel.JustTriggeredOn(turn))
        {
            var a = e.Article;
            if (a != null && a.Matches(item)) sum += a.demandKick;
        }
        return sum;
    }

    /// <summary>
    /// 掲載ターンに価格へ乗る倍率。記事が派手な面ほど大きく、高く買わされる。
    /// 誤報でも発生する。
    /// </summary>
    public float HypeRate(RuntimeItemData item, int turn)
    {
        if (newsModel == null || item == null) return 1f;

        float rate = 1f;
        foreach (var e in newsModel.IssueOf(turn))
        {
            var a = e.Article;
            if (a == null || a.hypeRate <= 0f) continue;
            if (a.Matches(item)) rate *= a.hypeRate;
        }
        return rate;
    }
}

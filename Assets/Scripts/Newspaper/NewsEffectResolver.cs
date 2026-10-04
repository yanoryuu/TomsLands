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
///   跳ねは発効ターンから NewsTuning.HypeUnwindTurns かけて剥がれる。本物なら同じ日に
///   2段目の需要が来て値を支え、誤報なら値だけが落ちる。
///
/// 誇張記事（exaggerated）は2段目が NewsTuning.ExaggeratedScale 倍になる（NewsIssueEntry.effectScale）。
/// 返す係数はすべて「効果なし」で 0 / 0 / 1 になり、ItemModel 側は従来と同じ計算になる。
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
            if (a != null && a.Matches(item)) sum += a.trendDelta * e.effectScale;
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
            if (a != null && a.Matches(item)) sum += a.demandKick * e.effectScale;
        }
        return sum;
    }

    /// <summary>
    /// そのターンの価格に掛ける倍率（1段目）。
    ///   掲載ターン      … × hypeRate（記事が派手な面ほど大きく、高く買わされる）
    ///   発効ターンから  … × hypeRate^(-1/N) を N ターン（N = HypeUnwindTurns）。合計で跳ねが剥がれる
    /// 誤報でも同じように起きる。掲載から発効までの間は跳ねたまま据え置かれるので、
    /// 「本当に来るか」はその間の値動きからは判別できない。
    /// </summary>
    public float HypeRate(RuntimeItemData item, int turn)
    {
        if (newsModel == null || item == null) return 1f;

        int unwindTurns = NewsTuning.HypeUnwindTurns;
        float rate = 1f;
        foreach (var e in newsModel.HypeSourcesOn(turn))
        {
            var a = e.Article;
            if (a == null || a.hypeRate <= 0f || Mathf.Approximately(a.hypeRate, 1f)) continue;
            if (!a.Matches(item)) continue;

            if (turn == e.publishTurn)
            {
                rate *= a.hypeRate;
                continue;
            }

            int unwindStart = Mathf.Max(e.effectTurn, e.publishTurn + 1);
            if (unwindTurns > 0 && turn >= unwindStart)
                rate *= Mathf.Pow(a.hypeRate, -1f / unwindTurns);
        }
        return Mathf.Clamp(rate, NewsTuning.HypeMin, NewsTuning.HypeMax);
    }
}

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
    /// 効果は<b>事象ごとに1回</b>（同じ事象を何社が報じても重ならない。Docs/News_Phase3_Spec.md §3.3）。
    /// </summary>
    public float TrendBias(RuntimeItemData item, int turn)
    {
        if (newsModel == null || item == null) return 0f;

        float sum = 0f;
        foreach (var e in newsModel.ActiveEffectsOn(turn))
            if (e.Matches(item)) sum += e.trendDelta * e.effectScale;
        return Mathf.Clamp(sum, -1f, 1f);
    }

    /// <summary>発効ターンに1回だけ需要へ乗る直撃分。</summary>
    public float DemandKick(RuntimeItemData item, int turn)
    {
        if (newsModel == null || item == null) return 0f;

        float sum = 0f;
        foreach (var e in newsModel.JustTriggeredOn(turn))
            if (e.Matches(item)) sum += e.demandKick * e.effectScale;
        return sum;
    }

    /// <summary>
    /// そのターンの価格に掛ける倍率（1段目）。
    ///   報じられた日   … × その日の跳ね（同じ事象は1日1回。その日の一番大きい面の値）
    ///   剥がれ始めから … × (跳ねの累計)^(-1/N) を N ターン（N = HypeUnwindTurns）。合計で跳ねが剥がれる
    /// 誤報でも同じように起きる。報道から発効までの間は跳ねたまま据え置かれるので、
    /// 「本当に来るか」はその間の値動きからは判別できない。
    /// </summary>
    public float HypeRate(RuntimeItemData item, int turn)
    {
        if (newsModel == null || item == null) return 1f;

        int unwindTurns = NewsTuning.HypeUnwindTurns;
        float rate = 1f;
        foreach (var e in newsModel.HypeSourcesOn(turn))
        {
            if (!e.Matches(item)) continue;

            if (e.hypeByDay.TryGetValue(turn, out var today))
            {
                rate *= today;
                continue;
            }

            int start = e.UnwindStart;
            if (unwindTurns > 0 && turn >= start && turn < start + unwindTurns)
                rate *= Mathf.Pow(e.TotalHype, -1f / unwindTurns);
        }
        return Mathf.Clamp(rate, NewsTuning.HypeMin, NewsTuning.HypeMax);
    }
}

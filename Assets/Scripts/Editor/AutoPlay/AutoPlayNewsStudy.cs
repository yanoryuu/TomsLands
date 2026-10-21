using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>新聞の事象1件の値動き（効き先の品の平均 ÷ 効き先でない品の平均）。</summary>
public sealed class AutoPlayNewsEventRow
{
    public string RunId;
    public string Bot;
    public string EventKey;
    public string Truth;         // true / exaggerated / false
    public int ScaleRank;        // 1 小 / 2 中 / 3 大 / 4 特大
    public int Sign;             // +1 / -1
    public int Lead;             // 最初の報道 → 発効
    public int Duration;
    public int Matched;          // 効き先で解放済みの品の数
    public bool Reported;
    /// <summary>log(値段比) の「効き先 − 効き先以外」。0.10 ≒ +10%。</summary>
    public float Hype;           // 報道の朝 ÷ 前の朝
    public float PubToEffect;    // 発効の朝 ÷ 報道の朝
    public float PubToBest;      // 効いている間の一番高い朝 ÷ 報道の朝
    public float PubToSettle;    // 決着の朝 ÷ 報道の朝
    public float PubToAfter;     // 決着+3 の朝 ÷ 報道の朝
    public float PreToBest;      // 効いている間の一番高い朝 ÷ 報道の前の朝（報道より前に買えた場合）
    public float DemandGain;     // 決着の朝の需要 − 報道の朝の需要（差）
    public float CustomerGain;   // 効いている間の客の数の平均 ÷ 報道の朝の客の数（log）
}

/// <summary>
/// 新聞の効果の計測（イベントスタディ）。各ランの朝の相場（AutoPlayHeadlessGame.MorningMarket）と事象の予定から、
/// 「報道の日に買って、効いている間に売る」と何%動くかを、効き先でない品との差で測る。
/// </summary>
public static class AutoPlayNewsStudy
{
    public static void Collect(AutoPlayHeadlessGame game, AutoPlayRunResult result)
    {
        try
        {
            var market = game.MorningMarket;
            if (market == null || market.Count == 0 || game.News == null) return;
            int lastTurn = market.Keys.Max();
            var items = game.ItemModel.RuntimeItems;
            foreach (var ev in game.News.Events)
            {
                if (!ev.HasTarget || ev.trendDelta == 0f) continue;
                int pub = ev.hypeByDay.Count > 0 ? ev.hypeByDay.Keys.Min() : ev.effectTurn;
                int eff = ev.effectTurn;
                int settle = ev.SettleTurn;
                if (pub < 2 || settle + 3 > lastTurn) continue;

                var matched = items.Where(r => ev.Matches(r)).Select(r => r.ItemId).ToHashSet();
                // その日に値段がある（解放済み）品だけ
                bool Has(int t, string id) => Near(market, t) is { } m && m.ContainsKey(id);
                var tgt = matched.Where(id => Has(pub - 1, id) && Has(pub, id)).ToList();
                var ctl = items.Select(r => r.ItemId).Where(id => !matched.Contains(id) && Has(pub - 1, id) && Has(pub, id)).ToList();
                if (tgt.Count == 0 || ctl.Count == 0) continue;

                float L(List<string> ids, int a, int b) => (float)ids.Average(id => Math.Log(Price(market, b, id) / Price(market, a, id)));
                float Best(List<string> ids, int from) => (float)ids.Average(id =>
                {
                    double best = double.MinValue;
                    for (int t = eff + 1; t <= settle; t++) best = Math.Max(best, Math.Log(Price(market, t, id) / Price(market, from, id)));
                    return best;
                });

                var row = new AutoPlayNewsEventRow
                {
                    RunId = result.RunId,
                    Bot = result.Bot,
                    EventKey = ev.key,
                    Truth = ev.truth,
                    ScaleRank = ev.scaleRank,
                    Sign = ev.trendDelta < 0f ? -1 : 1,
                    Lead = eff - pub,
                    Duration = ev.durationTurns,
                    Matched = tgt.Count,
                    Reported = ev.hypeByDay.Count > 0,
                    Hype = L(tgt, pub - 1, pub) - L(ctl, pub - 1, pub),
                    PubToEffect = L(tgt, pub, eff) - L(ctl, pub, eff),
                    PubToBest = Best(tgt, pub) - Best(ctl, pub),
                    PubToSettle = L(tgt, pub, settle) - L(ctl, pub, settle),
                    PubToAfter = L(tgt, pub, settle + 3) - L(ctl, pub, settle + 3),
                    PreToBest = Best(tgt, pub - 1) - Best(ctl, pub - 1),
                    DemandGain = (float)(tgt.Average(id => Demand(market, settle, id) - Demand(market, pub, id))
                                       - ctl.Average(id => Demand(market, settle, id) - Demand(market, pub, id))),
                    CustomerGain = (float)(tgt.Average(id => CustLog(market, pub, eff, settle, id)) - ctl.Average(id => CustLog(market, pub, eff, settle, id))),
                };
                result.NewsEventRows.Add(row);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AutoPlayNewsStudy] 集計に失敗: {e.Message}");
        }
    }

    private static Dictionary<string, (int price, float demand, int customers)> Near(
        Dictionary<int, Dictionary<string, (int price, float demand, int customers)>> m, int turn)
    {
        // 配信日などで朝の記録が無い日は、その前の一番近い朝で代える
        for (int t = turn; t >= turn - 3; t--)
            if (m.TryGetValue(t, out var d)) return d;
        return null;
    }

    private static double Price(Dictionary<int, Dictionary<string, (int price, float demand, int customers)>> m, int t, string id)
    {
        var d = Near(m, t);
        return d != null && d.TryGetValue(id, out var v) ? Math.Max(1, v.price) : 1;
    }

    private static double Demand(Dictionary<int, Dictionary<string, (int price, float demand, int customers)>> m, int t, string id)
    {
        var d = Near(m, t);
        return d != null && d.TryGetValue(id, out var v) ? v.demand : 0;
    }

    private static double CustLog(Dictionary<int, Dictionary<string, (int price, float demand, int customers)>> m, int pub, int eff, int settle, string id)
    {
        var d0 = Near(m, pub);
        if (d0 == null || !d0.TryGetValue(id, out var v0) || v0.customers <= 0) return 0;
        double sum = 0; int n = 0;
        for (int t = eff + 1; t <= settle; t++)
        {
            var d = Near(m, t);
            if (d != null && d.TryGetValue(id, out var v) && v.customers > 0) { sum += v.customers; n++; }
        }
        return n > 0 ? Math.Log(sum / n / v0.customers) : 0;
    }

    /// <summary>真偽×規模ごとの平均を表にする（％は exp(log)−1）。</summary>
    public static string Summarize(IEnumerable<AutoPlayRunResult> runs)
    {
        var rows = runs.SelectMany(r => r.NewsEventRows).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("## 新聞の事象ごとの値動き（効き先の品 − 効き先でない品。報道の朝に買った場合）");
        sb.AppendLine();
        sb.AppendLine("| 真偽 | 規模 | 向き | 件数 | 跳ね（報道の朝） | 報道→発効 | 報道→効いている間の最高 | 報道→決着 | 報道→決着+3 | 報道の前→最高 | 需要の差 | 客の数 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        string P(double x) => ((Math.Exp(x) - 1) * 100).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "%";
        foreach (var g in rows.Where(r => r.Reported).GroupBy(r => (r.Truth, r.ScaleRank, r.Sign)).OrderBy(g => g.Key.Truth).ThenBy(g => g.Key.ScaleRank).ThenByDescending(g => g.Key.Sign))
        {
            var l = g.ToList();
            sb.AppendLine($"| {g.Key.Truth} | {g.Key.ScaleRank} | {(g.Key.Sign > 0 ? "上" : "下")} | {l.Count} | {P(l.Average(x => x.Hype))} | {P(l.Average(x => x.PubToEffect))} | " +
                          $"{P(l.Average(x => x.PubToBest))} | {P(l.Average(x => x.PubToSettle))} | {P(l.Average(x => x.PubToAfter))} | {P(l.Average(x => x.PreToBest))} | " +
                          $"{l.Average(x => x.DemandGain):+0.000;-0.000} | {P(l.Average(x => x.CustomerGain))} |");
        }
        int unreported = rows.Count(r => !r.Reported);
        var truthShare = rows.Where(r => r.Reported).GroupBy(r => r.Truth).Select(g => $"{g.Key}={g.Count()}");
        sb.AppendLine();
        sb.AppendLine($"報道のあった事象の真偽: {string.Join(" / ", truthShare)}。報道の無い（効果だけ）事象: {unreported} 件。");
        // 上がる記事（本物・誇張・誤報を全部）を信じて報道の朝に買ったときの平均
        var upRows = rows.Where(r => r.Reported && r.Sign > 0).ToList();
        if (upRows.Count > 0)
            sb.AppendLine($"「上がる」記事を全部信じて報道の朝に買い、効いている間の最高で売った平均: {P(upRows.Average(x => x.PubToBest))}（決着の朝で売る: {P(upRows.Average(x => x.PubToSettle))}）。");
        return sb.ToString();
    }
}

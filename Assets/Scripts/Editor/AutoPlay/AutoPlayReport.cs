using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

/// <summary>
/// レポート出力。runs.csv / turns.csv / decisions.csv / anomalies.csv / summary.json / summary.md。
/// 出力先はプロジェクト直下の AutoPlayReports/（.gitignore 済み）。API キーは一切書かない。
/// </summary>
public static class AutoPlayReport
{
    /// <summary>概算の為替（表示用）。</summary>
    public const double JpyPerUsd = 150.0;

    public static string Write(string dir, AutoPlayBatchConfig config, IReadOnlyList<AutoPlayRunResult> runs)
    {
        Directory.CreateDirectory(dir);
        var inv = CultureInfo.InvariantCulture;

        // --- runs.csv ---
        var sb = new StringBuilder();
        sb.AppendLine("runId,bot,seed,mode,outcome,turns,finalMoney,peakMoney,minMoney,finalNetWorth,battles,heroWins,heroLosses,heroFinalLevel," +
                      "shopIncome,streamEarnings,defeatRewards,spend,debtPaid,supportSpend,upgradeSpend,supports,rejectedActions," +
                      "anomalies,logErrors,logWarnings,jevRequests,inputTokens,costUsd,wallSeconds," +
                      "interventions,interventionSpent,interventionRefund,heroSideSpent,dungeonSideSpent,dungeonFlips,dungeonFlipRewards,dungeonStreamRewards,heroFlips");
        foreach (var r in runs)
        {
            sb.AppendLine(string.Join(",", Csv(r.RunId), Csv(r.Bot), r.Seed, r.Mode, r.Outcome, r.Turns, r.FinalMoney, r.PeakMoney, r.MinMoney,
                r.FinalNetWorth, r.Battles, r.HeroWins, r.HeroLosses, r.HeroFinalLevel, r.TotalShopIncome, r.TotalStreamEarnings,
                r.TotalDefeatRewards, r.TotalSpend, r.TotalDebtPaid, r.TotalSupportSpend, r.TotalUpgradeSpend, r.Supports, r.RejectedActions,
                r.Anomalies.Count, r.LogErrors, r.LogWarnings, r.JevRequests, r.InputTokens,
                r.CostUsd.ToString("F6", inv), r.WallSeconds.ToString("F1", inv),
                r.InterventionCount, r.InterventionSpent, r.InterventionRefund, r.HeroSideSpent, r.DungeonSideSpent,
                r.DungeonFlips, r.DungeonFlipRewards, r.DungeonStreamRewards, r.HeroFlips));
        }
        File.WriteAllText(Path.Combine(dir, "runs.csv"), sb.ToString(), new UTF8Encoding(true));

        // --- turns.csv（所持金推移） ---
        sb.Clear();
        sb.AppendLine("runId,bot,turn,flowIndex,stage,money,stockValue,pendingOrders,netWorth,spend,shopIncome,streamEarnings,defeatReward," +
                      "debtPaid,supportSpend,upgradeSpend,battle,battleDungeon,clearPct,heroLevel,buzz,displayedKinds,rejected,interventionNet");
        foreach (var r in runs)
        foreach (var t in r.TurnRows)
        {
            sb.AppendLine(string.Join(",", Csv(t.RunId), Csv(r.Bot), t.Turn, t.FlowIndex, t.Stage, t.Money, t.StockValue, t.PendingOrders, t.NetWorth,
                t.Spend, t.ShopIncome, t.StreamEarnings, t.DefeatReward, t.DebtPaid, t.SupportSpend, t.UpgradeSpend, Csv(t.Battle),
                Csv(t.BattleDungeon), t.ClearPct.ToString("F0", inv), t.HeroLevel, Csv(t.Buzz), t.DisplayedKinds, t.Rejected, t.InterventionNet));
        }
        File.WriteAllText(Path.Combine(dir, "turns.csv"), sb.ToString(), new UTF8Encoding(true));

        // --- streams.csv（配信1回1行・介入の内訳） ---
        sb.Clear();
        sb.AppendLine("runId,bot,turn,dungeon,clearPct,baselineHeroWin,heroWin,planned,executed,skipped,heroSideSpent,dungeonSideSpent," +
                      "refund,specialMoves,viewerSpecials,rawSales,defeatReward,turns,baselineTurns");
        foreach (var r in runs)
        foreach (var x in r.StreamRows)
        {
            sb.AppendLine(string.Join(",", Csv(x.RunId), Csv(r.Bot), x.Turn, Csv(x.Dungeon), x.DisplayedClearPct.ToString("F0", inv),
                x.BaselineHeroWin ? 1 : 0, x.HeroWin ? 1 : 0, Csv(x.Planned), Csv(x.Executed), Csv(x.Skipped), x.HeroSideSpent,
                x.DungeonSideSpent, x.Refund, x.SpecialMoves, x.ViewerSpecials, x.RawSales, x.DefeatReward, x.Turns, x.BaselineTurns));
        }
        File.WriteAllText(Path.Combine(dir, "streams.csv"), sb.ToString(), new UTF8Encoding(true));

        // --- decisions.csv（判断と確率分布） ---
        sb.Clear();
        sb.AppendLine("runId,bot,turn,phase,question,chosen,score,confidence,probabilities");
        foreach (var r in runs)
        foreach (var d in r.Decisions)
        {
            string probs = d.Probabilities == null ? "" : JsonConvert.SerializeObject(
                d.Probabilities.OrderByDescending(kv => kv.Value).Take(8).ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 3)));
            sb.AppendLine(string.Join(",", Csv(r.RunId), Csv(r.Bot), d.Turn, d.Phase, d.Question, Csv(d.Chosen),
                d.Score.HasValue ? d.Score.Value.ToString("F3", inv) : "", d.Confidence.HasValue ? d.Confidence.Value.ToString("F3", inv) : "",
                Csv(probs)));
        }
        File.WriteAllText(Path.Combine(dir, "decisions.csv"), sb.ToString(), new UTF8Encoding(true));

        // --- anomalies.csv ---
        sb.Clear();
        sb.AppendLine("runId,bot,turn,kind,detail");
        foreach (var r in runs)
        foreach (var a in r.Anomalies)
            sb.AppendLine(string.Join(",", Csv(a.RunId), Csv(r.Bot), a.Turn, a.Kind, Csv(a.Detail)));
        File.WriteAllText(Path.Combine(dir, "anomalies.csv"), sb.ToString(), new UTF8Encoding(true));

        // --- summary.json / summary.md ---
        var summary = BuildSummary(config, runs);
        File.WriteAllText(Path.Combine(dir, "summary.json"), JsonConvert.SerializeObject(summary, Formatting.Indented), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "summary.md"), BuildMarkdown(config, summary, runs), new UTF8Encoding(false));
        return dir;
    }

    // =================================================================
    // 集計
    // =================================================================

    public sealed class BotSummary
    {
        public string Bot;
        public int Runs;
        public int Completed;
        public int Bankrupt;
        public int Stalled;
        public int Crashed;
        public double BankruptcyRate;
        public double MoneyMean;
        public double MoneyMedian;
        public double MoneyP10;
        public double MoneyP90;
        public double NetWorthMean;
        public double NetWorthMedian;
        public double NetWorthP10;
        /// <summary>合格ラインの判定（項目 → "OK/NG 実測値"）。</summary>
        public Dictionary<string, string> Verdicts = new Dictionary<string, string>();
        public bool Passed;

        // --- 配信中の介入 ---
        public double InterventionsPerRun;
        public double InterventionSpentMean;
        public double InterventionRefundMean;
        public double HeroSideSpentMean;
        public double DungeonSideSpentMean;
        public Dictionary<string, int> InterventionKinds = new Dictionary<string, int>();
        public int StreamsWithIntervention;
        public int StreamsWithoutIntervention;
        public double HeroWinRateWithIntervention;
        public double HeroWinRateWithoutIntervention;
        /// <summary>介入した配信のうち「介入なしなら勝敗が違った」割合。</summary>
        public double InterventionFlipRate;
        public int DungeonSideSpentTotal;
        public int DungeonStreamRewardsTotal;
        public int DungeonFlips;
        public int DungeonFlipRewardsTotal;
        /// <summary>ダンジョン側の介入の元が取れたか: 介入で反転した配信の防衛報酬 ÷ ダンジョン側の支出（1以上で黒字）。</summary>
        public double DungeonRoi;
        public int HeroFlips;

        // --- 防衛報酬狙いの収支（魔王軍支援＋介入） ---
        public int DefeatRewardsTotal;
        public int SupportSpendTotal;
        public int InterventionNetTotal;
        /// <summary>防衛報酬 ÷ (支援費 ＋ 介入の純支出)。1 を超えれば防衛報酬だけで元が取れている。</summary>
        public double DefenseRoi;
        /// <summary>1ラン単位で ROI が 1 を超えたラン数。</summary>
        public int RunsWithDefenseRoiAbove1;
        public double HeroWinRate;
        public double HeroFinalLevelMean;
        public double ShopIncomeMean;
        public double StreamEarningsMean;
        public double DefeatRewardsMean;
        public double SupportSpendMean;
        public double RejectedActionsMean;
        public int Anomalies;
        public int JevRequests;
        public long InputTokens;
        public double CostUsd;
        public Dictionary<string, int> AnomalyKinds = new Dictionary<string, int>();
        /// <summary>質問 → 選ばれた値の頻度（上位）。</summary>
        public Dictionary<string, Dictionary<string, int>> DecisionCounts = new Dictionary<string, Dictionary<string, int>>();
        /// <summary>質問 → score の平均（score 型の質問のみ）。</summary>
        public Dictionary<string, double> DecisionScoreMean = new Dictionary<string, double>();
        /// <summary>ターン → 所持金の平均（営業日の行）。</summary>
        public SortedDictionary<int, double> MoneyByTurn = new SortedDictionary<int, double>();
    }

    public sealed class BatchSummary
    {
        public string CreatedAt;
        public AutoPlayBatchConfig Config;
        public int TotalRuns;
        public int TotalJevRequests;
        public long TotalInputTokens;
        public double TotalCostUsd;
        public double TotalCostJpyApprox;
        public List<BotSummary> Bots = new List<BotSummary>();
    }

    private static BatchSummary BuildSummary(AutoPlayBatchConfig config, IReadOnlyList<AutoPlayRunResult> runs)
    {
        var s = new BatchSummary
        {
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Config = config,
            TotalRuns = runs.Count,
            TotalJevRequests = runs.Sum(r => r.JevRequests),
            TotalInputTokens = runs.Sum(r => r.InputTokens),
            TotalCostUsd = runs.Sum(r => r.CostUsd),
        };
        s.TotalCostJpyApprox = s.TotalCostUsd * JpyPerUsd;

        foreach (var g in runs.GroupBy(r => r.Bot))
        {
            var list = g.ToList();
            var money = list.Select(r => (double)r.FinalMoney).OrderBy(x => x).ToList();
            int battles = list.Sum(r => r.Battles);
            var b = new BotSummary
            {
                Bot = g.Key,
                Runs = list.Count,
                Completed = list.Count(r => r.Outcome == "Completed"),
                Bankrupt = list.Count(r => r.Outcome == "Bankrupt"),
                Stalled = list.Count(r => r.Outcome == "Stalled"),
                Crashed = list.Count(r => r.Outcome == "Crashed"),
                MoneyMean = money.Count > 0 ? money.Average() : 0,
                MoneyMedian = Percentile(money, 0.5),
                MoneyP10 = Percentile(money, 0.1),
                MoneyP90 = Percentile(money, 0.9),
                NetWorthMean = list.Average(r => (double)r.FinalNetWorth),
                HeroWinRate = battles > 0 ? list.Sum(r => r.HeroWins) / (double)battles : 0,
                HeroFinalLevelMean = list.Average(r => (double)r.HeroFinalLevel),
                ShopIncomeMean = list.Average(r => (double)r.TotalShopIncome),
                StreamEarningsMean = list.Average(r => (double)r.TotalStreamEarnings),
                DefeatRewardsMean = list.Average(r => (double)r.TotalDefeatRewards),
                SupportSpendMean = list.Average(r => (double)r.TotalSupportSpend),
                RejectedActionsMean = list.Average(r => (double)r.RejectedActions),
                Anomalies = list.Sum(r => r.Anomalies.Count),
                JevRequests = list.Sum(r => r.JevRequests),
                InputTokens = list.Sum(r => r.InputTokens),
                CostUsd = list.Sum(r => r.CostUsd),
            };
            b.BankruptcyRate = b.Runs > 0 ? b.Bankrupt / (double)b.Runs : 0;
            var nw = list.Select(r => (double)r.FinalNetWorth).OrderBy(x => x).ToList();
            b.NetWorthMedian = Percentile(nw, 0.5);
            b.NetWorthP10 = Percentile(nw, 0.1);

            // --- 配信中の介入 ---
            b.InterventionsPerRun = list.Average(r => (double)r.InterventionCount);
            b.InterventionSpentMean = list.Average(r => (double)r.InterventionSpent);
            b.InterventionRefundMean = list.Average(r => (double)r.InterventionRefund);
            b.HeroSideSpentMean = list.Average(r => (double)r.HeroSideSpent);
            b.DungeonSideSpentMean = list.Average(r => (double)r.DungeonSideSpent);
            foreach (var kv in list.SelectMany(r => r.InterventionKinds).GroupBy(kv => kv.Key))
                b.InterventionKinds[kv.Key] = kv.Sum(x => x.Value);
            var streams = list.SelectMany(r => r.StreamRows).ToList();
            var withIv = streams.Where(x => x.HeroSideSpent + x.DungeonSideSpent > 0).ToList();
            var withoutIv = streams.Where(x => x.HeroSideSpent + x.DungeonSideSpent == 0).ToList();
            b.StreamsWithIntervention = withIv.Count;
            b.StreamsWithoutIntervention = withoutIv.Count;
            b.HeroWinRateWithIntervention = withIv.Count > 0 ? withIv.Count(x => x.HeroWin) / (double)withIv.Count : 0;
            b.HeroWinRateWithoutIntervention = withoutIv.Count > 0 ? withoutIv.Count(x => x.HeroWin) / (double)withoutIv.Count : 0;
            b.InterventionFlipRate = withIv.Count > 0 ? withIv.Count(x => x.HeroWin != x.BaselineHeroWin) / (double)withIv.Count : 0;
            b.DungeonSideSpentTotal = list.Sum(r => r.DungeonSideSpent);
            b.DungeonStreamRewardsTotal = list.Sum(r => r.DungeonStreamRewards);
            b.DungeonFlips = list.Sum(r => r.DungeonFlips);
            b.DungeonFlipRewardsTotal = list.Sum(r => r.DungeonFlipRewards);
            b.DungeonRoi = b.DungeonSideSpentTotal > 0 ? b.DungeonFlipRewardsTotal / (double)b.DungeonSideSpentTotal : 0;
            b.HeroFlips = list.Sum(r => r.HeroFlips);
            b.DefeatRewardsTotal = list.Sum(r => r.TotalDefeatRewards);
            b.SupportSpendTotal = list.Sum(r => r.TotalSupportSpend);
            b.InterventionNetTotal = list.Sum(r => r.InterventionSpent - r.InterventionRefund);
            int invest = b.SupportSpendTotal + b.InterventionNetTotal;
            b.DefenseRoi = invest > 0 ? b.DefeatRewardsTotal / (double)invest : 0;
            b.RunsWithDefenseRoiAbove1 = list.Count(r =>
            {
                int inv = r.TotalSupportSpend + r.InterventionSpent - r.InterventionRefund;
                return inv > 0 && r.TotalDefeatRewards > inv;
            });

            foreach (var kind in list.SelectMany(r => r.Anomalies).GroupBy(a => a.Kind))
                b.AnomalyKinds[kind.Key] = kind.Count();

            foreach (var q in list.SelectMany(r => r.Decisions).GroupBy(d => d.Phase + "." + d.Question))
            {
                b.DecisionCounts[q.Key] = q.GroupBy(d => Bucket(d.Chosen))
                    .OrderByDescending(x => x.Count()).Take(10)
                    .ToDictionary(x => x.Key, x => x.Count());
                var scores = q.Where(d => d.Score.HasValue).Select(d => (double)d.Score.Value).ToList();
                if (scores.Count > 0) b.DecisionScoreMean[q.Key] = scores.Average();
            }

            foreach (var t in list.SelectMany(r => r.TurnRows).Where(t => t.Stage == "shop").GroupBy(t => t.Turn))
                b.MoneyByTurn[t.Key] = t.Average(x => (double)x.Money);

            if (config.Criteria != null) config.Criteria.Judge(b, config.InitMoneyOverride > 0 ? config.InitMoneyOverride : GameConst.InitMoney);
            s.Bots.Add(b);
        }
        return s;
    }

    /// <summary>判断の値をまとめる（数値の予算などは桁でまとめる）。</summary>
    private static string Bucket(string chosen)
    {
        if (string.IsNullOrEmpty(chosen)) return "(none)";
        if (int.TryParse(chosen, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1000)
        {
            int mag = (int)Math.Pow(10, Math.Floor(Math.Log10(n)));
            return $"~{n / mag * mag}";
        }
        return chosen.Length > 40 ? chosen.Substring(0, 40) : chosen;
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        double idx = p * (sorted.Count - 1);
        int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
    }

    // =================================================================
    // Markdown
    // =================================================================

    private static string BuildMarkdown(AutoPlayBatchConfig config, BatchSummary s, IReadOnlyList<AutoPlayRunResult> runs)
    {
        var md = new StringBuilder();
        md.AppendLine("# Jev AutoPlay レポート");
        md.AppendLine();
        md.AppendLine($"- 作成: {s.CreatedAt}");
        md.AppendLine($"- モード: {config.Mode} / シード {config.BaseSeed}〜{config.BaseSeed + config.Seeds - 1}（{config.Seeds}本）/ ボット: {string.Join(", ", s.Bots.Select(b => b.Bot))}");
        md.AppendLine($"- 配信サロゲート: 販売回数倍率 {config.StreamSalesScale:F2} / 勝敗 {(config.ProbabilisticBattle ? "クリア確率で抽選" : "ドライラン（決定論）")}");
        md.AppendLine($"- Jev: {s.TotalJevRequests} リクエスト / 入力 {s.TotalInputTokens:N0} tokens / 約 ${s.TotalCostUsd:F4}（≒{s.TotalCostJpyApprox:F1}円）");
        md.AppendLine();
        md.AppendLine("> 配信（戦闘・配信販売）はサロゲートで解決している。配信の売上と勝敗は実機と一致しない可能性がある（Docs/Jev_AutoPlay_Design.md §2.4）。");
        md.AppendLine();

        md.AppendLine("## ボット別の結果");
        md.AppendLine();
        md.AppendLine("| ボット | ラン | 完走 | 破産 | 停滞/例外 | 破産率 | 最終所持金 平均 | 中央値 | P10 | P90 | 純資産 平均 | 勇者勝率 | 勇者Lv | 営業入金 | 配信売上 | 防衛報酬 | 支援費 | 却下手 | 異常 |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var b in s.Bots)
        {
            md.AppendLine($"| {b.Bot} | {b.Runs} | {b.Completed} | {b.Bankrupt} | {b.Stalled}/{b.Crashed} | {b.BankruptcyRate:P0} | " +
                          $"{b.MoneyMean:N0} | {b.MoneyMedian:N0} | {b.MoneyP10:N0} | {b.MoneyP90:N0} | {b.NetWorthMean:N0} | {b.HeroWinRate:P0} | " +
                          $"{b.HeroFinalLevelMean:F1} | {b.ShopIncomeMean:N0} | {b.StreamEarningsMean:N0} | {b.DefeatRewardsMean:N0} | " +
                          $"{b.SupportSpendMean:N0} | {b.RejectedActionsMean:F1} | {b.Anomalies} |");
        }
        md.AppendLine();

        md.AppendLine("## 合格ライン（暫定）の判定");
        md.AppendLine();
        if (config.Criteria != null) md.AppendLine("- 基準: " + config.Criteria.Describe(config.InitMoneyOverride > 0 ? config.InitMoneyOverride : GameConst.InitMoney));
        md.AppendLine();
        var keys = s.Bots.SelectMany(b => b.Verdicts.Keys).Distinct().ToList();
        md.AppendLine("| ボット | 総合 | " + string.Join(" | ", keys) + " |");
        md.AppendLine("|---|---|" + string.Concat(keys.Select(_ => "---|")));
        foreach (var b in s.Bots)
            md.AppendLine($"| {b.Bot} | {(b.Passed ? "**合格**" : "不合格")} | " + string.Join(" | ", keys.Select(k => b.Verdicts.TryGetValue(k, out var v) ? v : ""))+ " |");
        md.AppendLine();

        md.AppendLine("## 所持金の推移（営業日・平均）");
        md.AppendLine();
        var turns = s.Bots.SelectMany(b => b.MoneyByTurn.Keys).Distinct().OrderBy(t => t).ToList();
        md.AppendLine("| ターン | " + string.Join(" | ", s.Bots.Select(b => b.Bot)) + " |");
        md.AppendLine("|---|" + string.Concat(s.Bots.Select(_ => "---|")));
        foreach (var t in turns)
            md.AppendLine($"| {t} | " + string.Join(" | ", s.Bots.Select(b => b.MoneyByTurn.TryGetValue(t, out var m) ? m.ToString("N0") : "")) + " |");
        md.AppendLine();

        md.AppendLine("## 配信中の介入（スパチャ）");
        md.AppendLine();
        md.AppendLine("「反転」= 同じ配信を介入なしでドライランした場合と勝敗が変わったもの。ROI = 反転させた配信の防衛報酬 ÷ ダンジョン側の支出（1以上で元が取れている）。");
        md.AppendLine();
        md.AppendLine("| ボット | 介入/ラン | 支出 平均 | 返金 平均 | 勇者側 | ダンジョン側 | 介入あり配信 | 勇者勝率(あり) | 勇者勝率(なし) | 反転率 | 勇者側で勝ちに反転 | ダンジョン側で負けに反転 | 反転の防衛報酬 | ダンジョン側配信の防衛報酬 | ROI |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var b in s.Bots)
        {
            md.AppendLine($"| {b.Bot} | {b.InterventionsPerRun:F1} | {b.InterventionSpentMean:N0} | {b.InterventionRefundMean:N0} | {b.HeroSideSpentMean:N0} | " +
                          $"{b.DungeonSideSpentMean:N0} | {b.StreamsWithIntervention}/{b.StreamsWithIntervention + b.StreamsWithoutIntervention} | " +
                          $"{b.HeroWinRateWithIntervention:P0} | {b.HeroWinRateWithoutIntervention:P0} | {b.InterventionFlipRate:P0} | {b.HeroFlips} | " +
                          $"{b.DungeonFlips} | {b.DungeonFlipRewardsTotal:N0} | {b.DungeonStreamRewardsTotal:N0} | " +
                          (b.DungeonSideSpentTotal > 0 ? $"{b.DungeonRoi:F2}{(b.DungeonRoi >= 1 ? "（黒字）" : "（赤字）")}" : "-") + " |");
        }
        md.AppendLine();
        md.AppendLine("**種類別の実行回数**（viewer_special = 視聴者の赤スパ起点の必殺技）");
        md.AppendLine();
        foreach (var b in s.Bots.Where(b => b.InterventionKinds.Count > 0))
            md.AppendLine($"- {b.Bot}: " + string.Join(", ", b.InterventionKinds.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}×{kv.Value}")));
        md.AppendLine();

        // 対照群（介入なしで同じシードを回したボット「〜+noint」）との比較
        var pairs = s.Bots.Where(b => !b.Bot.EndsWith(AutoPlayBatch.NoInterventionSuffix))
            .Select(b => (with: b, without: s.Bots.FirstOrDefault(x => x.Bot == b.Bot + AutoPlayBatch.NoInterventionSuffix)))
            .Where(p => p.without != null).ToList();
        if (pairs.Count > 0)
        {
            md.AppendLine("**介入あり／なし（同じシードの対照群）**");
            md.AppendLine();
            md.AppendLine("| ボット | 勇者勝率 あり→なし | 純資産中央値 あり | なし | 差 | 破産率 あり→なし |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (var (with, without) in pairs)
            {
                md.AppendLine($"| {with.Bot} | {with.HeroWinRate:P0} → {without.HeroWinRate:P0} | {with.NetWorthMedian:N0} | {without.NetWorthMedian:N0} | " +
                              $"{with.NetWorthMedian - without.NetWorthMedian:+#,0;-#,0;0} | {with.BankruptcyRate:P0} → {without.BankruptcyRate:P0} |");
            }
            md.AppendLine();
        }

        md.AppendLine("## 防衛報酬狙いの収支（魔王軍支援＋ダンジョン側介入）");
        md.AppendLine();
        md.AppendLine("ROI = 防衛報酬の総額 ÷ (支援費 ＋ 介入の純支出)。介入なしでも勇者が負けていた配信の防衛報酬も含む。");
        md.AppendLine();
        md.AppendLine("| ボット | 勇者勝率 | 防衛報酬 合計 | 支援費 合計 | 介入 純支出 合計 | ROI | ROI>1 のラン | 純資産中央値 | 破産率 | 反転率 |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var b in s.Bots)
        {
            md.AppendLine($"| {b.Bot} | {b.HeroWinRate:P0} | {b.DefeatRewardsTotal:N0} | {b.SupportSpendTotal:N0} | {b.InterventionNetTotal:N0} | " +
                          (b.SupportSpendTotal + b.InterventionNetTotal > 0 ? b.DefenseRoi.ToString("F2") : "-") +
                          $" | {b.RunsWithDefenseRoiAbove1}/{b.Runs} | {b.NetWorthMedian:N0} | {b.BankruptcyRate:P0} | {b.InterventionFlipRate:P0} |");
        }
        md.AppendLine();

        md.AppendLine("## 異常検知");
        md.AppendLine();
        var allAnoms = runs.SelectMany(r => r.Anomalies).ToList();
        if (allAnoms.Count == 0)
        {
            md.AppendLine("異常なし。");
        }
        else
        {
            md.AppendLine("| 種類 | 件数 | 例 |");
            md.AppendLine("|---|---|---|");
            foreach (var g in allAnoms.GroupBy(a => a.Kind).OrderByDescending(g => g.Count()))
            {
                var ex = g.First();
                md.AppendLine($"| {g.Key} | {g.Count()} | {ex.RunId} T{ex.Turn}: {Md(ex.Detail, 160)} |");
            }
        }
        md.AppendLine();

        md.AppendLine("## 判断の分布");
        md.AppendLine();
        foreach (var b in s.Bots)
        {
            if (b.DecisionCounts.Count == 0) continue;
            md.AppendLine($"### {b.Bot}");
            md.AppendLine();
            foreach (var q in b.DecisionCounts)
            {
                string mean = b.DecisionScoreMean.TryGetValue(q.Key, out var m) ? $"（score 平均 {m:F2}）" : "";
                md.AppendLine($"- **{q.Key}**{mean}: " + string.Join(", ", q.Value.Select(kv => $"{kv.Key}×{kv.Value}")));
            }
            md.AppendLine();
        }

        var logs = runs.SelectMany(r => r.SampleLogs.Select(l => $"{r.RunId}: {l}")).Take(30).ToList();
        if (logs.Count > 0)
        {
            md.AppendLine("## 警告・エラーログ（抜粋）");
            md.AppendLine();
            md.AppendLine("```");
            foreach (var l in logs) md.AppendLine(l.Replace("```", "'''"));
            md.AppendLine("```");
        }
        return md.ToString();
    }

    private static string Csv(string v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        bool quote = v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        v = v.Replace("\"", "\"\"");
        return quote ? "\"" + v + "\"" : v;
    }

    private static string Md(string v, int max)
    {
        if (string.IsNullOrEmpty(v)) return "";
        v = v.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        return v.Length > max ? v.Substring(0, max) + "…" : v;
    }
}

/// <summary>
/// 合格ライン（暫定）。GameConst とは別に AutoPlay の設定として持つ（AutoPlayBatchConfig.Criteria）。
/// 値の意図は Docs/Jev_AutoPlay_Design.md §4.2。
/// </summary>
[Serializable]
public sealed class AutoPlayPassCriteria
{
    /// <summary>破産率の上限。</summary>
    public double MaxBankruptcyRate = 0.15;
    /// <summary>勇者勝率の下限・上限（防衛報酬の戦略が成立する範囲）。</summary>
    public double MinHeroWinRate = 0.40;
    public double MaxHeroWinRate = 0.75;
    /// <summary>最終純資産の中央値 ≥ 初期資金 × この倍率。</summary>
    public double MinNetWorthMedianMultiple = 1.5;
    /// <summary>1件でも出たら不合格の異常。</summary>
    public List<string> ZeroAnomalyKinds = new List<string> { "negative_money", "clearprob_mismatch" };

    public string Describe(int initMoney) =>
        $"破産率 ≤ {MaxBankruptcyRate:P0} / 勇者勝率 {MinHeroWinRate:P0}〜{MaxHeroWinRate:P0} / " +
        $"純資産中央値 ≥ 初期資金×{MinNetWorthMedianMultiple:F1}（{initMoney * MinNetWorthMedianMultiple:N0}G）/ " +
        $"{string.Join("・", ZeroAnomalyKinds)} が 0 件（P10 が初期資金割れは許容）";

    public void Judge(AutoPlayReport.BotSummary b, int initMoney)
    {
        b.Verdicts.Clear();
        bool ok = true;
        void Add(string key, bool pass, string value)
        {
            b.Verdicts[key] = (pass ? "OK " : "NG ") + value;
            ok &= pass;
        }

        Add("破産率", b.BankruptcyRate <= MaxBankruptcyRate, b.BankruptcyRate.ToString("P0"));
        Add("勇者勝率", b.HeroWinRate >= MinHeroWinRate && b.HeroWinRate <= MaxHeroWinRate, b.HeroWinRate.ToString("P0"));
        Add("純資産中央値", b.NetWorthMedian >= initMoney * MinNetWorthMedianMultiple,
            $"{b.NetWorthMedian:N0}（×{b.NetWorthMedian / Math.Max(1, initMoney):F2}）");
        foreach (var kind in ZeroAnomalyKinds)
        {
            int n = b.AnomalyKinds.TryGetValue(kind, out var c) ? c : 0;
            Add(kind, n == 0, n + "件");
        }
        b.Passed = ok;
    }
}

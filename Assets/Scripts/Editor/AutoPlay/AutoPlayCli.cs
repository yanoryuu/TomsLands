using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AutoPlay のヘッドレス計測をバッチモードから回す入口（Unity エディタの画面を開かずに計測するため）。
///
/// 例（-quit は付けない。終わると自分で終了する）:
///   Unity.exe -batchmode -nographics -projectPath &lt;wt&gt; -executeMethod AutoPlayCli.Run
///     -apMode Medium -apSeeds 20 -apBots greedy,follower,golem,bond,upgrade,max,bargain
///     -apOut &lt;dir&gt; [-apAdvance 50000] [-apRemote upload|none|production] [-apNoInterventions] [-apLabel before]
///
/// セーブは AutoPlayBatch が Temp/AutoPlay/ 配下へ逃がす（ユーザーのスロットは触らない）。
/// 結果は -apOut に「戦略ごとの最終純資産」の表（table.md / table.csv）と、レポート一式の場所を書く。
/// </summary>
public static class AutoPlayCli
{
    private static double _deadline;
    private static string _outDir;
    private static string _label;
    private static AutoPlayBatchConfig _config;
    private static bool _started;

    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        string Arg(string key, string def = null)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : def;
        }
        bool Flag(string key) => Array.IndexOf(args, key) >= 0;

        var bots = (Arg("-apBots", "greedy,random") ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        _config = new AutoPlayBatchConfig
        {
            Mode = Enum.TryParse<GameModeId>(Arg("-apMode", "Medium"), out var mode) ? mode : GameModeId.Medium,
            Seeds = int.TryParse(Arg("-apSeeds", "10"), out var seeds) ? seeds : 10,
            BaseSeed = int.TryParse(Arg("-apBaseSeed", "1001"), out var bs) ? bs : 1001,
            UseGreedyBot = bots.Contains("greedy"),
            UseRandomBot = bots.Contains("random"),
            UseDungeonBot = bots.Any(b => b.StartsWith("dungeon_")),
            DungeonBotVariants = bots.Where(b => b.StartsWith("dungeon_")).Select(b => b.Substring("dungeon_".Length)).ToList(),
            StrategyBots = bots.Where(b => AutoPlayStrategyBots.All.Contains(b)).ToList(),
            JevPersonas = new List<string>(),
            ControlRunsWithoutInterventions = false,
            EnableInterventions = !Flag("-apNoInterventions"),
            TutorialAdvance = int.TryParse(Arg("-apAdvance", "0"), out var adv) ? adv : 0,
            CostLimitUsd = 0,
            Overrides = Arg("-apSet", "") ?? "",
            UploadFolderOverride = Arg("-apUploadDir", "") ?? "",
            RemoteConfig = Arg("-apRemote", "upload") switch
            {
                "none" => AutoPlayRemoteConfigSource.None,
                "production" => AutoPlayRemoteConfigSource.Production,
                _ => AutoPlayRemoteConfigSource.UploadFolder,
            },
        };
        _outDir = Arg("-apOut", Path.Combine(AutoPlayBatch.ReportsRoot, "cli"));
        _label = Arg("-apLabel", "run");
        Directory.CreateDirectory(_outDir);
        _deadline = EditorApplication.timeSinceStartup + (double.TryParse(Arg("-apTimeoutSec", "3000"), out var t) ? t : 3000);

        Debug.Log($"[AutoPlayCli] 開始: mode={_config.Mode} seeds={_config.Seeds} bots={string.Join(",", bots)} advance={_config.TutorialAdvance} out={_outDir}");
        _started = false;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!_started)
        {
            // コンパイル・インポートが落ち着いてから始める
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            _started = true;
            AutoPlayBatch.Start(_config);
            return;
        }

        if (AutoPlayBatch.IsRunning && EditorApplication.timeSinceStartup < _deadline) return;

        EditorApplication.update -= Tick;
        int code = 0;
        try
        {
            if (AutoPlayBatch.IsRunning)
            {
                AutoPlayBatch.Cancel();
                Debug.LogError("[AutoPlayCli] 時間切れ");
                code = 2;
            }
            WriteTable();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        Debug.Log($"[AutoPlayCli] 終了 code={code} report={AutoPlayBatch.LastReportDir} msg={AutoPlayBatch.LastMessage}");
        EditorApplication.Exit(code);
    }

    private static double Pct(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        double idx = (sorted.Count - 1) * p;
        int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
    }

    private static void WriteTable()
    {
        var runs = AutoPlayBatch.Runners.Select(r => r.Result).ToList();
        var inv = CultureInfo.InvariantCulture;
        var md = new StringBuilder();
        var csv = new StringBuilder();
        md.AppendLine($"# AutoPlay {_label} — {_config.Mode} × {_config.Seeds} シード（初期資金 {_config.InitMoneyOverride:N0}G・立て替え {_config.TutorialAdvance:N0}G）");
        md.AppendLine();
        md.AppendLine($"リモート配信: {AutoPlayBatch.LastRemoteInfo}  ");
        md.AppendLine($"レポート: {AutoPlayBatch.LastReportDir}");
        md.AppendLine();
        md.AppendLine("| ボット | 純資産 中央値 | P10 | P90 | 破産 | 勇者勝率 | 店の入金 中央値 | 広告費 | マシン費 | 債券 | 最終フォロワー | 店Lv | マシン台数 | 異常 |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        csv.AppendLine("bot,runs,nw_median,nw_p10,nw_p90,bankrupt_rate,hero_win_rate,shop_income_median,ad_spend_mean,machine_spend_mean,finance_spend_mean,followers_median,shop_level_mean,machines_mean,anomalies");
        foreach (var g in runs.GroupBy(r => r.Bot).OrderBy(g => g.Key))
        {
            var list = g.ToList();
            var nw = list.Select(r => (double)r.FinalNetWorth).OrderBy(x => x).ToList();
            var inc = list.Select(r => (double)r.TotalShopIncome).OrderBy(x => x).ToList();
            var fol = list.Select(r => (double)r.FinalFollowers).OrderBy(x => x).ToList();
            double bankrupt = list.Count(r => r.Outcome == "Bankrupt") / (double)Math.Max(1, list.Count);
            int battles = list.Sum(r => r.Battles);
            double win = battles > 0 ? list.Sum(r => r.HeroWins) / (double)battles : 0;
            int anomalies = list.Sum(r => r.Anomalies.Count);
            double ad = list.Average(r => (double)r.TotalAdSpend);
            double mc = list.Average(r => (double)r.TotalMachineSpend);
            double fi = list.Average(r => (double)r.TotalFinanceSpend);
            double lv = list.Average(r => (double)r.FinalShopLevel);
            double mn = list.Average(r => (double)r.FinalMachines);
            md.AppendLine($"| {g.Key} | {Pct(nw, .5):N0} | {Pct(nw, .1):N0} | {Pct(nw, .9):N0} | {bankrupt:P0} | {win:P0} | {Pct(inc, .5):N0} | {ad:N0} | {mc:N0} | {fi:N0} | {Pct(fol, .5):N0} | {lv:F1} | {mn:F1} | {anomalies} |");
            csv.AppendLine(string.Join(",", new[]
            {
                g.Key, list.Count.ToString(inv), Pct(nw, .5).ToString("F0", inv), Pct(nw, .1).ToString("F0", inv), Pct(nw, .9).ToString("F0", inv),
                bankrupt.ToString("F3", inv), win.ToString("F3", inv), Pct(inc, .5).ToString("F0", inv), ad.ToString("F0", inv), mc.ToString("F0", inv),
                fi.ToString("F0", inv), Pct(fol, .5).ToString("F0", inv), lv.ToString("F2", inv), mn.ToString("F2", inv), anomalies.ToString(inv),
            }));
        }

        // 異常の種類（上位）
        md.AppendLine();
        md.AppendLine("異常の内訳: " + string.Join(" / ", runs.SelectMany(r => r.Anomalies).GroupBy(a => a.Kind)
            .OrderByDescending(x => x.Count()).Select(x => $"{x.Key}={x.Count()}")));
        md.AppendLine();
        md.AppendLine("新聞費（平均/ラン）: " + string.Join(" / ", runs.GroupBy(r => r.Bot).OrderBy(g => g.Key)
            .Select(g => $"{g.Key} {g.Average(r => (double)r.TotalNewspaperSpend):N0}G")));
        md.AppendLine();
        md.AppendLine("占い師の枠（平均/ラン）: " + string.Join(" / ", runs.GroupBy(r => r.Bot).OrderBy(g => g.Key)
            .Select(g => $"{g.Key} {g.Average(r => (double)r.ProphetPlacements):0.0}回 {g.Average(r => (double)r.TotalProphetSpend):N0}G")));
        md.AppendLine();
        md.AppendLine(AutoPlayNewsStudy.Summarize(runs));
        var news = new StringBuilder("run,bot,event,truth,scale,sign,lead,duration,matched,reported,hype,pubToEffect,pubToBest,pubToSettle,pubToAfter,preToBest,demandGain,customerGain\n");
        foreach (var e in runs.SelectMany(r => r.NewsEventRows))
            news.AppendLine(string.Join(",", e.RunId, e.Bot, e.EventKey, e.Truth, e.ScaleRank, e.Sign, e.Lead, e.Duration, e.Matched, e.Reported,
                e.Hype.ToString("F4", inv), e.PubToEffect.ToString("F4", inv), e.PubToBest.ToString("F4", inv), e.PubToSettle.ToString("F4", inv),
                e.PubToAfter.ToString("F4", inv), e.PreToBest.ToString("F4", inv), e.DemandGain.ToString("F4", inv), e.CustomerGain.ToString("F4", inv)));
        File.WriteAllText(Path.Combine(_outDir, _label + "_news.csv"), news.ToString(), new UTF8Encoding(false));

        File.WriteAllText(Path.Combine(_outDir, _label + "_table.md"), md.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(_outDir, _label + "_table.csv"), csv.ToString(), new UTF8Encoding(false));
        // 1ランずつの最終値も残す（あとで比べるため）
        var per = new StringBuilder("run,bot,seed,outcome,final_money,final_networth,shop_income,stream_earnings,defeat_rewards,debt_paid,ad_spend,machine_spend,finance_spend,followers,shop_level,machines,hero_wins,battles\n");
        foreach (var r in runs)
            per.AppendLine(string.Join(",", r.RunId, r.Bot, r.Seed, r.Outcome, r.FinalMoney, r.FinalNetWorth, r.TotalShopIncome, r.TotalStreamEarnings,
                r.TotalDefeatRewards, r.TotalDebtPaid, r.TotalAdSpend, r.TotalMachineSpend, r.TotalFinanceSpend, r.FinalFollowers, r.FinalShopLevel,
                r.FinalMachines, r.HeroWins, r.Battles));
        File.WriteAllText(Path.Combine(_outDir, _label + "_runs.csv"), per.ToString(), new UTF8Encoding(false));
    }
}

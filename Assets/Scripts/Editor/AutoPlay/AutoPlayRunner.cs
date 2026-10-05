using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// =====================================================================
// Jev AutoPlay — 1ランの進行と記録・異常検知
// =====================================================================

public sealed class AutoPlayTurnRow
{
    public string RunId;
    public int Turn;
    public int FlowIndex;
    public string Stage;          // shop / stream / end
    public int Money;
    public int StockValue;
    public int PendingOrders;
    public int NetWorth;
    public int Spend;
    public int ShopIncome;
    public int StreamEarnings;
    public int DefeatReward;
    public int DebtPaid;
    public int SupportSpend;
    public int UpgradeSpend;
    public int InterventionNet;
    public string Battle;
    public string BattleDungeon;
    public float ClearPct;
    public int HeroLevel;
    public string Buzz;
    public int DisplayedKinds;
    public int Rejected;
}

public sealed class AutoPlayAnomaly
{
    public string RunId;
    public int Turn;
    public string Kind;
    public string Detail;
}

public sealed class AutoPlayRunResult
{
    public string RunId;
    public string Bot;
    public int Seed;
    public string Mode;
    public string Outcome = "Running"; // Completed / Bankrupt / Stalled / Crashed / Cancelled
    public int Turns;
    public int FinalMoney;
    public int PeakMoney;
    public int MinMoney = int.MaxValue;
    public int FinalNetWorth;
    public int Battles;
    public int HeroWins;
    public int HeroLosses;
    public int HeroFinalLevel;
    public int TotalShopIncome;
    public int TotalStreamEarnings;
    public int TotalDefeatRewards;
    public int TotalSpend;
    public int TotalDebtPaid;
    public int TotalSupportSpend;
    public int TotalUpgradeSpend;
    public int Supports;
    public int RejectedActions;
    public int LogErrors;
    public int LogWarnings;
    public int JevRequests;
    public long InputTokens;
    public double CostUsd;
    public double WallSeconds;

    // --- 配信中の介入 ---
    public int InterventionCount;
    public int InterventionSpent;
    public int InterventionRefund;
    public int HeroSideSpent;
    public int DungeonSideSpent;
    /// <summary>ダンジョン側の介入で「介入なしなら勇者が勝っていた」配信を負けに変えた回数と、そこで得た防衛報酬。</summary>
    public int DungeonFlips;
    public int DungeonFlipRewards;
    /// <summary>ダンジョン側の介入を使った配信の防衛報酬の合計（反転かどうかを問わない）。</summary>
    public int DungeonStreamRewards;
    /// <summary>勇者側の介入で負け→勝ちに変えた回数。</summary>
    public int HeroFlips;
    public readonly Dictionary<string, int> InterventionKinds = new Dictionary<string, int>();
    public readonly List<AutoPlayStreamRow> StreamRows = new List<AutoPlayStreamRow>();

    public readonly List<AutoPlayTurnRow> TurnRows = new List<AutoPlayTurnRow>();
    public readonly List<AutoPlayDecision> Decisions = new List<AutoPlayDecision>();
    public readonly List<AutoPlayAnomaly> Anomalies = new List<AutoPlayAnomaly>();
    public readonly List<string> SampleLogs = new List<string>();
}

public sealed class AutoPlayRunner
{
    private const int MaxSampleLogs = 20;

    private readonly AutoPlayHeadlessGame _game;
    private readonly IAutoPlayBot _bot;
    private readonly AutoPlayRunContext _ctx;
    private readonly int _maxSteps;

    public readonly AutoPlayRunResult Result;
    public string Status { get; private set; } = "待機";
    /// <summary>実行中の Jev 入力トークン（コスト監視用）。</summary>
    public long LiveInputTokens => _bot.InputTokens;

    public AutoPlayRunner(AutoPlayHeadlessGame game, IAutoPlayBot bot, AutoPlayRunContext ctx, int maxSteps)
    {
        _game = game;
        _bot = bot;
        _ctx = ctx;
        _maxSteps = maxSteps;
        Result = new AutoPlayRunResult
        {
            RunId = ctx.RunId,
            Bot = bot.Name,
            Seed = game.Config.Seed,
            Mode = game.Config.Mode.ToString(),
        };
    }

    /// <summary>
    /// ランを最後まで進める。メインスレッドから await すること（継続はメインスレッドへ戻る）。
    /// モデルに触る区間は必ず _ctx.Enter() の中で同期的に行い、await を跨がない。
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        Status = "開始";
        try
        {
            using (_ctx.Enter())
            {
                _game.StartNewRun();
                DrainLogs(0);
            }

            for (int step = 0; ; step++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield(); // エディタを固めない（ベースラインボットは同期で完了するため）

                if (step >= _maxSteps)
                {
                    AddAnomaly(_game.CurrentTurn, "stalled", $"{_maxSteps} ステップで終わらなかった");
                    Result.Outcome = "Stalled";
                    break;
                }

                AutoPlaySnapshot snap;
                using (_ctx.Enter())
                {
                    if (_game.Stage == AutoPlayStage.ShopDay) _game.BeginShopDay();
                    DrainLogs(_game.CurrentTurn);
                    if (IsTerminal(_game.Stage))
                    {
                        if (_game.Today.DebtPaid > 0 || _game.Stage == AutoPlayStage.Bankrupt) RecordRow("end");
                        break;
                    }
                    snap = _game.Snapshot();
                }

                Status = $"T{snap.RunTurn} {snap.Stage} {snap.Money:N0}G";

                // レリック3択（配信勝利・返済の報酬）
                if (snap.PendingRelicChoices.Count > 0)
                {
                    var relicPlan = await _bot.PlanRelicAsync(snap, ct);
                    using (_ctx.Enter())
                    {
                        Result.Decisions.AddRange(relicPlan.Decisions);
                        var r = relicPlan.ChoiceIndex >= 0 ? _game.ChooseRelic(relicPlan.ChoiceIndex) : _game.DeclineRelic();
                        if (!r.Ok) _game.DeclineRelic();
                        snap = _game.Snapshot();
                    }
                }

                if (snap.Stage == AutoPlayStage.ShopDay)
                {
                    var plan = await _bot.PlanDayAsync(snap, ct);
                    using (_ctx.Enter())
                    {
                        Result.Decisions.AddRange(plan.Decisions);
                        int indexBefore = _game.Flow.CurrentIndex;
                        ApplyDayPlan(plan);
                        var sales = _game.StartSales();
                        if (!sales.Ok) AddAnomaly(snap.RunTurn, "sales_failed", sales.Message);
                        RecordRow("shop");
                        CheckInvariants(snap.RunTurn);
                        CheckOverdueOrders(snap.RunTurn);

                        var end = _game.EndDay();
                        if (!end.Ok) AddAnomaly(snap.RunTurn, "end_day_failed", end.Message);
                        else if (_game.Flow.CurrentIndex == indexBefore && _game.Stage == AutoPlayStage.ShopDay)
                            AddAnomaly(snap.RunTurn, "turn_not_advanced", $"index={indexBefore}");
                        DrainLogs(snap.RunTurn);
                    }
                }
                else if (snap.Stage == AutoPlayStage.StreamDay)
                {
                    var plan = await _bot.PlanStreamAsync(snap, ct);
                    using (_ctx.Enter())
                    {
                        Result.Decisions.AddRange(plan.Decisions);
                        var r = _game.StartStream(plan.Items, plan.Interventions);
                        if (!r.Ok)
                        {
                            AddAnomaly(snap.RunTurn, "stream_failed", r.Message);
                            Result.Outcome = "Stalled";
                            break;
                        }

                        var b = _game.LastBattle;
                        Result.Battles++;
                        if (b.Victory) Result.HeroWins++; else Result.HeroLosses++;
                        if (!b.ConsistentWithDisplay)
                            AddAnomaly(snap.RunTurn, "clearprob_mismatch",
                                $"{_game.Today.BattleDungeon}: 表示 {b.DisplayedClearPct:F0}% だがサロゲートは {(b.Victory ? "勝利" : "敗北")}" +
                                (Mathf.Approximately(RelicBattleEffects.HeroPowerMul, 1f) ? "" : $"（HeroPowerMul={RelicBattleEffects.HeroPowerMul:F2} は表示確率に未反映）"));

                        RecordRow("stream", snap.RunTurn, snap.FlowIndex);
                        RecordStream(snap, b, plan);
                        CheckInvariants(snap.RunTurn);
                        DrainLogs(snap.RunTurn);
                    }
                }
                else
                {
                    break;
                }
            }

            if (Result.Outcome == "Running")
            {
                Result.Outcome = _game.Stage switch
                {
                    AutoPlayStage.Completed => "Completed",
                    AutoPlayStage.Bankrupt => "Bankrupt",
                    _ => "Stalled",
                };
            }
        }
        catch (OperationCanceledException)
        {
            Result.Outcome = "Cancelled";
        }
        catch (Exception e)
        {
            Result.Outcome = "Crashed";
            AddAnomaly(SafeTurn(), "exception", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
        }
        finally
        {
            sw.Stop();
            Result.WallSeconds = sw.Elapsed.TotalSeconds;
            FinishRun();
            Status = Result.Outcome;
        }
    }

    private static bool IsTerminal(AutoPlayStage stage) => stage == AutoPlayStage.Completed || stage == AutoPlayStage.Bankrupt;

    private int SafeTurn()
    {
        try { return _game.Flow != null ? _game.CurrentTurn : 0; }
        catch { return 0; }
    }

    // -----------------------------------------------------------------
    // 計画の実行（順序: 設備投資 → 魔王軍支援 → 仕入れ → 陳列）
    // -----------------------------------------------------------------

    private void ApplyDayPlan(AutoPlayDayPlan plan)
    {
        if (plan.Upgrade == "blacksmith") _game.UpgradeBlacksmith();
        else if (plan.Upgrade == "shop") _game.UpgradeShop();

        if (!string.IsNullOrEmpty(plan.SupportDungeon))
        {
            if (Enum.TryParse<DungeonName>(plan.SupportDungeon, out var key))
            {
                if (_game.SupportDungeon(key).Ok) Result.Supports++;
            }
        }

        if (plan.AutoBuyBudget > 0) _game.AutoBuy(plan.AutoBuyBudget, plan.AutoBuyStrategy);
        foreach (var p in plan.Purchases) _game.Buy(p.ItemId, p.Quantity);

        _game.ClearDisplay();
        int kinds = 0;
        int maxKinds = _game.MaxDisplayKinds;
        float frac = Mathf.Clamp01(plan.DisplayFraction);
        if (frac > 0f)
        {
            foreach (var id in plan.DisplayPriority)
            {
                if (kinds >= maxKinds) break;
                var r = _game.ItemModel.GetRuntimeItem(id);
                if (r == null || r.Stock.Value <= 0) continue;
                int cap = Mathf.Min(r.Stock.Value, _game.MaxDisplayPerItem);
                int qty = Mathf.Max(1, Mathf.RoundToInt(cap * frac));
                if (_game.SetDisplay(id, qty).Ok) kinds++;
            }
        }
    }

    // -----------------------------------------------------------------
    // 記録
    // -----------------------------------------------------------------

    private void RecordStream(AutoPlaySnapshot snap, AutoPlayBattleSurrogate.Outcome b, AutoPlayStreamPlan plan)
    {
        var day = _game.Today;
        var row = new AutoPlayStreamRow
        {
            RunId = Result.RunId,
            Turn = snap.RunTurn,
            Dungeon = day.BattleDungeon,
            DisplayedClearPct = b.DisplayedClearPct,
            BaselineHeroWin = b.BaselineVictory,
            HeroWin = b.Victory,
            Planned = string.Join("+", plan.Interventions.Select(i => $"{AutoPlayInterventionKinds.Key(i.Kind)}@{i.Timing:F2}")),
            Executed = string.Join("+", b.Executed),
            Skipped = string.Join("+", b.Skipped),
            HeroSideSpent = b.HeroSideSpent,
            DungeonSideSpent = b.DungeonSideSpent,
            Refund = b.InterventionRefund,
            SpecialMoves = b.SpecialMoves,
            ViewerSpecials = b.ViewerSpecials,
            RawSales = b.RawSales,
            DefeatReward = day.DefeatReward,
            Turns = b.Turns,
            BaselineTurns = b.BaselineTurns,
        };
        Result.StreamRows.Add(row);

        int executedPlayer = b.Executed.Count(e => !e.StartsWith("viewer_"));
        Result.InterventionCount += executedPlayer;
        Result.InterventionSpent += b.InterventionSpent;
        Result.InterventionRefund += b.InterventionRefund;
        Result.HeroSideSpent += b.HeroSideSpent;
        Result.DungeonSideSpent += b.DungeonSideSpent;
        foreach (var e in b.Executed)
        {
            string kind = e.Split('@')[0];
            Result.InterventionKinds[kind] = (Result.InterventionKinds.TryGetValue(kind, out var n) ? n : 0) + 1;
        }
        if (b.DungeonSideSpent > 0)
        {
            Result.DungeonStreamRewards += day.DefeatReward;
            if (b.BaselineVictory && !b.Victory)
            {
                Result.DungeonFlips++;
                Result.DungeonFlipRewards += day.DefeatReward;
            }
        }
        if (b.HeroSideSpent > 0 && !b.BaselineVictory && b.Victory) Result.HeroFlips++;
    }

    private void RecordRow(string stage, int turn = -1, int flowIndex = -1)
    {
        var day = _game.Today;
        var items = _game.ItemModel.RuntimeItems;
        int money = _game.TomsModel.PlayerMoney.Value;
        int stockValue = items.Sum(r => r.Stock.Value * r.CurrentPrice.Value);
        int pending = _game.SellOrders.PendingTotalEstimate.Value;

        var row = new AutoPlayTurnRow
        {
            RunId = Result.RunId,
            Turn = turn >= 0 ? turn : _game.CurrentTurn,
            FlowIndex = flowIndex >= 0 ? flowIndex : _game.Flow.CurrentIndex,
            Stage = stage,
            Money = money,
            StockValue = stockValue,
            PendingOrders = pending,
            NetWorth = money + stockValue + pending,
            Spend = day.Spend,
            ShopIncome = day.ShopIncome,
            StreamEarnings = day.StreamEarnings,
            DefeatReward = day.DefeatReward,
            DebtPaid = day.DebtPaid,
            SupportSpend = day.SupportSpend,
            UpgradeSpend = day.UpgradeSpend,
            InterventionNet = day.InterventionSpent - day.InterventionRefund,
            Battle = day.BattleResult,
            BattleDungeon = day.BattleDungeon,
            ClearPct = day.BattleClearPct,
            HeroLevel = _game.HeroModel.heroData?.level.Value ?? 0,
            Buzz = _game.Marketing.Buzz.IsBuzzActive.Value ? _game.Marketing.Buzz.CurrentBuzzType.Value.ToString() : "",
            DisplayedKinds = _game.ItemModel.CountDisplayedKinds(),
            Rejected = day.RejectedActions,
        };
        Result.TurnRows.Add(row);

        Result.PeakMoney = Mathf.Max(Result.PeakMoney, money);
        Result.MinMoney = Mathf.Min(Result.MinMoney, money);
        Result.TotalSpend += day.Spend;
        Result.TotalShopIncome += day.ShopIncome;
        Result.TotalStreamEarnings += day.StreamEarnings;
        Result.TotalDefeatRewards += day.DefeatReward;
        Result.TotalDebtPaid += day.DebtPaid;
        Result.TotalSupportSpend += day.SupportSpend;
        Result.TotalUpgradeSpend += day.UpgradeSpend;
        Result.RejectedActions += day.RejectedActions;
    }

    private void FinishRun()
    {
        try
        {
            using (_ctx.Enter())
            {
                if (_game.TomsModel != null)
                {
                    Result.Turns = _game.CurrentTurn;
                    Result.FinalMoney = _game.TomsModel.PlayerMoney.Value;
                    int stockValue = _game.ItemModel.RuntimeItems.Sum(r => r.Stock.Value * r.CurrentPrice.Value);
                    Result.FinalNetWorth = Result.FinalMoney + stockValue + _game.SellOrders.PendingTotalEstimate.Value;
                    Result.HeroFinalLevel = _game.HeroModel.heroData?.level.Value ?? 0;
                }
                DrainLogs(SafeTurn());
                _game.Dispose();
            }
        }
        catch (Exception e)
        {
            AddAnomaly(0, "finalize_exception", e.Message);
        }

        if (Result.MinMoney == int.MaxValue) Result.MinMoney = Result.FinalMoney;
        Result.JevRequests = _bot.Requests;
        Result.InputTokens = _bot.InputTokens;
        Result.CostUsd = JevApi.EstimateCostUsd(_bot.InputTokens);
    }

    // -----------------------------------------------------------------
    // 異常検知
    // -----------------------------------------------------------------

    private void CheckInvariants(int turn)
    {
        int money = _game.TomsModel.PlayerMoney.Value;
        if (money < 0) AddAnomaly(turn, "negative_money", $"所持金 {money}G");

        foreach (var r in _game.ItemModel.RuntimeItems)
        {
            if (r.Stock.Value < 0) AddAnomaly(turn, "negative_stock", $"{r.ItemId} stock={r.Stock.Value}");
            if (r.Stock.Value > r.MaxStock.Value) AddAnomaly(turn, "stock_over_max", $"{r.ItemId} {r.Stock.Value}/{r.MaxStock.Value}");
            if (r.DisplayStock.Value > r.Stock.Value) AddAnomaly(turn, "display_over_stock", $"{r.ItemId} display={r.DisplayStock.Value} stock={r.Stock.Value}");
            if (r.CurrentPrice.Value <= 0) AddAnomaly(turn, "invalid_price", $"{r.ItemId} price={r.CurrentPrice.Value}");
            float d = r.Demand.Value;
            if (float.IsNaN(d) || d < 0f || d > 1f) AddAnomaly(turn, "demand_out_of_range", $"{r.ItemId} demand={d}");
        }

        if (_game.ItemModel.CountDisplayedKinds() > _game.MaxDisplayKinds)
            AddAnomaly(turn, "display_kinds_over", $"{_game.ItemModel.CountDisplayedKinds()} > {_game.MaxDisplayKinds}");

        foreach (var o in _game.SellOrders.PendingOrders)
            if (o.Quantity <= 0 || o.OrderedPrice <= 0)
                AddAnomaly(turn, "invalid_sell_order", $"{o.ItemId} qty={o.Quantity} price={o.OrderedPrice}");
    }

    private void CheckOverdueOrders(int turn)
    {
        int overdue = _game.SellOrders.PendingOrders.Count(o => o.SettleTurn < turn);
        if (overdue > 0) AddAnomaly(turn, "sell_order_overdue", $"約定予定日を過ぎた売り注文 {overdue}件");
    }

    private void DrainLogs(int turn)
    {
        foreach (var (type, message) in _ctx.CapturedLogs)
        {
            if (type == LogType.Warning)
            {
                Result.LogWarnings++;
            }
            else
            {
                Result.LogErrors++;
                AddAnomaly(turn, type == LogType.Exception ? "log_exception" : "log_error", Truncate(message, 400));
            }

            if (Result.SampleLogs.Count < MaxSampleLogs && !Result.SampleLogs.Contains(message))
                Result.SampleLogs.Add($"[{type}] T{turn} {Truncate(message, 300)}");
        }
        _ctx.CapturedLogs.Clear();
    }

    private void AddAnomaly(int turn, string kind, string detail)
    {
        // 同じ種類の異常が延々と続く場合に備えて、1ランあたり種類ごと 30 件まで
        if (Result.Anomalies.Count(a => a.Kind == kind) >= 30) return;
        Result.Anomalies.Add(new AutoPlayAnomaly { RunId = Result.RunId, Turn = turn, Kind = kind, Detail = detail });
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";
}

/// <summary>配信1回分の記録（streams.csv）。</summary>
public sealed class AutoPlayStreamRow
{
    public string RunId;
    public int Turn;
    public string Dungeon;
    public float DisplayedClearPct;
    /// <summary>介入なしでもサロゲート上は勇者が勝っていたか。</summary>
    public bool BaselineHeroWin;
    public bool HeroWin;
    public string Planned;
    public string Executed;
    public string Skipped;
    public int HeroSideSpent;
    public int DungeonSideSpent;
    public int Refund;
    public int SpecialMoves;
    public int ViewerSpecials;
    public int RawSales;
    public int DefeatReward;
    public int Turns;
    public int BaselineTurns;
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// ターン終了サマリーのPresenter。
/// ターン終了時に売上情報・トレンド・次配信までの日数を集計してViewに表示し、
/// 確認ボタンで実際にNextTurnを実行する。
/// </summary>
public class TurnEndSummaryPresenter : IStartable, IDisposable
{
    private readonly TurnEndSummaryView view;
    private readonly ItemModel itemModel;
    private readonly GameFlowManager gameFlowManager;
    private readonly TomsModel tomsModel;
    private readonly StateManager stateManager;
    private readonly MarketingFacade marketingFacade;
    private readonly SellOrderModel sellOrderModel;
    private readonly ShopEconomySettings economySettings;
    private readonly RelicEffectResolver relicResolver;
    private readonly MetaProgressModel meta;
    private readonly CompositeDisposable disposables = new();

    // フェーズ再入（ForceNotify等）・営業ダイジェストからの呼び出しによる販売処理の二重実行を防ぐガード
    private int lastProcessedTurn = -1;
    /// <summary>lastProcessedTurn の日の精算結果（再入時はこれを表示し直す）。</summary>
    private TurnEndDayResult lastResult;

    /// <summary>直近に精算した日の結果（このシーンで一度も精算していなければ null）。</summary>
    public TurnEndDayResult LastDay => lastResult;

    public TurnEndSummaryPresenter(
        TurnEndSummaryView view,
        ItemModel itemModel,
        GameFlowManager gameFlowManager,
        TomsModel tomsModel,
        StateManager stateManager,
        MarketingFacade marketingFacade,
        SellOrderModel sellOrderModel,
        ShopEconomySettings economySettings,
        RelicEffectResolver relicResolver,
        MetaProgressModel meta = null)
    {
        this.view = view;
        this.itemModel = itemModel;
        this.gameFlowManager = gameFlowManager;
        this.tomsModel = tomsModel;
        this.stateManager = stateManager;
        this.marketingFacade = marketingFacade;
        this.sellOrderModel = sellOrderModel;
        this.economySettings = economySettings;
        this.relicResolver = relicResolver;
        this.meta = meta;

        stateManager.RegisterOnEnter(TomsShopGamePhase.TurnEndSummary, Entry);
    }

    public void Start()
    {
        // 確認ボタンが押されたらNextTurnを実行
        // （NextTurn内でChangeTomsShopPhaseが呼ばれ、Shop画面のEntry()が発火する）
        view.OnConfirmClicked
            .Subscribe(_ => gameFlowManager.NextTurn())
            .AddTo(disposables);
    }

    /// <summary>
    /// TurnEndSummaryフェーズに入った時に呼ばれる。その日の精算がまだなら行い、結果を表示する。
    /// </summary>
    public void Entry()
    {
        var day = ProcessTodayIfNeeded();
        if (day != null) Display(day);
    }

    /// <summary>
    /// その日の営業の精算を一度だけ行い、結果を返す（同じ日に何度呼んでも精算は1回）。
    /// 【売り注文制】①昨日以前の売り注文を約定して入金 → ②今日の陳列分を全量「売り注文」化（入金は明日）。
    /// 営業ダイジェスト（F-7）が演出の前に呼び、精算の結果（客が買っていく品・入金）を先に受け取る。
    /// </summary>
    public TurnEndDayResult ProcessTodayIfNeeded()
    {
        int currentTurn = gameFlowManager.CurrentTurn.Value;

        // フェーズ再入・演出からの呼び出しによる販売処理・入金の二重実行を防ぐ
        if (lastProcessedTurn == currentTurn)
        {
            Debug.Log($"[TurnEndSummary] Turn {currentTurn} は処理済みのためスキップ（再入）");
            return lastResult;
        }
        lastProcessedTurn = currentTurn;

        bool probabilistic = economySettings != null && economySettings.useProbabilisticShopSales;
        int delayTurns = economySettings != null ? economySettings.sellOrderDelayTurns : 1;

        // 1. 約定日を迎えた売り注文を精算して入金（昨日陳列した分が今日売れた）
        var settlement = sellOrderModel.SettleDue(currentTurn, itemModel, economySettings, marketingFacade, relicResolver);
        // 倍率の内訳は約定と同じ状態で取る（表示と入金をそろえるため）
        var breakdown = settlement.BaseIncome > 0 && marketingFacade != null ? marketingFacade.GetSalesBreakdown(settlement.BaseIncome) : null;
        if (settlement.TotalIncome > 0)
        {
            tomsModel.AddRevenue(settlement.TotalIncome);
            Debug.Log($"[TurnEndSummary] 売り注文の約定入金: +{settlement.TotalIncome}G → {tomsModel.PlayerMoney.Value}G");
        }

        // 2. 需要対応力：全アイテム中で需要が高い品がいくつ品出しされているか
        int trendUpTotal = 0;
        int trendUpDisplayed = 0;
        foreach (var r in itemModel.RuntimeItems)
        {
            if (r.Demand.Value >= 0.7f)
            {
                trendUpTotal++;
                if (r.IsDisplay.Value) trendUpDisplayed++;
            }
        }

        // 3. 今日の陳列分を販売処理（売り注文制では陳列数の全量が売れ、Stock が引き当てられる）
        var salesResult = itemModel.SimulateShopSales(probabilistic);

        // 4. 販売結果からサマリーを構築し、売り注文を作成（入金は約定日）
        var items = BuildSummaryItems(salesResult);
        int pendingEstimate = 0;
        int totalSoldCount = 0;

        foreach (var item in items)
        {
            pendingEstimate += item.Revenue;
            totalSoldCount += item.SoldCount;
        }

        var placed = new List<(RuntimeItemData, int)>();
        foreach (var kv in salesResult)
        {
            var runtime = itemModel.GetRuntimeItem(kv.Key);
            if (runtime != null && kv.Value > 0)
            {
                sellOrderModel.Place(runtime, kv.Value, currentTurn, delayTurns);
                placed.Add((runtime, kv.Value));
            }
        }

        // 5. 在庫・注文・所持金を同時に保存（引き当て済み在庫と注文の整合を保つ）
        itemModel.SaveData();
        sellOrderModel.SaveData();
        tomsModel.SavePlayerMoney();

        // 表示用の日数（明日なら 1）。GetTurnsUntilNextBattle は前日に 0 になるので使わない
        int daysUntilBattle = gameFlowManager.GetDaysUntilNextStream();

        Debug.Log($"[TurnEndSummary] 本日の入金: {settlement.TotalIncome}G / 売り注文（明日入金見込み）: {pendingEstimate}G, 売れた数: {totalSoldCount}, 次の配信まで: {daysUntilBattle}日");

        // 6. ターン評価
        var (grade, comment) = CalculateGrade(pendingEstimate, trendUpDisplayed, trendUpTotal, settlement);

        lastResult = new TurnEndDayResult
        {
            Turn = currentTurn,
            Settlement = settlement,
            Breakdown = breakdown,
            Items = items,
            PlacedItems = placed,
            PendingEstimate = pendingEstimate,
            SoldCount = totalSoldCount,
            DaysUntilStream = daysUntilBattle,
            Grade = grade,
            Comment = comment,
            RelicFactors = relicResolver != null ? relicResolver.GetContributors(RelicStatId.ShopRevenueMul) : new List<(RelicDefinition, float)>(),
        };
        return lastResult;
    }

    /// <summary>精算の結果をサマリーに表示する。</summary>
    private void Display(TurnEndDayResult day)
    {
        if (day.SoldCount > 0 || day.Settlement.TotalIncome > 0)
            SoundManager.Instance?.PlaySE("営業/SE_売上音");

        view.ShowGrade(day.Grade, day.Comment);
        view.UpdateContent(day.Items, day.PendingEstimate, day.SoldCount, day.DaysUntilStream);
        view.UpdateSettlementInfo(day.Settlement.TotalIncome, day.PendingEstimate);
        view.UpdateSettlementDetail(BuildDetailText(day));

        // 1周目で占い師に出会う（＝配信を知る）前は「次の配信まで」を出さない（K-6）
        bool streamKnown = meta == null || meta.TotalRuns > 0 || meta.HasSeenTutorial(TutorialSteps.ProphetMeeting);
        view.SetDaysUntilStreamVisible(streamKnown, streamKnown && day.DaysUntilStream > 0 && gameFlowManager.IsUpcomingStreamFinal());
    }

    /// <summary>
    /// 本日の入金の明細（F-2）: 値動きの大きかった銘柄を2行まで、倍率の内訳、実現損益（F-1）。
    /// 何も約定していない日は空文字（パネル側で欄を隠す）。
    /// </summary>
    private static string BuildDetailText(TurnEndDayResult day)
    {
        var s = day.Settlement;
        if (s == null || s.Settled.Count == 0) return "";
        var sb = new StringBuilder();

        var rows = s.Settled
            .OrderByDescending(o => (long)Mathf.Abs(o.SettledPrice - o.OrderedPrice) * o.Quantity)
            .ThenByDescending(o => o.Income)
            .Take(2);
        foreach (var o in rows)
        {
            float change = o.OrderedPrice > 0 ? (float)(o.SettledPrice - o.OrderedPrice) / o.OrderedPrice : 0f;
            bool moved = Mathf.Abs(change) >= 0.005f;
            string line = $"{o.ItemName} ×{o.Quantity}  {o.OrderedPrice:N0}G→{o.SettledPrice:N0}G";
            if (moved) line += $"（{FormatPercent(change)}）";
            sb.AppendLine(moved ? $"<color={(change > 0 ? GoldHex : GrayHex)}>{line}</color>" : line);
        }

        // 倍率の内訳（1 倍のものは出さない）
        var factors = new List<string>();
        var b = day.Breakdown;
        if (b != null)
        {
            if (!Mathf.Approximately(b.BuzzMultiplier, 1f)) factors.Add($"バズ{b.BuzzMultiplier:0.##}");
            if (!Mathf.Approximately(b.FollowerBonusRate, 0f)) factors.Add($"フォロワー{1f + b.FollowerBonusRate:0.##}");
            if (!Mathf.Approximately(b.MachineMultiplier, 1f)) factors.Add($"設備{b.MachineMultiplier:0.##}");
        }
        if (day.RelicFactors != null)
        {
            foreach (var (relic, factor) in day.RelicFactors)
                if (relic != null && !Mathf.Approximately(factor, 1f)) factors.Add($"{relic.relicName}{factor:0.##}");
        }
        if (factors.Count > 0)
            sb.AppendLine($"基本 {s.BaseIncome:N0}G × {string.Join(" × ", factors)} = <color={GoldHex}>{s.TotalIncome:N0}G</color>");

        // 実現損益（取得単価が分かる分だけ）
        if (s.HasRealizedProfit)
        {
            int p = s.RealizedProfit;
            string yield = s.RealizedYield.HasValue ? $"（利回り {FormatPercent(s.RealizedYield.Value)}）" : "";
            sb.AppendLine($"実現損益 <color={(p >= 0 ? GoldHex : RedHex)}>{(p >= 0 ? "+" : "")}{p:N0}G</color>{yield}");
        }
        return sb.ToString().TrimEnd();
    }

    private const string GoldHex = "#FFD25A";
    private const string GrayHex = "#D8CFC4";
    private const string RedHex = "#FF9A8A";

    private static string FormatPercent(float rate) => $"{(rate >= 0 ? "+" : "")}{rate * 100f:0}%";

    /// <summary>
    /// 3軸（資金効率・需要対応力・バズ活用度）の加重平均でターン評価を算出する。
    /// 資金効率40% + 需要対応力35% + バズ活用度25%
    /// 資金効率は、取得単価の分かる約定があれば実現損益の利回り（+10%で満点、0%で半分、赤字で0。F-3）、
    /// 無ければ従来どおり「売り注文の見込み額 ÷ 今日の仕入れ額」で見る。
    /// </summary>
    private (TurnGrade grade, string comment) CalculateGrade(
        int pendingEstimate,
        int trendUpDisplayed, int trendUpTotal,
        SellSettlementResult settlement)
    {
        float capitalEfficiency;
        float? realizedYield = settlement?.RealizedYield;
        if (realizedYield.HasValue)
        {
            capitalEfficiency = Mathf.Clamp01(0.5f + realizedYield.Value * 5f);
        }
        else
        {
            int spend = tomsModel.TurnProcurementSpend;
            capitalEfficiency = spend > 0
                ? Mathf.Clamp01((float)pendingEstimate / spend)
                : (pendingEstimate > 0 ? 1f : 0f);
        }

        // 需要対応力 (0~1): 需要が高いアイテム(Demand>=0.7)を何品品出しできていたか
        // 高需要品がないターンは満点扱い
        float demandResponse = trendUpTotal > 0
            ? Mathf.Clamp01((float)trendUpDisplayed / trendUpTotal)
            : 1f;

        // バズ活用度 (0~1): 本日の約定入金に適用されたマーケティング倍率（2倍で満点）
        float buzzMultiplier = settlement != null && settlement.BaseIncome > 0
            ? (float)settlement.TotalIncome / settlement.BaseIncome
            : (marketingFacade != null ? marketingFacade.CalculateFinalRevenue(1000) / 1000f : 1f);
        float buzzScore = Mathf.Clamp01((buzzMultiplier - 1f) / 1f);

        float score = capitalEfficiency * 0.4f + demandResponse * 0.35f + buzzScore * 0.25f;

        TurnGrade grade = score >= 0.85f ? TurnGrade.S
                        : score >= 0.70f ? TurnGrade.A
                        : score >= 0.55f ? TurnGrade.B
                        : score >= 0.40f ? TurnGrade.C
                        : score >= 0.25f ? TurnGrade.D
                        : TurnGrade.E;

        string comment = grade switch
        {
            TurnGrade.S => "在庫・市場・広告、全てが完璧に噛み合った日だ！",
            TurnGrade.A => "安定した経営センスを見せてくれた。",
            TurnGrade.B => "悪くはないが、まだ伸ばせる余地がある。",
            TurnGrade.C => "どこかで判断が一歩遅れた日だった。",
            TurnGrade.D => "今日は流れを掴めなかった。",
            _           => "明日こそ巻き返そう！",
        };

        Debug.Log($"[TurnGrade] CapitalEfficiency={capitalEfficiency:P0} DemandResponse={demandResponse:P0} Buzz={buzzScore:P0} → Score={score:F2} → {grade}");

        return (grade, comment);
    }

    /// <summary>
    /// サマリーパネルを表示する。
    /// TomsShopPresenter から営業フェーズの「営業開始」演出完了時に呼ばれる。
    /// </summary>
    public void ShowSummary() => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.TurnEndSummary);

    /// <summary>
    /// 品出し中のアイテムから売上データを集計する。
    /// SimulateShopSales の結果（itemId → soldCount）を使って正確な販売数を取得する。
    /// </summary>
    private List<TurnEndSummaryItemData> BuildSummaryItems(Dictionary<string, int> salesResult)
    {
        var result = new List<TurnEndSummaryItemData>();

        foreach (var runtime in itemModel.RuntimeItems)
        {
            // 販売結果に載っているアイテム、または品出し中で売れ残ったアイテムを対象にする。
            // ※ 全量売却で在庫が尽きると DisplayStock が 0 にクランプされるため、
            //    陳列状態だけで絞ると「売れたのにサマリーに出ない」ことになる。
            bool sold = salesResult.ContainsKey(runtime.ItemId);
            bool stillDisplayed = runtime.IsDisplay.Value && runtime.DisplayStock.Value > 0;
            if (!sold && !stillDisplayed)
                continue;

            // シミュレーション結果から販売数を取得
            int soldCount = salesResult.TryGetValue(runtime.ItemId, out var count) ? count : 0;
            int revenue = soldCount * runtime.CurrentPrice.Value;

            // 需要トレンドの判定
            DemandTrend trend;
            float demand = runtime.Demand.Value;
            if (demand >= 0.7f)
                trend = DemandTrend.Up;
            else if (demand <= 0.3f)
                trend = DemandTrend.Down;
            else
                trend = DemandTrend.Flat;

            // 需要変動の主因を判定
            float demandDelta = runtime.Demand.Value - runtime.PreviousDemand;
            DemandChangeCause cause;
            if (runtime.Trend > 0.4f && demandDelta > 0.02f)
                cause = DemandChangeCause.TrendUp;
            else if (demandDelta > 0.02f)
                cause = DemandChangeCause.Displayed;
            else if (demandDelta < -0.03f)
                cause = DemandChangeCause.TrendDown;
            else
                cause = DemandChangeCause.Stable;

            result.Add(new TurnEndSummaryItemData
            {
                ItemName = runtime.ItemName,
                SoldCount = soldCount,
                Revenue = revenue,
                Price = runtime.CurrentPrice.Value,
                Demand = demand,
                Trend = trend,
                ItemIcon = runtime.ItemIcon,
                Cause = cause,
                DemandDelta = demandDelta
            });
        }

        return result;
    }

    public void Dispose() => disposables.Dispose();
}

/// <summary>1日の営業の精算結果（サマリーと営業ダイジェストで共有する）。</summary>
public class TurnEndDayResult
{
    public int Turn;
    /// <summary>今日約定した売り注文（昨日以前に並べた分）。</summary>
    public SellSettlementResult Settlement;
    /// <summary>約定時の倍率の内訳（約定が無ければ null）。</summary>
    public SalesBreakdown Breakdown;
    public List<TurnEndSummaryItemData> Items;
    /// <summary>今日並べて売り注文になった品と数（入金は明日の夜）。</summary>
    public List<(RuntimeItemData item, int quantity)> PlacedItems;
    public int PendingEstimate;
    public int SoldCount;
    public int DaysUntilStream;
    public TurnGrade Grade;
    public string Comment;
    /// <summary>営業売上に効いたレリックと倍率。</summary>
    public List<(RelicDefinition relic, float factor)> RelicFactors;
}

using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// そのシーンにある Model を購読して実績の条件を判定し、AchievementService へ解除・進捗を送る EntryPoint。
/// 店（GameLifetimeScope）と村（VillageLifetimeScope）に登録する。依存はすべて任意で、
/// シーンに無い Model の実績は判定しない（DebugMenuView と同じ TryResolve 方式）。
///
/// エディタの自動テストプレイ（AutoPlayHeadlessGame）は DI を使わないので、Sources を直接渡して作る。
/// ReactiveProperty / OnChanged はロード時にも流れるので、「その時点の状態を見直す」合図として扱い、
/// 「手に入れた瞬間」の意味では使わない。
/// </summary>
public sealed class AchievementTracker : IInitializable, IDisposable
{
    /// <summary>判定に使う Model 一式（どれも null 可）。</summary>
    public sealed class Sources
    {
        public TomsModel Toms;
        public ItemModel Items;
        public SellOrderModel SellOrders;
        public ShopStatusModel ShopStatus;
        public ShopMachineModel Machines;
        public RelicInventoryModel Relics;
        public RelicEffectResolver RelicResolver;
        public ScrapbookModel Scrapbook;
        public NewspaperSubscriptionModel Subscriptions;
        public MarketingFacade Marketing;
        public GameFlowManager Flow;
        public HeroModel Hero;
        public DungeonRepository Dungeons;
        public VillageModel Village;
        /// <summary>
        /// 指定すると、この BattleOutputData から来た配信の精算だけを見る（自動テストで複数のランを交互に回すとき用）。
        /// null なら全部見る（本番はシーンに1つなので null でよい）。
        /// </summary>
        public BattleOutputData BattleOutput;
    }

    private readonly Sources src;
    private readonly AchievementService service;
    private readonly CompositeDisposable disposables = new();
    private int companyCount = -1;

    [Inject]
    public AchievementTracker(IObjectResolver resolver)
        : this(ResolveSources(resolver))
    {
    }

    public AchievementTracker(Sources sources)
    {
        src = sources ?? new Sources();
        service = AchievementService.Shared;
    }

    private static Sources ResolveSources(IObjectResolver resolver)
    {
        var s = new Sources();
        if (resolver == null) return s;
        resolver.TryResolve(out s.Toms);
        resolver.TryResolve(out s.Items);
        resolver.TryResolve(out s.SellOrders);
        resolver.TryResolve(out s.ShopStatus);
        resolver.TryResolve(out s.Machines);
        resolver.TryResolve(out s.Relics);
        resolver.TryResolve(out s.RelicResolver);
        resolver.TryResolve(out s.Scrapbook);
        resolver.TryResolve(out s.Subscriptions);
        resolver.TryResolve(out s.Marketing);
        resolver.TryResolve(out s.Flow);
        resolver.TryResolve(out s.Hero);
        resolver.TryResolve(out s.Dungeons);
        resolver.TryResolve(out s.Village);
        return s;
    }

    /// <summary>
    /// 購読を張る。IStartable.Start ではなく Initialize（全 EntryPoint の Start より前）で張るのは、
    /// 配信から戻った店で BattleResultHandler.Start が精算と日送り（売り注文の約定・バズ判定）を
    /// その場で行うため。Start で張ると、それより後に登録した分が取りこぼす。
    /// </summary>
    public void Initialize()
    {
        try
        {
            Bind();
        }
        catch (Exception e)
        {
            // 実績の不具合でゲームを止めない
            Debug.LogException(e);
        }
    }

    public void Dispose() => disposables.Dispose();

    private int CurrentTurn =>
        src.Flow != null ? src.Flow.CurrentTurn.Value : src.Toms != null ? src.Toms.CurrentTurn.Value : 0;

    private void Bind()
    {
        if (src.Toms != null)
        {
            // 10/11 百万長者・千万長者
            src.Toms.PlayerMoney.Subscribe(OnMoney).AddTo(disposables);
            // 16 首の皮一枚: 返済した（DebtCycle が増えた）直後の所持金。ロード時の値の入れ直しは増えないので数えない
            src.Toms.DebtCycle.Pairwise().Subscribe(p =>
            {
                if (p.Current > p.Previous) OnDebtPaid(p.Current);
            }).AddTo(disposables);
        }

        // 1/9 店の売り注文の入金
        src.SellOrders?.OnSettled.Subscribe(r =>
        {
            if (r != null && r.TotalIncome > 0) OnSalesIncome(r.TotalIncome, "店の売り注文が約定");
        }).AddTo(disposables);

        // 2-7 配信の精算
        AchievementEvents.StreamSettled.Subscribe(OnStreamSettled).AddTo(disposables);

        // 13 人気店
        src.ShopStatus?.Followers.Subscribe(f =>
        {
            service.ReportMax(AchievementStat.MaxFollowers, f);
            if (f >= AchievementDefinitions.PopularFollowers) service.Unlock(AchievementId.PopularShop, $"フォロワー {f:N0}人");
        }).AddTo(disposables);

        // 14 ゴーレム工房
        if (src.Machines != null)
        {
            src.Machines.OnPlacementChanged.Subscribe(_ => CheckGolems()).AddTo(disposables);
            CheckGolems();
        }

        // 18 収集家
        if (src.Relics != null)
        {
            src.Relics.OnChanged.Subscribe(_ => CheckRelics()).AddTo(disposables);
            CheckRelics();
        }

        // 3/8 新聞のスクラップ
        if (src.Scrapbook != null)
        {
            src.Scrapbook.OnPinned.Subscribe(x => OnScrapPinned(x.entry, x.turn)).AddTo(disposables);
            src.Scrapbook.OnConfirmed.Subscribe(x =>
            {
                // 誤報だと確かめた（ScrapState.ConfirmedMiss）
                if (x.scrap != null && x.scrap.confirmed && !x.scrap.hit)
                    service.Unlock(AchievementId.NotFooled, $"誤報を確認: {x.scrap.articleId}");
            }).AddTo(disposables);
        }

        // 17 全紙購読（ラン跨ぎで数える）
        if (src.Subscriptions != null)
        {
            src.Subscriptions.OnSubscribed.Subscribe(OnSubscribed).AddTo(disposables);
            foreach (var c in src.Subscriptions.ToPlain()) OnSubscribed(c.companyId);
        }

        // 12 バズの波 / 3 読み的中: 日送りの最後（経済・バズの処理が済んだ後）
        src.Marketing?.OnTurnProcessed.Subscribe(OnDayProcessed).AddTo(disposables);

        // 7 大穴: 配信日に入ったとき（寄り道を始めた時点）のクリア見込みを控える
        src.Flow?.IsAwaitingStream.Subscribe(awaiting =>
        {
            if (awaiting) RecordDepartureClearPct();
        }).AddTo(disposables);

        // 22 村おこし
        if (src.Village != null)
        {
            src.Village.OnInvested.Subscribe(_ => CheckVillage()).AddTo(disposables);
            CheckVillage();
        }
    }

    // ========================================
    // お金・入金
    // ========================================

    private void OnMoney(int money)
    {
        service.ReportMax(AchievementStat.MaxMoney, money);
        if (money >= AchievementDefinitions.MillionaireMoney) service.Unlock(AchievementId.Millionaire, $"所持金 {money:N0}G");
        if (money >= AchievementDefinitions.TenMillionaireMoney) service.Unlock(AchievementId.TenMillionaire, $"所持金 {money:N0}G");
    }

    /// <summary>売れて入った分だけを数える（配当・レリック換金・イベントの増減は入れない）。</summary>
    private void OnSalesIncome(int amount, string reason)
    {
        if (amount <= 0) return;
        service.Unlock(AchievementId.FirstIncome, $"{reason} +{amount:N0}G");

        int turn = CurrentTurn;
        var run = AchievementStore.LoadRun();
        if (run.incomeTurn != turn)
        {
            run.incomeTurn = turn;
            run.incomeSum = 0;
        }
        run.incomeSum = TomsModel.SaturatingAdd(run.incomeSum, amount);
        AchievementStore.SaveRun(run);

        service.ReportMax(AchievementStat.MaxDailyIncome, run.incomeSum);
        if (run.incomeSum > AchievementDefinitions.DailyIncomeThreshold)
            service.Unlock(AchievementId.Daily100k, $"{turn}日目の入金 {run.incomeSum:N0}G");
    }

    private void OnDebtPaid(int cycle)
    {
        int money = src.Toms.PlayerMoney.Value;
        if (money < AchievementDefinitions.ByAHairMoney)
            service.Unlock(AchievementId.ByAHair, $"返済{cycle}回目の後の所持金 {money:N0}G");
    }

    // ========================================
    // 配信
    // ========================================

    private void OnStreamSettled(StreamSettledInfo info)
    {
        if (info == null) return;
        if (src.BattleOutput != null && !ReferenceEquals(info.Origin, src.BattleOutput)) return;
        try
        {
            HandleStreamSettled(info);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private void HandleStreamSettled(StreamSettledInfo info)
    {
        // 途中終了（配信終了ボタン）は「見届けた」に数えない
        if (info.Result != BattleResult.Abort) service.Unlock(AchievementId.FirstStream, $"配信 {info.Result}");
        if (info.GrossSales > 0) OnSalesIncome(info.GrossSales, "配信で販売");

        var run = AchievementStore.LoadRun();
        bool changed = false;

        if (info.Result == BattleResult.Victory)
        {
            // 勝利ボーナス（値段×5）が掛かった装備を、まだ在庫として抱えている＝装備で儲けた
            string held = HeldEquipment(info);
            if (held != null)
            {
                service.Unlock(AchievementId.EquipAhead, $"勇者が勝利、装備 {held} を在庫で保有");
                if (!run.heroSideProfit) { run.heroSideProfit = true; changed = true; }

                // 配信日に入ったときのクリア見込みが「不利」以下だったか
                if (run.departureTurn == info.Turn && AchievementDefinitions.IsLongShotClearPct(run.departureClearPct))
                    service.Unlock(AchievementId.LongShot, $"クリア見込み {run.departureClearPct:F0}% から勝利");
            }
        }
        else if (info.Result == BattleResult.Defeat)
        {
            // 魔王軍支援でダンジョンを上げてあり、防衛報酬（配当）が出た
            int support = SupportLevels(info);
            int reward = DefenseReward(info);
            if (support > 0 && reward > 0)
            {
                service.Unlock(AchievementId.DemonDividend, $"出資 {support}段階、配当 {reward:N0}G");
                if (!run.demonSideProfit) { run.demonSideProfit = true; changed = true; }
            }
        }

        if (changed) AchievementStore.SaveRun(run);
        if (run.heroSideProfit && run.demonSideProfit)
            service.Unlock(AchievementId.BothSides, "同じ出店で勇者側・魔王側の両方で儲けた");
    }

    private string HeldEquipment(StreamSettledInfo info)
    {
        if (src.Items == null) return null;
        foreach (var id in AchievementEvents.EquippedIds(info))
        {
            var item = src.Items.GetRuntimeItem(id);
            if (item != null && item.Stock.Value > 0) return item.ItemName;
        }
        return null;
    }

    /// <summary>魔王軍支援でダンジョンを何段階上げたか（初期レベルからの差）。</summary>
    private int SupportLevels(StreamSettledInfo info)
    {
        if (src.Dungeons == null || !src.Dungeons.TryGetById(info.DungeonKey, out var d) || d == null) return 0;
        int level = info.DungeonLevel >= 0 ? info.DungeonLevel : d.currentDungeonLevel;
        return Mathf.Max(0, level - Mathf.Max(DungeonLevels.Unfunded, d.initDungeonLevel));
    }

    /// <summary>
    /// BattleSceneStarter.CalculateDefeatReward と同じ式（SupportOdds.Payout: 基本額 × レリック × 見込みの段のオッズ、出資の合計が下限）。
    /// 防衛報酬は TotalEarnings に混ざって返ってくるため計算し直す。
    /// </summary>
    private int DefenseReward(StreamSettledInfo info)
    {
        if (src.Dungeons == null || !src.Dungeons.TryGetById(info.DungeonKey, out var d) || d == null) return 0;
        // Lv0（未出資）は防衛報酬 0 なので「配当確定」は解除されない
        int level = info.DungeonLevel >= 0 ? info.DungeonLevel : d.currentDungeonLevel;
        int baseReward = d.GetDefenseReward(level);
        return ComputeDefenseReward(baseReward, RelicBattleEffects.DefeatRewardMul, info.PreStreamClearPct, d.GetInvestedTotal(level));
    }

    /// <summary>実績の「配当」の額（実際の支払いと同じ SupportOdds.Payout。stake = 出資の合計で元本保証）。テストから呼ぶため static。</summary>
    public static int ComputeDefenseReward(int baseReward, float relicMul, float preStreamClearPct, int stake, StreamTuningData tuning = null)
        => SupportOdds.Payout(baseReward, relicMul, preStreamClearPct, stake, tuning).Total;

    private void RecordDepartureClearPct()
    {
        if (src.Flow == null || src.Hero == null || src.Dungeons == null) return;
        var key = src.Flow.PendingBattleDungeon;
        if (key == null || !src.Dungeons.TryGetById(key.Value, out var dungeon) || dungeon == null) return;

        float pct = ClearProbabilityCalculator.Calculate(src.Hero.heroData, src.Items, dungeon, dungeon.currentDungeonLevel, src.RelicResolver);
        var run = AchievementStore.LoadRun();
        run.departureTurn = CurrentTurn;
        run.departureClearPct = pct;
        AchievementStore.SaveRun(run);
        Debug.Log($"[Achievement] 配信日のクリア見込みを記録: {key.Value} {pct:F0}% (turn {run.departureTurn})");
    }

    // ========================================
    // 店
    // ========================================

    private void CheckGolems()
    {
        int golems = 0;
        foreach (var p in src.Machines.Placed)
        {
            // ゴーレム＝毎朝お金を生むマシン（machine_money_s/m/l）
            var m = src.Machines.GetMachine(p.MachineId);
            if (m != null && m.effectType == ShopMachineEffectType.DailyMoney) golems++;
        }
        service.ReportMax(AchievementStat.MaxGolems, golems);
        if (golems >= AchievementDefinitions.GolemCount) service.Unlock(AchievementId.GolemWorkshop, $"ゴーレム {golems}台");
    }

    private void CheckRelics()
    {
        var run = AchievementStore.LoadRun();
        bool changed = false;
        foreach (var o in src.Relics.Owned)
        {
            if (o == null || string.IsNullOrEmpty(o.RelicId) || run.relicsSeen.Contains(o.RelicId)) continue;
            run.relicsSeen.Add(o.RelicId);
            changed = true;
        }
        if (!changed) return;
        AchievementStore.SaveRun(run);

        int count = run.relicsSeen.Count;
        service.ReportMax(AchievementStat.MaxRelicsInRun, count);
        if (count >= AchievementDefinitions.CollectorRelics) service.Unlock(AchievementId.Collector, $"このランのレリック {count}個");
    }

    private void OnDayProcessed(BuzzTurnResult _)
    {
        try
        {
            UpdateBuzzStreak();
            CheckScrapWatches();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private void UpdateBuzzStreak()
    {
        var buzz = src.Marketing?.Buzz;
        if (buzz == null) return;
        int turn = CurrentTurn;
        var run = AchievementStore.LoadRun();
        if (run.lastBuzzTurn == turn) return; // 同じ日に2回来ても数えない

        // 炎上はバズに数えない
        bool buzzing = buzz.IsBuzzActive.Value && buzz.CurrentBuzzType.Value != BuzzType.Flame;
        if (!buzzing)
        {
            if (run.buzzStreak == 0) return;
            run.buzzStreak = 0;
            AchievementStore.SaveRun(run);
            return;
        }

        run.buzzStreak = run.lastBuzzTurn == turn - 1 ? run.buzzStreak + 1 : 1;
        run.lastBuzzTurn = turn;
        AchievementStore.SaveRun(run);

        service.ReportMax(AchievementStat.BestBuzzStreak, run.buzzStreak);
        if (run.buzzStreak >= AchievementDefinitions.BuzzStreakDays)
            service.Unlock(AchievementId.BuzzWave, $"バズ {run.buzzStreak}日連続");
    }

    // ========================================
    // 新聞
    // ========================================

    private void OnScrapPinned(NewsIssueEntry entry, int turn)
    {
        // 値上がりを報じる本物の記事だけを見張る（誤報・効果なし・値下がりの記事では解除しない）
        var ev = entry?.eventInstance;
        if (ev == null || src.Items == null || !ev.HasRealEffect) return;
        if (ev.trendDelta <= 0f && ev.demandKick <= 0f) return;

        var watch = new AchievementRunData.ScrapWatch
        {
            entryKey = entry.Key,
            effectTurn = ev.effectTurn,
            settleTurn = ev.SettleTurn,
        };
        foreach (var item in src.Items.RuntimeItems)
        {
            if (!ev.Matches(item)) continue;
            watch.itemIds.Add(item.ItemId);
            watch.pinnedPrices.Add(item.CurrentPrice.Value);
        }
        if (watch.itemIds.Count == 0) return;

        var run = AchievementStore.LoadRun();
        run.scrapWatches.RemoveAll(w => w.entryKey == watch.entryKey);
        run.scrapWatches.Add(watch);
        AchievementStore.SaveRun(run);
    }

    /// <summary>
    /// 効き始め（発効）から決着の翌日までに、効き先のどれかがスクラップした日より高くなったら「読み的中」。
    /// 決着の翌日を過ぎた見張りは捨てる。
    /// </summary>
    private void CheckScrapWatches()
    {
        if (src.Items == null) return;
        var run = AchievementStore.LoadRun();
        if (run.scrapWatches.Count == 0) return;

        int turn = CurrentTurn;
        bool changed = false;
        for (int i = run.scrapWatches.Count - 1; i >= 0; i--)
        {
            var w = run.scrapWatches[i];
            if (turn < w.effectTurn) continue;

            if (TryFindRise(w, out string detail))
            {
                service.Unlock(AchievementId.ScrapPrediction, detail);
                run.scrapWatches.RemoveAt(i);
                changed = true;
            }
            else if (turn > w.settleTurn + 1)
            {
                run.scrapWatches.RemoveAt(i);
                changed = true;
            }
        }
        if (changed) AchievementStore.SaveRun(run);
    }

    private bool TryFindRise(AchievementRunData.ScrapWatch w, out string detail)
    {
        for (int k = 0; k < w.itemIds.Count && k < w.pinnedPrices.Count; k++)
        {
            var item = src.Items.GetRuntimeItem(w.itemIds[k]);
            // 相場の揺れで偶然上がっただけを拾わないよう、スクラップした日の値段から1割以上の値上がりを求める
            if (item == null || item.CurrentPrice.Value < w.pinnedPrices[k] * AchievementDefinitions.ScrapRiseRate) continue;
            detail = $"{w.entryKey} の効き先 {item.ItemName} が {w.pinnedPrices[k]:N0}G → {item.CurrentPrice.Value:N0}G";
            return true;
        }
        detail = null;
        return false;
    }

    private void OnSubscribed(string companyId)
    {
        service.AddSubscribedCompany(companyId);
        if (companyCount < 0)
        {
            var companies = NewsMasterLoader.LoadCompanies();
            companyCount = companies != null ? companies.Count : 0;
        }
        if (companyCount > 0 && service.SubscribedCompanyCount >= companyCount)
            service.Unlock(AchievementId.AllPapers, $"{service.SubscribedCompanyCount}社を購読");
    }

    // ========================================
    // 村
    // ========================================

    private void CheckVillage()
    {
        if (src.Village.AreAllFacilitiesBuilt())
            service.Unlock(AchievementId.VillageRevival, $"{src.Village.Facilities.Count}施設すべて建設");
    }

    /// <summary>デバッグメニュー用: このランの途中経過の要約。</summary>
    public static List<string> DescribeRun()
    {
        var run = AchievementStore.LoadRun();
        return new List<string>
        {
            $"今日の入金: {(run.incomeTurn >= 0 ? $"{run.incomeTurn}日目 {run.incomeSum:N0}G" : "-")}",
            $"バズ連続: {run.buzzStreak}日（最後 {run.lastBuzzTurn}日目）",
            $"勇者側で儲けた: {(run.heroSideProfit ? "済" : "未")} / 魔王側で儲けた: {(run.demonSideProfit ? "済" : "未")}",
            $"配信日のクリア見込み: {(run.departureTurn >= 0 ? $"{run.departureTurn}日目 {run.departureClearPct:F0}%" : "-")}",
            $"このランのレリック: {run.relicsSeen.Count}個",
            $"見張り中のスクラップ: {run.scrapWatches.Count}件",
            $"クリアを数えた: {(run.clearReported ? "済" : "未")}",
        };
    }
}

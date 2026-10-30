using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// =====================================================================
// Jev AutoPlay — ヘッドレスのゲーム本体
//
// 本物のモデル（ItemModel / TomsModel / SellOrderModel / GameFlowManager / BattleResultHandler …）を
// VContainer を使わずに直接組み立て、UI 無しでランを回す。
// - セーブは SaveSlotManager.DevRootOverride で隔離フォルダへ逃がす（ユーザーのスロットを汚さない）
// - シーン遷移は SceneTransitionService.DevSceneLoadInterceptor で横取りして「どこへ行こうとしたか」だけ記録
// - 配信（FightScene）は AutoPlayBattleSurrogate で解決し、結果の反映は本物の BattleResultHandler に任せる
// Presenter（View 依存）にしか無い処理は最小限だけここに写している（下の「写し」コメント参照）。
// =====================================================================

/// <summary>1バッチで使い回すマスターデータ・設定。メインスレッドで1回だけ読む。</summary>
public sealed class AutoPlayAssets
{
    public List<ItemData> MasterItems;
    public ItemVisualSettings Visual;
    public ShopEconomySettings Economy;
    public ShopLevelSettings ShopLevels;
    public FinanceSettings Finance;
    public List<FinancialProductData> FinancialProducts;
    public List<RelicDefinition> Relics;
    public List<ShopMachineData> Machines;
    public GameBalanceData Balance;
    public List<AdvertisementData> Advertisements;
    public List<FollowerMilestoneData> Milestones;
    public BuzzEffectData FlameBuzz;
    public BuzzEffectData NormalBuzz;
    public BuzzEffectData BigBuzz;
    public List<DungeonInfoScriptableObj> Dungeons;
    /// <summary>配信中の介入・視聴者スパチャの調整値（Addressable → RemoteBalance → 既定値）。</summary>
    public StreamingInteractionSettings Interaction;
    /// <summary>配信の値動き・返金騒ぎの割合（Addressable → RemoteBalance「battlePrice」→ 既定値）。</summary>
    public BattlePriceSettings BattlePrice;

    /// <summary>
    /// GameLifetimeScope.Configure と同じ経路（Addressables + RemoteBalance）で読む。
    /// ダンジョンだけはシーン上の DungeonRepository が持っているため AssetDatabase から集める。
    /// 失敗したら null と理由を返す（エディットモードで Addressables が読めない環境向けの案内付き）。
    /// </summary>
    public static AutoPlayAssets Load(out string error)
    {
        error = null;
        try
        {
            var a = new AutoPlayAssets();
            a.MasterItems = ItemMaster.ApplyOverrides(AddressableLoader.LoadAll<ItemData>("ItemData"));
            if (a.MasterItems == null || a.MasterItems.Count == 0)
            {
                error = "ItemData を Addressables から読めませんでした。エディットモードで読めない場合は " +
                        "TitleScene を Play してから実行してください。";
                return null;
            }

            a.Visual = AddressableLoader.Load<ItemVisualSettings>("ItemVisualSettings");
            a.Economy = Or(AddressableLoader.Load<ShopEconomySettings>("ShopEconomySettings"));
            a.Economy = RemoteBalance.ApplyOverwrite("shopEconomy", a.Economy);
            a.ShopLevels = RemoteBalance.ApplyOverwrite("shopLevel", Or(AddressableLoader.Load<ShopLevelSettings>("ShopLevelSettings")));
            a.Finance = RemoteBalance.ApplyOverwrite("finance", Or(AddressableLoader.Load<FinanceSettings>("FinanceSettings")));
            a.FinancialProducts = RemoteBalance.ApplyList("financialProducts",
                AddressableLoader.LoadAll<FinancialProductData>("FinancialProductData"), p => p.productId);
            a.Relics = RemoteBalance.ApplyList("relics", AddressableLoader.LoadAll<RelicDefinition>("RelicData"), r => r.relicId);
            a.Machines = ShopMachineData.ApplyRemoteBalance(AddressableLoader.LoadAll<ShopMachineData>("ShopMachineData"));
            a.Balance = RemoteBalance.ApplyOverwrite("gameBalance", Or(AddressableLoader.Load<GameBalanceData>("Marketing/GameBalanceData")));
            a.Advertisements = RemoteBalance.ApplyList("advertisements",
                AddressableLoader.LoadAll<AdvertisementData>("AdvertisementData"), x => x.advertisementName);
            a.Milestones = RemoteBalance.ApplyList("followerMilestones",
                AddressableLoader.LoadAll<FollowerMilestoneData>("FollowerMilestoneData"), m => m.requiredFollowers.ToString());

            var buzz = RemoteBalance.ApplyList("buzzEffects", AddressableLoader.LoadAll<BuzzEffectData>("BuzzEffectData"), b => b.buzzType.ToString());
            a.FlameBuzz = buzz.FirstOrDefault(b => b.buzzType == BuzzType.Flame) ?? MakeBuzz(BuzzType.Flame);
            a.NormalBuzz = buzz.FirstOrDefault(b => b.buzzType == BuzzType.Normal) ?? MakeBuzz(BuzzType.Normal);
            a.BigBuzz = buzz.FirstOrDefault(b => b.buzzType == BuzzType.Big) ?? MakeBuzz(BuzzType.Big);

            a.Dungeons = AssetDatabase.FindAssets("t:DungeonInfoScriptableObj")
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(path => AssetDatabase.LoadAssetAtPath<DungeonInfoScriptableObj>(path))
                .Where(d => d != null)
                .ToList();
            a.Dungeons = DungeonInfoScriptableObj.ApplyRemoteBalance(a.Dungeons);
            a.Interaction = StreamingInteractionSettings.Load();
            a.BattlePrice = RemoteBalance.ApplyOverwrite("battlePrice", Or(AddressableLoader.Load<BattlePriceSettings>("BattlePriceSettings")));
            if (a.Dungeons.Count == 0)
            {
                error = "DungeonInfoScriptableObj が見つかりませんでした。";
                return null;
            }

            return a;
        }
        catch (Exception e)
        {
            error = "アセットの読み込みに失敗しました（エディットモードで Addressables が使えない場合は " +
                    "TitleScene を Play してから実行してください）: " + e.Message;
            return null;
        }
    }

    private static T Or<T>(T asset) where T : ScriptableObject => asset != null ? asset : ScriptableObject.CreateInstance<T>();

    private static BuzzEffectData MakeBuzz(BuzzType type)
    {
        var b = ScriptableObject.CreateInstance<BuzzEffectData>();
        b.buzzType = type;
        return b;
    }
}

/// <summary>
/// 実行中のラン。複数ランをメインスレッド上で交互に進めるため、静的な状態
/// （セーブ先・UnityEngine.Random の状態・ログの帰属先・シーン遷移の受け手）を
/// <see cref="Enter"/> の間だけそのランへ切り替える。
/// </summary>
public sealed class AutoPlayRunContext
{
    public static AutoPlayRunContext Current { get; private set; }

    public readonly string RunId;
    public readonly string SaveRoot;
    private UnityEngine.Random.State _rng;
    private bool _rngInitialized;
    private readonly int _seed;

    /// <summary>横取りしたシーン遷移（最新）。</summary>
    public string LastSceneRequest;
    /// <summary>Enter 中に出た警告・エラー（ランナーが回収してクリアする）。</summary>
    public readonly List<(LogType type, string message)> CapturedLogs = new();

    public AutoPlayRunContext(string runId, string saveRoot, int seed)
    {
        RunId = runId;
        SaveRoot = saveRoot;
        _seed = seed;
    }

    public Scope Enter() => new Scope(this);

    public readonly struct Scope : IDisposable
    {
        private readonly AutoPlayRunContext _ctx;
        private readonly AutoPlayRunContext _prevCtx;
        private readonly string _prevRoot;
        private readonly UnityEngine.Random.State _prevRng;

        public Scope(AutoPlayRunContext ctx)
        {
            _ctx = ctx;
            _prevCtx = Current;
            _prevRoot = SaveSlotManager.DevRootOverride;
            _prevRng = UnityEngine.Random.state;

            Current = ctx;
            SaveSlotManager.DevRootOverride = ctx.SaveRoot;
            if (!ctx._rngInitialized)
            {
                UnityEngine.Random.InitState(ctx._seed);
                ctx._rngInitialized = true;
            }
            else
            {
                UnityEngine.Random.state = ctx._rng;
            }
        }

        public void Dispose()
        {
            _ctx._rng = UnityEngine.Random.state;
            UnityEngine.Random.state = _prevRng;
            SaveSlotManager.DevRootOverride = _prevRoot;
            Current = _prevCtx;
        }
    }
}

/// <summary>ヘッドレスのゲーム。<see cref="IPlayerActions"/> の実装。必ず ctx.Enter() の中で呼ぶ。</summary>
public sealed class AutoPlayHeadlessGame : IPlayerActions, IDisposable
{
    public sealed class Settings
    {
        public GameModeId Mode = GameModeId.Short;
        public int Seed = 1;
        /// <summary>0 以下なら GameConst.InitMoney。</summary>
        public int InitMoneyOverride;
        /// <summary>配信販売の回数倍率（サロゲートの較正用）。</summary>
        public float StreamSalesScale = 1f;
        /// <summary>配信に持ち込める銘柄数の上限（サロゲート）。</summary>
        public int StreamMaxKinds = 6;
        /// <summary>true = 勝敗をクリア確率で抽選 / false = ドライランで決定論（実戦闘と同じ扱い）。</summary>
        public bool ProbabilisticBattle;
        /// <summary>配信中の介入（スパチャ）をボットに使わせるか。false = 予定を渡されても無視（介入なしの比較用）。</summary>
        public bool EnableInterventions = true;
        /// <summary>視聴者の赤スパで必殺技が出るか。</summary>
        public AutoPlayViewerSuperChatMode ViewerSuperChat = AutoPlayViewerSuperChatMode.FollowSettings;
        /// <summary>視聴者の赤スパが1戦闘ターンに起きる確率（較正値）。</summary>
        public float ViewerRedChancePerTurn = 0.03f;
        /// <summary>1戦闘ターン ≒ 何秒か（クールダウンの換算用。較正値）。</summary>
        public float SecondsPerBattleTurn = 3f;
        /// <summary>false = 新聞を購読できない（新聞の効果を測る対照群「+nonews」用）。</summary>
        public bool AllowNewspaper = true;
        /// <summary>1周目の鍛冶屋の立て替え（TutorialPresenter.GrantFundsAdvanceOnce）を2日目の朝に再現する額（0 = なし＝2周目以降と同じ）。</summary>
        public int TutorialAdvance;
    }

    // --- 当日の集計（ランナーが読む） ---
    public sealed class DayLedger
    {
        public int Spend;
        public int ShopIncome;
        public int StreamEarnings;
        public int DefeatReward;
        public int DebtPaid;
        public int SupportSpend;
        public int UpgradeSpend;
        public string BattleResult = "";
        public int InterventionSpent;
        public int NewspaperSpend;
        public int InterventionRefund;
        public string BattleDungeon = "";
        public float BattleClearPct;
        public int RejectedActions;
        public int AdSpend;
        public int MachineSpend;
        public int FinanceSpend;
        public int FireSale;
        public readonly List<string> Notes = new();
    }

    public readonly Settings Config;
    public readonly AutoPlayAssets Assets;
    public readonly AutoPlayRunContext Context;

    // --- 本物のモデル群 ---
    public ItemModel ItemModel { get; private set; }
    public TomsModel TomsModel { get; private set; }
    public HeroModel HeroModel { get; private set; }
    public SellOrderModel SellOrders { get; private set; }
    public PortfolioModel Portfolio { get; private set; }
    public ShopMachineModel Machines { get; private set; }
    public RelicInventoryModel RelicInventory { get; private set; }
    public RelicEffectResolver RelicResolver { get; private set; }
    public RelicRewardService RelicRewards { get; private set; }
    public ShopStatusModel ShopStatus { get; private set; }
    public MarketingFacade Marketing { get; private set; }
    public NewsModel News { get; private set; }
    public NewsEffectResolver NewsEffects { get; private set; }
    public NewspaperSubscriptionModel Subscriptions { get; private set; }
    public ScrapbookModel Scrapbook { get; private set; }
    public GameFlowManager Flow { get; private set; }
    public DungeonRepository Dungeons { get; private set; }

    private MorningReportModel _morning;
    private PendingEventData _pendingEvent;
    private TomsEventExecutor _eventExecutor;
    private StateManager _stateManager;
    private SceneTransitionService _sceneTransition;
    private BattleInputData _battleIn;
    private BattleOutputData _battleOut;
    private EventInputData _eventIn;
    private EventOutputData _eventOut;
    private StartModeData _startMode;
    private System.Random _rng;

    private bool _streamPending;
    private bool _advanceGranted;
    private int _lastSalesTurn = -1;
    private int _lastDayBeginTurn = -1;

    // 占い師（I-11）: 買った日と見立て（品 → +1/-1）
    private int _fortuneTurn = -1;
    private readonly Dictionary<string, int> _fortune = new();
    public int FortuneSpend { get; private set; }

    // 読んだ記事（reader ボット用）: 事象キー → (事象, 記事から読める発効日)。訂正記事を読んだら外す
    private readonly Dictionary<string, (NewsEventInstance ev, int effectTurn)> _readEvents = new();

    /// <summary>朝の相場の記録（ターン → 銘柄 → 値段・需要・客の数）。新聞の効果の計測（イベントスタディ）に使う。</summary>
    public readonly Dictionary<int, Dictionary<string, (int price, float demand, int customers)>> MorningMarket = new();

    public AutoPlayStage Stage { get; private set; } = AutoPlayStage.ShopDay;
    /// <summary>新聞の発行カレンダーの概要（フェーズ3のデータで回っているかの確認用）。</summary>
    public string NewsInfo { get; private set; } = "";
    public DayLedger Today { get; private set; } = new DayLedger();
    public AutoPlayBattleSurrogate.Outcome LastBattle { get; private set; }

    public AutoPlayHeadlessGame(Settings config, AutoPlayAssets assets, AutoPlayRunContext context)
    {
        Config = config;
        Assets = assets;
        Context = context;
        _rng = new System.Random(config.Seed * 7919 + 17);
    }

    // =================================================================
    // 構築と新規ラン開始（GameLifetimeScope.Configure + GameLifecycleHandler.InitializeNewGame の写し）
    // =================================================================

    public void StartNewRun()
    {
        Directory.CreateDirectory(Context.SaveRoot);

        _startMode = ScriptableObject.CreateInstance<StartModeData>();
        _startMode.SetFlowSelection(Config.Mode, true);
        _battleIn = ScriptableObject.CreateInstance<BattleInputData>();
        _battleOut = ScriptableObject.CreateInstance<BattleOutputData>();
        _eventIn = ScriptableObject.CreateInstance<EventInputData>();
        _eventOut = ScriptableObject.CreateInstance<EventOutputData>();

        ItemModel = new ItemModel(Assets.MasterItems, Assets.Visual);
        TomsModel = new TomsModel();
        SellOrders = new SellOrderModel();
        Portfolio = new PortfolioModel(Assets.FinancialProducts, Assets.Finance);
        Machines = new ShopMachineModel(Assets.Machines);
        _morning = new MorningReportModel();
        RelicInventory = new RelicInventoryModel(Assets.Relics);
        RelicResolver = new RelicEffectResolver(RelicInventory);
        var relicHooks = new RelicHookDispatcher(RelicInventory, new RelicBehaviourRegistry());
        RelicRewards = new RelicRewardService(RelicInventory);
        News = new NewsModel();
        Subscriptions = new NewspaperSubscriptionModel();
        Scrapbook = new ScrapbookModel();
        var newsEffects = new NewsEffectResolver(News);
        NewsEffects = newsEffects;
        HeroModel = new HeroModel();
        _pendingEvent = new PendingEventData();

        ShopStatus = new ShopStatusModel(Assets.Balance);
        var followers = new FollowerSystem(ShopStatus, Assets.Milestones);
        var ads = new AdvertisementSystem(ShopStatus, TomsModel, followers, Assets.Advertisements);
        var buzz = new BuzzSystem(ShopStatus, followers, ads, Assets.Balance,
            Assets.FlameBuzz, Assets.NormalBuzz, Assets.BigBuzz, RelicResolver);
        var sales = new SalesCalculator(buzz, followers, Machines, RelicResolver);
        Marketing = new MarketingFacade(ShopStatus, ads, buzz, followers, sales);

        _eventExecutor = new TomsEventExecutor(TomsModel, ItemModel, new DarkShopManager(), new EventFragManager(),
            ShopStatus, RelicInventory, RelicRewards, RelicResolver);

        _stateManager = new StateManager(null, _startMode);
        Dungeons = CreateDungeonRepository(Assets.Dungeons);
        _sceneTransition = new SceneTransitionService(_battleIn, _battleOut);

        Flow = new GameFlowManager(_stateManager, Dungeons, _battleIn, ItemModel, Assets.Economy, TomsModel,
            _sceneTransition, HeroModel, _eventIn, _eventOut, _pendingEvent, Marketing, ShopStatus,
            SellOrders, Portfolio, Machines, _morning, RelicResolver, relicHooks, News, newsEffects);

        // 配信日に入ったら「寄り道ハンドラ」で止めてもらう＝FightScene へは行かずにこちらへ制御が戻る
        Flow.SetPreStreamHandler(_ => _streamPending = true);

        // --- InitializeNewGame の写し ---
        Dungeons.ResetToInitial();
        ItemModel.InitializeRuntimeItemsFromMaster();
        TomsModel.Initialize(Config.InitMoneyOverride > 0 ? Config.InitMoneyOverride : -1);
        HeroModel.InitializeRuntimeHeroFromMaster();
        HeroModel.SaveHeroData();
        ShopStatus.Reset();
        SellOrders.Clear();
        Portfolio.Clear();
        Machines.Clear();
        RelicInventory.Clear();

        TomsModel.UseAutoFlow = true;
        TomsModel.GameMode = Config.Mode;
        TomsModel.FlowSeed = Config.Seed;
        Flow.InitializeFlow(true, Config.Mode, Config.Seed);
        Flow.RestoreIndex(0);
        TomsModel.SavePlayerMoney();
        ItemModel.SaveData();
        News.Build(Config.Seed);
        NewsInfo = $"phase3={News.IsPhase3} events={News.Events.Count} calendar={News.Calendar.Count}";

        Stage = AutoPlayStage.ShopDay;

        // Steam 実績の判定を本物と同じ Model の購読で走らせる（記録は隔離フォルダ、Steam には送らない）。
        // ログの [Achievement] 解除 でボットがどの実績に届いたかが分かる
        _achievements = new AchievementTracker(new AchievementTracker.Sources
        {
            Toms = TomsModel, Items = ItemModel, SellOrders = SellOrders, ShopStatus = ShopStatus,
            Machines = Machines, Relics = RelicInventory, RelicResolver = RelicResolver,
            Scrapbook = Scrapbook, Subscriptions = Subscriptions, Marketing = Marketing,
            Flow = Flow, Hero = HeroModel, Dungeons = Dungeons, BattleOutput = _battleOut,
        });
        _achievements.Initialize();
    }

    private AchievementTracker _achievements;

    private static DungeonRepository CreateDungeonRepository(List<DungeonInfoScriptableObj> infos)
    {
        // MonoBehaviour なので非アクティブの GameObject に付けて Awake を走らせない
        // （Awake は現行スロットのセーブを読みに行くため）。保存もされない。
        var go = new GameObject("AutoPlay_DungeonRepository") { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false);
        var repo = go.AddComponent<DungeonRepository>();
        var field = typeof(DungeonRepository).GetField("dungeonInfos", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null) throw new MissingFieldException("DungeonRepository.dungeonInfos が見つかりません（名前が変わった？）");
        field.SetValue(repo, new List<DungeonInfoScriptableObj>(infos));
        repo.SetCatalog(repo.CreateCatalog());
        return repo;
    }

    public void Dispose()
    {
        if (Dungeons != null) UnityEngine.Object.DestroyImmediate(Dungeons.gameObject);
        foreach (var so in new ScriptableObject[] { _startMode, _battleIn, _battleOut, _eventIn, _eventOut })
            if (so != null) UnityEngine.Object.DestroyImmediate(so);
        _achievements?.Dispose();
        Flow?.Dispose();
        _stateManager?.Dispose();
    }

    // =================================================================
    // 状態
    // =================================================================

    public int CurrentTurn => Flow.CurrentTurn.Value;

    public int FlowLength
    {
        get
        {
            var f = typeof(GameFlowManager).GetField("_gameFlow", BindingFlags.NonPublic | BindingFlags.Instance);
            var flow = f?.GetValue(Flow) as GameFlow;
            return flow != null ? flow.GameFlowStack.Count : -1;
        }
    }

    public int MaxDisplayKinds
    {
        get
        {
            if (Assets.ShopLevels == null) return int.MaxValue;
            int baseKinds = Assets.ShopLevels.GetEntry(TomsModel.ShopLevel.Value).maxDisplayKinds;
            return Mathf.Max(1, RelicResolver.ModifyInt(RelicStatId.DisplayKindsAdd, baseKinds));
        }
    }

    /// <summary>
    /// 1銘柄あたりの陳列数の上限。実ゲームでは撤廃済み（ItemSelectionPresenter は在庫数まで並べられる）なので在庫の上限と同じ。
    /// 以前は ShopLevelSettings.maxDisplayStockPerItem（Lv1 で5個）で絞っていて、AutoPlay の収支が実ゲームより小さく出ていた（H-1）。
    /// </summary>
    public int MaxDisplayPerItem => Mathf.Max(1, GameConst.MaxItemStock);

    /// <summary>マシン設置枠（店レベル由来）。</summary>
    public int MachineSlots => Assets.ShopLevels != null ? Assets.ShopLevels.GetEntry(TomsModel.ShopLevel.Value).machineSlots : 0;

    /// <summary>
    /// 純資産 = 所持金 + 在庫の時価 + 未約定の売り注文 + 金融資産の評価額 + 設置済みマシンの撤去返金額。
    /// 戦略ごとの比較（H-1）で、マシン・債券に変えたお金を「消えた」扱いにしないため。
    /// </summary>
    public int NetWorth()
    {
        long total = TomsModel.PlayerMoney.Value;
        total += ItemModel.RuntimeItems.Sum(r => (long)r.Stock.Value * r.CurrentPrice.Value);
        total += SellOrders.PendingTotalEstimate.Value;
        if (Portfolio != null)
        {
            Portfolio.RefreshEstimate(ItemModel, TomsModel.BlacksmithLevel.Value);
            total += Portfolio.TotalAssetsEstimate.Value;
        }
        if (Machines != null)
            total += Machines.Placed.Sum(p => (long)Machines.GetRemoveRefund(p));
        return total >= int.MaxValue ? int.MaxValue : (int)total;
    }

    /// <summary>直近のシーン遷移要求を見てステージを決める（NextTurn・配信精算の後に呼ぶ）。</summary>
    private void ResolveStageAfterFlow()
    {
        var scene = Context.LastSceneRequest;
        Context.LastSceneRequest = null;
        if (scene == "ResultScene")
        {
            Stage = AutoPlayStage.Completed;
            // リザルト集計（ResultPresenter.Entry の写し）。実績のクリア・ランク S・皆済はここで判定される
            new ResultModel(TomsModel, ItemModel, Portfolio, null).BuildStatistics();
            return;
        }
        if (scene == "GameOver") { Stage = AutoPlayStage.Bankrupt; return; }
        if (_streamPending || Flow.IsAwaitingStream.Value) { Stage = AutoPlayStage.StreamDay; return; }
        Stage = AutoPlayStage.ShopDay;
    }

    public AutoPlaySnapshot Snapshot()
    {
        var s = new AutoPlaySnapshot
        {
            RunTurn = CurrentTurn,
            FlowIndex = Flow.CurrentIndex,
            FlowLength = FlowLength,
            Stage = Stage,
            GameMode = Config.Mode.ToString(),
            Money = TomsModel.PlayerMoney.Value,
            PendingSellOrderEstimate = SellOrders.PendingTotalEstimate.Value,
            ShopLevel = TomsModel.ShopLevel.Value,
            MaxDisplayKinds = MaxDisplayKinds,
            MaxDisplayPerItem = MaxDisplayPerItem,
            ShopUpgradeCost = Assets.ShopLevels != null ? Assets.ShopLevels.GetLevelUpCost(TomsModel.ShopLevel.Value) : -1,
            BlacksmithLevel = TomsModel.BlacksmithLevel.Value,
            BlacksmithUpgradeCost = TomsModel.BlacksmithLevel.Value >= GameConst.MaxBlackSmithLevel
                ? -1 : GameConst.GetBlackSmithLevelUpCost(TomsModel.BlacksmithLevel.Value),
            InfoBrokerLevel = TomsModel.InfoBrokerLevel.Value,
        };

        // --- 広告・マシン・金融 ---
        var status = ShopStatus;
        s.Followers = status.Followers.Value;
        s.Trust = status.Trust.Value;
        s.Attention = status.Attention.Value;
        s.Spread = status.Spread.Value;
        s.Retention = status.Retention.Value;
        s.StatMax = status.StatMax;
        foreach (var ad in Assets.Advertisements ?? new List<AdvertisementData>())
        {
            if (ad == null) continue;
            var eff = Marketing.Ads.GetEffectiveGains(ad);
            s.Ads.Add(new AutoPlayAdView
            {
                Name = ad.advertisementName,
                Cost = Marketing.Ads.GetDiscountedCost(ad),
                FollowerGain = eff.Followers,
                TrustGain = eff.Trust,
                AttentionGain = eff.Attention,
                SpreadGain = eff.Spread,
                RetentionGain = eff.Retention,
            });
        }
        s.MachineSlots = MachineSlots;
        s.PlacedMachines = Machines.Placed.Select(p => p.MachineId).ToList();
        foreach (var m in Machines.AllMachines)
        {
            if (m == null) continue;
            var placed = Machines.PlacementOf(m.machineId);
            int lv = placed?.Level ?? 1;
            int cost = Machines.GetPurchaseCost(m);
            s.Machines.Add(new AutoPlayMachineView
            {
                Id = m.machineId,
                Type = m.effectType.ToString(),
                Cost = cost,
                RequiredShopLevel = m.requiredShopLevel,
                DailyMoney = m.effectType == ShopMachineEffectType.DailyMoney ? m.DailyMoneyAt(lv) : 0,
                RevenueBonus = m.effectType == ShopMachineEffectType.RevenueMultiplier ? m.EffectValueAt(lv) : 0f,
                Purchasable = placed == null && Machines.PlacedCount < s.MachineSlots && m.requiredShopLevel <= TomsModel.ShopLevel.Value
                              && TomsModel.PlayerMoney.Value >= cost,
                Placed = placed != null,
                Level = placed?.Level ?? 0,
                MaxLevel = m.MaxLevel,
                EffectValue = m.EffectValueAt(lv),
                NextEffectValue = m.EffectValueAt(Mathf.Min(lv + 1, m.MaxLevel)),
                UpgradeCost = placed != null ? Machines.GetUpgradeCost(placed) : -1,
                Broken = placed != null && placed.Broken,
                Repairable = Machines.CanRepair(placed),
                RepairCost = placed != null ? Machines.GetRepairCost(placed) : 0,
                SafeMorningsLeft = placed != null ? Machines.SafeMorningsLeft(placed) : int.MaxValue,
                NextBreakChance = Machines.NextBreakChance(placed),
            });
        }
        foreach (var p in Portfolio.AllProducts)
        {
            if (p == null || p.kind != FinancialProductKind.Bond) continue;
            s.Bonds.Add(new AutoPlayBondView
            {
                Id = p.productId,
                UnitPrice = p.bondUnitPrice,
                InterestRate = p.bondInterestRate,
                MaturityTurns = p.bondMaturityTurns,
                Unlocked = p.unlockInfoBrokerLevel <= TomsModel.InfoBrokerLevel.Value,
                HeldUnits = Portfolio.GetHeldUnits(p.productId),
                RemainingUnits = Portfolio.GetRemainingBondUnits(p, TomsModel.InfoBrokerLevel.Value),
            });
        }
        s.PortfolioValue = Portfolio.TotalAssetsEstimate.Value;

        int cycle = TomsModel.DebtCycle.Value + 1;
        s.NextDebtAmount = DebtCalculator.GetAmount(cycle, TomsModel, RelicResolver);
        s.TurnsUntilDebt = cycle * GameConst.DebtPaymentInterval - CurrentTurn;

        var hero = HeroModel.heroData;
        if (hero != null)
        {
            s.HeroLevel = hero.level.Value;
            s.HeroHp = hero.hp.Value;
            s.HeroAttack = hero.attackPower.Value;
            s.HeroDefense = hero.defensePower.Value;
            s.HeroWeaponId = hero.weaponId.Value;
            s.HeroArmorId = hero.armorId.Value;
        }

        s.BuzzActive = Marketing.Buzz.IsBuzzActive.Value;
        s.BuzzType = s.BuzzActive ? Marketing.Buzz.CurrentBuzzType.Value.ToString() : "";

        // --- 次の配信 ---
        DungeonName? next = Flow.PendingBattleDungeon ?? Flow.GetNextBattleDungeon();
        s.TurnsUntilStream = Stage == AutoPlayStage.StreamDay ? 0 : (next.HasValue ? Flow.GetTurnsUntilNextBattle() + 1 : -1);
        if (next.HasValue)
        {
            var d = Dungeons.GetById(next.Value);
            s.NextStreamDungeon = next.Value.ToString();
            s.NextStreamDungeonLevel = d?.currentDungeonLevel ?? 1;
            s.NextStreamWeakness = d != null ? d.requiredAttribute.ToString() : "";
            s.NextStreamClearChancePct = d != null
                ? ClearProbabilityCalculator.Calculate(hero, ItemModel, d, d.currentDungeonLevel, RelicResolver) : 0f;
        }

        // --- 銘柄 ---
        int bs = TomsModel.BlacksmithLevel.Value;
        foreach (var r in ItemModel.RuntimeItems)
        {
            var m = ItemModel.GetMasterItem(r.ItemId);
            int price = r.CurrentPrice.Value;
            s.Items.Add(new AutoPlayItemView
            {
                Id = r.ItemId,
                Name = r.ItemName,
                Type = r.ItemType.ToString(),
                Attribute = r.ItemAttribute.ToString(),
                Tier = r.RequiredLevel.Value,
                Unlocked = r.RequiredLevel.Value <= bs,
                Price = price,
                BuyUnitPrice = RelicPricing.GetBuyUnitPrice(price, RelicResolver),
                BasePrice = m != null ? m.basePrice : price,
                PriceChange1d = r.PreviousPrice > 0 ? (price - r.PreviousPrice) / (float)r.PreviousPrice : 0f,
                Demand = r.Demand.Value,
                DemandChange1d = r.PreviousPrice > 0 ? r.Demand.Value - r.PreviousDemand : 0f,
                Heat = MarketHeat.Describe(MarketHeat.Compute(r.ShopPriceHistory)),
                Stock = r.Stock.Value,
                MaxStock = r.MaxStock.Value,
                DisplayStock = r.DisplayStock.Value,
                Displayed = r.IsDisplay.Value,
                SalesRate = r.SalesRate,
                DividendPerTurn = r.DividendPerTurn,
                Customers = ItemModel.GetDailyCustomers(r),
                RecommendScore = ItemModel.RecommendScoreOf(r),
                OracleNewsBias = OracleBias(r),
            });
        }

        // --- ダンジョン（魔王軍支援の画面に出ている情報） ---
        foreach (var d in Dungeons.GetAll())
        {
            bool isMax = d.currentDungeonLevel >= GameConst.MaxDungeonLevel;
            float clearPct = ClearProbabilityCalculator.Calculate(hero, ItemModel, d, d.currentDungeonLevel, RelicResolver);
            s.Dungeons.Add(new AutoPlayDungeonView
            {
                Id = d.key.ToString(),
                Name = d.dungeonName,
                Level = d.currentDungeonLevel,
                MaxLevel = GameConst.MaxDungeonLevel,
                SupportCost = isMax ? 0 : d.levelUpCost,
                // 見込みの段のオッズ込み（SupportOdds。オッズが既定 1 なら rewardGold そのまま。レリックはここでは掛けない＝今と同じ）
                DefeatReward = SupportOdds.Expected(d.rewardGold, () => clearPct),
                ClearChancePct = clearPct,
                IsNextStream = next.HasValue && next.Value == d.key,
            });
        }

        // --- 今日の朝刊: 購読中の社の号だけ。購読0件なら壁新聞（NewspaperPresenter.TodaysEntries の写し） ---
        int turnNow = CurrentTurn;
        var issue = News.IssueOf(turnNow);
        var visible = new List<NewsIssueEntry>();
        var subscribedNow = Subscriptions.ActiveOn(turnNow).Select(c => c.companyId).ToHashSet();
        s.WallPaperOnly = subscribedNow.Count == 0;
        if (!s.WallPaperOnly)
        {
            visible.AddRange(issue.Where(e => subscribedNow.Contains(e.companyId)));
        }
        else
        {
            // 壁新聞: かわら版の一面1本＋埋め草（NewspaperPresenter.WallPaperEntries と同じ規則。I-1）
            visible.AddRange(NewspaperPresenter.WallPaperEntries(issue));
        }
        foreach (var e in visible)
        {
            var a = e.Article;
            if (a == null) continue;
            s.News.Add(new AutoPlayNewsView
            {
                ArticleId = a.id,
                EntryKey = e.Key,
                CanPin = Scrapbook.CanPin(e),
                Company = e.Company != null ? e.Company.companyId : e.companyId,
                Page = a.page,
                SourceClarity = a.sourceClarity,
                Byline = string.IsNullOrEmpty(a.byline) ? "(unsigned)" : "signed",
                Kind = e.kind.ToString(),
                HeadlineJa = a.headline,
                SummaryEn = a.summaryEn,
            });
        }

        // --- 占い師の見立て（買った日だけ。I-11） ---
        if (_fortuneTurn == turnNow)
            foreach (var iv in s.Items)
                iv.FortuneDirection = _fortune.TryGetValue(iv.Id, out var dir) ? dir : 0;

        // --- 読んだ記事を覚える（reader ボット用。プレイヤーが紙面から読み取れる範囲: 効き先の推理＋社の早さから発効日） ---
        if (Stage == AutoPlayStage.ShopDay)
        {
            foreach (var e in visible)
            {
                var ev = e.eventInstance;
                if (ev == null) continue;
                if (e.kind == NewsEntryKind.Correction) { _readEvents.Remove(ev.key); continue; }
                if (e.kind != NewsEntryKind.Report || !ev.HasTarget || ev.trendDelta == 0f) continue;
                int eff = e.effectTurn > 0 ? e.effectTurn : ev.effectTurn;
                if (_readEvents.TryGetValue(ev.key, out var known)) eff = Mathf.Min(eff, known.effectTurn);
                _readEvents[ev.key] = (ev, eff);
            }
            var morning = new Dictionary<string, (int price, float demand, int customers)>();
            foreach (var iv in s.Items)
            {
                var r = ItemModel.GetRuntimeItem(iv.Id);
                if (r == null) continue;
                // 計測は解放済みの品だけ（未解放の品は値が動かないので、効き先の平均を薄めてしまう）
                if (iv.Unlocked) morning[iv.Id] = (iv.Price, iv.Demand, iv.Customers == int.MaxValue ? -1 : iv.Customers);
                float bias = 0f;
                int days = int.MaxValue;
                foreach (var kv in _readEvents.Values)
                {
                    int end = kv.effectTurn + Mathf.Max(1, kv.ev.durationTurns);
                    if (turnNow >= end || !kv.ev.Matches(r)) continue;
                    bias += kv.ev.trendDelta;
                    days = Mathf.Min(days, Mathf.Max(0, kv.effectTurn - turnNow));
                }
                iv.ReadNewsBias = bias;
                iv.ReadNewsDaysToEffect = days == int.MaxValue ? -1 : days;
            }
            MorningMarket[turnNow] = morning;
        }

        // --- 新聞社と購読枠・スクラップ ---
        s.SubscriptionSlots = NewsTuning.SubscriptionSlotsFor(TomsModel.ShopLevel.Value);
        s.SubscriptionUsed = Subscriptions.UsedSlots(turnNow);
        foreach (var c in NewsMasterLoader.LoadCompanies())
        {
            var contract = Subscriptions.ActiveOn(turnNow).FirstOrDefault(x => x.companyId == c.companyId);
            s.Newspapers.Add(new AutoPlayCompanyView
            {
                Id = c.companyId, Name = c.companyName, Price = c.price, ContractTurns = c.contractTurns,
                Trust = c.trustRating, Speed = c.speedRating, Pages = c.pages.Count, Categories = string.Join(",", c.categories),
                Subscribed = contract != null, TurnsLeft = contract != null ? contract.endTurn - turnNow : 0,
                CanSubscribe = Subscriptions.CanSubscribe(c, turnNow, s.SubscriptionSlots, s.Money),
                MeasuredAccuracy = Scrapbook.MeasuredAccuracy(c.companyId),
            });
        }
        s.ScrapCapacity = Scrapbook.Capacity;
        foreach (var sc in Scrapbook.Pinned)
            s.Scraps.Add(new AutoPlayScrapView
            {
                EntryKey = sc.entryKey, Company = sc.companyId, PublishTurn = sc.publishTurn,
                State = ScrapbookModel.StateOf(sc, News.FindEntry(sc.entryKey), turnNow).ToString(),
            });

        // --- レリック3択 ---
        foreach (var c in RelicRewards.PendingChoices)
        {
            s.PendingRelicChoices.Add(new AutoPlayRelicChoiceView
            {
                RelicId = c.relicId,
                Name = c.relicName,
                Rarity = c.rarity.ToString(),
                Description = c.description,
            });
        }
        s.RelicDeclineGold = RelicRewards.GetDeclineGold();

        if (Stage == AutoPlayStage.StreamDay) s.Stream = BuildStreamInfo(s.Money);

        return s;
    }

    /// <summary>この先3日の新聞の本当の効果（オラクル）。</summary>
    private float OracleBias(RuntimeItemData r)
    {
        if (NewsEffects == null) return 0f;
        float sum = 0f;
        for (int t = CurrentTurn + 1; t <= CurrentTurn + 3; t++)
            sum += NewsEffects.TrendBias(r, t) + NewsEffects.DemandKick(r, t);
        return sum;
    }

    // =================================================================
    // 営業日の頭（TomsShopPresenter.Entry の写し: 借金の強制返済 → 保留イベント）
    // =================================================================

    /// <summary>営業日の頭の処理。破産したら Stage=Bankrupt になる。1ターンに1回だけ実行される。</summary>
    public void BeginShopDay()
    {
        if (Stage != AutoPlayStage.ShopDay) return;
        int turn = CurrentTurn;
        if (_lastDayBeginTurn == turn) return;
        _lastDayBeginTurn = turn;
        Today = new DayLedger();

        // 朝レポートは表示するだけの消費型。ログとして控える
        if (_morning.HasLines) Today.Notes.AddRange(_morning.Consume());

        // 続報が出たスクラップは無料で名鑑へ（NewspaperPresenter.Entry と同じ。I-3）
        Scrapbook.DeliverFollowUps(News, turn);

        // 1周目の立て替え（TutorialPresenter.GrantFundsAdvanceOnce の写し。2日目の朝に一度だけ）
        if (Config.TutorialAdvance > 0 && !_advanceGranted && turn >= 2)
        {
            _advanceGranted = true;
            TomsModel.GrantFundsAdvance(Config.TutorialAdvance);
            Today.Notes.Add($"鍛冶屋の立て替え +{Config.TutorialAdvance}G（初回の納付に上乗せ）");
        }

        // 借金の強制返済（DebtPresenter.ShowForced → OnPay / OnBankruptcy の写し）
        if (turn / GameConst.DebtPaymentInterval > TomsModel.DebtCycle.Value)
        {
            int cycle = TomsModel.DebtCycle.Value + 1;
            int amount = DebtCalculator.GetAmount(cycle, TomsModel, RelicResolver);
            int money = TomsModel.PlayerMoney.Value;
            // 今日入金予定の売り注文を前倒しで受け取る（TomsShopPresenter → GameFlowManager.SettleTodaySellOrdersForDebt の写し）
            if (money < amount)
            {
                int early = Flow.SettleTodaySellOrdersForDebt();
                if (early > 0)
                {
                    _morning.Consume(); // 朝レポートに積まれた行は Notes に直接書く
                    Today.Notes.Add($"返済のため本日分の売り注文を前倒しで入金: +{early}G");
                }
                money = TomsModel.PlayerMoney.Value;
            }
            if (money < amount && Portfolio != null)
            {
                int shortfall = amount - money;
                int liquidatable = Portfolio.GetForcedLiquidationValue(ItemModel, TomsModel.BlacksmithLevel.Value);
                if (liquidatable > 0 && money + liquidatable + DebtCalculator.GetFireSaleValue(ItemModel) >= amount)
                {
                    Portfolio.LiquidateForDebt(shortfall, TomsModel, ItemModel, TomsModel.BlacksmithLevel.Value);
                    money = TomsModel.PlayerMoney.Value;
                    Today.Notes.Add("金融資産を強制売却して返済");
                }
            }

            // 在庫の投げ売り（DebtPresenter.ShowForced の写し。H-8）
            if (money < amount)
            {
                int fireSale = DebtCalculator.GetFireSaleValue(ItemModel);
                if (fireSale > 0 && money + fireSale >= amount)
                {
                    var lines = new List<string>();
                    int raised = DebtCalculator.LiquidateInventoryForDebt(amount - money, TomsModel, ItemModel, lines);
                    money = TomsModel.PlayerMoney.Value;
                    Today.FireSale += raised;
                    Today.Notes.Add($"在庫を投げ売りして返済: +{raised}G（{string.Join("・", lines)}）");
                }
            }

            if (money < amount)
            {
                Today.Notes.Add($"破産: 返済 {amount}G に対し所持金 {money}G");
                TomsModel.SavePlayerMoney();
                _sceneTransition.GoToGameOver();
                ResolveStageAfterFlow();
                if (Stage != AutoPlayStage.Bankrupt) Stage = AutoPlayStage.Bankrupt;
                return;
            }

            TomsModel.PurchaseItem(amount);
            TomsModel.DebtCycle.Value = cycle;
            DebtCalculator.OnPaid(TomsModel);
            TomsModel.SavePlayerMoney();
            RelicRewards.QueueReward($"返済報酬(サイクル{cycle})");
            Today.DebtPaid = amount;
        }

        // 保留イベントはポップアップの「確認」で実行される（選択肢が無いので自動で確認する）
        if (_pendingEvent.HasPendingEvent) ConfirmPendingEvent();
    }

    // =================================================================
    // IPlayerActions
    // =================================================================

    public AutoPlayActionResult Buy(string itemId, int quantity)
    {
        if (Stage == AutoPlayStage.Completed || Stage == AutoPlayStage.Bankrupt) return Reject("ラン終了後は買えない");
        var item = ItemModel.GetRuntimeItem(itemId);
        if (item == null) return Reject($"不明な銘柄 {itemId}");
        if (item.RequiredLevel.Value > TomsModel.BlacksmithLevel.Value) return Reject($"{itemId} は鍛冶屋レベル不足");
        int unit = RelicPricing.GetBuyUnitPrice(item.CurrentPrice.Value, RelicResolver);
        int qty = Mathf.Min(quantity, item.RemainToMax(), TomsModel.PlayerMoney.Value / Mathf.Max(1, unit));
        if (qty <= 0) return Reject($"{itemId} を買えない（在庫上限 or 資金不足）");

        // BlackSmithPresenter.HandlePurchase の写し
        int total = unit * qty;
        ItemModel.PurchaseItem(itemId, qty);
        TomsModel.PurchaseItem(total);
        TomsModel.RecordProcurementSpend(total);
        ItemModel.SaveData();
        TomsModel.SavePlayerMoney();
        Today.Spend += total;
        return AutoPlayActionResult.Success($"{itemId} x{qty} = {total}G");
    }

    public AutoPlayActionResult AutoBuy(int budget, AutoBuyStrategy strategy)
    {
        if (budget <= 0) return AutoPlayActionResult.Success("予算0");
        var results = ItemModel.AutoPurchase(budget, TomsModel.BlacksmithLevel.Value, TomsModel, null, RelicResolver, strategy);
        int spent = results.Sum(r => r.TotalCost);
        Today.Spend += spent;
        return AutoPlayActionResult.Success($"おまかせ {results.Count}銘柄 {spent}G");
    }

    public AutoPlayActionResult SupportDungeon(DungeonName dungeon)
    {
        // DungeonLevelUpPresenter.HandleLevelUp の写し
        var data = Dungeons.GetById(dungeon);
        if (data == null) return Reject($"不明なダンジョン {dungeon}");
        if (data.currentDungeonLevel >= GameConst.MaxDungeonLevel) return Reject($"{dungeon} は最大レベル");
        int cost = data.levelUpCost;
        if (TomsModel.PlayerMoney.Value < cost) return Reject($"{dungeon} の支援費用 {cost}G が足りない");
        TomsModel.PurchaseItem(cost);
        TomsModel.SavePlayerMoney();
        data.currentDungeonLevel++;
        Dungeons.Save();
        Today.SupportSpend += cost;
        return AutoPlayActionResult.Success($"{dungeon} Lv{data.currentDungeonLevel} ({cost}G)");
    }

    public AutoPlayActionResult UpgradeBlacksmith()
    {
        int before = TomsModel.PlayerMoney.Value;
        if (!TomsModel.UpgradeBlacksmith()) return Reject("鍛冶屋レベルアップ不可");
        Today.UpgradeSpend += before - TomsModel.PlayerMoney.Value;
        return AutoPlayActionResult.Success($"鍛冶屋 Lv{TomsModel.BlacksmithLevel.Value}");
    }

    /// <summary>HeroPanelPresenter.SetWeapon / SetArmor / SaveEquipment の写し（所持・在庫は問われない＝本体と同じ）。</summary>
    public AutoPlayActionResult EquipHero(string weaponId, string armorId)
    {
        var hero = HeroModel.heroData;
        if (hero == null) return Reject("勇者データなし");
        bool changed = false;
        if (weaponId != null)
        {
            var w = string.IsNullOrEmpty(weaponId) ? null : ItemModel.GetRuntimeItem(weaponId);
            if (weaponId != "" && (w == null || w.ItemType != ItemTypeData.ItemType.Weapon)) return Reject($"{weaponId} は武器ではない");
            hero.weaponId.Value = weaponId;
            hero.weaponName.Value = w != null ? w.ItemName : string.Empty;
            changed = true;
        }
        if (armorId != null)
        {
            var a = string.IsNullOrEmpty(armorId) ? null : ItemModel.GetRuntimeItem(armorId);
            if (armorId != "" && (a == null || a.ItemType != ItemTypeData.ItemType.Armor)) return Reject($"{armorId} は防具ではない");
            hero.armorId.Value = armorId;
            hero.armorName.Value = a != null ? a.ItemName : string.Empty;
            changed = true;
        }
        if (!changed) return AutoPlayActionResult.Success();

        HeroModel.ClearEquippedItems();
        if (!string.IsNullOrEmpty(hero.weaponId.Value)) HeroModel.EquipItem(hero.weaponId.Value);
        if (!string.IsNullOrEmpty(hero.armorId.Value)) HeroModel.EquipItem(hero.armorId.Value);
        _battleIn.EquippedItemIds = new List<string>(HeroModel.EquippedItemIds);
        HeroModel.SaveHeroData();
        return AutoPlayActionResult.Success($"装備 {hero.weaponId.Value}/{hero.armorId.Value}");
    }

    // ---------------- 新聞（NewspaperPresenter.Subscribe / ConfirmOne / ConfirmBatch の写し） ----------------

    /// <summary>連載の分岐がどの条件で決まったか（ヘッドレスでも heroWin/heroLose/hasStock/noStock が評価されているかの確認用）。</summary>
    public string ArcSummary()
    {
        if (News == null) return "";
        var counts = new Dictionary<string, int>();
        int resolved = 0;
        foreach (var slot in News.ArcSlots)
        {
            if (!slot.IsResolved) continue;
            resolved++;
            string cond = slot.resolvedEventId == "-" ? "end"
                : slot.options.Where(o => o.eventId == slot.resolvedEventId).Select(o => o.cond).FirstOrDefault() ?? "?";
            counts[cond] = (counts.TryGetValue(cond, out var n) ? n : 0) + 1;
        }
        return $"arcs={News.ArcSlots.Count} resolved={resolved} " + string.Join(" ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"))
               + $" lastStream={(News.LastStreamWon.HasValue ? (News.LastStreamWon.Value ? "win" : "lose") : "none")}";
    }

    public AutoPlayActionResult SubscribeNewspaper(string companyId)
    {
        if (!Config.AllowNewspaper) return Reject("新聞なしの対照群");
        var company = NewsMasterLoader.FindCompany(companyId);
        int turn = CurrentTurn;
        int slots = NewsTuning.SubscriptionSlotsFor(TomsModel.ShopLevel.Value);
        if (!Subscriptions.CanSubscribe(company, turn, slots, TomsModel.PlayerMoney.Value)) return Reject($"{companyId} を購読できない（枠・所持金・契約中）");
        TomsModel.PurchaseItem(company.price);
        Subscriptions.Subscribe(company, turn);
        TomsModel.SavePlayerMoney();
        Today.NewspaperSpend += company.price;
        return AutoPlayActionResult.Success($"購読 {companyId}");
    }

    /// <summary>占い師に占ってもらう（ProphetPresenter と同じ。1日1回 ProphetFortune.CurrentCost）。</summary>
    public AutoPlayActionResult BuyFortune()
    {
        if (_fortuneTurn == CurrentTurn) return AutoPlayActionResult.Success("今日は占い済み");
        if (TomsModel.PlayerMoney.Value < ProphetFortune.CurrentCost) return Reject("占いの代金が足りない");
        TomsModel.PurchaseItem(ProphetFortune.CurrentCost);
        FortuneSpend += ProphetFortune.CurrentCost;
        _fortuneTurn = CurrentTurn;
        _fortune.Clear();
        var (ups, downs) = ProphetFortune.Read(ItemModel.RuntimeItems, NewsEffects, CurrentTurn, TomsModel.FlowSeed, TomsModel.BlacksmithLevel.Value);
        foreach (var r in ups) _fortune[r.item.ItemId] = 1;
        foreach (var r in downs) _fortune[r.item.ItemId] = -1;
        return AutoPlayActionResult.Success($"占い 上{ups.Count} 下{downs.Count}");
    }

    public AutoPlayActionResult PinScrap(string entryKey)
    {
        var entry = News.FindEntry(entryKey);
        if (entry == null || entry.publishTurn != CurrentTurn) return Reject($"{entryKey} は今日の紙面に無い");
        if (!Subscriptions.IsSubscribed(entry.companyId, CurrentTurn) && !NewspaperPresenter.WallPaperEntries(News.IssueOf(CurrentTurn)).Contains(entry))
            return Reject("購読していない社の記事");
        if (!Scrapbook.Pin(entry, CurrentTurn)) return Reject("スクラップできない（枠が一杯・貼れない種類）");
        return AutoPlayActionResult.Success($"スクラップ {entryKey}");
    }

    public AutoPlayActionResult ConfirmScrap(string entryKey)
    {
        if (TomsModel.PlayerMoney.Value < NewsTuning.ConfirmCost) return Reject("確認の費用が足りない");
        var entry = News.FindEntry(entryKey);
        if (entry == null || !Scrapbook.Confirm(entryKey, entry, CurrentTurn)) return Reject("確認できない（未決着・未スクラップ）");
        TomsModel.PurchaseItem(NewsTuning.ConfirmCost);
        TomsModel.SavePlayerMoney();
        Today.NewspaperSpend += NewsTuning.ConfirmCost;
        return AutoPlayActionResult.Success($"確認 {entryKey}");
    }

    public AutoPlayActionResult ConfirmScrapBatch()
    {
        if (TomsModel.PlayerMoney.Value < NewsTuning.ConfirmBatchCost) return Reject("まとめて確認の費用が足りない");
        int turn = CurrentTurn;
        var targets = new List<(string key, NewsIssueEntry entry)>();
        foreach (var sc in Scrapbook.Pinned)
        {
            var entry = News.FindEntry(sc.entryKey);
            if (ScrapbookModel.StateOf(sc, entry, turn) == ScrapState.Unconfirmed) targets.Add((sc.entryKey, entry));
            if (targets.Count >= NewsTuning.ConfirmBatchCount) break;
        }
        if (targets.Count < NewsTuning.ConfirmBatchCount) return Reject("決着・未確認が3件そろっていない");
        foreach (var (key, entry) in targets) Scrapbook.Confirm(key, entry, turn);
        TomsModel.PurchaseItem(NewsTuning.ConfirmBatchCost);
        TomsModel.SavePlayerMoney();
        Today.NewspaperSpend += NewsTuning.ConfirmBatchCost;
        return AutoPlayActionResult.Success("まとめて確認");
    }

    public AutoPlayActionResult UpgradeShop()
    {
        int before = TomsModel.PlayerMoney.Value;
        if (!TomsModel.UpgradeShop(Assets.ShopLevels)) return Reject("店レベルアップ不可");
        Today.UpgradeSpend += before - TomsModel.PlayerMoney.Value;
        return AutoPlayActionResult.Success($"店 Lv{TomsModel.ShopLevel.Value}");
    }

    // ---------------- 広告・マシン・金融（H-1） ----------------

    public AutoPlayActionResult BuyAdvertisement(string advertisementName)
    {
        var ad = (Assets.Advertisements ?? new List<AdvertisementData>()).FirstOrDefault(a => a != null && a.advertisementName == advertisementName);
        if (ad == null) return Reject($"不明な広告 {advertisementName}");
        int before = TomsModel.PlayerMoney.Value;
        if (!Marketing.ExecuteAdvertisement(ad)) return Reject($"{advertisementName} を打てない（資金不足）");
        Today.AdSpend += before - TomsModel.PlayerMoney.Value;
        return AutoPlayActionResult.Success($"広告 {advertisementName}");
    }

    public AutoPlayActionResult PlaceMachine(string machineId)
    {
        var m = Machines.GetMachine(machineId);
        if (m == null) return Reject($"不明なマシン {machineId}");
        int before = TomsModel.PlayerMoney.Value;
        if (Machines.TryPurchaseAndPlace(m, TomsModel, MachineSlots) == null) return Reject($"{machineId} を置けない（枠・店レベル・資金）");
        Today.MachineSpend += before - TomsModel.PlayerMoney.Value;
        return AutoPlayActionResult.Success($"マシン {machineId}");
    }

    public AutoPlayActionResult UpgradeMachine(string machineId)
    {
        var placed = Machines.PlacementOf(machineId);
        if (placed == null) return Reject($"{machineId} を置いていない");
        int before = TomsModel.PlayerMoney.Value;
        if (!Machines.TryUpgrade(placed.PlacementId, TomsModel)) return Reject($"{machineId} を強化できない（最大レベル・資金）");
        Today.MachineSpend += before - TomsModel.PlayerMoney.Value;
        return AutoPlayActionResult.Success($"マシン強化 {machineId} Lv{placed.Level}");
    }

    public AutoPlayActionResult RepairMachine(string machineId)
    {
        var placed = Machines.PlacementOf(machineId);
        if (placed == null) return Reject($"{machineId} を置いていない");
        int before = TomsModel.PlayerMoney.Value;
        if (!Machines.TryRepair(placed.PlacementId, TomsModel)) return Reject($"{machineId} を修理できない（壊れていない・資金）");
        Today.MachineSpend += before - TomsModel.PlayerMoney.Value;
        return AutoPlayActionResult.Success($"マシン修理 {machineId}");
    }

    public AutoPlayActionResult BuyBond(string productId, int units)
    {
        var p = Portfolio.GetProduct(productId);
        if (p == null || p.kind != FinancialProductKind.Bond) return Reject($"不明な債券 {productId}");
        if (p.unlockInfoBrokerLevel > TomsModel.InfoBrokerLevel.Value) return Reject($"{productId} は情報屋レベル不足");
        int qty = Mathf.Min(units, TomsModel.PlayerMoney.Value / Mathf.Max(1, p.bondUnitPrice),
            Portfolio.GetRemainingBondUnits(p, TomsModel.InfoBrokerLevel.Value));
        if (qty <= 0) return Reject($"{productId} を買えない（資金・口数の上限）");
        if (!Portfolio.BuyBond(p, qty, TomsModel, CurrentTurn)) return Reject($"{productId} の購入に失敗");
        Today.FinanceSpend += qty * p.bondUnitPrice;
        return AutoPlayActionResult.Success($"債券 {productId} x{qty}");
    }

    public AutoPlayActionResult ClearDisplay()
    {
        foreach (var r in ItemModel.RuntimeItems)
        {
            r.UpdateIsDisplay(false);
            r.UpdateDisplayStock(0);
        }
        ItemModel.SaveData();
        return AutoPlayActionResult.Success();
    }

    public AutoPlayActionResult SetDisplay(string itemId, int quantity)
    {
        var item = ItemModel.GetRuntimeItem(itemId);
        if (item == null) return Reject($"不明な銘柄 {itemId}");
        if (quantity <= 0)
        {
            item.UpdateIsDisplay(false);
            item.UpdateDisplayStock(0);
            ItemModel.SaveData();
            return AutoPlayActionResult.Success();
        }
        if (item.Stock.Value <= 0) return Reject($"{itemId} は在庫なし");
        // ItemSelectionPresenter の陳列枠ガードの写し
        if (!item.IsDisplay.Value && !ItemModel.CanDisplayMore(MaxDisplayKinds)) return Reject("陳列枠が一杯");
        int qty = Mathf.Clamp(quantity, 1, Mathf.Min(item.Stock.Value, MaxDisplayPerItem));
        item.UpdateIsDisplay(true);
        item.UpdateDisplayStock(qty);
        ItemModel.SaveData();
        return AutoPlayActionResult.Success($"{itemId} 陳列 {qty}");
    }

    public AutoPlayActionResult ConfirmPendingEvent()
    {
        if (!_pendingEvent.HasPendingEvent) return Reject("保留イベントなし");
        var ev = _pendingEvent.PendingEvent;
        int before = TomsModel.PlayerMoney.Value;
        _eventExecutor.Execute(ev);
        _pendingEvent.Clear();
        Today.Notes.Add($"イベント {ev.id} {ev.title}（所持金 {TomsModel.PlayerMoney.Value - before:+#;-#;0}G）");
        return AutoPlayActionResult.Success(ev.id);
    }

    public AutoPlayActionResult ChooseRelic(int index)
    {
        if (RelicRewards.PendingChoices.Count == 0) return Reject("レリック3択なし");
        string name = index >= 0 && index < RelicRewards.PendingChoices.Count ? RelicRewards.PendingChoices[index].relicId : "?";
        if (!RelicRewards.ChoosePending(index, CurrentTurn)) return Reject($"レリック選択失敗 index={index}");
        RelicInventory.SaveData();
        return AutoPlayActionResult.Success(name);
    }

    public AutoPlayActionResult DeclineRelic()
    {
        int gold = RelicRewards.DeclineForGold();
        if (gold > 0)
        {
            TomsModel.AddRevenue(gold);
            TomsModel.SavePlayerMoney();
        }
        return AutoPlayActionResult.Success($"辞退 +{gold}G");
    }

    /// <summary>TurnEndSummaryPresenter.Entry の写し（View 呼び出しと評価計算を除く）。</summary>
    public AutoPlayActionResult StartSales()
    {
        if (Stage != AutoPlayStage.ShopDay) return Reject("営業日ではない");
        int currentTurn = CurrentTurn;
        if (_lastSalesTurn == currentTurn) return Reject("本日は営業済み");
        _lastSalesTurn = currentTurn;

        var economy = Assets.Economy;
        bool probabilistic = economy != null && economy.useProbabilisticShopSales;
        int delayTurns = economy != null ? economy.sellOrderDelayTurns : 1;

        var settlement = SellOrders.SettleDue(currentTurn, ItemModel, economy, Marketing, RelicResolver);
        if (settlement.TotalIncome > 0) TomsModel.AddRevenue(settlement.TotalIncome);
        Today.ShopIncome += settlement.TotalIncome;

        var salesResult = ItemModel.SimulateShopSales(probabilistic);
        foreach (var kv in salesResult)
        {
            var runtime = ItemModel.GetRuntimeItem(kv.Key);
            if (runtime != null && kv.Value > 0) SellOrders.Place(runtime, kv.Value, currentTurn, delayTurns);
        }

        ItemModel.SaveData();
        SellOrders.SaveData();
        TomsModel.SavePlayerMoney();
        return AutoPlayActionResult.Success($"約定入金 {settlement.TotalIncome}G / 売り注文 {salesResult.Count}件");
    }

    public AutoPlayActionResult EndDay()
    {
        if (Stage != AutoPlayStage.ShopDay) return Reject("営業日ではない");
        if (_lastSalesTurn != CurrentTurn) return Reject("営業サマリーを経ていない（StartSales が先）");
        Flow.NextTurn();
        ResolveStageAfterFlow();
        return AutoPlayActionResult.Success($"→ turn {CurrentTurn} / {Stage}");
    }

    public AutoPlayActionResult StartStream(IReadOnlyList<AutoPlayStreamItem> items, IReadOnlyList<AutoPlayInterventionOrder> interventions = null)
    {
        if (Stage != AutoPlayStage.StreamDay) return Reject("配信日ではない");
        Today = new DayLedger();

        // 品出し（StreamingSettingPresenter の確定結果相当）
        var selected = new List<BattleInputItem>();
        if (items != null)
        {
            foreach (var it in items.Take(Mathf.Max(1, Config.StreamMaxKinds)))
            {
                var r = ItemModel.GetRuntimeItem(it.ItemId);
                if (r == null || it.Quantity <= 0 || r.Stock.Value <= 0) continue;
                int qty = Mathf.Min(it.Quantity, r.Stock.Value);
                selected.Add(new BattleInputItem { ItemId = r.ItemId, Quantity = qty, Price = r.CurrentPrice.Value });
            }
        }

        // 交互実行で他ランが上書きしている可能性があるので、配信直前に取り直す
        RelicBattleEffects.SetFrom(RelicResolver);

        _streamPending = false;
        int moneyAtStart = TomsModel.PlayerMoney.Value;
        Flow.ProceedToBattle(); // セーブ → GoToBattle（シーンロードは横取りされる）
        Context.LastSceneRequest = null;
        _battleIn.SelectedItems = selected;

        var dungeon = Dungeons.GetById(_battleIn.DungeonKey);
        int level = _battleIn.DungeonLevel;
        var outcome = AutoPlayBattleSurrogate.Simulate(BuildStreamInput(dungeon, level, selected,
            Config.EnableInterventions ? interventions : null, moneyAtStart));
        LastBattle = outcome;
        int rawSales = outcome.RawSales;

        // BattleSceneStarter.CalculateDefeatReward と同じ式（SupportOdds.Payout: レリック × 配信開始時の見込みの段のオッズ）
        int defeatReward = 0;
        if (!outcome.Victory)
        {
            int reward = dungeon?.GetDefenseReward(level) ?? 0;
            defeatReward = SupportOdds.Payout(reward, RelicBattleEffects.DefeatRewardMul, _battleIn.PreStreamClearPct).Total;
        }

        // 勇者敗北の返金騒ぎ（BattleSceneStarter の写し）: 負けたら武器・防具の売上の一部を返す
        var battleResult = outcome.Victory ? BattleResult.Victory : BattleResult.Defeat;
        float refundRate = Assets.BattlePrice != null ? Assets.BattlePrice.heroDefeatRefundRate : 0f;
        int heroDefeatRefund = HeroDefeatRefund.Calculate(battleResult, outcome.Sold, HeroDefeatRefund.TypeLookup(ItemModel), refundRate);

        // 純利益 = 売上 − 補充(なし) + 返金(なし) + 防衛報酬 − 介入支出 + 未実行介入の返金 − 返金騒ぎ（BattleSceneStarter の写し）
        int totalEarnings = rawSales + defeatReward - outcome.InterventionSpent + outcome.InterventionRefund - heroDefeatRefund;

        string weaponId = _battleIn.EquippedItemIds.Count > 0 ? _battleIn.EquippedItemIds[0] : "";
        string armorId = _battleIn.EquippedItemIds.Count > 1 ? _battleIn.EquippedItemIds[1] : "";
        _battleOut.SetResult(battleResult, weaponId, armorId, outcome.Sold, totalEarnings,
            outcome.MobsDefeated, outcome.BossesDefeated);
        _battleOut.SetStreamingStats(outcome.InterventionSpent, outcome.InterventionRefund, 0, outcome.ViewerSpecials, outcome.SpecialMoves);
        _battleOut.SetHeroDefeatRefund(heroDefeatRefund);

        Today.StreamEarnings = rawSales - heroDefeatRefund; // 売上から返金騒ぎを引いた額（レポートの配信収入）
        Today.DefeatReward = defeatReward;
        Today.InterventionSpent = outcome.InterventionSpent;
        Today.InterventionRefund = outcome.InterventionRefund;
        Today.BattleResult = outcome.Victory ? "HeroWin" : "HeroLose";
        Today.BattleDungeon = $"{_battleIn.DungeonKey} Lv{level}";
        Today.BattleClearPct = outcome.DisplayedClearPct;

        // 精算と日送りは本物のハンドラで（TomsShop 帰還時の BattleResultHandler.Start と同じ）
        var handler = new BattleResultHandler(_battleOut, _battleIn, ItemModel, _stateManager, Flow,
            Assets.Economy, TomsModel, HeroModel, RelicRewards, null, null, RelicInventory);
        try
        {
            handler.Start();
        }
        finally
        {
            handler.Dispose();
        }
        // 新聞の連載（heroWin / heroLose）は BattleResultHandler が RunHistory に書いた勝敗から次の朝に読まれる。
        // 念のため直接も伝えておく（新聞担当の申し送り。RunHistory を通らない経路でも分岐できるように）
        News?.SetLastStreamResult(outcome.Victory);

        ResolveStageAfterFlow();
        return AutoPlayActionResult.Success($"{Today.BattleResult} 売上 {rawSales}G 防衛報酬 {defeatReward}G"
                                            + (heroDefeatRefund > 0 ? $" 返金騒ぎ -{heroDefeatRefund}G" : ""));
    }

    // =================================================================
    // 配信中の介入（Docs/Jev_AutoPlay_Design.md §2.5）
    // =================================================================

    private float ViewerRedChance
    {
        get
        {
            var s = Assets.Interaction;
            return Config.ViewerSuperChat switch
            {
                AutoPlayViewerSuperChatMode.Off => 0f,
                AutoPlayViewerSuperChatMode.On => Config.ViewerRedChancePerTurn,
                _ => s != null && s.viewerSuperChatEnabled && s.viewerRedTriggersSpecial ? Config.ViewerRedChancePerTurn : 0f,
            };
        }
    }

    private AutoPlayBattleSurrogate.StreamInput BuildStreamInput(DungeonData dungeon, int level, List<BattleInputItem> selected,
        IReadOnlyList<AutoPlayInterventionOrder> orders, int startMoney)
    {
        return new AutoPlayBattleSurrogate.StreamInput
        {
            Hero = HeroModel.heroData,
            ItemModel = ItemModel,
            Dungeon = dungeon,
            Level = level,
            HeroPowerMul = RelicBattleEffects.HeroPowerMul,
            Probabilistic = Config.ProbabilisticBattle,
            Rng = _rng,
            Selected = selected,
            SalesTicksPerTurn = Mathf.Max(0f, Config.StreamSalesScale),
            Settings = Assets.Interaction,
            Relic = RelicResolver,
            Orders = orders,
            StartMoney = startMoney,
            SecondsPerTurn = Config.SecondsPerBattleTurn,
            ViewerRedChancePerTurn = ViewerRedChance,
        };
    }

    /// <summary>配信日の介入メニュー（＋貪欲ボット用のオラクル what-if）。</summary>
    private AutoPlayStreamInfo BuildStreamInfo(int money)
    {
        var key = Flow.PendingBattleDungeon;
        if (!key.HasValue) return null;
        var dungeon = Dungeons.GetById(key.Value);
        if (dungeon == null) return null;
        int level = dungeon.currentDungeonLevel;
        var hero = HeroModel.heroData;
        var s = Assets.Interaction;

        float powerMul = Mathf.Max(0.1f, RelicResolver.Modify(RelicStatId.HeroPowerMul, 1f));
        float rewardMul = Mathf.Max(0f, RelicResolver.Modify(RelicStatId.DefeatRewardMul, 1f));
        var equip = HeroEquipmentBonus.Get(hero, ItemModel);
        int Scale(int v) => Mathf.Max(1, Mathf.RoundToInt(v * powerMul));
        var (count, hasBoss) = AutoPlayBattleSurrogate.DescribeEnemies(dungeon, level);

        float displayedClearPct = ClearProbabilityCalculator.Calculate(hero, ItemModel, dungeon, level, RelicResolver);
        var info = new AutoPlayStreamInfo
        {
            Dungeon = key.Value.ToString(),
            Level = level,
            DisplayedClearPct = displayedClearPct,
            HasBoss = hasBoss,
            EnemyCount = count,
            // 実際の支払いと同じ式（レリック × 見込みの段のオッズ。オッズが既定 1 なら今と同じ）
            DefeatReward = SupportOdds.Payout(dungeon.GetDefenseReward(level), rewardMul, displayedClearPct).Total,
            HeroMaxHp = hero != null ? Scale(Mathf.RoundToInt(hero.hp.Value * equip.Hp)) : 0,
            HeroAttack = hero != null ? Scale(Mathf.RoundToInt(hero.attackPower.Value * equip.Attack)) : 0,
            HeroDefense = hero != null ? Scale(Mathf.RoundToInt(hero.defensePower.Value * equip.Defense)) : 0,
            InterventionsEnabled = Config.EnableInterventions && s != null,
            CooldownTurns = s != null ? Mathf.CeilToInt(s.cooldownSeconds / Mathf.Max(0.1f, Config.SecondsPerBattleTurn)) : 0,
        };

        // オラクル: 介入なし／各介入を中盤に1回だけ使った場合（販売・視聴者赤スパなし、乱数は固定）
        AutoPlayBattleSurrogate.Outcome WhatIf(AutoPlayInterventionOrder order)
        {
            var input = BuildStreamInput(dungeon, level, null, order != null ? new[] { order } : null, money);
            input.Probabilistic = false;
            input.ViewerRedChancePerTurn = 0f;
            input.HeroPowerMul = powerMul;
            input.Rng = new System.Random(Config.Seed);
            return AutoPlayBattleSurrogate.Simulate(input);
        }

        var baseline = WhatIf(null);
        info.BaselineHeroWins = baseline.Victory;
        info.BaselineTurns = baseline.Turns;

        if (info.InterventionsEnabled)
        {
            foreach (AutoPlayInterventionKind k in Enum.GetValues(typeof(AutoPlayInterventionKind)))
            {
                if (k == AutoPlayInterventionKind.BossBuff && !hasBoss) continue;
                int price = AutoPlayInterventionKinds.Price(k, s);
                var w = WhatIf(new AutoPlayInterventionOrder(k, 0.5f));
                info.Options.Add(new AutoPlayInterventionOption
                {
                    Kind = k,
                    Price = price,
                    Affordable = price <= money,
                    DescriptionEn = AutoPlayInterventionKinds.DescribeEn(k, s),
                    WhatIfHeroWins = w.Victory,
                    WhatIfTurns = w.Turns,
                });
            }
        }
        return info;
    }

    private AutoPlayActionResult Reject(string message)
    {
        Today.RejectedActions++;
        return AutoPlayActionResult.Fail(message);
    }
}

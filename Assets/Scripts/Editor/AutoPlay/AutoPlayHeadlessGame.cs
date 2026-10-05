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
            a.Machines = RemoteBalance.ApplyList("shopMachines", AddressableLoader.LoadAll<ShopMachineData>("ShopMachineData"), m => m.machineId);
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
            a.Dungeons = RemoteBalance.ApplyList("dungeons", a.Dungeons, d => d.key.ToString());
            a.Interaction = StreamingInteractionSettings.Load();
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
    public readonly List<(LogType type, string message)> CapturedLogs = new List<(LogType, string)>();

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
        public int InterventionRefund;
        public string BattleDungeon = "";
        public float BattleClearPct;
        public int RejectedActions;
        public readonly List<string> Notes = new List<string>();
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
    private GameObject _dungeonHost;
    private System.Random _rng;

    private bool _streamPending;
    private int _lastSalesTurn = -1;
    private int _lastDayBeginTurn = -1;

    public AutoPlayStage Stage { get; private set; } = AutoPlayStage.ShopDay;
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
        var newsEffects = new NewsEffectResolver(News);
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

        Stage = AutoPlayStage.ShopDay;
    }

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

    public int MaxDisplayPerItem =>
        Assets.ShopLevels != null ? Mathf.Max(1, Assets.ShopLevels.GetEntry(TomsModel.ShopLevel.Value).maxDisplayStockPerItem) : 99;

    /// <summary>直近のシーン遷移要求を見てステージを決める（NextTurn・配信精算の後に呼ぶ）。</summary>
    private void ResolveStageAfterFlow()
    {
        var scene = Context.LastSceneRequest;
        Context.LastSceneRequest = null;
        if (scene == "ResultScene") { Stage = AutoPlayStage.Completed; return; }
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
        };

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
            });
        }

        // --- ダンジョン（魔王軍支援の画面に出ている情報） ---
        foreach (var d in Dungeons.GetAll())
        {
            bool isMax = d.currentDungeonLevel >= GameConst.MaxDungeonLevel;
            s.Dungeons.Add(new AutoPlayDungeonView
            {
                Id = d.key.ToString(),
                Name = d.dungeonName,
                Level = d.currentDungeonLevel,
                MaxLevel = GameConst.MaxDungeonLevel,
                SupportCost = isMax ? 0 : d.levelUpCost,
                DefeatReward = d.rewardGold,
                ClearChancePct = ClearProbabilityCalculator.Calculate(hero, ItemModel, d, d.currentDungeonLevel, RelicResolver),
                IsNextStream = next.HasValue && next.Value == d.key,
            });
        }

        // --- 今日の朝刊（購読制は未実装のため紙面全体が見える） ---
        foreach (var e in News.IssueOf(CurrentTurn))
        {
            var a = e.Article;
            if (a == null) continue;
            s.News.Add(new AutoPlayNewsView
            {
                ArticleId = a.id,
                Company = e.Company != null ? e.Company.companyId : e.companyId,
                Page = a.page,
                SourceClarity = a.sourceClarity,
                Byline = string.IsNullOrEmpty(a.byline) ? "(unsigned)" : "signed",
                Kind = e.kind.ToString(),
                HeadlineJa = a.headline,
                SummaryEn = a.summaryEn,
            });
        }

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

        // 借金の強制返済（DebtPresenter.ShowForced → OnPay / OnBankruptcy の写し）
        if (turn / GameConst.DebtPaymentInterval > TomsModel.DebtCycle.Value)
        {
            int cycle = TomsModel.DebtCycle.Value + 1;
            int amount = DebtCalculator.GetAmount(cycle, TomsModel, RelicResolver);
            int money = TomsModel.PlayerMoney.Value;
            if (money < amount && Portfolio != null)
            {
                int shortfall = amount - money;
                int liquidatable = Portfolio.GetForcedLiquidationValue(ItemModel, TomsModel.BlacksmithLevel.Value);
                if (liquidatable > 0 && money + liquidatable >= amount)
                {
                    Portfolio.LiquidateForDebt(shortfall, TomsModel, ItemModel, TomsModel.BlacksmithLevel.Value);
                    money = TomsModel.PlayerMoney.Value;
                    Today.Notes.Add("金融資産を強制売却して返済");
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

    public AutoPlayActionResult UpgradeShop()
    {
        int before = TomsModel.PlayerMoney.Value;
        if (!TomsModel.UpgradeShop(Assets.ShopLevels)) return Reject("店レベルアップ不可");
        Today.UpgradeSpend += before - TomsModel.PlayerMoney.Value;
        return AutoPlayActionResult.Success($"店 Lv{TomsModel.ShopLevel.Value}");
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

        // BattleSceneStarter.CalculateDefeatReward の写し
        int defeatReward = 0;
        if (!outcome.Victory)
        {
            int reward = dungeon?.GetLevelData(level)?.rewardGold ?? 0;
            defeatReward = Mathf.RoundToInt(reward * RelicBattleEffects.DefeatRewardMul);
        }

        // 純利益 = 売上 − 補充(なし) + 返金(なし) + 防衛報酬 − 介入支出 + 未実行介入の返金（BattleSceneStarter の写し）
        int totalEarnings = rawSales + defeatReward - outcome.InterventionSpent + outcome.InterventionRefund;

        string weaponId = _battleIn.EquippedItemIds.Count > 0 ? _battleIn.EquippedItemIds[0] : "";
        string armorId = _battleIn.EquippedItemIds.Count > 1 ? _battleIn.EquippedItemIds[1] : "";
        var result = outcome.Victory ? BattleResult.Victory : BattleResult.Defeat;
        _battleOut.SetResult(result, weaponId, armorId, outcome.Sold, totalEarnings,
            outcome.MobsDefeated, outcome.BossesDefeated);
        _battleOut.SetStreamingStats(outcome.InterventionSpent, outcome.InterventionRefund, 0, outcome.ViewerSpecials, outcome.SpecialMoves);

        Today.StreamEarnings = rawSales;
        Today.DefeatReward = defeatReward;
        Today.InterventionSpent = outcome.InterventionSpent;
        Today.InterventionRefund = outcome.InterventionRefund;
        Today.BattleResult = outcome.Victory ? "HeroWin" : "HeroLose";
        Today.BattleDungeon = $"{_battleIn.DungeonKey} Lv{level}";
        Today.BattleClearPct = outcome.DisplayedClearPct;

        // 精算と日送りは本物のハンドラで（TomsShop 帰還時の BattleResultHandler.Start と同じ）
        var handler = new BattleResultHandler(_battleOut, _battleIn, ItemModel, _stateManager, Flow,
            Assets.Economy, TomsModel, HeroModel, RelicRewards, null);
        try
        {
            handler.Start();
        }
        finally
        {
            handler.Dispose();
        }

        ResolveStageAfterFlow();
        return AutoPlayActionResult.Success($"{Today.BattleResult} 売上 {rawSales}G 防衛報酬 {defeatReward}G");
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

        var info = new AutoPlayStreamInfo
        {
            Dungeon = key.Value.ToString(),
            Level = level,
            DisplayedClearPct = ClearProbabilityCalculator.Calculate(hero, ItemModel, dungeon, level, RelicResolver),
            HasBoss = hasBoss,
            EnemyCount = count,
            DefeatReward = Mathf.RoundToInt((dungeon.GetLevelData(level)?.rewardGold ?? 0) * rewardMul),
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

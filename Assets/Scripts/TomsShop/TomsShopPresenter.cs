using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer.Unity;

public class TomsShopPresenter : IDisposable, IPresenter, IStartable
{
    /// <summary>配信前日の営業開始で一度だけ出す確認の見出し（G-3。Debug/DebugAutoPlayer が見出しで承認する）。</summary>
    public const string StreamEveConfirmTitle = "明日は配信です";

    /// <summary>品出しゼロで営業開始を押したときの確認の見出し（Debug/DebugAutoPlayer が見出しで承認する）。</summary>
    public const string EmptyDisplayConfirmTitle = "品出しゼロで営業します";

    /// <summary>最初のレリック3択の前に流す説明（K-10。Assets/TomsScenario/Scenarios/Tutorial.csv）。</summary>
    private const string RelicIntroLabel = "Tutorial_RelicFirst";

    private readonly TomsShopView tomsShopView;
    private readonly ItemSelectionPresenter itemSelectionPresenter;
    private readonly ItemModel itemModel;
    private readonly TomsModel tomsShopModel;
    private readonly CompositeDisposable disposables = new();
    private readonly CommonView commonView;
    private readonly StateManager stateManager;
    private readonly GameFlowManager gameFlowManager;
    private readonly TurnEndSummaryPresenter turnEndSummaryPresenter;
    private readonly PendingEventData pendingEventData;
    private readonly EventView eventView;
    private readonly TomsEventExecutor tomsEventExecutor;
    private readonly MarketingFacade marketingFacade;
    private readonly DebtPresenter debtPresenter;
    private readonly TurnPhaseManager turnPhaseManager;
    private readonly SalesPhaseView salesPhaseView;
    private readonly MorningReportModel morningReportModel;
    private readonly ShopMachineModel shopMachineModel;
    private readonly RelicInventoryModel relicInventory;
    private readonly RelicRewardService relicRewardService;
    private readonly RelicEffectResolver relicResolver;
    private readonly PopUpManager popUpManager;
    private readonly TutorialScenarioService scenarioService;
    private readonly MetaProgressModel meta;
    private readonly PortfolioModel portfolioModel;
    private readonly HeroModel heroModel;
    private readonly DungeonRepository dungeonRepository;
    private readonly SellOrderModel sellOrderModel;
    private readonly CancellationTokenSource lifetimeCts = new();

    /// <summary>朝の通知を順に出している処理（Entry のたびに作り直す）。</summary>
    private CancellationTokenSource morningCts;

    /// <summary>まだ閉じられていない朝レポートの文（Entry をやり直しても失わないよう持っておく）。</summary>
    private string pendingMorningReport;

    /// <summary>朝レポートを閉じたときに HUD の所持金を数え上げるか。</summary>
    private bool morningMoneyHeld;

    /// <summary>配信前日の確認を出したターン（一度だけ出す）。</summary>
    private int streamEveConfirmedTurn = -1;

    /// <summary>前回Entry()時のターン番号。初回はスキップ用に-1。</summary>
    private int _lastKnownTurn = -1;

    /// <summary>初回Entryでターンフェーズを一度だけ開始するためのフラグ。</summary>
    private bool _turnPhaseInitialized = false;

    // 営業開始演出の再生中フラグ（ボタン連打防止。フェーズが変わったら解除）
    private bool _salesStarting = false;

    public TomsShopPresenter(
        TomsShopView tomsShopView,
        ItemSelectionPresenter itemSelectionPresenter,
        ItemModel itemModel,
        TomsModel tomsShopModel,
        CommonView commonView,
        StateManager stateManager,
        GameFlowManager gameFlowManager,
        TurnEndSummaryPresenter turnEndSummaryPresenter,
        PendingEventData pendingEventData,
        EventView eventView,
        TomsEventExecutor tomsEventExecutor,
        GamePanelManager gamePanelManager,
        MarketingFacade marketingFacade,
        DebtPresenter debtPresenter,
        TurnPhaseManager turnPhaseManager,
        SalesPhaseView salesPhaseView,
        MorningReportModel morningReportModel,
        ShopMachineModel shopMachineModel,
        RelicInventoryModel relicInventory,
        RelicRewardService relicRewardService,
        RelicEffectResolver relicResolver,
        PopUpManager popUpManager,
        TutorialScenarioService scenarioService = null,
        MetaProgressModel meta = null,
        PortfolioModel portfolioModel = null,
        HeroModel heroModel = null,
        DungeonRepository dungeonRepository = null,
        SellOrderModel sellOrderModel = null)
    {
        this.tomsShopView = tomsShopView;
        this.itemSelectionPresenter = itemSelectionPresenter;
        this.itemModel = itemModel;
        this.tomsShopModel = tomsShopModel;
        this.commonView = commonView;
        this.stateManager = stateManager;
        this.gameFlowManager = gameFlowManager;
        this.turnEndSummaryPresenter = turnEndSummaryPresenter;
        this.pendingEventData = pendingEventData;
        this.eventView = eventView;
        this.tomsEventExecutor = tomsEventExecutor;
        this.marketingFacade = marketingFacade;
        this.debtPresenter = debtPresenter;
        this.turnPhaseManager = turnPhaseManager;
        this.salesPhaseView = salesPhaseView;
        this.morningReportModel = morningReportModel;
        this.shopMachineModel = shopMachineModel;
        this.relicInventory = relicInventory;
        this.relicRewardService = relicRewardService;
        this.relicResolver = relicResolver;
        this.popUpManager = popUpManager;
        this.scenarioService = scenarioService;
        this.meta = meta;
        this.portfolioModel = portfolioModel;
        this.heroModel = heroModel;
        this.dungeonRepository = dungeonRepository;
        this.sellOrderModel = sellOrderModel;

        // EventViewにGamePanelManagerを注入
        eventView.Initialize(gamePanelManager);

        stateManager.RegisterOnEnter(TomsShopGamePhase.Shop, Entry);
    }

    public void Start()
    {
        SoundManager.Instance?.PlayBGM("通常営業");
        Bind();
    }

    private void Bind()
    {
        // 「鍛冶屋」ボタン
        tomsShopView.OnBlacksmithClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.BlackSmith))
            .AddTo(disposables);

        tomsShopView.OnHeroClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Hero))
            .AddTo(disposables);

        //　情報屋ボタン
        tomsShopView.OnInfoClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Broker))
            .AddTo(disposables);

        //　道具屋ボタン
        tomsShopView.OnToolClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.ToolShop))
            .AddTo(disposables);

        // 「陳列設定」ボタン
        tomsShopView.OnSetItemClicked
            .Subscribe(_ => OpenSelectionPanel())
            .AddTo(disposables);

        // 陳列パネルを閉じたら机の表示を更新
        itemSelectionPresenter.OnClosed
            .Subscribe(_ => tomsShopView.RefreshDeskDisplay(itemModel.RuntimeItems))
            .AddTo(disposables);

        //　マップボタン
        tomsShopView.OnMapClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Map))
            .AddTo(disposables);

        //　ダンジョンレベルアップボタン
        tomsShopView.OnDungeonLevelUpClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.DungeonLevelUp))
            .AddTo(disposables);

        //　広告購入画面ボタン
        tomsShopView.OnAdvertisementClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Advertisement))
            .AddTo(disposables);

        //　預言者画面ボタン
        tomsShopView.OnProphetClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Prophet))
            .AddTo(disposables);

        //　店の改装（店レベルアップ）画面ボタン
        tomsShopView.OnShopUpgradeClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.ShopUpgrade))
            .AddTo(disposables);

        //　マシンショップ（店カスタマイズ）画面ボタン
        tomsShopView.OnMachineShopClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.MachineShop))
            .AddTo(disposables);

        //　朝刊（情報ターミナル）。閉じると NewspaperPresenter が開く前のフェーズ（Shop）へ戻す
        tomsShopView.OnNewspaperClicked
            .Subscribe(_ =>
            {
                if (stateManager.HasHandler(TomsShopGamePhase.Newspaper))
                    stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Newspaper);
            })
            .AddTo(disposables);

        //　レリック3択の選択/スキップ
        tomsShopView.OnRelicChoiceSelected
            .Subscribe(index =>
            {
                if (relicRewardService != null &&
                    relicRewardService.ChoosePending(index, gameFlowManager.CurrentTurn.Value))
                {
                    SoundManager.Instance?.PlaySE("営業/SE_仕入れ完了");
                }
                tomsShopView.HideRelicChoices();
                RefreshRelicBar();
            })
            .AddTo(disposables);

        // レリックバーのアイコンクリック → 説明ポップアップ
        tomsShopView.OnRelicIconClicked
            .Subscribe(ShowRelicDetailPopup)
            .AddTo(disposables);

        tomsShopView.OnRelicChoiceSkipped
            .Subscribe(_ =>
            {
                // 辞退したら代わりにゴールドをもらう（額は選択肢の最高レア度で決まる）
                int gold = relicRewardService?.DeclineForGold() ?? 0;
                if (gold > 0)
                {
                    tomsShopModel.AddRevenue(gold);
                    tomsShopModel.SavePlayerMoney();
                    SoundManager.Instance?.PlaySE("営業/SE_売上音");
                    Debug.Log($"[Relic] レリックを辞退して {gold}G を獲得");
                }
                tomsShopView.HideRelicChoices();
            })
            .AddTo(disposables);

        //　営業フェーズの「営業開始」ボタン →（配信前日は一度だけ確認）→ 営業ダイジェスト → サマリー表示
        tomsShopView.OnStartShopClicked
            .Subscribe(_ => RequestStartSales())
            .AddTo(disposables);

        // フェーズが動いたら営業開始ボタンの連打ガードを解除する
        // （次ターンのSales再突入時や、演出中に戻った場合の復帰用）
        turnPhaseManager.CurrentTurnPhase
            .Subscribe(_ =>
            {
                _salesStarting = false;
                tomsShopView.SetStartShopInteractable(true);
            })
            .AddTo(disposables);

        // 借金返済ボタン → 任意払いパネルを表示
        tomsShopView.OnDebtPaymentClicked
            .Subscribe(_ => debtPresenter.ShowVoluntary())
            .AddTo(disposables);

        // 返済完了 → 返済報酬のレリック3択を即表示し、次回返済表示を更新
        debtPresenter.OnDebtPaid
            .Subscribe(_ =>
            {
                RefreshNextDebtDisplay();
                ShowPendingRelicChoicesAsync(lifetimeCts.Token).Forget();
            })
            .AddTo(disposables);

        //　ターン表示の更新（CommonView）
        gameFlowManager.CurrentTurn
            .Subscribe(turn => commonView.UpdateCurrentTurn(turn))
            .AddTo(disposables);

        // 次回借金返済額と残りターン数の表示（DebtCycle または CurrentTurn が変わるたびに再計算）
        tomsShopModel.DebtCycle
            .Subscribe(_ => RefreshNextDebtDisplay())
            .AddTo(disposables);

        gameFlowManager.CurrentTurn
            .Subscribe(_ => RefreshNextDebtDisplay())
            .AddTo(disposables);

        // ※ターン切り替え演出は Entry() 内で一元管理する
        //   バズ発生時はバズ演出のみ表示し、ターン演出はスキップするため

        // バズ中の常時演出（バズモードオーバーレイ）
        // IsBuzzActive と CurrentBuzzType は BuzzSystem 内で別々に更新されるため CombineLatest で同期する
        marketingFacade.Buzz.IsBuzzActive
            .CombineLatest(marketingFacade.Buzz.CurrentBuzzType, (isActive, buzzType) => (isActive, buzzType))
            .Subscribe(x => tomsShopView.SetBuzzModeActive(x.isActive, x.buzzType))
            .AddTo(disposables);

        marketingFacade.Buzz.RemainingTurns
            .Subscribe(turns => tomsShopView.UpdateBuzzRemainingTurns(turns))
            .AddTo(disposables);

        // イベントポップアップの確認ボタン押下時
        eventView.OnConfirmClicked
            .Subscribe(_ => OnEventPopupConfirmed())
            .AddTo(disposables);
    }

    private void RefreshNextDebtDisplay()
    {
        int cycle = tomsShopModel.DebtCycle.Value;
        // 借入・猶予証・レリック補正込みの額（実際の支払いと同じ DebtCalculator を使う）
        int nextAmount = DebtCalculator.GetAmount(cycle + 1, tomsShopModel, relicResolver);
        int nextPaymentTurn = (cycle + 1) * GameConst.DebtPaymentInterval;
        int remainingTurns = nextPaymentTurn - gameFlowManager.CurrentTurn.Value;
        tomsShopView.UpdateNextDebt(nextAmount, remainingTurns);
    }

    public void Entry()
    {
        // --- 配信前の寄り道中 ---
        // 日送りは済んで配信日に入っている。ここで店のホームを開くと
        // 朝刊・借金・朝レポート消費・ターンフェーズ開始が「配信日の営業」として走ってしまうため、
        // _lastKnownTurn などに一切触れずに抜け、配信の判断へ戻す（戻った先で再び鍛冶屋か配信を選ぶ）。
        if (gameFlowManager.IsAwaitingStream.Value)
        {
            Debug.LogWarning("[TomsShopPresenter] 配信前の寄り道中に Shop へ入ろうとしたため、配信の判断へ戻します");
            if (stateManager.HasHandler(TomsShopGamePhase.BlackSmith))
                stateManager.ChangeTomsShopPhase(TomsShopGamePhase.BlackSmith);
            gameFlowManager.RequestPreStreamDecision(true);
            return;
        }

        SoundManager.Instance?.PlayBGM("通常営業");

        tomsShopView.RefreshDeskDisplay(itemModel.RuntimeItems);
        tomsShopView.RefreshMachineDisplay(shopMachineModel);

        // ターン変化はフェーズ開始判定に使うため、_lastKnownTurn が他で書き換わる前に捕捉する
        int currentTurn = gameFlowManager.CurrentTurn.Value;
        bool turnChanged = _lastKnownTurn != -1 && _lastKnownTurn != currentTurn;

        // 朝刊はターン頭に自動では開かない（店画面のボタンから開く）

        // 朝の通知（F-4）: 表示の順番は従来どおり「保留イベント → 税金 → ターン/バズ演出 → 朝レポート → レリック3択」。
        // 以前は同じフレームで全部開いて重なっていたので、前のものが閉じる（会話が終わる）まで次を待つ。
        // 中身の決定（イベントの保留・返済の判定・バズ結果と朝レポートの消費）はここで同期的に行い、順序の意味は変えない。
        var steps = new List<MorningStep>();

        ShowPendingEventIfExists();

        // 借金返済チェック: 支払うべきサイクル数 > 支払い済みサイクル数 なら強制返済パネルを表示
        if (currentTurn / GameConst.DebtPaymentInterval > tomsShopModel.DebtCycle.Value)
        {
            _lastKnownTurn = currentTurn;
            // 現金が足りなければ、今日入金予定の売り注文を前倒しで入金してから判定する
            steps.Add(new MorningStep(
                () => debtPresenter.ShowForced(gameFlowManager.SettleTodaySellOrdersForDebt),
                () => debtPresenter.IsShowing));
        }

        var announce = PrepareTurnOrBuzzAnnounce();
        if (announce != null) steps.Add(new MorningStep(announce, null, AnnounceSeconds));

        // 朝レポート（売り注文の持ち越し精算・配当・債券償還・マシンの設備収入/生成）
        // 消費型: NextTurn 中に溜まった行を1つの通知にまとめて表示する
        if (morningReportModel != null && morningReportModel.HasLines)
        {
            int income = morningReportModel.MorningIncome;
            var lines = morningReportModel.Consume();
            string body = string.Join("\n", lines);
            string head = BuildMorningHeader(income);
            pendingMorningReport = string.IsNullOrEmpty(pendingMorningReport)
                ? (string.IsNullOrEmpty(head) ? body : $"{head}\n\n{body}")
                : $"{pendingMorningReport}\n{body}";
            // 朝の入金は HUD の所持金を前日の額に据え置き、レポートを閉じたときに数え上げて見せる
            if (income > 0 && !morningMoneyHeld)
            {
                morningMoneyHeld = true;
                commonView.HoldMoneyDisplay(tomsShopModel.PlayerMoney.Value - income);
            }
        }
        if (!string.IsNullOrEmpty(pendingMorningReport))
        {
            string text = pendingMorningReport;
            steps.Add(new MorningStep(() =>
            {
                if (tomsShopView.IsMorningReportOpen) return;
                if (!tomsShopView.ShowMorningReport(text))
                {
                    // パネル未配線時はログにフォールバック（お金は適用済み）
                    Debug.Log($"[MorningReport]\n{text}");
                    OnMorningReportClosed();
                }
            }, () => tomsShopView.IsMorningReportOpen, onClosed: OnMorningReportClosed));
        }

        // 配信勝利報酬のレリック3択（保留があれば表示。初回だけ説明の会話を先に流す）
        steps.Add(new MorningStep(null, () => tomsShopView.IsRelicChoiceOpen, asyncShow: ShowPendingRelicChoicesAsync));

        RunMorningNotices(steps);

        // 所持レリックバーの更新
        RefreshRelicBar();

        // --- ターン進行フェーズの開始 / 再適用 ---
        if (turnChanged || !_turnPhaseInitialized)
        {
            // 新ターン（または初回Entry）: 保留イベントがあればEvent、無ければProcurementから開始
            _turnPhaseInitialized = true;
            turnPhaseManager.BeginTurnPhases(pendingEventData.HasPendingEvent);
        }
        else
        {
            // 詳細画面からのホーム復帰: 現フェーズのUIを再適用（フェーズは保持）
            turnPhaseManager.CurrentTurnPhase.ForceNotify();
        }
    }

    // ========================================
    // 朝の通知を1枚ずつ（F-4）
    // ========================================

    /// <summary>ターン/バズ演出を見せてから次の通知を出すまでの秒数。</summary>
    private const float AnnounceSeconds = 1.2f;

    /// <summary>朝の通知1つぶん。show で出し、isOpen が false になったら次へ。</summary>
    private sealed class MorningStep
    {
        public readonly Action Show;
        public readonly Func<CancellationToken, UniTask> AsyncShow;
        public readonly Func<bool> IsOpen;
        public readonly float HoldSeconds;
        public readonly Action OnClosed;

        public MorningStep(Action show, Func<bool> isOpen, float holdSeconds = 0f, Action onClosed = null,
            Func<CancellationToken, UniTask> asyncShow = null)
        {
            Show = show; IsOpen = isOpen; HoldSeconds = holdSeconds; OnClosed = onClosed; AsyncShow = asyncShow;
        }
    }

    /// <summary>
    /// 朝の通知を順に出す。前の通知（保留イベント・会話を含む）が閉じるまで次を出さない。
    /// 待つものが無ければ同じフレームのうちに出し切る（以前と同じタイミング）。
    /// Entry がやり直されたら、前の続きは捨てて作り直す（中身は Entry 側で持ち直す）。
    /// </summary>
    private void RunMorningNotices(List<MorningStep> steps)
    {
        morningCts?.Cancel();
        morningCts?.Dispose();
        morningCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
        RunMorningNoticesAsync(steps, morningCts.Token).Forget();
    }

    private async UniTaskVoid RunMorningNoticesAsync(List<MorningStep> steps, CancellationToken ct)
    {
        if (morningReportModel != null) morningReportModel.IsShowingNotices = true;
        try
        {
            foreach (var step in steps)
            {
                // 保留イベント（確認を押すまで）と会話の再生中は、次の通知を重ねない
                await WaitUntilFreeAsync(ct);
                if (step.AsyncShow != null) await step.AsyncShow(ct);
                else step.Show?.Invoke();
                if (step.HoldSeconds > 0f)
                    await UniTask.Delay(TimeSpan.FromSeconds(step.HoldSeconds * GameSettings.EffectDurationScale), cancellationToken: ct);
                if (step.IsOpen != null)
                {
                    while (step.IsOpen()) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    step.OnClosed?.Invoke();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Entry のやり直し・シーン破棄
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            if (!ct.IsCancellationRequested && morningReportModel != null) morningReportModel.IsShowingNotices = false;
        }
    }

    private async UniTask WaitUntilFreeAsync(CancellationToken ct)
    {
        // ScenarioPlayer.Instance は見つからないとエラーログを出すので、ここでは直接探す
        var player = UnityEngine.Object.FindFirstObjectByType<ScenarioPlayer>();
        while (pendingEventData.HasPendingEvent || (player != null && player.IsPlaying.CurrentValue))
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
    }

    private void OnMorningReportClosed()
    {
        pendingMorningReport = null;
        if (morningMoneyHeld)
        {
            morningMoneyHeld = false;
            commonView.ReleaseMoneyDisplay();
        }
    }

    /// <summary>
    /// 朝レポートの見出し: 今朝の入金の合計（金色・大きく）と、総資産と昨日比（R1-A-12）。
    /// 総資産 = 現金 + 在庫の評価額（今の値段）+ 未約定の売り注文 + 金融資産。
    /// </summary>
    private string BuildMorningHeader(int income)
    {
        int netWorth = CalculateNetWorth();
        int? last = morningReportModel?.LastNetWorth;
        if (morningReportModel != null) morningReportModel.LastNetWorth = netWorth;

        var sb = new StringBuilder();
        if (income > 0) sb.Append($"<size=140%><color=#E8A33D>今朝の入金 +{income:N0}G</color></size>\n");
        sb.Append($"総資産 {netWorth:N0}G");
        if (last.HasValue)
        {
            int diff = netWorth - last.Value;
            string color = diff >= 0 ? "#E8A33D" : "#A8332B";
            float rate = last.Value > 0 ? (float)diff / last.Value : 0f;
            sb.Append($"（昨日比 <color={color}>{(diff >= 0 ? "+" : "")}{diff:N0}G {(rate >= 0 ? "+" : "")}{rate * 100f:0.0}%</color>）");
        }
        return sb.ToString();
    }

    private int CalculateNetWorth()
    {
        long total = tomsShopModel.PlayerMoney.Value;
        foreach (var r in itemModel.RuntimeItems) total += (long)r.Stock.Value * r.CurrentPrice.Value;
        if (sellOrderModel != null) total += sellOrderModel.PendingTotalEstimate.Value;
        if (portfolioModel != null)
        {
            portfolioModel.RefreshEstimate(itemModel, tomsShopModel.BlacksmithLevel.Value);
            total += portfolioModel.TotalAssetsEstimate.Value;
        }
        return (int)Math.Min(int.MaxValue, total);
    }

    // ========================================
    // 営業開始 → 営業ダイジェスト → サマリー（F-7, G-3）
    // ========================================

    private void RequestStartSales()
    {
        if (turnPhaseManager.CurrentTurnPhase.Value != TurnPhase.Sales) return;
        if (_salesStarting) return; // 演出中の連打防止

        if (TryShowEmptyDisplayConfirm()) return;
        ContinueStartSales();
    }

    /// <summary>品出しの確認のあと: 配信前日なら一度だけ装備の確認、それ以外はそのまま営業。</summary>
    private void ContinueStartSales()
    {
        if (TryShowStreamEveConfirm()) return;
        StartSales();
    }

    /// <summary>
    /// 何も並べずに「営業開始」を押したときだけ確認を出す（M-7）。出したら true。
    /// 「陳列にもどる」で陳列フェーズへ戻す。
    /// </summary>
    private bool TryShowEmptyDisplayConfirm()
    {
        if (popUpManager == null) return false;
        if (itemModel.RuntimeItems.Any(r => r.IsDisplay.Value && r.DisplayStock.Value > 0)) return false;

        popUpManager.Show(new PopUpData
        {
            Title = EmptyDisplayConfirmTitle,
            Message = "まだ何も並べていません。\nこのまま営業すると、今日の売上はありません。",
            ConfirmButtonText = "営業する",
            CancelButtonText = "陳列にもどる",
            Size = PopupSizeEnum.Medium,
            OnConfirm = ContinueStartSales,
            OnCancel = () =>
            {
                if (turnPhaseManager.CurrentTurnPhase.Value == TurnPhase.Sales) turnPhaseManager.GoBackTurnPhase();
            },
        });
        return true;
    }

    /// <summary>
    /// 配信の前日、営業を始める前に一度だけ「明日は配信です。勇者の装備はこれでいいですか？」を出す（G-3）。
    /// 装備の賭けは配信の当日には変えられないので、ここが締め切り。出したら true。
    /// </summary>
    private bool TryShowStreamEveConfirm()
    {
        if (popUpManager == null) return false;
        int turn = gameFlowManager.CurrentTurn.Value;
        if (streamEveConfirmedTurn == turn) return false;
        if (gameFlowManager.GetDaysUntilNextStream() != 1) return false;
        // 1周目で配信を知る前（占い師に出会う前）は出さない
        if (meta != null && meta.TotalRuns <= 0 && !meta.HasSeenTutorial(TutorialSteps.ProphetMeeting)) return false;

        streamEveConfirmedTurn = turn;
        bool final = gameFlowManager.IsUpcomingStreamFinal();
        var sb = new StringBuilder();
        sb.AppendLine("勇者の装備はこれでいいですか？");
        string dungeon = null;
        var key = gameFlowManager.GetNextBattleDungeon();
        if (key.HasValue && dungeonRepository != null) dungeon = dungeonRepository.GetById(key.Value)?.dungeonName;
        if (!string.IsNullOrEmpty(dungeon)) sb.AppendLine($"配信先: {dungeon}");
        sb.AppendLine($"勇者の装備: {DescribeHeroEquipment()}");
        sb.Append(final
            ? "最後の配信です。終わったら決算で、在庫は今の相場で評価されます。"
            : "配信の日は装備を変えられません。");

        popUpManager.Show(new PopUpData
        {
            Title = final ? "明日は最後の配信です" : StreamEveConfirmTitle,
            Message = sb.ToString(),
            ConfirmButtonText = "このまま営業する",
            CancelButtonText = "勇者を見る",
            Size = PopupSizeEnum.Medium,
            OnConfirm = StartSales,
            OnCancel = () =>
            {
                if (stateManager.HasHandler(TomsShopGamePhase.Hero))
                    stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Hero);
            },
        });
        return true;
    }

    /// <summary>「鋼の剣（手持ち12）、革の鎧（手持ち3）」。何も持たせていなければ「なし」。</summary>
    private string DescribeHeroEquipment()
    {
        var ids = heroModel?.EquippedItemIds;
        if (ids == null || ids.Count == 0) return "なし";
        var parts = new List<string>();
        foreach (var id in ids)
        {
            var r = itemModel.GetRuntimeItem(id);
            if (r != null) parts.Add($"{r.ItemName}（手持ち{r.Stock.Value}）");
        }
        return parts.Count > 0 ? string.Join("、", parts) : "なし";
    }

    private void StartSales()
    {
        if (turnPhaseManager.CurrentTurnPhase.Value != TurnPhase.Sales) return;
        if (_salesStarting) return;
        _salesStarting = true;
        tomsShopView.SetStartShopInteractable(false);
        RunSalesAsync(lifetimeCts.Token).Forget();
    }

    /// <summary>
    /// 精算を先に済ませてから（TurnEndSummaryPresenter のガードで1日1回）、客が買っていくダイジェストを見せてサマリーへ。
    /// ダイジェストが未配線・見せる品が無いときは従来の暗転。
    /// </summary>
    private async UniTaskVoid RunSalesAsync(CancellationToken ct)
    {
        TurnEndDayResult day = null;
        try
        {
            day = turnEndSummaryPresenter.ProcessTodayIfNeeded();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }

        var digest = tomsShopView.SalesDigest;
        var entries = BuildDigestEntries(day);
        bool useDigest = digest != null && digest.IsWired && entries.Count > 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (DebugAutoPlayer.Running) useDigest = false; // 自動プレイは演出を待たない
#endif
        if (!useDigest)
        {
            salesPhaseView.PlayAndThen(() =>
            {
                tomsShopView.RefreshDeskDisplay(itemModel.RuntimeItems);
                turnEndSummaryPresenter.ShowSummary();
            });
            return;
        }

        try
        {
            PopRelicGains(day);
            await digest.PlayAsync(entries, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        // 精算は済んでいるので、ここからは必ずサマリーへ進める（同じ日の営業をやり直させない）
        tomsShopView.RefreshDeskDisplay(itemModel.RuntimeItems);
        turnEndSummaryPresenter.ShowSummary();
    }

    private static readonly Color DigestGold = new(1f, 0.82f, 0.3f, 1f);
    private static readonly Color DigestGray = new(0.78f, 0.75f, 0.72f, 1f);
    private static readonly Color DigestWhite = Color.white;

    /// <summary>
    /// 客1人 = 1銘柄。今日入金された注文（昨日並べた分）を先に、値上がりして約定した品は金色で「いい値！」。
    /// 続いて今日並べて注文になった品（入金は明日の夜）。
    /// </summary>
    private List<SalesDigestView.Entry> BuildDigestEntries(TurnEndDayResult day)
    {
        var list = new List<SalesDigestView.Entry>();
        if (day == null) return list;

        var settled = day.Settlement?.Settled;
        float mul = day.Settlement != null ? day.Settlement.Multiplier : 1f;
        var settledIds = new HashSet<string>();
        if (settled != null)
        {
            foreach (var o in settled.OrderByDescending(x => x.Income))
            {
                settledIds.Add(o.ItemId);
                var icon = itemModel.GetRuntimeItem(o.ItemId)?.ItemIcon;
                int income = (int)Math.Min(int.MaxValue, Math.Round(o.Income * (double)mul));
                bool up = o.SettledPrice > o.OrderedPrice;
                bool down = o.SettledPrice < o.OrderedPrice;
                string label = up ? $"+{income:N0}G いい値！" : $"+{income:N0}G";
                list.Add(new SalesDigestView.Entry(icon, label, up ? DigestGold : down ? DigestGray : DigestWhite, up));
            }
        }
        if (day.PlacedItems != null)
        {
            foreach (var (item, qty) in day.PlacedItems.OrderByDescending(x => x.quantity * x.item.CurrentPrice.Value))
            {
                if (item == null || settledIds.Contains(item.ItemId)) continue;
                list.Add(new SalesDigestView.Entry(item.ItemIcon, $"注文 ×{qty}", DigestWhite, false));
            }
        }
        return list;
    }

    /// <summary>営業売上に効いたレリックのアイコンを跳ねさせ、そのレリックで増えた額を浮かせる（F-8）。</summary>
    private void PopRelicGains(TurnEndDayResult day)
    {
        if (day?.RelicFactors == null || day.Settlement == null || day.Settlement.TotalIncome <= 0) return;
        float delay = 0.6f;
        foreach (var (relic, factor) in day.RelicFactors)
        {
            if (relic == null || factor <= 0f || Mathf.Approximately(factor, 1f)) continue;
            int gain = (int)Math.Round(day.Settlement.TotalIncome * (1.0 - 1.0 / factor));
            string text = gain >= 0 ? $"+{gain:N0}G" : $"-{-gain:N0}G";
            tomsShopView.PopRelicIcon(relic.relicId, text, delay);
            delay += 0.15f;
        }
    }

    // ========================================
    // レリック3択
    // ========================================

    /// <summary>
    /// 配信勝利・返済の報酬のレリック3択を表示する。最初の1回だけ、占い師の説明（K-10）を先に流す。
    /// パネル未配線の場合は最初の候補を自動獲得してログに残す（機能自体は成立させる）。
    /// </summary>
    private async UniTask ShowPendingRelicChoicesAsync(CancellationToken ct)
    {
        if (relicRewardService == null || relicRewardService.PendingChoices.Count == 0) return;
        if (tomsShopView.IsRelicChoiceOpen) return;

        if (scenarioService != null && !scenarioService.HasSeen(RelicIntroLabel))
        {
            try
            {
                await scenarioService.PlayOnceAsync(RelicIntroLabel, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (relicRewardService.PendingChoices.Count == 0 || tomsShopView.IsRelicChoiceOpen) return;
        }
        ShowPendingRelicChoicesIfAny();
    }

    /// <summary>
    /// 3択の各カードに、このランの実績からの「だいたいいくら」を1行ずつ（F-9）。出せない種類は空。
    /// </summary>
    private List<string> BuildRelicEstimates(IReadOnlyList<RelicDefinition> choices)
    {
        var result = new List<string>();
        int remainingDays = Mathf.Max(0, gameFlowManager.GetRemainingShopDays());
        var last = turnEndSummaryPresenter.LastDay;
        int dailyIncome = Mathf.Max(last?.Settlement?.TotalIncome ?? 0, sellOrderModel?.PendingTotalEstimate.Value ?? 0);
        foreach (var relic in choices)
        {
            result.Add(EstimateRelic(relic, dailyIncome, remainingDays));
        }
        return result;
    }

    private string EstimateRelic(RelicDefinition relic, int dailyIncome, int remainingDays)
    {
        if (relic?.modifiers == null) return null;
        foreach (var m in relic.modifiers)
        {
            float factor = m.op == RelicOp.Mul ? m.value : 1f + m.value;
            switch (m.stat)
            {
                case RelicStatId.ShopRevenueMul when dailyIncome > 0:
                {
                    int perDay = Mathf.RoundToInt(dailyIncome * (factor - 1f));
                    if (perDay == 0) break;
                    string total = remainingDays > 1 ? $"（残り{remainingDays}日 約{(long)perDay * remainingDays:N0}G）" : "";
                    return $"目安 1日 {Signed(perDay)}G{total}";
                }
                case RelicStatId.DebtAmountMul when m.op == RelicOp.Mul:
                {
                    int next = DebtCalculator.GetAmount(tomsShopModel.DebtCycle.Value + 1, tomsShopModel, relicResolver);
                    int diff = Mathf.RoundToInt(next * (factor - 1f));
                    if (diff == 0) break;
                    return $"目安 次の納付で {Signed(diff)}G";
                }
                case RelicStatId.DividendMul when m.op == RelicOp.Mul:
                {
                    int dividend = itemModel.CalculateDividendIncome();
                    int perDay = Mathf.RoundToInt(dividend * (factor - 1f));
                    if (perDay == 0) break;
                    return $"目安 1日 {Signed(perDay)}G（今の配当で）";
                }
            }
        }
        return null;
    }

    private static string Signed(long v) => v >= 0 ? $"+{v:N0}" : $"-{-v:N0}";

    /// <summary>
    /// 配信勝利報酬のレリック3択を表示する。
    /// パネル未配線の場合は最初の候補を自動獲得してログに残す（機能自体は成立させる）。
    /// </summary>
    private void ShowPendingRelicChoicesIfAny()
    {
        if (relicRewardService == null || relicRewardService.PendingChoices.Count == 0) return;

        string skipLabel = $"辞退して {relicRewardService.GetDeclineGold():N0}G もらう";
        var estimates = BuildRelicEstimates(relicRewardService.PendingChoices);
        if (!tomsShopView.ShowRelicChoices(relicRewardService.PendingChoices, skipLabel, estimates))
        {
            string autoName = relicRewardService.PendingChoices[0].relicName;
            relicRewardService.ChoosePending(0, gameFlowManager.CurrentTurn.Value);
            Debug.Log($"[Relic] 3択パネル未配線のため自動獲得: {autoName}");
            RefreshRelicBar();
        }
    }

    private void RefreshRelicBar()
    {
        if (relicInventory == null) return;
        tomsShopView.UpdateRelicBar(relicInventory.OwnedDefinitions().ToList());
    }

    /// <summary>レリックアイコンのクリック → 説明ポップアップ。</summary>
    private void ShowRelicDetailPopup(string relicId)
    {
        var relic = relicInventory?.OwnedDefinitions().FirstOrDefault(d => d != null && d.relicId == relicId);
        if (relic == null) return;

        popUpManager?.Show(new PopUpData
        {
            Title = relic.relicName,
            Message = string.IsNullOrEmpty(relic.description) ? "（効果の説明はまだない）" : relic.description,
            IsCloseOnly = true,
            Size = PopupSizeEnum.Medium,
        });
    }

    /// <summary>
    /// ターン変更を検出し、バズ演出またはターン演出のどちらかを表示する。
    ///
    /// 【排他制御】
    /// - バズ発生/終了時 → バズ演出のみ表示（ターン演出はスキップ）
    /// - バズなし → 通常のターン切り替え演出を表示
    /// - 初回Entry（_lastKnownTurn == -1） → 演出なし（初期表示時はスキップ）
    /// - サブフェーズ間遷移（ターン番号変化なし） → 演出なし
    /// </summary>
    /// <returns>演出を出す処理（出すものが無ければ null）。判定とバズ結果の消費はここで済ませる。</returns>
    private Action PrepareTurnOrBuzzAnnounce()
    {
        int currentTurn = gameFlowManager.CurrentTurn.Value;
        bool turnChanged = _lastKnownTurn != -1 && _lastKnownTurn != currentTurn;
        _lastKnownTurn = currentTurn;

        // ターンが変わっていなければ演出不要（サブフェーズ間の遷移など）
        if (!turnChanged)
        {
            // バズ結果が残っていれば消費だけして捨てる
            marketingFacade?.ConsumeLastBuzzResult();
            return null;
        }

        // バズ結果を消費型で取得
        var buzzResult = marketingFacade?.ConsumeLastBuzzResult();
        bool hasBuzzEvent = buzzResult != null && (buzzResult.NewBuzzOccurred || buzzResult.BuzzEnded);

        if (hasBuzzEvent)
        {
            string detail = buzzResult.NewBuzzOccurred ? BuildBuzzDetail(buzzResult.NewBuzzType) : null;
            return () =>
            {
                // バズ演出を表示（ターン演出はスキップ）
                if (buzzResult.BuzzEnded)
                {
                    Debug.Log($"[TomsShopPresenter] バズ終了演出表示: {buzzResult.EndedBuzzType}");
                    tomsShopView.ShowBuzzEndedAnnounce(buzzResult.EndedBuzzType);
                }

                if (buzzResult.NewBuzzOccurred)
                {
                    Debug.Log($"[TomsShopPresenter] バズ発生演出表示: {buzzResult.NewBuzzType}");
                    tomsShopView.ShowBuzzAnnounce(buzzResult.NewBuzzType, detail);
                }
            };
        }
        return () => tomsShopView.ShowTurnAnnounce(currentTurn);
    }

    /// <summary>
    /// バズの告知に、倍率と「いつの入金に効くか」を出す（F-6）。
    /// 倍率は約定日（＝今夜の営業サマリー）の状態で掛かるので、効くのは昨日並べて今夜入金される分。
    /// </summary>
    private string BuildBuzzDetail(BuzzType type)
    {
        var buzz = marketingFacade?.Buzz;
        if (buzz == null) return null;
        float mul = buzz.GetCurrentRevenueMultiplier();
        int remaining = buzz.RemainingTurns.Value;
        string rest = remaining > 1 ? $"（続けば あと最大{remaining}日）" : "";
        return $"今夜の入金 ×{mul:0.##}{rest}";
    }

    /// <summary>
    /// 保留中のイベントがあればポップアップを表示する
    /// </summary>
    private void ShowPendingEventIfExists()
    {
        if (!pendingEventData.HasPendingEvent) return;

        var tomsEvent = pendingEventData.PendingEvent;
        Debug.Log($"[TomsShopPresenter] Showing pending event popup: {tomsEvent.title}");

        // イベント表示の失敗で Entry 全体（ターンフェーズ開始）を巻き込まないようにガードする。
        // 失敗した場合は保留イベントを破棄してターンを続行する。
        try
        {
            string effectText = BuildEffectText(tomsEvent.commands);
            eventView.ShowEvent(tomsEvent.title, tomsEvent.description, effectText);
        }
        catch (Exception e)
        {
            Debug.LogError($"[TomsShopPresenter] イベントポップアップ表示に失敗したためスキップします: {tomsEvent.id} {tomsEvent.title}\n{e}");
            pendingEventData.Clear();
        }
    }

    /// <summary>
    /// イベントポップアップの確認ボタンが押された時の処理
    /// </summary>
    private void OnEventPopupConfirmed()
    {
        if (!pendingEventData.HasPendingEvent) return;

        var tomsEvent = pendingEventData.PendingEvent;
        Debug.Log($"[TomsShopPresenter] Event popup confirmed: {tomsEvent.title}");

        // コマンドを実行
        tomsEventExecutor.Execute(tomsEvent);

        // 保留データをクリア
        pendingEventData.Clear();

        // イベントフェーズ完了 → 仕入れフェーズへ前進
        if (turnPhaseManager.CurrentTurnPhase.Value == TurnPhase.Event)
        {
            turnPhaseManager.AdvanceTurnPhase();
        }
    }

    /// <summary>
    /// コマンドの効果をテキストとして構築する
    /// </summary>
    private string BuildEffectText(List<TomsEventCommand> commands)
    {
        var sb = new StringBuilder();

        foreach (var cmd in commands)
        {
            switch (cmd.command)
            {
                case "ChangeMoney":
                    // 不正なマスターデータ（数値でないamount）でも落とさない
                    if (cmd.parameters.TryGetValue("amount", out var moneyStr) && int.TryParse(moneyStr, out var amount))
                    {
                        sb.AppendLine(amount >= 0 ? $"所持金 +{amount:N0}G" : $"所持金 {amount:N0}G");
                    }
                    break;

                case "ChangeTrust":
                    if (cmd.parameters.TryGetValue("amount", out var trustStr) && float.TryParse(trustStr, out var trustAmount))
                    {
                        sb.AppendLine(trustAmount >= 0 ? $"信頼度 +{trustAmount}" : $"信頼度 {trustAmount}");
                    }
                    break;

                case "AddItem":
                    if (cmd.parameters.TryGetValue("itemId", out var itemId))
                    {
                        sb.AppendLine($"アイテム獲得: {itemId}");
                    }
                    break;
            }
        }

        return sb.ToString().TrimEnd();
    }

    private void OpenSelectionPanel() => itemSelectionPresenter.OnOpenSelectionPanel();

    public void Dispose()
    {
        lifetimeCts.Cancel();
        lifetimeCts.Dispose();
        morningCts?.Dispose();
        disposables.Dispose();
    }
}

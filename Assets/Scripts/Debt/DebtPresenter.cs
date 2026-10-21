using System;
using R3;
using VContainer.Unity;

public class DebtPresenter : IDisposable, IStartable
{
    private readonly DebtView debtView;
    private readonly TomsModel tomsModel;
    private readonly SceneTransitionService sceneTransitionService;
    private readonly PortfolioModel portfolioModel;
    private readonly ItemModel itemModel;
    private readonly RelicEffectResolver relicResolver;
    private readonly RelicRewardService relicRewardService;
    private readonly GameFlowManager gameFlowManager;
    private readonly PopUpManager popUpManager;
    private readonly DungeonRepository dungeonRepository;
    private readonly CompositeDisposable disposables = new();

    /// <summary>開いている返済パネルが村の銀行 Lv5「勝負の融資」の返済か（false = 通常の納付）。</summary>
    private bool payingBankLoan;

    /// <summary>勝負の融資を持ちかけた日（同じ日に何度も出さない）と、断った配信の GameFlow インデックス（同じ配信の前に出し直さない）。</summary>
    private int bankLoanOfferedTurn = -1;
    private int bankLoanDeclinedBattleTurn = -1;
    private bool bankLoanOfferShowing;

    /// <summary>
    /// 返済が完了したときに発火する（TomsShopPresenter が購読し、
    /// 返済報酬のレリック3択の表示と次回返済表示の更新を行う）。
    /// </summary>
    public Subject<R3.Unit> OnDebtPaid { get; } = new();

    /// <summary>勝負の融資を借りた・返したときに発火する（店ホームの納付の表示を更新する）。</summary>
    public Subject<R3.Unit> OnBankLoanChanged { get; } = new();

    /// <summary>返済パネル（強制・任意・破産）を開いているか。</summary>
    public bool IsShowing => debtView != null && debtView.IsShowing;

    public DebtPresenter(
        DebtView debtView,
        TomsModel tomsModel,
        SceneTransitionService sceneTransitionService,
        PortfolioModel portfolioModel,
        ItemModel itemModel,
        RelicEffectResolver relicResolver,
        RelicRewardService relicRewardService,
        GameFlowManager gameFlowManager = null,
        PopUpManager popUpManager = null,
        DungeonRepository dungeonRepository = null)
    {
        this.gameFlowManager = gameFlowManager;
        this.popUpManager = popUpManager;
        this.dungeonRepository = dungeonRepository;
        this.debtView = debtView;
        this.tomsModel = tomsModel;
        this.sceneTransitionService = sceneTransitionService;
        this.portfolioModel = portfolioModel;
        this.itemModel = itemModel;
        this.relicResolver = relicResolver;
        this.relicRewardService = relicRewardService;
    }

    /// <summary>
    /// 借入・猶予証・レリック補正を適用した返済額。
    /// 表示（ShowVoluntary/ShowForced）と実際の支払い（OnPay）で必ず同じ値になるよう
    /// DebtCalculator に一元化している。
    /// </summary>
    private int GetDebtAmount(int cycle) => DebtCalculator.GetAmount(cycle, tomsModel, relicResolver);

    public void Start() => Bind();

    private void Bind()
    {
        debtView.OnPayClicked
            .Subscribe(_ => OnPay())
            .AddTo(disposables);

        debtView.OnCloseClicked
            .Subscribe(_ => debtView.Hide())
            .AddTo(disposables);

        debtView.OnGoToResultClicked
            .Subscribe(_ => OnBankruptcy())
            .AddTo(disposables);
    }

    /// <summary>
    /// ショップボタンから任意払いで開く。閉じるボタンあり。
    /// </summary>
    public void ShowVoluntary()
    {
        payingBankLoan = false;
        int cycle = tomsModel.DebtCycle.Value + 1;
        int debtAmount = GetDebtAmount(cycle);
        debtView.ShowPayment(debtAmount, tomsModel.PlayerMoney.Value, cycle, forced: false);
    }

    /// <summary>
    /// 10ターンごとの強制返済で開く。閉じるボタンなし。払えなければゲームオーバー。
    /// </summary>
    /// <param name="settleTodaySellOrders">
    /// 現金が足りないときに、今日入金予定の売り注文を前倒しで約定して入金する処理（戻り値は入金額）。
    /// 入金は通常その日の夜だが、返済の判定は朝なので、これが無いと「前日に並べた分」を数えずに破産する。
    /// </param>
    public void ShowForced(Func<int> settleTodaySellOrders = null)
    {
        payingBankLoan = false;
        int cycle = tomsModel.DebtCycle.Value + 1;
        int debtAmount = GetDebtAmount(cycle);
        int currentMoney = RaiseCashFor(debtAmount, settleTodaySellOrders);

        if (currentMoney < debtAmount)
            debtView.ShowBankruptcy(debtAmount, currentMoney, cycle);
        else
            debtView.ShowPayment(debtAmount, currentMoney, cycle, forced: true);
    }

    /// <summary>
    /// 現金が amount に足りないとき、納付と同じ順で現金を作る: 今日の売上の前倒し → 金融資産の強制売却 → 在庫の投げ売り（H-8）。
    /// 戻り値はそのあとの所持金（それでも足りなければ破産）。
    /// </summary>
    private int RaiseCashFor(int debtAmount, Func<int> settleTodaySellOrders)
    {
        int currentMoney = tomsModel.PlayerMoney.Value;

        // まず今日入金予定の売り注文を前倒しで受け取る（金融資産の強制売却より先。こちらは損が出ない）
        if (currentMoney < debtAmount && settleTodaySellOrders != null)
        {
            int income = settleTodaySellOrders();
            currentMoney = tomsModel.PlayerMoney.Value;
            if (income > 0)
                UnityEngine.Debug.Log($"[Debt] 返済のため本日分の売り注文を前倒し約定: +{income}G → 所持金 {currentMoney}G");
        }

        // 破産判定は現金のみ。ただし現金不足でも金融資産（ファンド・債券）の
        // 強制売却（割増手数料・債券は中途解約ペナルティ）で返済額に届くなら、
        // 自動で売却して救済する。売っても届かない場合のみ破産。
        if (currentMoney < debtAmount && portfolioModel != null)
        {
            int shortfall = debtAmount - currentMoney;
            int liquidatable = portfolioModel.GetForcedLiquidationValue(itemModel, tomsModel.BlacksmithLevel.Value);
            // 在庫の投げ売りと合わせて届くなら金融資産から先に売る（金融のほうが損が小さい）
            if (liquidatable > 0 && currentMoney + liquidatable + DebtCalculator.GetFireSaleValue(itemModel) >= debtAmount)
            {
                int raised = portfolioModel.LiquidateForDebt(shortfall, tomsModel, itemModel, tomsModel.BlacksmithLevel.Value);
                currentMoney = tomsModel.PlayerMoney.Value;
                UnityEngine.Debug.Log($"[Debt] 返済のため金融資産を強制売却: +{raised}G → 所持金 {currentMoney}G");
            }
        }

        // それでも足りなければ在庫を相場の DebtFireSaleRate（既定 60%）で投げ売りする（H-8）。
        // 在庫で大きく張っていた日に返済が来ても、痛いが続けられる失敗にする
        if (currentMoney < debtAmount)
        {
            int fireSale = DebtCalculator.GetFireSaleValue(itemModel);
            if (fireSale > 0 && currentMoney + fireSale >= debtAmount)
            {
                int raised = DebtCalculator.LiquidateInventoryForDebt(debtAmount - currentMoney, tomsModel, itemModel);
                currentMoney = tomsModel.PlayerMoney.Value;
                UnityEngine.Debug.Log($"[Debt] 返済のため在庫を投げ売り: +{raised}G → 所持金 {currentMoney}G");
            }
        }

        return currentMoney;
    }

    private void OnPay()
    {
        if (payingBankLoan)
        {
            OnPayBankLoan();
            return;
        }

        int cycle = tomsModel.DebtCycle.Value + 1;
        int debtAmount = GetDebtAmount(cycle);

        tomsModel.PurchaseItem(debtAmount);
        tomsModel.DebtCycle.Value = cycle;
        DebtCalculator.OnPaid(tomsModel);
        tomsModel.SavePlayerMoney();

        debtView.Hide();

        // 返済報酬: レリックの3択を保留に積む（表示は OnDebtPaid 購読側が行う）
        relicRewardService?.QueueReward($"返済報酬(サイクル{cycle})");
        OnDebtPaid.OnNext(R3.Unit.Default);
    }

    private void OnBankruptcy()
    {
        tomsModel.SavePlayerMoney();
        sceneTransitionService.GoToGameOver();
    }

    // ========================================
    // 村の銀行 Lv5「勝負の融資」（BankLoan）
    // ========================================

    /// <summary>勝負の融資の確認ポップアップを開いているか（朝の通知の待ち合わせ用）。</summary>
    public bool IsBankLoanOfferShowing => bankLoanOfferShowing && popUpManager != null && popUpManager.CurrentData != null;

    /// <summary>
    /// 配信の前日の朝に「勝負の融資」を持ちかける（銀行 Lv5・このランで未使用のときだけ）。出したら true。
    /// 「今回は使わない」なら、この配信の前には出し直さない（次の配信の前日にまた出す）。
    /// </summary>
    public bool TryOfferBankLoan()
    {
        if (popUpManager == null || gameFlowManager == null) return false;
        if (!BankLoan.CanOffer(tomsModel, gameFlowManager)) return false;
        int turn = gameFlowManager.CurrentTurn.Value;
        int battleTurn = turn + gameFlowManager.GetDaysUntilNextStream();
        if (bankLoanOfferedTurn == turn || bankLoanDeclinedBattleTurn == battleTurn) return false;
        if (popUpManager.CurrentData != null) return false; // 別のポップアップの最中は出さない
        bankLoanOfferedTurn = turn;

        int principal = BankLoan.Principal;
        int owed = BankLoan.Repayment;
        int due = BankLoan.DueTurnFor(turn, gameFlowManager.GetDaysUntilNextStream());
        bool final = gameFlowManager.IsUpcomingStreamFinal();
        string dungeon = null;
        var key = gameFlowManager.GetNextBattleDungeon();
        if (key.HasValue && dungeonRepository != null) dungeon = dungeonRepository.GetById(key.Value)?.dungeonName;
        string where = string.IsNullOrEmpty(dungeon) ? "" : $"（{dungeon}）";
        string repayLine = final
            ? $"最後の配信なので、決算で {owed:N0}G を差し引きます。"
            : $"配信の次の日（{DayText.DayOf(due)}）に利息をつけて {owed:N0}G を返します。
返せないときは納付と同じく、手持ちを売って返します。";

        bankLoanOfferShowing = true;
        popUpManager.Show(new PopUpData
        {
            Title = BankLoan.OfferTitle,
            Message = $"明日は配信{where}。
銀行から {principal:N0}G を借りますか？（1ランに1回）
{repayLine}",
            ConfirmButtonText = BankLoan.BorrowButton,
            CancelButtonText = BankLoan.SkipButton,
            Size = PopupSizeEnum.Medium,
            OnConfirm = () =>
            {
                bankLoanOfferShowing = false;
                BankLoan.Take(tomsModel, gameFlowManager);
                OnBankLoanChanged.OnNext(R3.Unit.Default);
            },
            OnCancel = () =>
            {
                bankLoanOfferShowing = false;
                bankLoanDeclinedBattleTurn = battleTurn;
            },
        });
        return true;
    }

    /// <summary>
    /// 勝負の融資の返済日（配信の次の日の朝）に開く。閉じるボタンなし。
    /// 現金が足りなければ納付と同じく売上の前倒し → 金融資産 → 在庫の投げ売りで作り、それでも足りなければ破産。
    /// </summary>
    public void ShowBankLoanRepayment(Func<int> settleTodaySellOrders = null)
    {
        if (!BankLoan.HasOutstanding(tomsModel)) return;
        payingBankLoan = true;
        int owed = tomsModel.BankLoanOwed;
        int cycle = tomsModel.DebtCycle.Value + 1;
        int currentMoney = RaiseCashFor(owed, settleTodaySellOrders);

        if (currentMoney < owed)
            debtView.ShowBankruptcy(owed, currentMoney, cycle, BankLoan.RepayTitle, BankLoanAmountLabel);
        else
            debtView.ShowPayment(owed, currentMoney, cycle, forced: true, BankLoan.RepayTitle, BankLoanAmountLabel);
    }

    private const string BankLoanAmountLabel = "返済額";

    private void OnPayBankLoan()
    {
        payingBankLoan = false;
        if (tomsModel.PlayerMoney.Value < tomsModel.BankLoanOwed) return;
        tomsModel.RepayBankLoan();
        BankLoan.OnRepaid(tomsModel.CurrentTurn.Value);
        debtView.Hide();
        UnityEngine.Debug.Log($"[BankLoan] 勝負の融資を返済 → 所持金 {tomsModel.PlayerMoney.Value}G");
        OnBankLoanChanged.OnNext(R3.Unit.Default);
    }

    public void Dispose() => disposables.Dispose();
}

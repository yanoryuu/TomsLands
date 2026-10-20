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
    private readonly CompositeDisposable disposables = new();

    /// <summary>
    /// 返済が完了したときに発火する（TomsShopPresenter が購読し、
    /// 返済報酬のレリック3択の表示と次回返済表示の更新を行う）。
    /// </summary>
    public Subject<R3.Unit> OnDebtPaid { get; } = new();

    public DebtPresenter(
        DebtView debtView,
        TomsModel tomsModel,
        SceneTransitionService sceneTransitionService,
        PortfolioModel portfolioModel,
        ItemModel itemModel,
        RelicEffectResolver relicResolver,
        RelicRewardService relicRewardService)
    {
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
        int cycle = tomsModel.DebtCycle.Value + 1;
        int debtAmount = GetDebtAmount(cycle);
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

        if (currentMoney < debtAmount)
            debtView.ShowBankruptcy(debtAmount, currentMoney, cycle);
        else
            debtView.ShowPayment(debtAmount, currentMoney, cycle, forced: true);
    }

    private void OnPay()
    {
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

    public void Dispose() => disposables.Dispose();
}

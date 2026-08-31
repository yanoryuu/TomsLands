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
    private readonly CompositeDisposable disposables = new();

    public DebtPresenter(
        DebtView debtView,
        TomsModel tomsModel,
        SceneTransitionService sceneTransitionService,
        PortfolioModel portfolioModel,
        ItemModel itemModel)
    {
        this.debtView = debtView;
        this.tomsModel = tomsModel;
        this.sceneTransitionService = sceneTransitionService;
        this.portfolioModel = portfolioModel;
        this.itemModel = itemModel;
    }

    public void Start()
    {
        Bind();
    }

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
        int debtAmount = GameConst.GetDebtAmount(cycle);
        debtView.ShowPayment(debtAmount, tomsModel.PlayerMoney.Value, cycle, forced: false);
    }

    /// <summary>
    /// 10ターンごとの強制返済で開く。閉じるボタンなし。払えなければゲームオーバー。
    /// </summary>
    public void ShowForced()
    {
        int cycle = tomsModel.DebtCycle.Value + 1;
        int debtAmount = GameConst.GetDebtAmount(cycle);
        int currentMoney = tomsModel.PlayerMoney.Value;

        // 破産判定は現金のみ。ただし現金不足でも金融資産（ファンド・債券）の
        // 強制売却（割増手数料・債券は中途解約ペナルティ）で返済額に届くなら、
        // 自動で売却して救済する。売っても届かない場合のみ破産。
        if (currentMoney < debtAmount && portfolioModel != null)
        {
            int shortfall = debtAmount - currentMoney;
            int liquidatable = portfolioModel.GetForcedLiquidationValue(itemModel, tomsModel.BlacksmithLevel.Value);
            if (liquidatable > 0 && currentMoney + liquidatable >= debtAmount)
            {
                int raised = portfolioModel.LiquidateForDebt(shortfall, tomsModel, itemModel, tomsModel.BlacksmithLevel.Value);
                currentMoney = tomsModel.PlayerMoney.Value;
                UnityEngine.Debug.Log($"[Debt] 返済のため金融資産を強制売却: +{raised}G → 所持金 {currentMoney}G");
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
        int debtAmount = GameConst.GetDebtAmount(cycle);

        tomsModel.PurchaseItem(debtAmount);
        tomsModel.DebtCycle.Value = cycle;
        tomsModel.SavePlayerMoney();

        debtView.Hide();
    }

    private void OnBankruptcy()
    {
        tomsModel.SavePlayerMoney();
        sceneTransitionService.GoToGameOver();
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

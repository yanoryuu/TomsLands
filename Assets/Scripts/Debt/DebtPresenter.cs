using System;
using R3;
using VContainer.Unity;

public class DebtPresenter : IDisposable, IStartable
{
    private readonly DebtView debtView;
    private readonly TomsModel tomsModel;
    private readonly SceneTransitionService sceneTransitionService;
    private readonly CompositeDisposable disposables = new();

    public DebtPresenter(
        DebtView debtView,
        TomsModel tomsModel,
        SceneTransitionService sceneTransitionService)
    {
        this.debtView = debtView;
        this.tomsModel = tomsModel;
        this.sceneTransitionService = sceneTransitionService;
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
        int debtAmount = DebtDataLoader.GetAmount(cycle);
        debtView.ShowPayment(debtAmount, tomsModel.PlayerMoney.Value, cycle, forced: false);
    }

    /// <summary>
    /// 10ターンごとの強制返済で開く。閉じるボタンなし。払えなければゲームオーバー。
    /// </summary>
    public void ShowForced()
    {
        int cycle = tomsModel.DebtCycle.Value + 1;
        int debtAmount = DebtDataLoader.GetAmount(cycle);
        int currentMoney = tomsModel.PlayerMoney.Value;

        if (currentMoney < debtAmount)
            debtView.ShowBankruptcy(debtAmount, currentMoney, cycle);
        else
            debtView.ShowPayment(debtAmount, currentMoney, cycle, forced: true);
    }

    private void OnPay()
    {
        int cycle = tomsModel.DebtCycle.Value + 1;
        int debtAmount = DebtDataLoader.GetAmount(cycle);

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

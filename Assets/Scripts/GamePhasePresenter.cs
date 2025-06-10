using R3;
using System;

public class GamePhasePresenter : IDisposable
{
    private readonly GameManager gameManager;
    private readonly ItemPresenter itemPresenter;
    private readonly ItemShopView itemShopView;
    private readonly PreparationView preparationView;
    private readonly BattleView battleView;
    private readonly EndPhaseView endPhaseView;
    private readonly TomsShopView tomsShopView;

    private readonly CompositeDisposable disposables = new();

    public GamePhasePresenter(
        GameManager gameManager,
        ItemPresenter itemPresenter,
        ItemShopView itemShopView,
        PreparationView preparationView,
        BattleView battleView,
        EndPhaseView endPhaseView,
        TomsShopView tomsShopView)
    {
        this.gameManager = gameManager;
        this.itemPresenter = itemPresenter;
        this.itemShopView = itemShopView;
        this.preparationView = preparationView;
        this.battleView = battleView;
        this.endPhaseView = endPhaseView;
        this.tomsShopView = tomsShopView;

        // GameManagerのフェーズを購読
        gameManager.CurrentPhase
            .Subscribe(OnPhaseChanged)
            .AddTo(disposables);

        // Viewの「次フェーズへ」ボタンを購読
        itemShopView.OnNextPhaseRequested
            .Subscribe(_ => gameManager.ProceedToNextPhase())
            .AddTo(disposables);
    }

    private void OnPhaseChanged(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.Preparation:
                itemPresenter.RefreshPrices(GamePhase.Preparation);
                preparationView.ShowPreparationUI();
                break;
            case GamePhase.Battle:
                itemPresenter.RefreshPrices(GamePhase.Battle);
                battleView.ShowBattleUI();
                break;
            case GamePhase.End:
                endPhaseView.ShowEndPhaseUI();
                break;
            case GamePhase.TomsShop:
                tomsShopView.ShowTomsShopUI();
                break;
        }
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

using R3;
using System;

public class GamePhasePresenter : IDisposable
{
    private readonly GameManager gameManager;
    private readonly ItemPresenter itemPresenter;
    private readonly ItemShopView itemShopView;
    private readonly PreparationView preparationView;
    private readonly StreamingView streamingView;
    private readonly EndPhaseView endPhaseView;
    private readonly TomsShopView tomsShopView;

    private readonly CompositeDisposable disposables = new();

    public GamePhasePresenter(
        GameManager gameManager,
        ItemPresenter itemPresenter,
        ItemShopView itemShopView,
        PreparationView preparationView,
        StreamingView streamingView,
        EndPhaseView endPhaseView,
        TomsShopView tomsShopView)
    {
        this.gameManager = gameManager;
        this.itemPresenter = itemPresenter;
        this.itemShopView = itemShopView;
        this.preparationView = preparationView;
        this.streamingView = streamingView;
        this.endPhaseView = endPhaseView;
        this.tomsShopView = tomsShopView;

        // GameManagerのフェーズを購読
        gameManager.CurrentPhase
            .Subscribe(OnPhaseChanged)
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
            case GamePhase.StreamingSetting:
                
                break;
            case GamePhase.Streaming:
                itemPresenter.RefreshPrices(GamePhase.Streaming);
                streamingView.ShowStreamingUI();
                break;
            case GamePhase.End:
                endPhaseView.ShowEndPhaseUI();
                break;
            case GamePhase.TomsShop:
                tomsShopView.ShowTomsShopUI();
                break;
        }
    }

    public void ChangePhase(GamePhase phase)
    {
        gameManager.CurrentPhase.Value = phase;
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

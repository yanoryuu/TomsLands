using R3;
using System;

public class StateManager : IDisposable
{
    public ReactiveProperty<GamePhase> currentPhase { get; private set; }
    private readonly ItemPresenter itemPresenter;
    private readonly StreamingItemPresenter streamingItemPresenter;
    private readonly ItemShopView itemShopView;
    private readonly PreparationView preparationView;
    private readonly StreamingView streamingView;
    private readonly EndPhaseView endPhaseView;
    private readonly TomsShopView tomsShopView;
    private readonly BattleManager battleManager;
    private readonly TitleView titleView;
    private readonly CompositeDisposable disposables = new();

    public StateManager(
        ItemPresenter itemPresenter,
        StreamingItemPresenter streamingItemPresenter,
        ItemShopView itemShopView,
        PreparationView preparationView,
        StreamingView streamingView,
        EndPhaseView endPhaseView,
        TomsShopView tomsShopView,
        BattleManager battleManager,
        TitleView titleView
        )
    {
        this.itemPresenter = itemPresenter;
        this.itemShopView = itemShopView;
        this.preparationView = preparationView;
        this.streamingView = streamingView;
        this.endPhaseView = endPhaseView;
        this.tomsShopView = tomsShopView;
        this.streamingItemPresenter = streamingItemPresenter;
        this.battleManager = battleManager;
        this.titleView = titleView;
        currentPhase = new ReactiveProperty<GamePhase>(GamePhase.Title);

        Bind();
    }

    private void Bind()
    {
        // GameManagerのフェーズを購読
        currentPhase.Subscribe(OnPhaseChanged)
            .AddTo(disposables);
    }

    private void OnPhaseChanged(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.Title:
                titleView.ShowTitleScreen();
                break;
            case GamePhase.Preparation:
                itemPresenter.RefreshPrices(GamePhase.Preparation);
                preparationView.ShowPreparationUI();
                break;
            case GamePhase.StreamingSetting:
                break;
            case GamePhase.Streaming:
                itemPresenter.RefreshPrices(GamePhase.Streaming);
                streamingItemPresenter.Initialize();
                battleManager.BattleStart();
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
        currentPhase.Value = phase;
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

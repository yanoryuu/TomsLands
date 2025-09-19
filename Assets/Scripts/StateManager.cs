using R3;
using System;
using UnityEngine;

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
    private readonly GamePanelManager gamePanelManager;

    public StateManager(
        ItemPresenter itemPresenter,
        StreamingItemPresenter streamingItemPresenter,
        ItemShopView itemShopView,
        PreparationView preparationView,
        StreamingView streamingView,
        EndPhaseView endPhaseView,
        TomsShopView tomsShopView,
        BattleManager battleManager,
        TitleView titleView,
        GamePanelManager gamePanelManager
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
        this.gamePanelManager = gamePanelManager;

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
                gamePanelManager.ShowPanel(GamePhase.Title);
                
                break;
            case GamePhase.Preparation:
                itemPresenter.RefreshPrices(GamePhase.Preparation);
                gamePanelManager.ShowPanel(GamePhase.Preparation);
                break;
            case GamePhase.StreamingSetting:
                gamePanelManager.ShowPanel(GamePhase.StreamingSetting);
                break;
            case GamePhase.Streaming:
                itemPresenter.RefreshPrices(GamePhase.Streaming);
                streamingItemPresenter.Initialize();
                battleManager.BattleStart();
                gamePanelManager.ShowPanel(GamePhase.Streaming);
                break;
            case GamePhase.End:
                gamePanelManager.ShowPanel(GamePhase.End);
                break;
            case GamePhase.TomsShop:
                gamePanelManager.ShowPanel(GamePhase.TomsShop);
                break;
        }
    }

    public void ChangePhase(GamePhase phase)
    {
        currentPhase.Value = phase;
        Debug.Log($"Phase changed to {phase}");
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

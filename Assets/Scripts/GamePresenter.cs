using R3;
using System;
using UnityEngine;

public class GamePhasePresenter : IDisposable
{ 
    [SerializeField] private GameManager gameManager;
    [SerializeField] private ItemPresenter itemPresenter;
    [SerializeField] private ItemShopView itemShopView;
    [SerializeField] private PreparationView preparationView;
    [SerializeField] private BattleView battleView;
    [SerializeField] private EndPhaseView endPhaseView;
    [SerializeField] private TomsShopView tomsShopView;

    private readonly CompositeDisposable disposables = new CompositeDisposable();

    public GamePhasePresenter(GameManager gameManager, ItemPresenter itemPresenter, ItemShopView itemShopView)
    {
        this.gameManager = gameManager;
        this.itemPresenter = itemPresenter;
        this.itemShopView = itemShopView;

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
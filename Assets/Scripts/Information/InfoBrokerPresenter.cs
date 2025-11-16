
using System;
using R3;

public class InfoBrokerPresenter : IDisposable,IPresenter
{
    private readonly InfoBrokerModel infoBrokerModel;
    private readonly InfoBrokerView infoBrokerView;
    private readonly ItemModel itemModel;
    private readonly CompositeDisposable disposables = new();
    private readonly StateManager stateManager;
    public InfoBrokerPresenter(InfoBrokerModel infoBrokerModel, InfoBrokerView infoBrokerView, ItemModel itemModel,StateManager stateManager)
    {
        this.infoBrokerModel = infoBrokerModel;
        this.infoBrokerView = infoBrokerView;
        this.itemModel = itemModel;
        this.stateManager = stateManager;
        stateManager.RegisterOnEnter(GamePhase.InfoBroker,Entry);
        Bind();
    }

    
    public void Entry()
    {
        
    }
    private void Bind()
    {
        
        // ビューのイベント購読
        infoBrokerView.OnCloseRequested
            .Subscribe(_ => stateManager.ChangePhase(GamePhase.TomsShop))
            .AddTo(disposables);

        infoBrokerView.OnRefreshRequested
            .Subscribe(_ => infoBrokerModel.UpdateInfoMessages())
            .AddTo(disposables);

        // モデルの変更を監視
        infoBrokerModel.CurrentInfoMessages
            .Subscribe(messages => infoBrokerView.DisplayInfoMessages(messages))
            .AddTo(disposables);
    }

    public void ShowInfoBroker()
    {
        infoBrokerModel.UpdateInfoMessages();
    }

    public void RecordHeroPurchase(string itemId, int quantity, int price)
    {
        infoBrokerModel.RecordHeroPurchase(itemId, quantity, price);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

using System;
using R3;

public class InfoBrokerPresenter : IDisposable
{
    private readonly InfoBrokerModel infoBrokerModel;
    private readonly InfoBrokerView infoBrokerView;
    private readonly ItemModel itemModel;
    private readonly CompositeDisposable disposables = new();

    public InfoBrokerPresenter(InfoBrokerModel model, InfoBrokerView view, ItemModel itemModel)
    {
        this.infoBrokerModel = model;
        this.infoBrokerView = view;
        this.itemModel = itemModel;

        // ビューのイベント購読
        infoBrokerView.OnCloseRequested
            .Subscribe(_ => infoBrokerView.Hide())
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
        infoBrokerView.Show();
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

using System;
using R3;
using VContainer.Unity;

public class InfoBrokerPresenter : IDisposable,IPresenter,IStartable
{
    private readonly InfoBrokerModel infoBrokerModel;
    private readonly InfoBrokerView infoBrokerView;
    private readonly ItemModel itemModel;
    private readonly CompositeDisposable disposables = new();
    private readonly StateManager stateManager;
    private readonly HeroInfoView heroInfoView;
    private readonly HeroModel heroModel;
    public InfoBrokerPresenter(InfoBrokerModel infoBrokerModel, InfoBrokerView infoBrokerView, ItemModel itemModel,StateManager stateManager
    , HeroInfoView heroInfoView,HeroModel heroModel)
    {
        this.infoBrokerModel = infoBrokerModel;
        this.infoBrokerView = infoBrokerView;
        this.itemModel = itemModel;
        this.stateManager = stateManager;
        this.heroInfoView = heroInfoView;
        stateManager.RegisterOnEnter(GamePhase.InfoBroker,Entry);
    }

    public void Start()
    {
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
        
        heroInfoView.OnPurchaseButtonClicked.Subscribe(_ =>
        {
            UpdateHeroInfo();
        })
        .AddTo(disposables);
    }
    
    public void UpdateHeroInfo()
    {
        heroInfoView.UpdateHeroInfo(infoBrokerModel.currentHeroData);
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
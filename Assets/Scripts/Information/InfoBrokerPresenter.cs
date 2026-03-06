using System;
using UnityEngine;
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
    private readonly MapInfoView mapInfoView;
    public InfoBrokerPresenter(InfoBrokerModel infoBrokerModel, InfoBrokerView infoBrokerView, ItemModel itemModel,StateManager stateManager
    , HeroInfoView heroInfoView,HeroModel heroModel, MapInfoView mapInfoView)
    {
        this.infoBrokerModel = infoBrokerModel;
        this.infoBrokerView = infoBrokerView;
        this.itemModel = itemModel;
        this.stateManager = stateManager;
        this.heroInfoView = heroInfoView;
        this.mapInfoView = mapInfoView;
        stateManager.RegisterOnEnter(GamePhase.InfoBroker,Entry);
    }

    public void Start()
    {
        Bind();
    }

    
    public void Entry()
    {
        // 初期タブ（地図）を表示
        infoBrokerView.ShowPanel(InfoBrokerTab.Map);
        infoBrokerView.SortItemTab(InfoBrokerTab.Map);
        OnTabChanged(InfoBrokerTab.Map);
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

        // // タブ切り替えイベント
        infoBrokerView.OnChangePanel
            .Subscribe(tab =>
            {
                infoBrokerView.SortItemTab(tab);
                infoBrokerView.ShowPanel(tab);
                OnTabChanged(tab);
            })
            .AddTo(disposables);

        // // [Guess] モデルの変更を監視
        // infoBrokerModel.CurrentInfoMessages
        //     .Subscribe(messages => infoBrokerView.DisplayInfoMessages(messages))
        //     .AddTo(disposables);
        
        heroInfoView.OnPurchaseButtonClicked.Subscribe(_ =>
        {
            // 勇者情報を購入
            infoBrokerModel.PurchaseHeroInfo();
            // 表示を更新
            UpdateHeroInfo();
        })
        .AddTo(disposables);
        
        // マップ情報購入イベント
        mapInfoView.OnMapPurchaseClicked
            .Subscribe(dungeonName =>
            {
                infoBrokerModel.PurchaseDungeonInfo(dungeonName);
            })
            .AddTo(disposables);
    }

    /// <summary>
    /// タブ切り替え時の処理
    /// </summary>
    private void OnTabChanged(InfoBrokerTab tab)
    {
        switch (tab)
        {
            case InfoBrokerTab.Map:
                mapInfoView.SetMapSlot(
                    infoBrokerModel.availableDungeons,
                    infoBrokerModel.GetDungeonInfoCosts(),
                    1);
                break;
            case InfoBrokerTab.Hero:
                UpdateHeroInfo();
                break;
            // case InfoBrokerTab.Guess:
            //     infoBrokerModel.UpdateInfoMessages();
            //     break;
        }
    }
    
    public void UpdateHeroInfo()
    {
        // 最新の勇者データを取得
        infoBrokerModel.RefreshHeroData();
        
        if (infoBrokerModel.currentHeroData == null)
        {
            Debug.LogWarning("currentHeroData is null. Cannot update hero info.");
            return;
        }
        
        // 購入済みフラグと共に表示を更新
        heroInfoView.UpdateHeroInfo(infoBrokerModel.currentHeroData, infoBrokerModel.IsHeroInfoPurchased);
    }

    // public void ShowInfoBroker()
    // {
    //     infoBrokerModel.UpdateInfoMessages();
    // }

    public void RecordHeroPurchase(string itemId, int quantity, int price)
    {
        infoBrokerModel.RecordHeroPurchase(itemId, quantity, price);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
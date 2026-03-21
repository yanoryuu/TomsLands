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
    private readonly TomsModel tomsModel;
    public InfoBrokerPresenter(InfoBrokerModel infoBrokerModel, InfoBrokerView infoBrokerView, ItemModel itemModel,StateManager stateManager
    , HeroInfoView heroInfoView,HeroModel heroModel, MapInfoView mapInfoView, TomsModel tomsModel)
    {
        this.infoBrokerModel = infoBrokerModel;
        this.infoBrokerView = infoBrokerView;
        this.itemModel = itemModel;
        this.stateManager = stateManager;
        this.heroInfoView = heroInfoView;
        this.mapInfoView = mapInfoView;
        this.tomsModel = tomsModel;
        stateManager.RegisterOnEnter(TomsShopGamePhase.Broker,Entry);
    }

    public void Start()
    {
        Bind();
    }

    
    public void Entry()
    {
        // ダンジョンデータを最新の状態に更新
        infoBrokerModel.InitializeDungeons();
        
        // 初期タブ（マップ）を表示
        infoBrokerView.ShowPanel(InfoBrokerTab.Map);
        infoBrokerView.SortItemTab(InfoBrokerTab.Map);
        OnTabChanged(InfoBrokerTab.Map);
    }
    private void Bind()
    {
        // ?r???[??C?x???g?w??
        infoBrokerView.OnCloseRequested
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposables);

        infoBrokerView.OnRefreshRequested
            .Subscribe(_ => infoBrokerModel.UpdateInfoMessages())
            .AddTo(disposables);

        // // ?^?u??????C?x???g
        infoBrokerView.OnChangePanel
            .Subscribe(tab =>
            {
                infoBrokerView.SortItemTab(tab);
                infoBrokerView.ShowPanel(tab);
                OnTabChanged(tab);
            })
            .AddTo(disposables);

        // // [Guess] ???f?????X?????
        // infoBrokerModel.CurrentInfoMessages
        //     .Subscribe(messages => infoBrokerView.DisplayInfoMessages(messages))
        //     .AddTo(disposables);
        
        heroInfoView.OnPurchaseButtonClicked.Subscribe(_ =>
        {
            // ?E??????w??
            infoBrokerModel.PurchaseHeroInfo();
            // ?\?????X?V
            UpdateHeroInfo();
        })
        .AddTo(disposables);
        
        // マップ情報の購入イベント
        mapInfoView.OnMapPurchaseClicked
            .Subscribe(dungeonName =>
            {
                var costs = infoBrokerModel.GetDungeonInfoCosts();
                if (!costs.ContainsKey(dungeonName))
                {
                    Debug.LogWarning($"[InfoBrokerPresenter] コストが見つかりません: {dungeonName}");
                    return;
                }

                int currentTurn = tomsModel.CurrentTurn.Value;
                int[] costArray = costs[dungeonName];
                int cost = (costArray.Length >= currentTurn && currentTurn > 0)
                    ? costArray[currentTurn - 1]
                    : costArray[costArray.Length - 1];

                if (tomsModel.PlayerMoney.Value < cost)
                {
                    Debug.Log($"[InfoBrokerPresenter] お金が足りません！ 必要: {cost}G, 所持: {tomsModel.PlayerMoney.Value}G");
                    return;
                }

                // 所持金を減らす
                tomsModel.PurchaseItem(cost);
                // ダンジョン情報を購入済みにする
                infoBrokerModel.PurchaseDungeonInfo(dungeonName);
                Debug.Log($"[InfoBrokerPresenter] {dungeonName} の情報を {cost}G で購入しました。残金: {tomsModel.PlayerMoney.Value}G");
            })
            .AddTo(disposables);
    }

    /// <summary>
    /// ?^?u????????????
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
        // ??V??E??f?[?^?????
        infoBrokerModel.RefreshHeroData();
        
        if (infoBrokerModel.currentHeroData == null)
        {
            Debug.LogWarning("currentHeroData is null. Cannot update hero info.");
            return;
        }
        
        // ?w?????t???O?????\?????X?V
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
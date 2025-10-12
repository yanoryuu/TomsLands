using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

public class BlackSmithPresenter : IDisposable, IPresenter
{
    private readonly BlackSmithModel blackSmithModel;
    private readonly ItemModel itemModel;
    private readonly BlackSmithView blackSmithView;
    private readonly StateManager stateManager;
    private readonly TomsShopModel tomsShopModel;
    
    private readonly CompositeDisposable disposables = new();
    public BlackSmithPresenter(TomsShopModel tomsShopModel, ItemModel itemModel, BlackSmithView blackSmithView, StateManager stateManager,BlackSmithModel blackSmithModel)
    {
        this.blackSmithModel = blackSmithModel;
        this.tomsShopModel = tomsShopModel;
        this.itemModel = itemModel;
        this.blackSmithView = blackSmithView;
        this.stateManager = stateManager;
        Bind();
    }
    
    public void Entry()
    {
        //ここにこの画面に移動した時にここを呼び出す。
    }

    public void Bind()
    {
        blackSmithView.OnCloseRequested.Subscribe(_ =>
        {
            stateManager.ChangePhase(GamePhase.TomsShop);
        }).AddTo(disposables);

        blackSmithView.OnWeaponPanelRequested.Subscribe(_ =>
        {
            // ChangePurchasePanel();
        }).AddTo(disposables);
    }

    private void HandlePurchase(string itemId, int quantity)
    {
        Debug.Log(itemId);
        var item = itemModel.GetRuntimeItem(itemId);
        int totalPrice = item.CurrentPrice.Value * quantity;
        if (tomsShopModel.PlayerMoney.Value >= totalPrice)
        {
            tomsShopModel.PlayerMoney.Value -= totalPrice;
            itemModel.PurchaseItem(itemId, quantity);
        }
        else
        {
            Debug.Log("お金が足りません！");
        }
    }
    
    private void ChangePurchasePanel(List<RuntimeItemData> items,BlackSmithTab itemType)
    {
        var itemSlots = blackSmithView.PopulateItemList(items);
        
        foreach (var slot  in itemSlots)
        {
            var itemdata = itemModel.GetRuntimeItem(slot.itemId);

            //アイテムの情報に保存されているストック情報をuIに反映
            itemdata.DisplayStock.Subscribe(x =>
                {
                    slot.SetDisplayQuantity(x);
                })
                .AddTo(disposables);

            //99から現在の所持ストックを引いた数が残り買える個数
            itemdata.Stock.Subscribe(x =>
                {
                    slot.SetMaxDisplayQuantity(x);
                })
                .AddTo(disposables);
            
            //購入の数がUI側で変更されれば変更
            slot.OnDisplayQuantityChanged.Subscribe(x =>
                {
                    
                })
                .AddTo(disposables);
            
            //アイテムの現在の売値価格
            itemdata.CurrentPrice.Subscribe(x =>
                {
                    slot.SetPrice(x);
                })
                .AddTo(disposables);
            
            //アイテムの説明を
            slot.OnInfoRequested.Subscribe(id =>
                {
                    blackSmithView.SetDescription(itemModel.GetRuntimeItem(id).ItemDescription);
                })
                .AddTo(disposables);
        }
        
        blackSmithView.SortItemTab(itemType);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
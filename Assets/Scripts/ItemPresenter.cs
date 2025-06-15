using R3;
using UnityEngine;
using System.Collections.Generic;

public class ItemPresenter
{
    private readonly ItemModel itemModel;
    private readonly TomsShopModel tomsShopModel;
    private readonly ItemShopView itemShopView;
    private readonly TomsShopView tomsShopView;
    private readonly CompositeDisposable disposables = new CompositeDisposable();

    public ItemPresenter(ItemModel itemModel, ItemShopView itemShopView, TomsShopView tomsShopView,TomsShopModel tomsShopModel)
    {
        this.itemModel = itemModel;
        this.itemShopView = itemShopView;
        this.tomsShopView = tomsShopView;
        this.tomsShopModel = tomsShopModel;

        // 購入イベント購読
        itemShopView.OnPurchaseRequested
            .Subscribe(tuple => HandlePurchase(tuple.itemId, tuple.quantity))
            .AddTo(disposables);

        // 売却イベント購読
        itemShopView.OnSellRequested
            .Subscribe(tuple => HandleSell(tuple.itemId, tuple.quantity))
            .AddTo(disposables);

        // 所持金更新（ModelのデータからViewへ）
        tomsShopModel.PlayerMoney
            .Subscribe(money =>
            {
                itemShopView.UpdatePlayerMoney(money);
                tomsShopView.UpdatePlayerMoney(money);
            })
            .AddTo(disposables);
    }
    public void BindItemSelectionView(ItemSelectionView itemSelectionView)
    {
        itemSelectionView.OnConfirmSelection
            .Subscribe(selectedItems =>
            {
                itemModel.SetShopItemList(selectedItems);
            })
            .AddTo(disposables);
    }

    private void HandlePurchase(string itemId, int quantity)
    {
        itemModel.PurchaseItem(itemId, quantity);
    }

    private void HandleSell(string itemId, int quantity)
    {
        itemModel.SellItem(itemId, quantity);
    }

    public void RefreshPrices(GamePhase phase)
    {
        itemModel.UpdateItemPrices(phase);
        itemShopView.PopulateItemList(itemModel.RuntimeItems);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
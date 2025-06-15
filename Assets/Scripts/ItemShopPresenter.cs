using System;
using R3;

public class ItemShopPresenter : IDisposable
{
    private readonly TomsShopModel tomsShopModel;
    private readonly ItemModel itemModel;
    private readonly ItemShopView itemShopView;
    private readonly CompositeDisposable disposables = new();

    public ItemShopPresenter(TomsShopModel tomsShopModel, ItemModel itemModel, ItemShopView itemShopView)
    {
        this.tomsShopModel = tomsShopModel;
        this.itemModel = itemModel;
        this.itemShopView = itemShopView;

        itemShopView.OnPurchaseRequested
            .Subscribe(tuple => HandlePurchase(tuple.itemId, tuple.quantity))
            .AddTo(disposables);

        itemShopView.OnSellRequested
            .Subscribe(tuple => HandleSell(tuple.itemId, tuple.quantity))
            .AddTo(disposables);
    }

    private void HandlePurchase(string itemId, int quantity)
    {
        var item = itemModel.GetRuntimeItem(itemId);
        int totalPrice = item.CurrentPrice.Value * quantity;
        if (tomsShopModel.PlayerMoney.Value >= totalPrice)
        {
            tomsShopModel.PlayerMoney.Value -= totalPrice;
            itemModel.PurchaseItem(itemId, quantity);
        }
        else
        {
            UnityEngine.Debug.Log("お金が足りません！");
        }
    }

    private void HandleSell(string itemId, int quantity)
    {
        var item = itemModel.GetRuntimeItem(itemId);
        if (item != null && item.Stock.Value >= quantity)
        {
            itemModel.SellItem(itemId, quantity);
            tomsShopModel.PlayerMoney.Value += item.CurrentPrice.Value * quantity;
        }
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
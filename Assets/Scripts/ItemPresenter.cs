using R3;
using UnityEngine;

public class ItemPresenter
{
    private readonly ItemModel itemModel;
    private readonly ItemShopView itemShopView;
    private readonly CompositeDisposable disposables = new CompositeDisposable();

    public ItemPresenter(ItemModel itemModel, ItemShopView itemShopView)
    {
        this.itemModel = itemModel;
        this.itemShopView = itemShopView;

        // 購入イベント購読
        itemShopView.OnPurchaseRequested
            .Subscribe(tuple => HandlePurchase(tuple.itemId, tuple.quantity))
            .AddTo(disposables);

        // 売却イベント購読
        itemShopView.OnSellRequested
            .Subscribe(tuple => HandleSell(tuple.itemId, tuple.quantity))
            .AddTo(disposables);

        // 所持金更新（ModelのデータからViewへ）
        itemModel.PlayerMoney
            .Subscribe(money => itemShopView.UpdatePlayerMoney(money))
            .AddTo(disposables);
    }

    private void HandlePurchase(string itemId, int quantity)
    {
        itemModel.PurchaseItem(itemId, quantity);
        // 必要なら価格再計算も呼ぶ
    }

    private void HandleSell(string itemId, int quantity)
    {
        itemModel.SellItem(itemId, quantity);
    }

    public void RefreshPrices(GamePhase phase)
    {
        itemModel.UpdateItemPrices(phase);
        itemShopView.PopulateItemList(itemModel.RuntimeItems, itemModel);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
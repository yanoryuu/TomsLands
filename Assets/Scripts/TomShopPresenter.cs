using System;
using R3;

public class TomShopPresenter : IDisposable
{
    private readonly GameManager gameManager;
    private readonly TomsShopView tomsShopView;
    private readonly ItemShopView itemShopView;
    private readonly ItemSelectionView itemSelectionView;
    private readonly ItemModel itemModel;

    private readonly CompositeDisposable disposables = new();

    public TomShopPresenter(
        GameManager gameManager,
        TomsShopView tomsShopView,
        ItemShopView itemShopView,
        ItemSelectionView itemSelectionView,
        ItemModel itemModel)
    {
        this.gameManager = gameManager;
        this.tomsShopView = tomsShopView;
        this.itemShopView = itemShopView;
        this.itemSelectionView = itemSelectionView;
        this.itemModel = itemModel;

        // 購入ボタン
        tomsShopView.OnPurchaseClicked
            .Subscribe(_ =>
            {
                tomsShopView.HideTomsShopUI();
                itemShopView.Show();
            })
            .AddTo(disposables);

        // 店頭商品設定ボタン
        tomsShopView.OnSetItemClicked
            .Subscribe(_ =>
            {
                itemSelectionView.PopulateItemList(itemModel.RuntimeItems);
                tomsShopView.HideTomsShopUI();
                itemSelectionView.Show();
            })
            .AddTo(disposables);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
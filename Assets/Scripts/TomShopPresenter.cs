using System;
using R3;

public class TomShopPresenter : IDisposable
{
    private readonly TomsShopView tomsShopView;
    private readonly ItemShopView itemShopView;
    private readonly ItemSelectionView itemSelectionView;
    private readonly ItemModel itemModel;
    private readonly CompositeDisposable disposables = new();

    public TomShopPresenter(
        TomsShopView tomsShopView,
        ItemShopView itemShopView,
        ItemSelectionView itemSelectionView,
        ItemModel itemModel)
    {
        this.tomsShopView = tomsShopView;
        this.itemShopView = itemShopView;
        this.itemSelectionView = itemSelectionView;
        this.itemModel = itemModel;

        // 「仕入れ」ボタン
        tomsShopView.OnPurchaseClicked
            .Subscribe(_ =>
            {
                itemShopView.PopulateItemList(itemModel.RuntimeItems);
                itemShopView.Show();
            })
            .AddTo(disposables);

        // 「陳列設定」ボタン
        tomsShopView.OnSetItemClicked
            .Subscribe(_ =>
            {
                itemSelectionView.PopulateItemList(itemModel.RuntimeItems);
                
                itemSelectionView.OnConfirmSelection
                    .Take(1) // 一度だけ受け取る
                    .Subscribe(selectedItems =>
                    {
                        itemModel.SetDisplayItemList(selectedItems);
                        itemShopView.PopulateItemList(itemModel.DisplayItemList);
                    })
                    .AddTo(disposables);

                itemSelectionView.Show();
            })
            .AddTo(disposables);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
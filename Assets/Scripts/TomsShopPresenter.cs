using System;
using R3;

public class TomsShopPresenter : IDisposable
{
    private readonly TomsShopView tomsShopView;
    private readonly ItemShopView itemShopView;
    private readonly ItemSelectionView itemSelectionView;
    private readonly ItemModel itemModel;
    private readonly TomsShopModel tomsShopModel;
    private readonly CompositeDisposable disposables = new();

    public TomsShopPresenter(
        TomsShopView tomsShopView,
        ItemShopView itemShopView,
        ItemSelectionView itemSelectionView,
        ItemModel itemModel,
        TomsShopModel tomsShopModel)
    {
        this.tomsShopView = tomsShopView;
        this.itemShopView = itemShopView;
        this.itemSelectionView = itemSelectionView;
        this.itemModel = itemModel;
        this.tomsShopModel = tomsShopModel;

        // 「仕入れ」ボタン
        tomsShopView.OnPurchaseClicked
            .Subscribe(_ => OpenPurchase())
            .AddTo(disposables);

        // 「陳列設定」ボタン
        tomsShopView.OnSetItemClicked
            .Subscribe(_ => OpenSetItem())
            .AddTo(disposables);

        // 陳列設定確定
        itemSelectionView.OnConfirmSelection
            .Subscribe(selectedItems =>
            {
                itemModel.CreateItemListForDisplay(selectedItems);
                itemShopView.PopulateItemList(itemModel.CreateItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Weapon),itemShopView.BlackSmithWeaponParent);
            })
            .AddTo(disposables);

        // 閉じるボタン系
        itemSelectionView.OnCloseRequested
            .Subscribe(_ => CloseSelectionView())
            .AddTo(disposables);

        itemShopView.OnCloseRequested
            .Subscribe(_ => CloseShopView())
            .AddTo(disposables);
    }

    private void OpenPurchase()
    {
        itemShopView.PopulateItemList(itemModel.RuntimeItems,itemShopView.BlackSmithWeaponParent);
        itemShopView.PopulateItemList(itemModel.RuntimeItems, itemShopView.BlackSmithArmorParent);
        itemShopView.PopulateItemList(itemModel.RuntimeItems,itemShopView.ToolParent);
        itemShopView.Show();
        itemSelectionView.Hide();
        tomsShopView.HideTomsShopUI();
    }

    private void OpenSetItem()
    {
        itemSelectionView.PopulateItemList(itemModel.RuntimeItems);
        itemSelectionView.Show();
        itemShopView.Hide();
        tomsShopView.HideTomsShopUI();
    }

    private void CloseSelectionView()
    {
        itemSelectionView.Hide();
        tomsShopView.ShowTomsShopUI();
        itemModel.SaveData();
    }

    private void CloseShopView()
    {
        itemShopView.Hide();
        tomsShopView.ShowTomsShopUI();
        itemModel.SaveData();
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

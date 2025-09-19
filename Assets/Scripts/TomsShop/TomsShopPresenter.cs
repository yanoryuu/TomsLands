using System;
using R3;

public class TomsShopPresenter : IDisposable, IPresenter
{
    private readonly TomsShopView tomsShopView;
    private readonly ItemShopView itemShopView;
    private readonly ItemSelectionView itemSelectionView;
    private readonly ItemModel itemModel;
    private readonly TomsShopModel tomsShopModel;
    private readonly CompositeDisposable disposables = new();
    private readonly CommonView commonView;

    public TomsShopPresenter(
        TomsShopView tomsShopView,
        ItemShopView itemShopView,
        ItemSelectionView itemSelectionView,
        ItemModel itemModel,
        TomsShopModel tomsShopModel,
        CommonView commonView)
    {
        this.tomsShopView = tomsShopView;
        this.itemShopView = itemShopView;
        this.itemSelectionView = itemSelectionView;
        this.itemModel = itemModel;
        this.tomsShopModel = tomsShopModel;
        this.commonView = commonView;
        
        Bind();
    }

    private void Bind()
    {
        tomsShopView.OnPurchaseClicked
            .Subscribe(_ => OpenPurchase())
            .AddTo(disposables);

        // 「陳列設定」ボタン
        // tomsShopView.OnSetItemClicked
        //     .Subscribe(_ => OpenSetItem())
        //     .AddTo(disposables);

        // 陳列設定確定
        itemSelectionView.OnConfirmSelection
            .Subscribe(selectedItems =>
            {
                itemModel.CreateItemListForDisplay(selectedItems);
                // itemShopView.PopulateItemList(itemModel.CreateItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Weapon),itemShopView.BlackSmithWeaponParent);
            })
            .AddTo(disposables);

        // 閉じるボタン系
        itemSelectionView.OnCloseRequested
            .Subscribe(_ => CloseSelectionView())
            .AddTo(disposables);

        itemShopView.OnCloseRequested
            .Subscribe(_ => CloseShopView())
            .AddTo(disposables);
        
        // 所持金更新（ModelのデータからViewへ）
        tomsShopModel.PlayerMoney
            .Subscribe(money =>
            {
                commonView.UpdatePlayerMoney(money);
            })
            .AddTo(disposables);
        
        tomsShopModel.CurrentTurn.Subscribe(date =>
            {
                commonView.UpdateCurrentTurn(date);
            })
            .AddTo(disposables);
    }
    
    public void Entry()
    {
        //ここにこの画面に移動した時にここを呼び出す。
    }

    private void OpenPurchase()
    {
        itemShopView.PopulateItemList(itemModel.CreateItemRuntimeList(itemModel.RuntimeItems,ItemTypeData.ItemType.Weapon,tomsShopModel.BlacksmithLevel.Value),itemShopView.BlackSmithWeaponParent);
        itemShopView.BlackSmithWeaponPanel.SetActive(true);
        itemShopView.BlackSmithArmorPanel.SetActive(false);
        itemShopView.ToolPanel.SetActive(false);
        itemShopView.Show();
        itemSelectionView.Hide();
        // tomsShopView.HideTomsShopUI();
    }

    private void OpenSetItem()
    {
        itemSelectionView.PopulateItemList(itemModel.RuntimeItems);
        itemSelectionView.Show();
        itemShopView.Hide();
        // tomsShopView.HideTomsShopUI();
    }

    private void CloseSelectionView()
    {
        itemSelectionView.Hide();
        // tomsShopView.ShowTomsShopUI();
        itemModel.SaveData();
    }

    private void CloseShopView()
    {
        itemShopView.Hide();
        // tomsShopView.ShowTomsShopUI();
        itemModel.SaveData();
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
    
    public void LevelUpBlacksmith()
    {
        tomsShopModel.BlacksmithLevel.Value++;
        itemShopView.PopulateItemList(itemModel.CreateItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Weapon, tomsShopModel.BlacksmithLevel.Value), itemShopView.BlackSmithWeaponParent);
    }
}

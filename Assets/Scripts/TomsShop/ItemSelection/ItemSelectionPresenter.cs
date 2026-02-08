using System;
using System.Collections.Generic;
using R3;
using VContainer.Unity;

public class ItemSelectionPresenter :IDisposable,IStartable
{
    private readonly ItemSelectionModel selectionModel;
    private readonly ItemSelectionView selectionView;
    private readonly ItemModel itemModel;
    private readonly TomsModel tomsModel;
    private readonly CompositeDisposable disposables = new();

    public ItemSelectionPresenter(ItemSelectionModel selectionModel, ItemSelectionView selectionView, ItemModel itemModel,TomsModel tomsModel)
    {
        this.selectionModel = selectionModel;
        this.selectionView = selectionView;
        this.itemModel = itemModel;
        this.tomsModel = tomsModel;
        
    }
    
    public void Start()
    {
        Bind();
    }

    public void OnOpenSelectionPanel()
    {
        selectionView.Show();
        SetRuntimeItems();
    }

    //画面を閉じるとき
    public void OnCloseSelectionPanel()
    {
        selectionView.Hide();
        itemModel.SaveData();
    }

    public void Initialize()
    {
        
    }
    
    private void Bind()
    {
        //陳列アイテムの表示を閉じる
        selectionView.OnCloseRequested
            .Subscribe(_ => OnCloseSelectionPanel())
            .AddTo(disposables);

        //防具画面を表示
        selectionView.OnArmorPanelRequested
            .Subscribe(_ =>
            {
                ChangeSelectionPanel(selectionModel.AarmorRuntimeItems , ItemTypeData.ItemType.Armor);
            })
            .AddTo(disposables);
        
        //武器画面を表示
        selectionView.OnWeaponPanelRequested
            .Subscribe(_ =>
            {
                ChangeSelectionPanel(selectionModel.WeaponRuntimeItems,ItemTypeData.ItemType.Weapon);
            })
            .AddTo(disposables);
    }
    

    private void ChangeSelectionPanel(List<RuntimeItemData> items,ItemTypeData.ItemType itemType)
    {
        var itemSlots = selectionView.PopulateItemList(items);
        
        foreach (var slot  in itemSlots)
        {
            var itemdata = itemModel.GetRuntimeItem(slot.itemId);

            //アイテムの情報に保存されているストック情報をuIに反映
            itemdata.DisplayStock.Subscribe(x =>
                {
                    slot.SetDisplayQuantity(x);
                })
                .AddTo(disposables);

            //現在の所持ストックがSlotの最大品出し数
            itemdata.Stock.Subscribe(x =>
                {
                    slot.SetMaxDisplayQuantity(x);
                })
                .AddTo(disposables);
            
            //品出し用の数がUI側で変更されれば変更
            slot.OnDisplayQuantityChanged.Subscribe(x =>
                {
                    itemdata.UpdateDisplayStock(x);
                })
                .AddTo(disposables);

            //スロットのトグルが変更されれば変更
            slot.OnToggleChanged.Subscribe(x =>
                {
                    itemdata.UpdateIsDisplay(x);
                })
                .AddTo(disposables);

            //アイテムデータのトグルとUIを同期
            itemdata.IsDisplay.Subscribe(x =>
                {
                    slot.SetSelectToggle(x);
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
                    selectionView.SetDescription(itemModel.GetRuntimeItem(id).ItemDescription,itemModel.GetRuntimeItem(id).ItemIcon);
                })
                .AddTo(disposables);
        }
        
        selectionView.SortItemTab(itemType);
    }

    private void SetRuntimeItems()
    {
        var isStockRuntimeItems = itemModel.PickItemRuntimeListForStock(itemModel.RuntimeItems);

        var armorRuntimeItems = itemModel.PickItemRuntimeList(isStockRuntimeItems, ItemTypeData.ItemType.Armor,
            tomsModel.BlacksmithLevel.Value);

        var weaponRuntimeItems = itemModel.PickItemRuntimeList(isStockRuntimeItems, ItemTypeData.ItemType.Weapon,
            tomsModel.BlacksmithLevel.Value);
        
        selectionModel.SetRuntimeItems(armorRuntimeItems,weaponRuntimeItems);
        
        ChangeSelectionPanel(weaponRuntimeItems,ItemTypeData.ItemType.Weapon);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
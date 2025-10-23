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
    private readonly TomsModel tomsModel;

    private readonly CompositeDisposable disposables = new();
    private CompositeDisposable panelDisposables = new();

    public BlackSmithPresenter(
        TomsModel tomsModel,
        ItemModel itemModel,
        BlackSmithView blackSmithView,
        StateManager stateManager,
        BlackSmithModel blackSmithModel)
    {
        this.blackSmithModel = blackSmithModel;
        this.tomsModel = tomsModel;
        this.itemModel = itemModel;
        this.blackSmithView = blackSmithView;
        this.stateManager = stateManager;
        
        stateManager.RegisterOnEnter(GamePhase.BlackSmith, Entry);
        Bind();
    }

    public void Entry()
    {
        blackSmithModel.SetRuntimeItems(
            itemModel.PickItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Weapon, tomsModel.BlacksmithLevel.Value),
            itemModel.PickItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Armor, tomsModel.BlacksmithLevel.Value)
        );
        ChangePurchasePanel(blackSmithModel.weaponRuntimeItems, BlackSmithTab.Weapon);
    }

    private void Bind()
    {
        blackSmithView.OnCloseRequested.Subscribe(_ =>
        {
            stateManager.ChangePhase(GamePhase.TomsShop);
        }).AddTo(disposables);

        blackSmithView.OnChangePanel
            .Subscribe(type =>
            {
                switch (type)
                {
                    case BlackSmithTab.Weapon:
                        ChangePurchasePanel(blackSmithModel.weaponRuntimeItems, type);
                        break;
                    case BlackSmithTab.Armor:
                        ChangePurchasePanel(blackSmithModel.armorRuntimeItems, type);
                        break;
                    case BlackSmithTab.Development:
                        ShowDevelopmentPanel();
                        break;
                }
            })
            .AddTo(disposables);
    }

    private void HandlePurchase(string itemId, int quantity)
    {
        var item = itemModel.GetRuntimeItem(itemId);
        int totalPrice = item.CurrentPrice.Value * quantity;
        
        if (tomsModel.PlayerMoney.Value >= totalPrice)
        {
            Debug.Log($"{totalPrice}ゴールドのアイテムを購入");
            itemModel.PurchaseItem(itemId, quantity);
            tomsModel.PurchaseItem(totalPrice);
        }
        else
        {
            Debug.Log("お金が足りません！");
        }
    }

    private void ChangePurchasePanel(List<RuntimeItemData> items, BlackSmithTab itemType)
{

    panelDisposables.Dispose();
    panelDisposables = new CompositeDisposable();

    var itemSlots = blackSmithView.PopulateItemList(items);

    foreach (var slot in itemSlots)
    {
        var itemdata = itemModel.GetRuntimeItem(slot.itemId);

        slot.SetItem(
            itemdata.ItemId,
            itemdata.ItemIcon,
            itemdata.CurrentPrice.Value,
            itemdata.MaxStock.Value,
            itemdata.Stock.Value,
            itemdata.IsPopular.Value
        );

        // 残り購入可能数を取得
        int initialMax = itemdata.RemainToMax();

        // BlackSmithModel内に購入予定数・残りmaxを登録
        blackSmithModel.SetItemCount(slot.itemId, 0, initialMax);

        // 以下、各種購読（Subscribe）処理が続く ↓
        blackSmithModel.itemCount[slot.itemId].count
            .Subscribe(count => slot.SetDisplayQuantity(count))
            .AddTo(panelDisposables);

        itemdata.Stock
            .Subscribe(_ =>
            {
                int remainMax = itemdata.RemainToMax();
                blackSmithModel.SetItemCount(
                    slot.itemId,
                    Mathf.Min(blackSmithModel.itemCount[slot.itemId].count.Value, remainMax),
                    remainMax
                );
            })
            .AddTo(panelDisposables);

        itemdata.MaxStock
            .Subscribe(_ =>
            {
                int remainMax = itemdata.RemainToMax();
                blackSmithModel.SetItemCount(
                    slot.itemId,
                    Mathf.Min(blackSmithModel.itemCount[slot.itemId].count.Value, remainMax),
                    remainMax
                );
            })
            .AddTo(panelDisposables);

        blackSmithModel.itemCount[slot.itemId].maxCount
            .Subscribe(max => slot.SetMaxDisplayQuantity(max))
            .AddTo(panelDisposables);

        slot.OnDisplayQuantityChanged
            .Subscribe(x =>
            {
                int remainMax = itemdata.RemainToMax();
                blackSmithModel.SetItemCount(slot.itemId, x, remainMax);
            })
            .AddTo(panelDisposables);

        itemdata.CurrentPrice
            .Subscribe(price => slot.SetPrice(price))
            .AddTo(panelDisposables);

        slot.OnInfoRequested
            .Subscribe(id => blackSmithView.SetDescription(itemModel.GetRuntimeItem(id).ItemDescription))
            .AddTo(panelDisposables);

        slot.OnPurchaseClicked
            .Subscribe(_ =>
            {
                int reserved = blackSmithModel.itemCount[slot.itemId].count.Value;
                int afterRemain = Mathf.Max(0, itemdata.MaxStock.Value - (itemdata.Stock.Value + reserved));

                int quantity = blackSmithModel.PurchaseItem(slot.itemId, afterRemain);
                HandlePurchase(itemdata.ItemId, quantity);
            })
            .AddTo(panelDisposables);
    }

    blackSmithView.SortItemTab(itemType);
}

    private void ShowDevelopmentPanel()
    {
        blackSmithView.SortItemTab(BlackSmithTab.Development);
    }

    public void Dispose()
    {
        panelDisposables.Dispose();
        disposables.Dispose();
    }
}
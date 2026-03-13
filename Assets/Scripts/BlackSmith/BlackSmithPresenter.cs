using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using VContainer.Unity;

public class BlackSmithPresenter : IPresenter, IDisposable, IStartable
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

        stateManager.RegisterOnEnter(TomsShopGamePhase.BlackSmith, Entry);
    }
    
    public void Start()
    {
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
            stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop);
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
                itemdata.ItemName,
                itemdata.ItemIcon,
                itemdata.CurrentPrice.Value,
                itemdata.MaxStock.Value,
                itemdata.Stock.Value,
                itemdata.IsPopular.Value
            );

            // 残り購入可能数
            int initialMax = itemdata.RemainToMax();

            // Model 内に初期登録
            blackSmithModel.SetItemCount(slot.itemId, 0, initialMax);

            // --- 購読: Model → View (予約数の反映) ---
            blackSmithModel.itemCount[slot.itemId].count
                .Subscribe(count => slot.SetDisplayQuantity(count))
                .AddTo(panelDisposables);

            // 残りmaxの変化（在庫やMaxStockの変動時）
            itemdata.Stock
                .Subscribe(_ =>
                {
                    int remainMax = itemdata.RemainToMax();
                    // 既存の予約数が上限を超えないようにクランプ
                    blackSmithModel.SetItemCount(
                        slot.itemId,
                        Mathf.Min(blackSmithModel.itemCount[slot.itemId].count.Value, remainMax),
                        remainMax
                    );
                    slot.SetCurrentStock(itemdata.Stock.Value);
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

            // --- 購読: Model → View (上限の反映) ---
            blackSmithModel.itemCount[slot.itemId].maxCount
                .Subscribe(max => slot.SetMaxDisplayQuantity(max))
                .AddTo(panelDisposables);

            // --- 購読: View → Model (スライダー変更) ---
            slot.OnDisplayQuantityChanged
                .Subscribe(x =>
                {
                    int remainMax = itemdata.RemainToMax();
                    blackSmithModel.SetItemCount(slot.itemId, x, remainMax);
                })
                .AddTo(panelDisposables);

            // --- 購読: View → Model（＋／－ボタン） ---
            slot.OnStepClicked
                .Subscribe(step =>
                {
                    // step は +1 or -1
                    blackSmithModel.AddToCount(slot.itemId, step);
                    // AddToCount により ReactiveProperty が発火 → slot.SetDisplayQuantity に反映される
                })
                .AddTo(panelDisposables);

            // 価格の変化
            itemdata.CurrentPrice
                .Subscribe(price => slot.SetPrice(price))
                .AddTo(panelDisposables);

            // 情報パネル
            slot.OnInfoRequested
                .Subscribe(id => blackSmithView.SetDescription(itemModel.GetRuntimeItem(id).ItemDescription))
                .AddTo(panelDisposables);

            // 購入確定
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

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
    private readonly ItemPopUpManager itemPopUpManager;

    private readonly CompositeDisposable disposables = new();
    private CompositeDisposable panelDisposables = new();

    public BlackSmithPresenter(
        TomsModel tomsModel,
        ItemModel itemModel,
        BlackSmithView blackSmithView,
        StateManager stateManager,
        BlackSmithModel blackSmithModel,
        ItemPopUpManager itemPopUpManager)
    {
        this.blackSmithModel = blackSmithModel;
        this.tomsModel = tomsModel;
        this.itemModel = itemModel;
        this.blackSmithView = blackSmithView;
        this.stateManager = stateManager;
        this.itemPopUpManager = itemPopUpManager;

        stateManager.RegisterOnEnter(TomsShopGamePhase.BlackSmith, Entry);
    }
    
    public void Start()
    {
        Bind();
    }

    public void Entry()
    {
        blackSmithView.ShowDialogue(BlackSmithDialogueLoader.Get("open"));
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

        blackSmithView.OnAutoBuyRequested.Subscribe(_ => HandleAutoBuy()).AddTo(disposables);

        blackSmithView.OnChangePanel
            .Subscribe(type =>
            {
                switch (type)
                {
                    case BlackSmithTab.Weapon:
                        blackSmithView.ShowDialogue(BlackSmithDialogueLoader.Get("weapon"));
                        ChangePurchasePanel(blackSmithModel.weaponRuntimeItems, type);
                        break;
                    case BlackSmithTab.Armor:
                        blackSmithView.ShowDialogue(BlackSmithDialogueLoader.Get("armor"));
                        ChangePurchasePanel(blackSmithModel.armorRuntimeItems, type);
                        break;
                    case BlackSmithTab.Development:
                        blackSmithView.ShowDialogue(BlackSmithDialogueLoader.Get("development"));
                        ShowDevelopmentPanel();
                        break;
                }
            })
            .AddTo(disposables);

        // 鍛冶屋レベルアップボタン
        blackSmithView.OnLevelUpRequested
            .Subscribe(_ => HandleBlackSmithLevelUp())
            .AddTo(disposables);
    }

    private void HandleAutoBuy()
    {
        int budget = tomsModel.PlayerMoney.Value;
        var results = itemModel.AutoPurchase(budget, tomsModel.BlacksmithLevel.Value, tomsModel);
        blackSmithView.ShowAutoBuyResult(results, tomsModel.PlayerMoney.Value);

        // 購入後にスロット表示を更新
        blackSmithModel.SetRuntimeItems(
            itemModel.PickItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Weapon, tomsModel.BlacksmithLevel.Value),
            itemModel.PickItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Armor, tomsModel.BlacksmithLevel.Value)
        );
        ChangePurchasePanel(
            blackSmithModel.weaponRuntimeItems.Count > 0 ? blackSmithModel.weaponRuntimeItems : blackSmithModel.armorRuntimeItems,
            blackSmithModel.weaponRuntimeItems.Count > 0 ? BlackSmithTab.Weapon : BlackSmithTab.Armor
        );
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
            
            // 購入結果を即座に永続化
            itemModel.SaveData();
            tomsModel.SavePlayerMoney();
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

        // 購入パネルを表示、開発パネルを非表示
        blackSmithView.SwitchPanel(itemType);

        var itemSlots = blackSmithView.PopulateItemList(items);

        foreach (var slot in itemSlots)
        {
            var itemdata = itemModel.GetRuntimeItem(slot.itemId);

            slot.SetItem(
                itemdata.ItemId,
                itemdata.ItemName,
                itemdata.ItemIcon,
                itemdata.ItemBackground,
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

            // 情報パネル（infoボタン）
            slot.OnInfoRequested
                .Subscribe(id => blackSmithView.SetDescription(itemModel.GetRuntimeItem(id).ItemDescription))
                .AddTo(panelDisposables);

            // ホバーでアイテム説明を表示
            slot.OnHoverEnter
                .Subscribe(id => blackSmithView.SetDescription(itemModel.GetRuntimeItem(id).ItemDescription))
                .AddTo(panelDisposables);

            slot.OnHoverExit
                .Subscribe(_ => blackSmithView.SetDescription(string.Empty))
                .AddTo(panelDisposables);

            // アイコンクリック → 市場分析ポップアップ
            slot.OnIconClicked
                .Subscribe(id => ShowMarketAnalysisPopup(id))
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

    /// <summary>
    /// アイテムの市場分析ポップアップを表示する
    /// </summary>
    private void ShowMarketAnalysisPopup(string itemId)
    {
        var runtime = itemModel.GetRuntimeItem(itemId);
        if (runtime == null) return;

        var master = itemModel.GetMasterItem(itemId);
        if (master == null) return;

        var data = new ItemPopUpData
        {
            // 基本情報
            ItemName        = runtime.ItemName,
            Description     = runtime.ItemDescription,
            Icon            = runtime.ItemIcon,
            ItemType        = runtime.ItemType,
            ItemAttribute   = runtime.ItemAttribute,

            // 需要
            Demand          = runtime.Demand.Value,
            PreviousDemand  = runtime.PreviousDemand,

            // 価格
            CurrentPrice    = runtime.CurrentPrice.Value,
            BasePrice       = master.basePrice,
            PreviousPrice   = runtime.PreviousPrice,

            // 在庫
            Stock           = runtime.Stock.Value,
            MaxStock        = runtime.MaxStock.Value,

            // 売れやすさ・販売実績
            SalesRate       = runtime.SalesRate,
            WasSoldLastTurn = runtime.WasSoldLastTurn,
            IsPopular       = runtime.IsPopular.Value,

            // ボタン
            ConfirmButtonText = "閉じる",
            OnConfirm       = null
        };

        itemPopUpManager.Show(data);
    }

    private void ShowDevelopmentPanel()
    {
        panelDisposables.Dispose();
        panelDisposables = new CompositeDisposable();

        // 開発パネルを表示、購入パネルを非表示
        blackSmithView.SwitchPanel(BlackSmithTab.Development);
        blackSmithView.SortItemTab(BlackSmithTab.Development);

        // 初回表示
        RefreshDevelopmentPanel();

        // 所持金が変わったらボタン有効/無効を再評価
        tomsModel.PlayerMoney
            .Subscribe(_ => RefreshDevelopmentPanel())
            .AddTo(panelDisposables);

        // 鍛冶屋レベルが変わったら再描画
        tomsModel.BlacksmithLevel
            .Subscribe(_ => RefreshDevelopmentPanel())
            .AddTo(panelDisposables);
    }

    /// <summary>
    /// 開発パネルの表示を最新状態に更新する
    /// </summary>
    private void RefreshDevelopmentPanel()
    {
        int currentLevel = tomsModel.BlacksmithLevel.Value;
        int cost = GameConst.GetBlackSmithLevelUpCost(currentLevel);
        if (cost < 0) cost = 0; // MAX時

        blackSmithView.UpdateDevelopmentPanel(
            currentLevel,
            GameConst.MaxBlackSmithLevel,
            cost,
            tomsModel.PlayerMoney.Value
        );
    }

    /// <summary>
    /// 鍛冶屋レベルアップ処理
    /// </summary>
    private void HandleBlackSmithLevelUp()
    {
        int prevLevel = tomsModel.BlacksmithLevel.Value;

        if (!tomsModel.UpgradeBlacksmith())
        {
            Debug.Log("[BlackSmith] レベルアップ失敗（資金不足 or 最大レベル）");
            return;
        }

        Debug.Log($"[BlackSmith] 鍛冶屋 Lv.{prevLevel} → Lv.{tomsModel.BlacksmithLevel.Value}");

        // レベルが上がったので商品ラインナップを更新
        blackSmithModel.SetRuntimeItems(
            itemModel.PickItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Weapon, tomsModel.BlacksmithLevel.Value),
            itemModel.PickItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Armor, tomsModel.BlacksmithLevel.Value)
        );

        // パネル表示を再更新（ReactivePropertyの購読でも更新されるが念のため）
        RefreshDevelopmentPanel();
    }

    public void Dispose()
    {
        panelDisposables.Dispose();
        disposables.Dispose();
    }
}

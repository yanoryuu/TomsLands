using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly GameFlowManager gameFlowManager;
    private readonly DungeonRepository dungeonRepository;
    private readonly HeroModel heroModel;

    private readonly CompositeDisposable disposables = new();
    private CompositeDisposable panelDisposables = new();
    private CompositeDisposable selectionDisposables = new();
    private int characterTalkIndex;

    // 現在のタブ・並べ替え・選択銘柄（並べ替え再描画と選択維持に使う）
    private BlackSmithTab currentTab = BlackSmithTab.Weapon;
    private BlackSmithSortMode currentSort = BlackSmithSortMode.Recommend;
    private string selectedItemId;

    // 次の戦闘ダンジョンの弱点属性（おすすめスコアの属性ボーナス・自動仕入れに使う）
    private ItemTypeData.ItemAttribute? nextDungeonAttr;

    public BlackSmithPresenter(
        TomsModel tomsModel,
        ItemModel itemModel,
        BlackSmithView blackSmithView,
        StateManager stateManager,
        BlackSmithModel blackSmithModel,
        ItemPopUpManager itemPopUpManager,
        GameFlowManager gameFlowManager,
        DungeonRepository dungeonRepository,
        HeroModel heroModel)
    {
        this.blackSmithModel = blackSmithModel;
        this.tomsModel = tomsModel;
        this.itemModel = itemModel;
        this.blackSmithView = blackSmithView;
        this.stateManager = stateManager;
        this.itemPopUpManager = itemPopUpManager;
        this.gameFlowManager = gameFlowManager;
        this.dungeonRepository = dungeonRepository;
        this.heroModel = heroModel;

        stateManager.RegisterOnEnter(TomsShopGamePhase.BlackSmith, Entry);
    }
    
    public void Start()
    {
        Bind();
    }

    public void Entry()
    {
        characterTalkIndex = 0;
        blackSmithView.ShowDialogue(BlackSmithDialogueLoader.Get("open"));
        UpdateNextDungeonBanner();
        blackSmithModel.SetRuntimeItems(
            itemModel.PickItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Weapon, tomsModel.BlacksmithLevel.Value),
            itemModel.PickItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Armor, tomsModel.BlacksmithLevel.Value)
        );
        ChangePurchasePanel(blackSmithModel.weaponRuntimeItems, BlackSmithTab.Weapon);
    }

    /// <summary>
    /// 次の戦闘ダンジョン情報バナーを更新し、おすすめスコア用の弱点属性を確定する。
    /// </summary>
    private void UpdateNextDungeonBanner()
    {
        int heroLevel = heroModel.heroData != null ? heroModel.heroData.level.Value : 1;
        string weaponName = EquippedName(heroModel.heroData?.weaponId.Value);
        string armorName = EquippedName(heroModel.heroData?.armorId.Value);

        var header = blackSmithView.Header;
        var nextKey = gameFlowManager.GetNextBattleDungeon();
        var dungeon = nextKey.HasValue ? dungeonRepository.GetById(nextKey.Value) : null;

        if (dungeon == null)
        {
            nextDungeonAttr = null;
            header?.ShowNoBattle(heroLevel, weaponName, armorName);
            return;
        }

        nextDungeonAttr = dungeon.requiredAttribute;
        int turnsUntil = gameFlowManager.GetTurnsUntilNextBattle();
        string weakness = $"弱点:{AttributeToJapanese(dungeon.requiredAttribute)}";
        header?.Show(dungeon.dungeonIcon, dungeon.dungeonName, weakness, turnsUntil, heroLevel, weaponName, armorName);
    }

    private string EquippedName(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        return itemModel.GetRuntimeItem(itemId)?.ItemName;
    }

    private static string AttributeToJapanese(ItemTypeData.ItemAttribute attr) => attr switch
    {
        ItemTypeData.ItemAttribute.Fire  => "火",
        ItemTypeData.ItemAttribute.Water => "水",
        ItemTypeData.ItemAttribute.Earth => "土",
        ItemTypeData.ItemAttribute.Wind  => "風",
        ItemTypeData.ItemAttribute.Light => "光",
        ItemTypeData.ItemAttribute.Dark  => "闇",
        _ => attr.ToString()
    };

    private void Bind()
    {
        blackSmithView.OnCloseRequested.Subscribe(_ =>
        {
            stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop);
        }).AddTo(disposables);

        // 鍛冶屋専用の所持金表示（鍛冶屋表示中はCommonViewを出さないため常時追従）
        tomsModel.PlayerMoney
            .Subscribe(money => blackSmithView.UpdatePlayerMoney(money))
            .AddTo(disposables);

        blackSmithView.OnAutoBuyRequested
            .Subscribe(_ => blackSmithView.ShowBudgetPopup(tomsModel.PlayerMoney.Value))
            .AddTo(disposables);

        blackSmithView.OnAutoBuyBudgetConfirmed
            .Subscribe(budget =>
            {
                blackSmithView.HideBudgetPopup();
                HandleAutoBuy(budget);
            })
            .AddTo(disposables);

        blackSmithView.OnCharacterClicked
            .Subscribe(_ => blackSmithView.ShowDialogue(GetNextCharacterTalk()))
            .AddTo(disposables);

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
                        SoundManager.Instance?.PlaySE("営業/SE_開発開始");
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

        // 並べ替え変更 → 現在のタブを並べ替えて再描画
        blackSmithView.OnSortChanged
            .Subscribe(mode =>
            {
                currentSort = mode;
                if (currentTab == BlackSmithTab.Weapon || currentTab == BlackSmithTab.Armor)
                    ChangePurchasePanel(GetTabItems(currentTab), currentTab);
            })
            .AddTo(disposables);
    }

    /// <summary>タブに対応する元アイテムリストを取得する。</summary>
    private List<RuntimeItemData> GetTabItems(BlackSmithTab tab) =>
        tab == BlackSmithTab.Armor ? blackSmithModel.armorRuntimeItems : blackSmithModel.weaponRuntimeItems;

    /// <summary>現在の並べ替えモードでアイテムリストを並べ替える（おすすめ計算式に統一）。</summary>
    private List<RuntimeItemData> ApplySort(List<RuntimeItemData> items)
    {
        IEnumerable<RuntimeItemData> sorted = currentSort switch
        {
            BlackSmithSortMode.Demand => items.OrderByDescending(r => r.Demand.Value),
            BlackSmithSortMode.Price  => items.OrderByDescending(r => r.CurrentPrice.Value),
            _ => items.OrderByDescending(ItemModel.ExpectedRevenueOf)
        };
        return sorted.ToList();
    }

    private string GetNextCharacterTalk()
    {
        characterTalkIndex = (characterTalkIndex % 3) + 1;
        return BlackSmithDialogueLoader.Get($"character_talk_{characterTalkIndex}");
    }

    private void HandleAutoBuy(int budget)
    {
        var results = itemModel.AutoPurchase(budget, tomsModel.BlacksmithLevel.Value, tomsModel, nextDungeonAttr);
        if (results.Count > 0)
            SoundManager.Instance?.PlaySE("営業/SE_仕入れ完了");
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
            SoundManager.Instance?.PlaySE("営業/SE_仕入れ完了");

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
        selectionDisposables.Dispose();
        selectionDisposables = new CompositeDisposable();

        currentTab = itemType;

        // 購入パネルを表示、開発パネルを非表示
        blackSmithView.SwitchPanel(itemType);

        // 並べ替え（おすすめ計算式に統一）
        var sortedItems = ApplySort(items);

        var itemSlots = blackSmithView.PopulateItemList(sortedItems);

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

            // 市況（需要・前回比トレンド）の初期反映
            slot.SetDemand(itemdata.Demand.Value, itemdata.IsPopular.Value);
            slot.SetPriceTrend(itemdata.CurrentPrice.Value, itemdata.PreviousPrice);

            // Model 内に予約数エントリを確保（注文は選択時に詳細パネルへ結線する）
            int initialMax = itemdata.RemainToMax();
            int keepCount = blackSmithModel.itemCount.TryGetValue(slot.itemId, out var existing) ? existing.count.Value : 0;
            blackSmithModel.SetItemCount(slot.itemId, Mathf.Min(keepCount, initialMax), initialMax);

            // 在庫の変化 → 行の在庫表示＋予約数クランプ
            itemdata.Stock
                .Subscribe(_ =>
                {
                    int remainMax = itemdata.RemainToMax();
                    if (blackSmithModel.itemCount.TryGetValue(slot.itemId, out var v))
                        blackSmithModel.SetItemCount(slot.itemId, Mathf.Min(v.count.Value, remainMax), remainMax);
                    slot.SetCurrentStock(itemdata.Stock.Value);
                })
                .AddTo(panelDisposables);

            // 価格の変化（行：価格＋前回比トレンド矢印）
            itemdata.CurrentPrice
                .Subscribe(price =>
                {
                    slot.SetPrice(price);
                    slot.SetPriceTrend(price, itemdata.PreviousPrice);
                })
                .AddTo(panelDisposables);

            // 需要の変化（行：%・バー・人気バッジ）
            itemdata.Demand
                .Subscribe(d => slot.SetDemand(d, itemdata.IsPopular.Value))
                .AddTo(panelDisposables);

            // 情報・ホバーで説明表示
            slot.OnInfoRequested
                .Subscribe(id => blackSmithView.SetDescription(itemModel.GetRuntimeItem(id).ItemDescription))
                .AddTo(panelDisposables);
            slot.OnHoverEnter
                .Subscribe(id => blackSmithView.SetDescription(itemModel.GetRuntimeItem(id).ItemDescription))
                .AddTo(panelDisposables);
            slot.OnHoverExit
                .Subscribe(_ => blackSmithView.SetDescription(string.Empty))
                .AddTo(panelDisposables);

            // アイコン／行クリック → 銘柄選択（詳細パネル表示）
            slot.OnIconClicked
                .Subscribe(id => SelectItem(id, itemSlots))
                .AddTo(panelDisposables);
            slot.OnRowSelected
                .Subscribe(id => SelectItem(id, itemSlots))
                .AddTo(panelDisposables);
        }

        blackSmithView.SortItemTab(itemType);

        // 先頭銘柄を自動選択（直前の選択が残っていればそれを優先）
        if (sortedItems.Count > 0)
        {
            string target = sortedItems.Any(r => r.ItemId == selectedItemId) ? selectedItemId : sortedItems[0].ItemId;
            SelectItem(target, itemSlots);
        }
        else
        {
            selectedItemId = null;
            blackSmithView.DetailPanel?.Hide();
        }
    }

    /// <summary>
    /// 在庫の空きと所持金の両方でクランプした最大購入可能数。
    /// スライダー／＋ボタンはこの範囲までしか動かせない。
    /// </summary>
    private int MaxPurchasableQuantity(RuntimeItemData runtime)
    {
        int remainMax = runtime.RemainToMax();
        int price = Mathf.Max(1, runtime.CurrentPrice.Value);
        int affordable = tomsModel.PlayerMoney.Value / price;
        return Mathf.Clamp(affordable, 0, remainMax);
    }

    /// <summary>所持金・価格の変動に応じて選択銘柄の購入上限を再計算する。</summary>
    private void RefreshQuantityLimit(string itemId, RuntimeItemData runtime)
    {
        if (!blackSmithModel.itemCount.TryGetValue(itemId, out var entry)) return;
        int limit = MaxPurchasableQuantity(runtime);
        blackSmithModel.SetItemCount(itemId, Mathf.Min(entry.count.Value, limit), limit);
    }

    /// <summary>
    /// 銘柄を選択し、詳細パネル（チャート・市場分析・注文）を結線する。
    /// 注文の予約数は BlackSmithModel が保持し、選択銘柄だけをパネルに張り替える。
    /// </summary>
    private void SelectItem(string itemId, List<ItemShopSlot> itemSlots)
    {
        var runtime = itemModel.GetRuntimeItem(itemId);
        if (runtime == null) return;

        var master = itemModel.GetMasterItem(itemId);
        int basePrice = master != null ? master.basePrice : runtime.CurrentPrice.Value;

        selectedItemId = itemId;

        // 選択ハイライト＋説明
        foreach (var s in itemSlots) s.SetSelected(s.itemId == itemId);
        blackSmithView.SetDescription(runtime.ItemDescription);

        var panel = blackSmithView.DetailPanel;
        if (panel == null) return;

        // 選択中アイテム専用の購読をリセット
        selectionDisposables.Dispose();
        selectionDisposables = new CompositeDisposable();

        // 予約数エントリを確保（上限=在庫の空き×所持金で買える数の小さい方）
        int quantityLimit = MaxPurchasableQuantity(runtime);
        int currentCount = blackSmithModel.itemCount.TryGetValue(itemId, out var entry) ? entry.count.Value : 0;
        blackSmithModel.SetItemCount(itemId, Mathf.Min(currentCount, quantityLimit), quantityLimit);

        panel.ShowItem(runtime, basePrice, itemModel.GetRecommendScore(runtime, nextDungeonAttr));

        // Model → Panel（max を先に張ってから count をクランプ反映）
        blackSmithModel.itemCount[itemId].maxCount
            .Subscribe(m => panel.SetMaxQuantity(m))
            .AddTo(selectionDisposables);
        blackSmithModel.itemCount[itemId].count
            .Subscribe(c => panel.SetQuantity(c))
            .AddTo(selectionDisposables);

        // Panel → Model
        panel.OnDisplayQuantityChanged
            .Subscribe(x => blackSmithModel.SetItemCount(itemId, x, MaxPurchasableQuantity(runtime)))
            .AddTo(selectionDisposables);
        panel.OnStepClicked
            .Subscribe(step =>
            {
                blackSmithModel.AddToCount(itemId, step);
                SoundManager.Instance?.PlaySE("営業/SE_数の増減");
            })
            .AddTo(selectionDisposables);

        // 価格・需要のライブ更新（パネル表示）。価格が変わると買える数も変わる
        runtime.CurrentPrice
            .Subscribe(p =>
            {
                panel.SetPrice(p);
                RefreshQuantityLimit(itemId, runtime);
            })
            .AddTo(selectionDisposables);

        // 所持金の変動（購入・レベルアップ等）に合わせて購入上限を追従させる
        tomsModel.PlayerMoney
            .Subscribe(_ => RefreshQuantityLimit(itemId, runtime))
            .AddTo(selectionDisposables);
        runtime.Demand
            .Subscribe(_ => panel.RefreshMarket(runtime, basePrice, itemModel.GetRecommendScore(runtime, nextDungeonAttr)))
            .AddTo(selectionDisposables);

        // 購入確定
        panel.OnPurchaseClicked
            .Subscribe(_ =>
            {
                int reserved = blackSmithModel.itemCount[itemId].count.Value;
                int afterRemain = Mathf.Max(0, runtime.MaxStock.Value - (runtime.Stock.Value + reserved));
                int quantity = blackSmithModel.PurchaseItem(itemId, afterRemain);
                HandlePurchase(itemId, quantity);
            })
            .AddTo(selectionDisposables);
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
        selectionDisposables.Dispose();
        selectionDisposables = new CompositeDisposable();

        currentTab = BlackSmithTab.Development;

        // 開発パネルを表示、購入パネル・詳細パネルを非表示
        blackSmithView.SwitchPanel(BlackSmithTab.Development);
        blackSmithView.DetailPanel?.Hide();
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

        SoundManager.Instance?.PlaySE("営業/SE_開発完了");
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
        selectionDisposables.Dispose();
        panelDisposables.Dispose();
        disposables.Dispose();
    }
}

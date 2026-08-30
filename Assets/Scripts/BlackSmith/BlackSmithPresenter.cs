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
    private readonly PortfolioModel portfolioModel;
    private readonly FinanceSettings financeSettings;
    private readonly RelicEffectResolver relicResolver;

    private readonly CompositeDisposable disposables = new();
    private CompositeDisposable panelDisposables = new();
    private CompositeDisposable selectionDisposables = new();
    private CompositeDisposable financeDisposables = new();
    private int characterTalkIndex;
    private string selectedProductId;

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
        HeroModel heroModel,
        PortfolioModel portfolioModel,
        FinanceSettings financeSettings,
        RelicEffectResolver relicResolver)
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
        this.portfolioModel = portfolioModel;
        this.financeSettings = financeSettings;
        this.relicResolver = relicResolver;

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
                    case BlackSmithTab.Special:
                        blackSmithView.ShowDialogue("取引所へようこそ。債券やファンドで余った資金を働かせよう。");
                        ShowFinancePanel();
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
        var results = itemModel.AutoPurchase(budget, tomsModel.BlacksmithLevel.Value, tomsModel, nextDungeonAttr, relicResolver);
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
        int totalPrice = BuyUnitPrice(item) * quantity;

        if (tomsModel.PlayerMoney.Value >= totalPrice)
        {
            Debug.Log($"{totalPrice}ゴールドのアイテムを購入");
            itemModel.PurchaseItem(itemId, quantity);
            tomsModel.PurchaseItem(totalPrice);
            tomsModel.RecordProcurementSpend(totalPrice);
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

    // ========================================
    // 取引所（Special タブ）
    // ========================================

    private void ShowFinancePanel()
    {
        blackSmithView.SwitchPanel(BlackSmithTab.Special);
        blackSmithView.SortItemTab(BlackSmithTab.Special);
        RefreshFinanceList();
    }

    private void RefreshFinanceList()
    {
        financeDisposables.Dispose();
        financeDisposables = new CompositeDisposable();

        var products = portfolioModel.AllProducts;
        var slots = blackSmithView.PopulateFinanceList(products.Count);

        for (int i = 0; i < slots.Count && i < products.Count; i++)
        {
            var product = products[i];
            var slot = slots[i];
            bool unlocked = product.unlockInfoBrokerLevel <= tomsModel.InfoBrokerLevel.Value;
            slot.Setup(product, GetUnitPrice(product), portfolioModel.GetHeldUnits(product.productId), unlocked);
            slot.SetSelected(product.productId == selectedProductId);

            slot.OnSelected
                .Subscribe(id => SelectProduct(id))
                .AddTo(financeDisposables);
        }

        var detail = blackSmithView.FinanceDetail;
        if (detail != null)
        {
            detail.OnBuyClicked.Subscribe(qty => HandleFinanceBuy(qty)).AddTo(financeDisposables);
            detail.OnSellClicked.Subscribe(qty => HandleFinanceSell(qty)).AddTo(financeDisposables);
        }

        // 選択中の商品があれば詳細を維持
        if (!string.IsNullOrEmpty(selectedProductId))
            ShowFinanceDetail();
        else
            detail?.Hide();
    }

    private int GetUnitPrice(FinancialProductData product) =>
        product.kind == FinancialProductKind.Bond
            ? product.bondUnitPrice
            : portfolioModel.CalculateFundUnitPrice(product, itemModel, tomsModel.BlacksmithLevel.Value);

    private void SelectProduct(string productId)
    {
        selectedProductId = productId;
        var product = portfolioModel.GetProduct(productId);
        if (product == null) return;

        if (product.unlockInfoBrokerLevel > tomsModel.InfoBrokerLevel.Value)
        {
            blackSmithView.ShowDialogue($"それは情報屋レベル {product.unlockInfoBrokerLevel} で取り扱いが解禁される。");
            return;
        }

        ShowFinanceDetail();
    }

    private void ShowFinanceDetail()
    {
        var detail = blackSmithView.FinanceDetail;
        var product = portfolioModel.GetProduct(selectedProductId);
        if (detail == null || product == null) return;

        int unitPrice = GetUnitPrice(product);
        int held = portfolioModel.GetHeldUnits(product.productId);
        float buyFee = financeSettings != null ? financeSettings.fundBuyFeeRate : 0.02f;
        bool canAfford = tomsModel.PlayerMoney.Value >= Mathf.RoundToInt(unitPrice * (1f + (product.kind == FinancialProductKind.IndexFund ? buyFee : 0f)));

        detail.Show(product, unitPrice, held, portfolioModel.GetNavHistory(product.productId), buyFee, canAfford);
    }

    private void HandleFinanceBuy(int quantity)
    {
        var product = portfolioModel.GetProduct(selectedProductId);
        if (product == null) return;

        bool success = product.kind == FinancialProductKind.Bond
            ? portfolioModel.BuyBond(product, quantity, tomsModel, gameFlowManager.CurrentTurn.Value)
            : portfolioModel.BuyFund(product, quantity, tomsModel, itemModel, tomsModel.BlacksmithLevel.Value, gameFlowManager.CurrentTurn.Value);

        if (success)
        {
            SoundManager.Instance?.PlaySE("営業/SE_仕入れ完了");
            blackSmithView.ShowDialogue(product.kind == FinancialProductKind.Bond
                ? $"{product.productName} を購入した。満期は {product.bondMaturityTurns} 日後だ。"
                : $"{product.productName} を {quantity}口 購入した。");
            portfolioModel.RefreshEstimate(itemModel, tomsModel.BlacksmithLevel.Value);
            RefreshFinanceList();
        }
        else
        {
            blackSmithView.ShowDialogue("資金が足りないようだ。");
        }
    }

    private void HandleFinanceSell(int quantity)
    {
        var product = portfolioModel.GetProduct(selectedProductId);
        if (product == null || product.kind != FinancialProductKind.IndexFund) return;

        int income = portfolioModel.SellFund(product.productId, quantity, tomsModel, itemModel, tomsModel.BlacksmithLevel.Value);
        if (income > 0)
        {
            SoundManager.Instance?.PlaySE("営業/SE_売上音");
            blackSmithView.ShowDialogue($"{product.productName} を解約して {income:N0}G を受け取った。");
            portfolioModel.RefreshEstimate(itemModel, tomsModel.BlacksmithLevel.Value);
            RefreshFinanceList();
        }
        else
        {
            blackSmithView.ShowDialogue("解約できる保有口数がない。");
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
        blackSmithView.SortItemTab(itemType);

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
    /// <summary>
    /// 仕入れの実効単価（レリックの仕入れ割引 ProcurementCostMul 適用後）。
    /// 注文ウィジェットの表示・購入上限・決済は必ずこれを使う（表示と請求のズレ防止）。
    /// </summary>
    private int BuyUnitPrice(RuntimeItemData runtime) =>
        RelicPricing.GetBuyUnitPrice(runtime.CurrentPrice.Value, relicResolver);

    private int MaxPurchasableQuantity(RuntimeItemData runtime)
    {
        int remainMax = runtime.RemainToMax();
        int price = BuyUnitPrice(runtime);
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
        // 注文ウィジェットの単価はレリック割引適用後の実効単価にする
        panel.SetPrice(BuyUnitPrice(runtime));

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
                panel.SetPrice(BuyUnitPrice(runtime));
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

        // 所持金の変化はボタン有効/無効の再評価のみ（解放プレビューはレベル依存なので再構築しない）
        tomsModel.PlayerMoney
            .Subscribe(_ => RefreshDevelopmentButtons())
            .AddTo(panelDisposables);

        // 鍛冶屋レベルが変わったら全体を再描画（購読時に現在値が流れるため初回表示もここで行われる）
        tomsModel.BlacksmithLevel
            .Subscribe(_ => RefreshDevelopmentPanel())
            .AddTo(panelDisposables);
    }

    /// <summary>
    /// 開発パネルのレベル・コスト・ボタン状態のみ更新する（所持金変化時用）。
    /// </summary>
    private void RefreshDevelopmentButtons()
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
    /// 開発パネルの表示を最新状態に更新する
    /// </summary>
    private void RefreshDevelopmentPanel()
    {
        RefreshDevelopmentButtons();

        int currentLevel = tomsModel.BlacksmithLevel.Value;

        // 次レベルで解放される商品（武器・防具）のプレビュー
        bool isMax = currentLevel >= GameConst.MaxBlackSmithLevel;
        int nextLevel = Mathf.Min(currentLevel + 1, GameConst.MaxBlackSmithLevel);
        var unlocks = new List<UnlockItemDisplayData>();
        if (!isMax)
        {
            foreach (var r in itemModel.RuntimeItems)
            {
                if (r.RequiredLevel.Value != nextLevel) continue;
                if (r.ItemType != ItemTypeData.ItemType.Weapon && r.ItemType != ItemTypeData.ItemType.Armor) continue;

                unlocks.Add(new UnlockItemDisplayData
                {
                    Icon = r.ItemIcon,
                    Name = r.ItemName,
                    Info = $"{TypeToJapanese(r.ItemType)}・{AttributeToJapanese(r.ItemAttribute)}属性・{r.CurrentPrice.Value:N0}G",
                    Description = r.ItemDescription
                });
            }
        }
        blackSmithView.UpdateUnlockPreview(nextLevel, isMax, unlocks);
    }

    private static string TypeToJapanese(ItemTypeData.ItemType type) => type switch
    {
        ItemTypeData.ItemType.Weapon => "武器",
        ItemTypeData.ItemType.Armor  => "防具",
        ItemTypeData.ItemType.Tool   => "道具",
        _ => type.ToString()
    };

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
        financeDisposables.Dispose();
        disposables.Dispose();
    }
}

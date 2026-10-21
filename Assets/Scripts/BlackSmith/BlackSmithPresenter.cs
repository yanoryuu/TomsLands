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
    private readonly GameFlowManager gameFlowManager;
    private readonly DungeonRepository dungeonRepository;
    private readonly HeroModel heroModel;
    private readonly RelicEffectResolver relicResolver;

    private readonly CompositeDisposable disposables = new();
    private CompositeDisposable panelDisposables = new();
    private CompositeDisposable selectionDisposables = new();
    private int characterTalkIndex;

    /// <summary>鍛冶屋を開く直前のフェーズ。閉じたらここへ戻す（NewspaperPresenter と同じ流儀）。</summary>
    /// <remarks>
    /// 朝刊（鍛冶屋から開いて鍛冶屋へ戻る）とサマリー（再入すると日の処理が走る）は戻り先にしない。
    /// 配信前の寄り道中は戻り先を使わず、GameFlowManager に配信の判断を返す。
    /// </remarks>
    private TomsShopGamePhase returnPhase = TomsShopGamePhase.Shop;

    // 現在のタブ・並べ替え・選択銘柄（並べ替え再描画と選択維持に使う）
    private BlackSmithTab currentTab = BlackSmithTab.Weapon;
    private BlackSmithSortMode currentSort = BlackSmithSortMode.Recommend;
    private string selectedItemId;

    // 次の戦闘ダンジョンの弱点属性（おすすめスコアの属性ボーナス・自動仕入れに使う）
    private ItemTypeData.ItemAttribute? nextDungeonAttr;
    private readonly DungeonIntelModel dungeonIntel;
    private readonly MetaProgressModel metaProgress;
    private readonly ScrapbookModel scrapbook;
    private readonly NewsModel newsModel;
    private readonly NewspaperSubscriptionModel subscriptions;

    /// <summary>
    /// 初回チュートリアルの「オート購入（全額・調整不可）だけ」状態。
    /// この間は閉じる・タブ・並べ替え・個別仕入れ・開発の入力を無視する（View でも押せなくしている）。
    /// </summary>
    private bool tutorialAutoBuyOnly;

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
        RelicEffectResolver relicResolver,
        DungeonIntelModel dungeonIntel,
        MetaProgressModel metaProgress,
        ScrapbookModel scrapbook = null,
        NewsModel newsModel = null,
        NewspaperSubscriptionModel subscriptions = null)
    {
        this.scrapbook = scrapbook;
        this.newsModel = newsModel;
        this.subscriptions = subscriptions;
        this.blackSmithModel = blackSmithModel;
        this.tomsModel = tomsModel;
        this.itemModel = itemModel;
        this.blackSmithView = blackSmithView;
        this.stateManager = stateManager;
        this.gameFlowManager = gameFlowManager;
        this.dungeonRepository = dungeonRepository;
        this.heroModel = heroModel;
        this.relicResolver = relicResolver;
        this.dungeonIntel = dungeonIntel;
        this.metaProgress = metaProgress;

        stateManager.RegisterOnEnter(TomsShopGamePhase.BlackSmith, Entry);
    }
    
    public void Start() => Bind();

    public void Entry()
    {
        characterTalkIndex = 0;
        blackSmithView.ShowDialogue(BlackSmithDialogueLoader.Get("open"));
        UpdateNextDungeonBanner();
        RenderStickyNotes();
        RefreshLineup();
        ChangePurchasePanel(blackSmithModel.weaponRuntimeItems, BlackSmithTab.Weapon);

        ApplyTutorialAutoBuyState();
    }

    // ========================================
    // 会話チュートリアル
    // ========================================

    /// <summary>
    /// 初回オート購入待ちなら「オート購入だけ」状態にする。
    /// 説明会話（Tutorial_Blacksmith）を見終わっていれば（中断→再開など）すぐ予算ポップアップを出す。
    /// まだなら会話終了時に TutorialPresenter から TutorialAutoBuyRequested が来る。
    /// </summary>
    private void ApplyTutorialAutoBuyState()
    {
        tutorialAutoBuyOnly = TutorialSteps.IsFirstAutoBuyPending(metaProgress);
        blackSmithView.SetTutorialAutoBuyOnly(tutorialAutoBuyOnly);

        if (tutorialAutoBuyOnly && metaProgress != null && metaProgress.HasSeenTutorial(TutorialSteps.Blacksmith))
            ShowTutorialAutoBuyPopup();
    }

    /// <summary>予算=所持金全額で固定し、「購入」だけ押せる予算ポップアップを開く。</summary>
    private void ShowTutorialAutoBuyPopup()
    {
        if (!tutorialAutoBuyOnly) return;
        blackSmithView.ShowBudgetPopup(tomsModel.PlayerMoney.Value, locked: true);
    }

    /// <summary>初回オート購入を済ませた → 段階フラグを立てて操作制限を解除する（店へ戻れるようになる）。</summary>
    private void CompleteTutorialAutoBuy()
    {
        tutorialAutoBuyOnly = false;
        metaProgress?.MarkTutorialSeen(TutorialSteps.StepFirstAutoBuy);
        blackSmithView.SetTutorialAutoBuyOnly(false);
    }

    /// <summary>
    /// 「需要度が高くて、かつ価格が安い」商品を選ぶ（チュートリアルの強調表示用）。
    /// スコア = 需要 ÷ (現在価格 / 基準価格)。最高スコアの 85% 以上を上から最大3件。
    /// </summary>
    private List<string> PickBargainItemIds()
    {
        var candidates = new List<(string id, float score)>();
        foreach (var r in blackSmithModel.weaponRuntimeItems.Concat(blackSmithModel.armorRuntimeItems))
        {
            if (r == null || r.CurrentPrice.Value <= 0 || r.RemainToMax() <= 0) continue;
            var master = itemModel.GetMasterItem(r.ItemId);
            float basePrice = master != null && master.basePrice > 0 ? master.basePrice : r.CurrentPrice.Value;
            float priceRatio = r.CurrentPrice.Value / basePrice;
            candidates.Add((r.ItemId, r.Demand.Value / Mathf.Max(0.05f, priceRatio)));
        }
        if (candidates.Count == 0) return new List<string>();

        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        float threshold = candidates[0].score * 0.85f;
        return candidates
            .Where(c => c.score >= threshold)
            .Take(3)
            .Select(c => c.id)
            .ToList();
    }

    /// <summary>強調表示が始まったら、一番の候補があるタブを開いてその商品を選択する（チャートを見せる）。</summary>
    private void FocusBestBargain()
    {
        var ids = PickBargainItemIds();
        if (ids.Count == 0) return;
        string best = ids[0];
        bool inArmor = blackSmithModel.armorRuntimeItems.Any(r => r.ItemId == best);
        selectedItemId = best;
        var tab = inArmor ? BlackSmithTab.Armor : BlackSmithTab.Weapon;
        ChangePurchasePanel(GetTabItems(tab), tab);
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
        // 配信前の寄り道中は「今日これから配信するダンジョン」を出す（Next は今日より先を探すため）
        var nextKey = gameFlowManager.GetUpcomingStreamDungeon();
        var dungeon = nextKey.HasValue ? dungeonRepository.GetById(nextKey.Value) : null;

        if (dungeon == null)
        {
            nextDungeonAttr = null;
            if (header != null) header.ShowNoBattle(heroLevel, weaponName, armorName);
            return;
        }

        // 弱点は「知っている」ときだけ開示する。知らなければ null のままにして、
        // お任せ仕入れ(DungeonFocus)の属性優先も働かせない（Docs/News_Spec.md §2 C4）。
        bool known = dungeonIntel != null && dungeonIntel.IsWeaknessKnown(dungeon.key);
        nextDungeonAttr = known ? dungeon.requiredAttribute : (ItemTypeData.ItemAttribute?)null;
        // 表示用の日数（今日 0・明日 1）。GetTurnsUntilNextBattle は前日に 0 になるので使わない
        int turnsUntil = gameFlowManager.GetDaysUntilNextStream();
        string weakness = ItemAttributeText.Weakness(dungeon.requiredAttribute, known);
        if (header != null) header.Show(dungeon.dungeonIcon, dungeon.dungeonName, weakness, turnsUntil, heroLevel, weaponName, armorName);
    }

    // ========================================
    // 付箋（I-6）と朝刊の入り口（I-7）
    // ========================================

    private void OpenNewspaper()
    {
        if (tutorialAutoBuyOnly) return;
        if (stateManager.HasHandler(TomsShopGamePhase.Newspaper))
            stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Newspaper);
    }

    /// <summary>
    /// スクラップした記事を付箋に出す: 1行目「第N号 予想: 水の防具」、2行目「あと2日で効く／効いている／○ 本物…」。
    /// 続報が届いていれば、ここでも無料で答えを出す（新聞を開かなくても分かるように）。
    /// </summary>
    private void RenderStickyNotes()
    {
        var ui = blackSmithView.StickyNotes;
        if (ui == null || scrapbook == null || newsModel == null) return;
        int seed = tomsModel.FlowSeed;
        int turn = gameFlowManager.CurrentTurn.Value;
        newsModel.Build(seed);
        NewspaperSaveStore.EnsureLoaded(seed, subscriptions, scrapbook, newsModel);
        if (scrapbook.DeliverFollowUps(newsModel, turn) > 0)
            NewspaperSaveStore.Save(seed, subscriptions, scrapbook, newsModel);

        var lines = new List<string>();
        void Add(ScrapbookModel.Scrap s)
        {
            var entry = newsModel.FindEntry(s.entryKey);
            string what = string.IsNullOrEmpty(s.prediction)
                ? Shorten(entry?.Article?.headline, 12)
                : $"予想: {ScrapPrediction.Label(s.prediction)}";
            lines.Add($"第{s.publishTurn}号 {what}\n{StickyStatus(s, entry, turn)}");
        }
        foreach (var s in scrapbook.Pinned) Add(s);
        foreach (var s in scrapbook.ConfirmedOn(turn))
            if (lines.Count < scrapbook.Capacity) Add(s);
        ui.Show(lines);
    }

    private static string StickyStatus(ScrapbookModel.Scrap s, NewsIssueEntry entry, int turn)
    {
        switch (ScrapbookModel.StateOf(s, entry, turn))
        {
            case ScrapState.ConfirmedHit: return "<color=#2F6E2B>○ 本物</color>";
            case ScrapState.ConfirmedPartial: return "<color=#A4500E>△ 誇張</color>";
            case ScrapState.ConfirmedMiss: return "<color=#A0281C>× 誤報</color>";
            case ScrapState.Unconfirmed:
                return entry != null && turn >= entry.SettleTurn ? "<color=#7A4A20>決着した</color>" : "<color=#2F6E2B>効いている</color>";
            default:
                int days = entry != null ? ScrapbookModel.ConfirmableTurn(entry) - turn : -1;
                return days > 0 ? $"<color=#7A4A20>あと{days}日で効く</color>" : "<color=#7A4A20>結果待ち</color>";
        }
    }

    private static string Shorten(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    private static bool IsReturnablePhase(TomsShopGamePhase p) =>
        p != TomsShopGamePhase.BlackSmith
        && p != TomsShopGamePhase.Newspaper
        && p != TomsShopGamePhase.TurnEndSummary;

    private string EquippedName(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        return itemModel.GetRuntimeItem(itemId)?.ItemName;
    }

    private void Bind()
    {
        // 鍛冶屋以外のフェーズを通るたびに戻り先を覚えておく
        stateManager.CurrentTomsShopPhase
            .Subscribe(p => { if (IsReturnablePhase(p)) returnPhase = p; })
            .AddTo(disposables);

        // 朝刊（付箋を押しても開く）。閉じると NewspaperPresenter が鍛冶屋へ戻す（I-7）
        if (blackSmithView.StickyNotes != null) blackSmithView.StickyNotes.OnNewspaperClicked += OpenNewspaper;

        blackSmithView.OnCloseRequested.Subscribe(_ =>
        {
            // 初回チュートリアルのオート購入が済むまでは閉じさせない
            if (tutorialAutoBuyOnly) return;
            // おすすめ商品の強調は鍛冶屋を出たら終わり
            blackSmithModel.TutorialBargainHighlight.Value = false;

            // 配信前の寄り道中: 店へは戻さず（戻ると新しい営業日が始まってしまう）、配信へ進むか尋ねる
            if (gameFlowManager.IsAwaitingStream.Value)
            {
                gameFlowManager.RequestPreStreamDecision(true);
                return;
            }

            var target = stateManager.HasHandler(returnPhase) ? returnPhase : TomsShopGamePhase.Shop;
            stateManager.ChangeTomsShopPhase(target);
        }).AddTo(disposables);

        // 鍛冶屋専用の所持金表示（鍛冶屋表示中はCommonViewを出さないため常時追従）
        tomsModel.PlayerMoney
            .Subscribe(money => blackSmithView.UpdatePlayerMoney(money))
            .AddTo(disposables);

        blackSmithView.OnAutoBuyRequested
            .Subscribe(_ => blackSmithView.ShowBudgetPopup(tomsModel.PlayerMoney.Value, tutorialAutoBuyOnly))
            .AddTo(disposables);

        blackSmithView.OnAutoBuyBudgetConfirmed
            .Subscribe(x =>
            {
                blackSmithView.HideBudgetPopup();
                if (tutorialAutoBuyOnly)
                {
                    // 初回チュートリアル: 予算は所持金全額で固定。方針だけは自分で選ばせる（K-5。最初の5分で一度は「自分で選んだ」にする）
                    HandleAutoBuy(tomsModel.PlayerMoney.Value, x.strategy);
                    CompleteTutorialAutoBuy();
                    return;
                }
                HandleAutoBuy(x.budget, x.strategy);
            })
            .AddTo(disposables);

        // 鍛冶屋の説明会話が終わった → 初回オート購入の予算ポップアップを出す
        blackSmithModel.TutorialAutoBuyRequested
            .Subscribe(_ =>
            {
                if (stateManager.CurrentTomsShopPhase.Value != TomsShopGamePhase.BlackSmith) return;
                ShowTutorialAutoBuyPopup();
            })
            .AddTo(disposables);

        // 立て替え後の会話が終わった → 「需要が高く価格が安い」商品を強調し、一番の候補を選択する
        blackSmithModel.TutorialBargainHighlight
            .Subscribe(on =>
            {
                if (stateManager.CurrentTomsShopPhase.Value != TomsShopGamePhase.BlackSmith) return;
                if (on) FocusBestBargain();
                else if (currentTab == BlackSmithTab.Weapon || currentTab == BlackSmithTab.Armor)
                    ChangePurchasePanel(GetTabItems(currentTab), currentTab);
            })
            .AddTo(disposables);

        blackSmithView.OnCharacterClicked
            .Subscribe(_ => blackSmithView.ShowDialogue(GetNextCharacterTalk()))
            .AddTo(disposables);

        blackSmithView.OnChangePanel
            .Subscribe(type =>
            {
                if (tutorialAutoBuyOnly) return;
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
                        PlaySE("営業/SE_開発開始");
                        blackSmithView.ShowDialogue(BlackSmithDialogueLoader.Get("development"));
                        ShowDevelopmentPanel();
                        break;
                    // Special（取引所）は情報屋画面へ移設した（タブはシーン上で非表示）
                }
            })
            .AddTo(disposables);

        // 鍛冶屋レベルアップボタン
        blackSmithView.OnLevelUpRequested
            .Subscribe(_ => { if (!tutorialAutoBuyOnly) HandleBlackSmithLevelUp(); })
            .AddTo(disposables);

        // 並べ替え変更 → 現在のタブを並べ替えて再描画
        blackSmithView.OnSortChanged
            .Subscribe(mode =>
            {
                if (tutorialAutoBuyOnly) return;
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

    private void HandleAutoBuy(int budget, AutoBuyStrategy strategy)
    {
        var results = itemModel.AutoPurchase(budget, tomsModel.BlacksmithLevel.Value, tomsModel, nextDungeonAttr, relicResolver, strategy);
        if (results.Count > 0)
            PlaySE("営業/SE_仕入れ完了");
        blackSmithView.ShowAutoBuyResult(results, tomsModel.PlayerMoney.Value);

        // 購入後にスロット表示を更新
        RefreshLineup();
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
            item?.RecordAcquisition(quantity, BuyUnitPrice(item)); // 取得単価（在庫を増やす前に記録）
            itemModel.PurchaseItem(itemId, quantity);
            tomsModel.PurchaseItem(totalPrice);
            tomsModel.RecordProcurementSpend(totalPrice);
            PlaySE("営業/SE_仕入れ完了");

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

        // チュートリアル: 「需要が高く価格が安い」商品を強調
        if (blackSmithModel.TutorialBargainHighlight.Value)
        {
            var bargainIds = PickBargainItemIds();
            foreach (var slot in itemSlots)
                slot.SetTutorialHighlight(bargainIds.Contains(slot.itemId));
        }

        // 先頭銘柄を自動選択（直前の選択が残っていればそれを優先）
        if (sortedItems.Count > 0)
        {
            string target = sortedItems.Any(r => r.ItemId == selectedItemId) ? selectedItemId : sortedItems[0].ItemId;
            SelectItem(target, itemSlots);
        }
        else
        {
            selectedItemId = null;
            HideDetailPanel();
        }
    }

    private static readonly Color StreamMarkerColor = new(1f, 0.82f, 0.3f, 1f);
    private static readonly Color ScrapMarkerColor = new(0.93f, 0.9f, 0.82f, 1f);

    /// <summary>
    /// チャートの出来事の印（G-12）: 配信した日（金）と、貼った記事が載った日（紙色）。
    /// どの記事がこの銘柄に効いたかは出さない（Docs/News_Spec.md §9.3「記事→銘柄のリンクは作らない」）。
    /// 履歴の末尾が今日なので、i 番目は「今日 −(末尾からの距離)」の日。
    /// </summary>
    private List<PriceChartView.Marker> BuildChartMarkers(RuntimeItemData runtime)
    {
        var result = new List<PriceChartView.Marker>();
        int n = runtime.ShopPriceHistory.Count;
        if (n < 2) return result;
        int today = gameFlowManager.CurrentTurn.Value;
        int IndexOf(int turn) => n - 1 - (today - turn);

        foreach (int turn in gameFlowManager.GetPastStreamTurns())
        {
            int i = IndexOf(turn);
            if (i >= 0 && i < n) result.Add(new PriceChartView.Marker(i, StreamMarkerColor));
        }
        if (scrapbook != null)
        {
            var seen = new HashSet<int>();
            foreach (var scrap in scrapbook.Pinned)
            {
                int i = IndexOf(scrap.publishTurn);
                if (i >= 0 && i < n && seen.Add(i)) result.Add(new PriceChartView.Marker(i, ScrapMarkerColor));
            }
        }
        return result;
    }

    /// <summary>
    /// 仕入れの実効単価（レリックの仕入れ割引 ProcurementCostMul 適用後）。
    /// 注文ウィジェットの表示・購入上限・決済は必ずこれを使う（表示と請求のズレ防止）。
    /// </summary>
    private int BuyUnitPrice(RuntimeItemData runtime) =>
        RelicPricing.GetBuyUnitPrice(runtime.CurrentPrice.Value, relicResolver);

    /// <summary>
    /// 在庫の空きと所持金の両方でクランプした最大購入可能数。
    /// スライダー／＋ボタンはこの範囲までしか動かせない。
    /// </summary>
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

        panel.ShowItem(runtime, basePrice, ItemModel.ExpectedRevenueOf(runtime), markers: BuildChartMarkers(runtime));
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
                PlaySE("営業/SE_数の増減");
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
            .Subscribe(_ => panel.RefreshMarket(runtime, basePrice, ItemModel.ExpectedRevenueOf(runtime)))
            .AddTo(selectionDisposables);

        // 購入確定
        panel.OnPurchaseClicked
            .Subscribe(_ =>
            {
                if (tutorialAutoBuyOnly) return;
                int reserved = blackSmithModel.itemCount[itemId].count.Value;
                int afterRemain = Mathf.Max(0, runtime.MaxStock.Value - (runtime.Stock.Value + reserved));
                int quantity = blackSmithModel.PurchaseItem(itemId, afterRemain);
                HandlePurchase(itemId, quantity);
            })
            .AddTo(selectionDisposables);
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
        HideDetailPanel();
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
                    Info = $"{TypeToJapanese(r.ItemType)}・{ItemAttributeText.WithSuffix(r.ItemAttribute)}・{r.CurrentPrice.Value:N0}G",
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

        PlaySE("営業/SE_開発完了");
        Debug.Log($"[BlackSmith] 鍛冶屋 Lv.{prevLevel} → Lv.{tomsModel.BlacksmithLevel.Value}");

        // レベルが上がったので商品ラインナップを更新
        RefreshLineup();

        // パネル表示を再更新（ReactivePropertyの購読でも更新されるが念のため）
        RefreshDevelopmentPanel();
    }

    /// <summary>鍛冶屋レベルに応じた武器・防具のラインナップを Model へ入れ直す。</summary>
    private void RefreshLineup()
    {
        int level = tomsModel.BlacksmithLevel.Value;
        blackSmithModel.SetRuntimeItems(
            itemModel.PickItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Weapon, level),
            itemModel.PickItemRuntimeList(itemModel.RuntimeItems, ItemTypeData.ItemType.Armor, level)
        );
    }

    private void HideDetailPanel()
    {
        var panel = blackSmithView.DetailPanel;
        if (panel != null) panel.Hide();
    }

    private static void PlaySE(string key)
    {
        var sound = SoundManager.Instance;
        if (sound != null) sound.PlaySE(key);
    }

    public void Dispose()
    {
        selectionDisposables.Dispose();
        panelDisposables.Dispose();
        disposables.Dispose();
    }
}

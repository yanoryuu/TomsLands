using System;
using R3;
using UnityEngine;
using VContainer.Unity;

public class InfoBrokerPresenter : IDisposable, IPresenter, IStartable
{
    private readonly InfoBrokerModel infoBrokerModel;
    private readonly InfoBrokerView infoBrokerView;
    private readonly CompositeDisposable disposables = new();
    private readonly StateManager stateManager;
    private readonly MapInfoView mapInfoView;
    private readonly TomsModel tomsModel;
    private readonly GameFlowManager gameFlowManager;
    private readonly PortfolioModel portfolioModel;
    private readonly ExchangePanelController exchange;
    private int characterTalkIndex;

    public InfoBrokerPresenter(
        InfoBrokerModel infoBrokerModel,
        InfoBrokerView infoBrokerView,
        StateManager stateManager,
        MapInfoView mapInfoView,
        TomsModel tomsModel,
        GameFlowManager gameFlowManager,
        PortfolioModel portfolioModel,
        FinanceSettings financeSettings,
        ItemModel itemModel)
    {
        this.infoBrokerModel = infoBrokerModel;
        this.infoBrokerView = infoBrokerView;
        this.stateManager = stateManager;
        this.mapInfoView = mapInfoView;
        this.tomsModel = tomsModel;
        this.gameFlowManager = gameFlowManager;
        this.portfolioModel = portfolioModel;

        // 取引所（金融商品の売買）は情報屋の1タブとして提供する
        exchange = new ExchangePanelController(
            portfolioModel, financeSettings, tomsModel, itemModel, gameFlowManager,
            infoBrokerView.PopulateFinanceRows,
            () => infoBrokerView.FinanceDetail,
            infoBrokerView.ShowDialogue);

        stateManager.RegisterOnEnter(TomsShopGamePhase.Broker, Entry);
    }

    public void Start()
    {
        Bind();
    }

    public void Entry()
    {
        characterTalkIndex = 0;
        infoBrokerModel.InitializeDungeons();
        infoBrokerView.ShowDialogue(InfoBrokerDialogueLoader.Get("open"));
        infoBrokerView.ShowPanel(InfoBrokerTab.Map);
        infoBrokerView.SortItemTab(InfoBrokerTab.Map);
        ShowMapInfo();
    }

    private void Bind()
    {
        infoBrokerView.OnCloseRequested
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposables);

        infoBrokerView.OnCharacterClicked
            .Subscribe(_ => infoBrokerView.ShowDialogue(GetNextCharacterTalk()))
            .AddTo(disposables);

        // 情報屋専用の所持金表示（情報屋表示中はCommonViewを出さないため常時追従）
        // 所持金の変化で開発パネルのボタン活性も追従させる
        tomsModel.PlayerMoney
            .Subscribe(money =>
            {
                infoBrokerView.UpdatePlayerMoney(money);
                RefreshDevelopmentPanel();
            })
            .AddTo(disposables);

        // レベルアップボタン
        infoBrokerView.OnLevelUpRequested
            .Subscribe(_ => HandleLevelUp())
            .AddTo(disposables);

        infoBrokerView.OnRefreshRequested
            .Subscribe(_ => infoBrokerModel.UpdateInfoMessages())
            .AddTo(disposables);

        infoBrokerView.OnChangePanel
            .Subscribe(tab =>
            {
                infoBrokerView.SortItemTab(tab);
                infoBrokerView.ShowPanel(tab);
                switch (tab)
                {
                    case InfoBrokerTab.Map:
                        infoBrokerView.ShowDialogue(InfoBrokerDialogueLoader.Get("map"));
                        ShowMapInfo();
                        break;
                    case InfoBrokerTab.Exchange:
                        infoBrokerView.ShowDialogue("取引所へようこそ。債券やファンドで余った資金を働かせよう。");
                        exchange.Refresh();
                        break;
                    case InfoBrokerTab.Development:
                        infoBrokerView.ShowDialogue("情報網に投資するかい？レベルが上がれば、取引所で扱える商品が増えるよ。");
                        RefreshDevelopmentPanel();
                        break;
                }
            })
            .AddTo(disposables);

        mapInfoView.OnMapPurchaseClicked
            .Subscribe(PurchaseDungeonInfo)
            .AddTo(disposables);
    }

    private string GetNextCharacterTalk()
    {
        characterTalkIndex = (characterTalkIndex % 3) + 1;
        return InfoBrokerDialogueLoader.Get($"character_talk_{characterTalkIndex}");
    }

    private void ShowMapInfo()
    {
        mapInfoView.SetMapSlot(
            infoBrokerModel.availableDungeons,
            infoBrokerModel.GetDungeonInfoCosts(),
            gameFlowManager.BattleCount.Value + 1);
    }

    private void PurchaseDungeonInfo(DungeonName dungeonName)
    {
        var costs = infoBrokerModel.GetDungeonInfoCosts();
        if (!costs.ContainsKey(dungeonName))
        {
            Debug.LogWarning($"[InfoBrokerPresenter] 情報料が見つかりません: {dungeonName}");
            return;
        }

        int battleCount = gameFlowManager.BattleCount.Value;
        int[] costArray = costs[dungeonName];
        int cost = costArray.Length > battleCount
            ? costArray[battleCount]
            : costArray[costArray.Length - 1];

        if (tomsModel.PlayerMoney.Value < cost)
        {
            infoBrokerView.ShowDialogue($"金が足りないな。その情報は {cost:N0}G だ。");
            Debug.Log($"[InfoBrokerPresenter] 所持金不足: 必要 {cost}G / 所持 {tomsModel.PlayerMoney.Value}G");
            return;
        }

        tomsModel.PurchaseItem(cost);
        tomsModel.SavePlayerMoney();
        infoBrokerModel.PurchaseDungeonInfo(dungeonName);
        SoundManager.Instance?.PlaySE("営業/SE_仕入れ完了");

        // リストを購入済み表示に更新し、右の詳細に解放された情報をそのまま見せる
        ShowMapInfo();
        mapInfoView.SelectDungeon(dungeonName);
        infoBrokerView.ShowDialogue("いい買い物だ。右の詳細を見てくれ。弱点を突けば配信も楽になる。");
        Debug.Log($"[InfoBrokerPresenter] {dungeonName} の情報を {cost}G で購入しました。残金: {tomsModel.PlayerMoney.Value}G");
    }

    // =====================================================
    // 開発（情報屋レベルアップ）タブ — 鍛冶屋の開発パネルと同じ流儀
    // =====================================================

    /// <summary>開発パネルのレベル・費用・解禁プレビュー表示を最新化する。</summary>
    private void RefreshDevelopmentPanel()
    {
        int currentLevel = tomsModel.InfoBrokerLevel.Value;
        int cost = GameConst.GetInfoBrokerLevelUpCost(currentLevel);
        if (cost < 0) cost = 0; // MAX時

        infoBrokerView.UpdateDevelopmentPanel(
            currentLevel,
            GameConst.MaxInfoBrokerLevel,
            cost,
            tomsModel.PlayerMoney.Value);

        // 次レベルで解禁される金融商品のプレビュー
        bool isMax = currentLevel >= GameConst.MaxInfoBrokerLevel;
        if (isMax)
        {
            infoBrokerView.UpdateUnlockPreview("最大レベル", "これ以上解禁される商品はありません。");
            return;
        }

        int nextLevel = currentLevel + 1;
        var unlocks = new System.Collections.Generic.List<string>();
        foreach (var p in portfolioModel.AllProducts)
        {
            if (p == null || p.unlockInfoBrokerLevel != nextLevel) continue;
            string kind = p.kind == FinancialProductKind.Bond
                ? $"債券・利率{p.bondInterestRate:P0}・{p.bondMaturityTurns}日満期"
                : "ファンド・いつでも解約可";
            unlocks.Add($"・{p.productName}（{kind}）");
        }

        infoBrokerView.UpdateUnlockPreview(
            $"Lv.{nextLevel} で取引所に並ぶ商品",
            unlocks.Count > 0 ? string.Join("\n", unlocks) : "Lv." + nextLevel + " で追加される商品はありません。");
    }

    /// <summary>情報屋レベルアップ処理。</summary>
    private void HandleLevelUp()
    {
        int prevLevel = tomsModel.InfoBrokerLevel.Value;

        if (!tomsModel.UpgradeInfoBroker())
        {
            infoBrokerView.ShowDialogue("資金が足りないか、もう鍛えるところがないね。");
            return;
        }

        SoundManager.Instance?.PlaySE("営業/SE_開発完了");
        infoBrokerView.ShowDialogue($"いい投資だ。情報網が広がった（Lv.{prevLevel} → Lv.{tomsModel.InfoBrokerLevel.Value}）。取引所を見てみな。");
        RefreshDevelopmentPanel();
    }

    public void RecordHeroPurchase(string itemId, int quantity, int price)
    {
        infoBrokerModel.RecordHeroPurchase(itemId, quantity, price);
    }

    public void Dispose()
    {
        exchange.Dispose();
        disposables.Dispose();
    }
}

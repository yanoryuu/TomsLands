using System;
using System.Collections.Generic;
using System.Text;
using R3;
using UnityEngine;
using VContainer.Unity;

public class TomsShopPresenter : IDisposable, IPresenter, IStartable
{
    private readonly TomsShopView tomsShopView;
    private readonly ItemSelectionPresenter itemSelectionPresenter;
    private readonly ItemModel itemModel;
    private readonly TomsModel tomsShopModel;
    private readonly CompositeDisposable disposables = new();
    private readonly CommonView commonView;
    private readonly StateManager stateManager;
    private readonly GameFlowManager gameFlowManager;
    private readonly TurnEndSummaryPresenter turnEndSummaryPresenter;
    private readonly PendingEventData pendingEventData;
    private readonly EventView eventView;
    private readonly TomsEventExecutor tomsEventExecutor;

    public TomsShopPresenter(
        TomsShopView tomsShopView,
        ItemSelectionPresenter itemSelectionPresenter,
        ItemModel itemModel,
        TomsModel tomsShopModel,
        CommonView commonView,
        StateManager stateManager,
        GameFlowManager gameFlowManager,
        TurnEndSummaryPresenter turnEndSummaryPresenter,
        PendingEventData pendingEventData,
        EventView eventView,
        TomsEventExecutor tomsEventExecutor,
        GamePanelManager gamePanelManager)
    {
        this.tomsShopView = tomsShopView;
        this.itemSelectionPresenter = itemSelectionPresenter;
        this.itemModel = itemModel;
        this.tomsShopModel = tomsShopModel;
        this.commonView = commonView;
        this.stateManager = stateManager;
        this.gameFlowManager = gameFlowManager;
        this.turnEndSummaryPresenter = turnEndSummaryPresenter;
        this.pendingEventData = pendingEventData;
        this.eventView = eventView;
        this.tomsEventExecutor = tomsEventExecutor;
        
        // EventViewにGamePanelManagerを注入
        eventView.Initialize(gamePanelManager);
        
        stateManager.RegisterOnEnter(TomsShopGamePhase.Shop,Entry);
    }
    
    public void Start()
    {
        Bind();   
    }

    private void Bind()
    {
        // 「鍛冶屋」ボタン
        tomsShopView.OnBlacksmithClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.BlackSmith))
            .AddTo(disposables);
        
        //　情報屋ボタン
        tomsShopView.OnInfoClicked
            .Subscribe(_=> stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Broker))
            .AddTo(disposables);
        
        //　道具屋ボタン
        tomsShopView.OnToolClicked
            .Subscribe(_=> stateManager.ChangeTomsShopPhase(TomsShopGamePhase.ToolShop))
            .AddTo(disposables);

        // 「陳列設定」ボタン
        tomsShopView.OnSetItemClicked
            .Subscribe(_ => OpenSelectionPanel())
            .AddTo(disposables);
        
        //　マップボタン
        tomsShopView.OnMapClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Map))
            .AddTo(disposables);
        
        //　ダンジョンレベルアップボタン
        tomsShopView.OnDungeonLevelUpClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.DungeonLevelUp))
            .AddTo(disposables);
        
        //　次のターンに進むボタン → サマリーパネルを表示
        tomsShopView.OnNextTurnClicked
            .Subscribe(_ => turnEndSummaryPresenter.ShowSummary())
            .AddTo(disposables);
        
        //　ターン表示の更新（CommonView）
        gameFlowManager.CurrentTurn
            .Subscribe(turn => commonView.UpdateCurrentTurn(turn))
            .AddTo(disposables);
        
        //　ターン切り替え演出（初期値はスキップ）
        gameFlowManager.CurrentTurn
            .Skip(1)
            .Subscribe(turn => tomsShopView.ShowTurnAnnounce(turn))
            .AddTo(disposables);

        // イベントポップアップの確認ボタン押下時
        eventView.OnConfirmClicked
            .Subscribe(_ => OnEventPopupConfirmed())
            .AddTo(disposables);
    }
    
    public void Entry()
    {
        //ここにこの画面に移動した時にここを呼び出す。
        Initialize();
        
        // 保留イベントがあればポップアップを表示
        ShowPendingEventIfExists();
    }
    
    //初期化
    private void Initialize()
    {
        // tomsShopView.Initialize();
        // tomsShopModel.Initialize();
        // itemSelectionPresenter.Initialize();
    }

    /// <summary>
    /// 保留中のイベントがあればポップアップを表示する
    /// </summary>
    private void ShowPendingEventIfExists()
    {
        if (!pendingEventData.HasPendingEvent) return;

        var tomsEvent = pendingEventData.PendingEvent;
        Debug.Log($"[TomsShopPresenter] Showing pending event popup: {tomsEvent.title}");

        // エフェクトテキストを構築
        string effectText = BuildEffectText(tomsEvent.commands);

        // ポップアップを表示
        eventView.ShowEvent(tomsEvent.title, tomsEvent.description, effectText);
    }

    /// <summary>
    /// イベントポップアップの確認ボタンが押された時の処理
    /// </summary>
    private void OnEventPopupConfirmed()
    {
        if (!pendingEventData.HasPendingEvent) return;

        var tomsEvent = pendingEventData.PendingEvent;
        Debug.Log($"[TomsShopPresenter] Event popup confirmed: {tomsEvent.title}");

        // コマンドを実行
        tomsEventExecutor.Execute(tomsEvent);

        // 保留データをクリア
        pendingEventData.Clear();
    }

    /// <summary>
    /// コマンドの効果をテキストとして構築する
    /// </summary>
    private string BuildEffectText(List<TomsEventCommand> commands)
    {
        var sb = new StringBuilder();

        foreach (var cmd in commands)
        {
            switch (cmd.command)
            {
                case "ChangeMoney":
                    if (cmd.parameters.TryGetValue("amount", out var moneyStr))
                    {
                        int amount = int.Parse(moneyStr);
                        sb.AppendLine(amount >= 0 ? $"所持金 +{amount}G" : $"所持金 {amount}G");
                    }
                    break;

                case "ChangeTrust":
                    if (cmd.parameters.TryGetValue("amount", out var trustStr))
                    {
                        float trustAmount = float.Parse(trustStr);
                        sb.AppendLine(trustAmount >= 0 ? $"信頼度 +{trustAmount}" : $"信頼度 {trustAmount}");
                    }
                    break;

                case "AddItem":
                    if (cmd.parameters.TryGetValue("itemId", out var itemId))
                    {
                        sb.AppendLine($"アイテム獲得: {itemId}");
                    }
                    break;
            }
        }

        return sb.ToString().TrimEnd();
    }

    //陳列画面を表示
    private void OpenSelectionPanel()
    {
        itemSelectionPresenter.OnOpenSelectionPanel();
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

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
    private readonly MarketingFacade marketingFacade;

    /// <summary>前回Entry()時のターン番号。初回はスキップ用に-1。</summary>
    private int _lastKnownTurn = -1;

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
        GamePanelManager gamePanelManager,
        MarketingFacade marketingFacade)
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
        this.marketingFacade = marketingFacade;
        
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

        tomsShopView.OnHeroClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Hero))
            .AddTo(disposables);

        //　情報屋ボタン
        tomsShopView.OnInfoClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Broker))
            .AddTo(disposables);

        //　道具屋ボタン
        tomsShopView.OnToolClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.ToolShop))
            .AddTo(disposables);

        // 「陳列設定」ボタン
        tomsShopView.OnSetItemClicked
            .Subscribe(_ => OpenSelectionPanel())
            .AddTo(disposables);

        // 陳列パネルを閉じたら机の表示を更新
        itemSelectionPresenter.OnClosed
            .Subscribe(_ => tomsShopView.RefreshDeskDisplay(itemModel.RuntimeItems))
            .AddTo(disposables);

        //　マップボタン
        tomsShopView.OnMapClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Map))
            .AddTo(disposables);

        //　ダンジョンレベルアップボタン
        tomsShopView.OnDungeonLevelUpClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.DungeonLevelUp))
            .AddTo(disposables);

        //　広告購入画面ボタン
        tomsShopView.OnAdvertisementClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Advertisement))
            .AddTo(disposables);

        //　預言者画面ボタン
        tomsShopView.OnProphetClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Prophet))
            .AddTo(disposables);

        //　次のターンに進むボタン → サマリーパネルを表示
        tomsShopView.OnNextTurnClicked
            .Subscribe(_ => turnEndSummaryPresenter.ShowSummary())
            .AddTo(disposables);
        
        //　ターン表示の更新（CommonView）
        gameFlowManager.CurrentTurn
            .Subscribe(turn => commonView.UpdateCurrentTurn(turn))
            .AddTo(disposables);

        // ※ターン切り替え演出は Entry() 内で一元管理する
        //   バズ発生時はバズ演出のみ表示し、ターン演出はスキップするため

        // イベントポップアップの確認ボタン押下時
        eventView.OnConfirmClicked
            .Subscribe(_ => OnEventPopupConfirmed())
            .AddTo(disposables);
    }
    
    public void Entry()
    {
        //ここにこの画面に移動した時にここを呼び出す。
        Initialize();

        SoundManager.Instance?.PlayBGM("通常営業");

        // 机の陳列を更新
        tomsShopView.RefreshDeskDisplay(itemModel.RuntimeItems);

        // 保留イベントがあればポップアップを表示
        ShowPendingEventIfExists();

        // ターン変更を検出し、バズ演出またはターン演出を表示する
        ShowTurnOrBuzzAnnounce();
    }
    
    //初期化
    private void Initialize()
    {
        // tomsShopView.Initialize();
        // tomsShopModel.Initialize();
        // itemSelectionPresenter.Initialize();
    }

    /// <summary>
    /// ターン変更を検出し、バズ演出またはターン演出のどちらかを表示する。
    /// 
    /// 【排他制御】
    /// - バズ発生/終了時 → バズ演出のみ表示（ターン演出はスキップ）
    /// - バズなし → 通常のターン切り替え演出を表示
    /// - 初回Entry（_lastKnownTurn == -1） → 演出なし（初期表示時はスキップ）
    /// - サブフェーズ間遷移（ターン番号変化なし） → 演出なし
    /// </summary>
    private void ShowTurnOrBuzzAnnounce()
    {
        int currentTurn = gameFlowManager.CurrentTurn.Value;
        bool turnChanged = _lastKnownTurn != -1 && _lastKnownTurn != currentTurn;
        _lastKnownTurn = currentTurn;

        // ターンが変わっていなければ演出不要（サブフェーズ間の遷移など）
        if (!turnChanged)
        {
            // バズ結果が残っていれば消費だけして捨てる
            marketingFacade?.ConsumeLastBuzzResult();
            return;
        }

        // バズ結果を消費型で取得
        var buzzResult = marketingFacade?.ConsumeLastBuzzResult();
        bool hasBuzzEvent = buzzResult != null && (buzzResult.NewBuzzOccurred || buzzResult.BuzzEnded);

        if (hasBuzzEvent)
        {
            // バズ演出を表示（ターン演出はスキップ）
            if (buzzResult.BuzzEnded)
            {
                Debug.Log($"[TomsShopPresenter] バズ終了演出表示: {buzzResult.EndedBuzzType}");
                tomsShopView.ShowBuzzEndedAnnounce(buzzResult.EndedBuzzType);
            }

            if (buzzResult.NewBuzzOccurred)
            {
                Debug.Log($"[TomsShopPresenter] バズ発生演出表示: {buzzResult.NewBuzzType}");
                tomsShopView.ShowBuzzAnnounce(buzzResult.NewBuzzType);
            }
        }
        else
        {
            // バズなし → 通常のターン切り替え演出
            tomsShopView.ShowTurnAnnounce(currentTurn);
        }
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

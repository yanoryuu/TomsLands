using System;
using R3;
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

    public TomsShopPresenter(
        TomsShopView tomsShopView,
        ItemSelectionPresenter itemSelectionPresenter,
        ItemModel itemModel,
        TomsModel tomsShopModel,
        CommonView commonView,
        StateManager stateManager,
        GameFlowManager gameFlowManager)
    {
        this.tomsShopView = tomsShopView;
        this.itemSelectionPresenter = itemSelectionPresenter;
        this.itemModel = itemModel;
        this.tomsShopModel = tomsShopModel;
        this.commonView = commonView;
        this.stateManager = stateManager;
        this.gameFlowManager = gameFlowManager;
        
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
        
        //　次のターンに進むボタン
        tomsShopView.OnNextTurnClicked
            .Subscribe(_ => gameFlowManager.NextTurn())
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
    }
    
    public void Entry()
    {
        //ここにこの画面に移動した時にここを呼び出す。
        Initialize();
    }
    
    //初期化
    private void Initialize()
    {
        // tomsShopView.Initialize();
        // tomsShopModel.Initialize();
        // itemSelectionPresenter.Initialize();
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

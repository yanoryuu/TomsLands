using System;
using R3;

public class TomsShopPresenter : IDisposable, IPresenter
{
    private readonly TomsShopView tomsShopView;
    private readonly ItemSelectionPresenter itemSelectionPresenter;
    private readonly ItemModel itemModel;
    private readonly TomsModel tomsShopModel;
    private readonly CompositeDisposable disposables = new();
    private readonly CommonView commonView;
    private readonly StateManager stateManager;

    public TomsShopPresenter(
        TomsShopView tomsShopView,
        ItemSelectionPresenter itemSelectionPresenter,
        ItemModel itemModel,
        TomsModel tomsShopModel,
        CommonView commonView,
        StateManager stateManager)
    {
        this.tomsShopView = tomsShopView;
        this.itemSelectionPresenter = itemSelectionPresenter;
        this.itemModel = itemModel;
        this.tomsShopModel = tomsShopModel;
        this.commonView = commonView;
        this.stateManager = stateManager;
        
        stateManager.RegisterOnEnter(GamePhase.TomsShop,Entry);
        
        Bind();
    }

    private void Bind()
    {
        // 「鍛冶屋」ボタン
        tomsShopView.OnBlacksmithClicked
            .Subscribe(_ => stateManager.ChangePhase(GamePhase.BlackSmith))
            .AddTo(disposables);
        
        //　情報屋ボタン
        tomsShopView.OnInfoClicked
            .Subscribe(_=> stateManager.ChangePhase(GamePhase.InfoBroker))
            .AddTo(disposables);
        
        //　道具屋ボタン
        tomsShopView.OnToolClicked
            .Subscribe(_=> stateManager.ChangePhase(GamePhase.ToolShop))
            .AddTo(disposables);

        // 「陳列設定」ボタン
        tomsShopView.OnSetItemClicked
            .Subscribe(_ => OpenSelectionPanel())
            .AddTo(disposables);
        
        //　マップボタン
        tomsShopView.OnMapClicked
            .Subscribe(_ => stateManager.ChangePhase(GamePhase.Map))
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
        tomsShopView.Initialize();
        tomsShopModel.Initialize();
        itemSelectionPresenter.Initialize();
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

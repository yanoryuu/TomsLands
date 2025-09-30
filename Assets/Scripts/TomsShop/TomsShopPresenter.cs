using System;
using R3;

public class TomsShopPresenter : IDisposable, IPresenter
{
    private readonly TomsShopView tomsShopView;
    private readonly ItemSelectionView itemSelectionView;
    private readonly ItemModel itemModel;
    private readonly TomsShopModel tomsShopModel;
    private readonly CompositeDisposable disposables = new();
    private readonly CommonView commonView;
    private readonly StateManager stateManager;

    public TomsShopPresenter(
        TomsShopView tomsShopView,
        ItemSelectionView itemSelectionView,
        ItemModel itemModel,
        TomsShopModel tomsShopModel,
        CommonView commonView,
        StateManager stateManager)
    {
        this.tomsShopView = tomsShopView;
        this.itemSelectionView = itemSelectionView;
        this.itemModel = itemModel;
        this.tomsShopModel = tomsShopModel;
        this.commonView = commonView;
        this.stateManager = stateManager;
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

        // 陳列設定確定
        itemSelectionView.OnConfirmSelection
            .Subscribe(selectedItems =>
            {
                itemModel.CreateItemListForDisplay(selectedItems);
            })
            .AddTo(disposables);

        // 閉じるボタン系
        itemSelectionView.OnCloseRequested
            .Subscribe(_ => CloseSelectionPanel())
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
        itemSelectionView.Initialize();
    }

    //陳列画面を表示
    private void OpenSelectionPanel()
    {
        itemSelectionView.PopulateItemList(itemModel.RuntimeItems);
        itemSelectionView.Show();
    }

    //陳列画面を非表示、保存
    private void CloseSelectionPanel()
    {
        itemSelectionView.Hide();
        itemModel.SaveData();
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

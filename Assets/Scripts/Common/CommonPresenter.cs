using UnityEngine;
using R3;
public class CommonPresenter
{
    private readonly CommonView commonView;
    private readonly TomsShopModel tomsShopModel;
    private CompositeDisposable disposables = new();

    public CommonPresenter(CommonView commonView, TomsShopModel tomsShopModel)
    {
        this.commonView = commonView;
        this.tomsShopModel = tomsShopModel;
    }
    private void Bind()
    {
        // 所持金更新（ModelのデータからViewへ）
        tomsShopModel.PlayerMoney
            .Subscribe(money =>
            {
                commonView.UpdatePlayerMoney(money);
            })
            .AddTo(disposables);
        
        // 現在のターン更新（ModelのデータからViewへ）
        tomsShopModel.CurrentTurn.Subscribe(date =>
            {
                commonView.UpdateCurrentTurn(date);
            })
            .AddTo(disposables);
    }
}

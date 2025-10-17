using UnityEngine;
using R3;
public class CommonPresenter
{
    private readonly CommonView commonView;
    private readonly TomsModel tomsModel;
    private CompositeDisposable disposables = new();

    public CommonPresenter(CommonView commonView, TomsModel tomsModel)
    {
        this.commonView = commonView;
        this.tomsModel = tomsModel;
        
        Bind();
    }
    private void Bind()
    {
        // 所持金更新（ModelのデータからViewへ）
        tomsModel.PlayerMoney
            .Subscribe(money =>
            {
                Debug.Log($"PlayerMoney: {money}");
                commonView.UpdatePlayerMoney(money);
            })
            .AddTo(disposables);
        
        // 現在のターン更新（ModelのデータからViewへ）
        tomsModel.CurrentTurn.Subscribe(date =>
            {
                Debug.Log($"CurrentTurn: {date}");
                commonView.UpdateCurrentTurn(date);
            })
            .AddTo(disposables);
    }
}

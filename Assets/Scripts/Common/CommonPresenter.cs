using System;
using UnityEngine;
using R3;
using VContainer.Unity;

public class CommonPresenter:IStartable,IDisposable
{
    private readonly CommonView commonView;
    private readonly TomsModel tomsModel;
    private CompositeDisposable disposables = new();

    public CommonPresenter(CommonView commonView, TomsModel tomsModel)
    {
        this.commonView = commonView;
        this.tomsModel = tomsModel;
    }
    
    public void Start()
    {
        Bind();
    }
    public void Dispose()
    {
        disposables.Dispose();
    }
    
    private void Bind()
    {
        
        Debug.Log("CommonPresenter.Bind");
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

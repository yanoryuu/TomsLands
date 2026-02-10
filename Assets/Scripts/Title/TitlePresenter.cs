using System;
using R3;
using UnityEngine;
using VContainer.Unity;

public class TitlePresenter : IPresenter,IStartable,IDisposable
{
    private TitleView titleView;
    private CompositeDisposable disposable = new CompositeDisposable();
    private StateManager stateManager;
    
    public TitlePresenter(TitleView titleView ,StateManager stateManager)
    {
        this.titleView = titleView;
        this.stateManager = stateManager;
        
        stateManager.RegisterOnEnter(GamePhase.Title,Entry);
    }

    public void Entry()
    {
        
    }
    
    public void Start()
    {
        Bind();
    }

    public void Dispose()
    {
        
    }

    private void Bind()
    {
        titleView.OnNewGameRequested.Subscribe(_ =>
        {
            // ロード処理
            stateManager.ChangePhase(GamePhase.Preparation);
        }).AddTo(disposable);
        
        titleView.OnLoadGameRequested.Subscribe(_ =>
        {
            // ロード処理
            stateManager.ChangePhase(GamePhase.TomsShop);
            Debug.Log("ロード処理");
        }).AddTo(disposable);
    }
}

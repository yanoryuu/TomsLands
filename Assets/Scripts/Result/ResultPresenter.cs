using System;
using UnityEngine;
using VContainer.Unity;

public class ResultPresenter : IPresenter, IDisposable, IStartable
{
    private readonly ResultView resultView;
    private readonly ResultModel resultModel;
    private readonly StateManager stateManager;
    public ResultPresenter(ResultView resultView, ResultModel resultModel, StateManager stateManager)
    {
        this.resultView = resultView;
        this.resultModel = resultModel; 
        this.stateManager = stateManager;
        stateManager.RegisterOnEnter(GamePhase.Result,Entry);
    }

    private void Bind()
    {
        
    }
    
    public void Entry()
    {
        //ここにこの画面に移動した時にここを呼び出す。
    }
    
    public void Start()
    {
        Bind();
    }
    public void Dispose()
    {
        
    }
}

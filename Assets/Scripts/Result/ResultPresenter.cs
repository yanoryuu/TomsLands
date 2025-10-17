using UnityEngine;

public class ResultPresenter : IPresenter
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
        Bind();
    }

    private void Bind()
    {
        
    }
    
    public void Entry()
    {
        //ここにこの画面に移動した時にここを呼び出す。
    }
}

using R3;
using UnityEngine;

public class TitlePresenter
{
    private TitleView titleView;
    private CompositeDisposable disposable = new CompositeDisposable();
    private StateManager stateManager;
    
    public TitlePresenter(TitleView titleView ,StateManager stateManager)
    {
        this.titleView = titleView;
        this.stateManager = stateManager;
        Bind();
    }

    private void Bind()
    {
        titleView.OnNewGameRequested.Subscribe(_ =>
        {
            // ロード処理
            stateManager.currentPhase.Value = GamePhase.TomsShop;
            Debug.Log(stateManager.currentPhase);
        }).AddTo(disposable);
        
        titleView.OnLoadGameRequested.Subscribe(_ =>
        {
            // ロード処理
            stateManager.currentPhase.Value = GamePhase.TomsShop;
            Debug.Log("Load Game Requested");
        }).AddTo(disposable);
    }
}

using R3;
using UnityEngine;

public class TitlePresenter : IPresenter
{
    private TitleView titleView;
    private CompositeDisposable disposable = new CompositeDisposable();
    private StateManager stateManager;
    
    public TitlePresenter(TitleView titleView ,StateManager stateManager)
    {
        this.titleView = titleView;
        this.stateManager = stateManager;
        Bind();
        
        stateManager.RegisterOnEnter(GamePhase.Title,Entry);
    }

    public void Entry()
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
        }).AddTo(disposable);
    }
}

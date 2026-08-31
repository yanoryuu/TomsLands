using System;
using R3;
using VContainer.Unity;

public class GameOverPresenter : IDisposable, IStartable
{
    private readonly GameOverView gameOverView;
    private readonly TomsModel tomsModel;
    private readonly SceneTransitionService sceneTransitionService;
    private readonly CompositeDisposable disposables = new();

    public GameOverPresenter(
        GameOverView gameOverView,
        TomsModel tomsModel,
        SceneTransitionService sceneTransitionService)
    {
        this.gameOverView = gameOverView;
        this.tomsModel = tomsModel;
        this.sceneTransitionService = sceneTransitionService;
    }

    public void Start()
    {
        gameOverView.Setup(tomsModel.CurrentTurn.Value);

        // もう一度：タイトルへ戻る（タイトル画面でニューゲームを選択）
        gameOverView.OnRetryClicked
            .Subscribe(_ =>
            {
                // 破産したランのセーブを削除（残すと「続きから」で破産直前が復活してしまう）
                RunSaveCleaner.DeleteRunFiles();
                sceneTransitionService.GoToTitle();
            })
            .AddTo(disposables);

        // タイトル画面に戻る
        gameOverView.OnGoToTitleClicked
            .Subscribe(_ =>
            {
                RunSaveCleaner.DeleteRunFiles();
                sceneTransitionService.GoToTitle();
            })
            .AddTo(disposables);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

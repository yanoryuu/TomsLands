using System;
using R3;
using VContainer.Unity;

public class GameOverPresenter : IDisposable, IStartable
{
    private readonly GameOverView gameOverView;
    private readonly TomsModel tomsModel;
    private readonly SceneTransitionService sceneTransitionService;
    private readonly MetaProgressModel metaProgress;
    private readonly CompositeDisposable disposables = new();

    private bool metaAwarded;

    public GameOverPresenter(
        GameOverView gameOverView,
        TomsModel tomsModel,
        SceneTransitionService sceneTransitionService,
        MetaProgressModel metaProgress)
    {
        this.gameOverView = gameOverView;
        this.tomsModel = tomsModel;
        this.sceneTransitionService = sceneTransitionService;
        this.metaProgress = metaProgress;
    }

    public void Start()
    {
        gameOverView.Setup(tomsModel.CurrentTurn.Value);

        // もう一度：タイトルへ戻る（タイトル画面でニューゲームを選択）
        gameOverView.OnRetryClicked
            .Subscribe(_ => FinishRunAndGoTitle())
            .AddTo(disposables);

        // タイトル画面に戻る
        gameOverView.OnGoToTitleClicked
            .Subscribe(_ => FinishRunAndGoTitle())
            .AddTo(disposables);
    }

    /// <summary>
    /// 破産ランの後始末: メタ通貨を少量精算（到達ターン分。「無駄なラン」を無くす）してから
    /// ラン内セーブを削除しタイトルへ（残すと「続きから」で破産直前が復活してしまう）。
    /// </summary>
    private void FinishRunAndGoTitle()
    {
        if (!metaAwarded && metaProgress != null)
        {
            metaAwarded = true;
            int earned = metaProgress.RecordRunEnd(
                cleared: false, netWorth: 0, rank: "", totalTurns: tomsModel.CurrentTurn.Value);
            UnityEngine.Debug.Log($"[GameOverPresenter] 破産でもメタ通貨（信用）を獲得: +{earned}");
        }
        RunSaveCleaner.DeleteRunFiles();
        sceneTransitionService.GoToTitle();
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

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

        // もう一度 / タイトルへ: いずれも村へ帰還する（村に[タイトルへ]ボタンがある）
        gameOverView.OnRetryClicked
            .Subscribe(_ => FinishRunAndGoVillage())
            .AddTo(disposables);

        gameOverView.OnGoToTitleClicked
            .Subscribe(_ => FinishRunAndGoVillage())
            .AddTo(disposables);
    }

    /// <summary>
    /// 破産ランの後始末: メタ通貨を少量精算（到達ターン分）+ 手元現金の一部を村資金へ変換
    /// （「破産したけど村は育った」= 敗北の無害化）してから、ラン内セーブを削除し村へ帰還する
    /// （残すと「続きから」で破産直前が復活してしまう）。
    /// </summary>
    private void FinishRunAndGoVillage()
    {
        if (!metaAwarded && metaProgress != null)
        {
            metaAwarded = true;
            int earned = metaProgress.RecordRunEnd(
                cleared: false, netWorth: 0, rank: "", totalTurns: tomsModel.CurrentTurn.Value);

            int finalCash = tomsModel.PlayerMoney.Value;
            int converted = metaProgress.ConvertRunToVillageFunds(
                cleared: false, netWorth: 0, finalCash: finalCash);
            VillageArrivalReport.Set(cleared: false, earned: finalCash, converted: converted);

            UnityEngine.Debug.Log($"[GameOverPresenter] 破産でも信用+{earned} / 村資金+{converted}G");
        }
        RunSaveCleaner.DeleteRunFiles();
        sceneTransitionService.GoToVillage();
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

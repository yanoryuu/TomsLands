using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using VContainer.Unity;

public class GameOverPresenter : IDisposable, IStartable
{
    private readonly GameOverView gameOverView;
    private readonly TomsModel tomsModel;
    private readonly SceneTransitionService sceneTransitionService;
    private readonly MetaProgressModel metaProgress;
    private readonly PopUpManager popUpManager;
    private readonly CompositeDisposable disposables = new();
    private bool demoUpsellShown;

    private bool metaAwarded;

    /// <summary>破産画面の振り返り（このランの記録・最高の儲け・村へ入る額の見込み）。</summary>
    private struct BankruptReview
    {
        public RunHistoryData History;
        public int PeakProfit;
        public int Carried;
        public int VillageFunds;
        public VillageFundsBreakdown Breakdown;
    }

    private BankruptReview? review;

    private BankruptReview Review
    {
        get
        {
            if (review.HasValue) return review.Value;
            var history = RunHistory.Load();
            var peak = RunProfit.ComputePeak(history, metaProgress, tomsModel.PlayerMoney.Value, tomsModel.BankLoanOwed);
            var r = new BankruptReview
            {
                History = history,
                PeakProfit = peak.Profit,
                Carried = peak.Carried,
                // 破産の救済は「土台 + 納付を済ませた分」なので、済ませた回数を渡す
                Breakdown = MetaProgressModel.CalculateVillageFundsBreakdown(false, peak.Profit, peak.Carried, tomsModel.GameMode, tomsModel.DebtCycle.Value),
            };
            r.VillageFunds = r.Breakdown.Total;
            review = r;
            return r;
        }
    }

    // 破産を受け止めるトコの会話。再生中は村へ戻るボタンを受け付けない（会話を途中で切らないため）
    private readonly CancellationTokenSource cts = new();
    private bool isTalking;

    public GameOverPresenter(
        GameOverView gameOverView,
        TomsModel tomsModel,
        SceneTransitionService sceneTransitionService,
        MetaProgressModel metaProgress,
        PopUpManager popUpManager = null)
    {
        this.popUpManager = popUpManager;
        this.gameOverView = gameOverView;
        this.tomsModel = tomsModel;
        this.sceneTransitionService = sceneTransitionService;
        this.metaProgress = metaProgress;
    }

    public void Start()
    {
        var r = Review;
        gameOverView.Setup(tomsModel.CurrentTurn.Value);
        // 体験版は村が無いので「村へ +○G」の行を出さない（-1）。ボタンも「次へ」
        if (DemoBuild.IsDemo) gameOverView.ApplyDemoLabels();
        gameOverView.ShowReview(r.History.moneyValues, r.History.streamWins, r.History.streamLosses,
            r.History.streamEarningsTotal, r.Carried, DemoBuild.IsDemo ? -1 : r.VillageFunds, DemoBuild.IsDemo ? null : r.Breakdown);

        // 村へ戻る（村に[タイトルへ]ボタンがある）。旧「タイトル」ボタンも同じ行き先
        gameOverView.OnRetryClicked
            .Subscribe(_ => FinishRunAndGoVillage())
            .AddTo(disposables);

        gameOverView.OnGoToTitleClicked
            .Subscribe(_ => FinishRunAndGoVillage())
            .AddTo(disposables);

        // 破産画面のあとに、トコがその場で受け止める（いきなり終わらないように）
        PlayBankruptTalkAsync().Forget();
    }

    private async UniTaskVoid PlayBankruptTalkAsync()
    {
        isTalking = true;
        try
        {
            await TokoTalk.PlayAsync(TokoTalk.SelectBankruptLabel(tomsModel.DebtCycle.Value), cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[GameOverPresenter] 破産の会話に失敗しました。\n{e}");
        }
        finally
        {
            isTalking = false;
        }
    }

    /// <summary>
    /// 破産ランの後始末: メタ通貨を少量精算（到達ターン分）+ 手元現金の一部を村資金へ変換
    /// （「破産したけど村は育った」= 敗北の無害化）してから、ラン内セーブを削除し村へ帰還する
    /// （残すと「続きから」で破産直前が復活してしまう）。
    /// </summary>
    private void FinishRunAndGoVillage()
    {
        if (isTalking) return;
        if (DemoBuild.IsDemo)
        {
            // 体験版: 村資金・ランの記録は付けず、ラン内セーブを消して「製品版へ」→ タイトルへ
            if (demoUpsellShown) return;
            demoUpsellShown = true;
            RunSaveCleaner.DeleteRunFiles();
            DemoUpsell.Show(popUpManager, cleared: false, profit: Review.PeakProfit, rank: "",
                goToTitle: sceneTransitionService.GoToTitle);
            return;
        }
        if (!metaAwarded && metaProgress != null)
        {
            metaAwarded = true;
            var review = Review;
            int converted = metaProgress.ConvertRunToVillageFunds(
                cleared: false, profit: review.PeakProfit, carried: review.Carried, mode: tomsModel.GameMode, dueDebtCycles: tomsModel.DebtCycle.Value,
                out var breakdown);

            metaProgress.RecordRunEnd(new RunRecordPlain
            {
                mode = (int)tomsModel.GameMode,
                cleared = false,
                profit = review.PeakProfit,
                netWorth = tomsModel.PlayerMoney.Value,
                rank = "",
                turns = tomsModel.CurrentTurn.Value,
                streamWins = review.History.streamWins,
                streamLosses = review.History.streamLosses,
                streamEarnings = review.History.streamEarningsTotal,
                villageFunds = converted,
                endedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm"),
            });
            VillageArrivalReport.Set(cleared: false, earned: review.PeakProfit, converted: converted, breakdown: breakdown);

            UnityEngine.Debug.Log($"[GameOverPresenter] 破産: 村資金+{converted}G（最高の儲け {review.PeakProfit}G から）");
        }
        RunSaveCleaner.DeleteRunFiles();
        sceneTransitionService.GoToVillage();
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
        disposables.Dispose();
    }
}

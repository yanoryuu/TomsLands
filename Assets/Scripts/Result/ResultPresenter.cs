using System;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// リザルト画面のPresenter（ResultScene 専用）。
/// シーン開始時に統計データを集計してViewに表示し、
/// タイトルへ戻る / もう一度遊ぶ ボタンを処理する。
/// </summary>
public class ResultPresenter : IPresenter, IDisposable, IStartable
{
    private readonly ResultView _resultView;
    private readonly ResultModel _resultModel;
    private readonly SceneTransitionService _sceneTransition;
    private readonly MetaProgressModel _metaProgress;
    private readonly CompositeDisposable _disposables = new();

    private ResultStatisticsData _lastStatistics;
    private bool _metaAwarded;

    public ResultPresenter(
        ResultView resultView,
        ResultModel resultModel,
        SceneTransitionService sceneTransition,
        MetaProgressModel metaProgress)
    {
        _resultView = resultView;
        _resultModel = resultModel;
        _sceneTransition = sceneTransition;
        _metaProgress = metaProgress;
    }

    public void Start()
    {
        Bind();
        // ResultScene はシーン開始時に即表示
        Entry();
    }

    private void Bind()
    {
        // どちらのボタンも「精算 → ラン内セーブ削除 → 村へ帰還」の順を厳守する
        // （先に遷移するとランデータが残り「続きから」に破産/クリア済みランが復活するバグの再発経路になる）
        if (_resultView != null)
        {
            _resultView.OnGoToTitleClicked
                .Subscribe(_ => FinishRunAndGoVillage())
                .AddTo(_disposables);

            _resultView.OnRetryClicked
                .Subscribe(_ => FinishRunAndGoVillage())
                .AddTo(_disposables);
        }
    }

    /// <summary>
    /// リザルト画面を表示する。
    /// シーン開始時に Start() から呼ばれる。
    /// </summary>
    public void Entry()
    {
        Debug.Log("[ResultPresenter] Entry: Building result statistics...");

        SoundManager.Instance?.PlayBGM("リザルト画面");

        var statistics = _resultModel.BuildStatistics();
        _lastStatistics = statistics;

        if (_resultView != null)
        {
            _resultView.UpdateContent(statistics);
        }

        Debug.Log($"[ResultPresenter] Result displayed: Rank={statistics.Rank}, NetWorth={statistics.NetWorth}G");
    }

    /// <summary>
    /// ラン終了の後始末: メタ精算 → ラン内セーブ削除 → 村シーンへ帰還。
    /// </summary>
    private void FinishRunAndGoVillage()
    {
        Debug.Log("[ResultPresenter] Finishing run. Deleting run save data → Village.");
        AwardMetaCurrencyOnce();
        RunSaveCleaner.DeleteRunFiles();
        _sceneTransition.GoToVillage();
    }

    /// <summary>
    /// ランクリアのメタ通貨精算と村資金への変換（1回のみ）。ラン内データを消す前に metaData.json へ加算保存する。
    /// </summary>
    private void AwardMetaCurrencyOnce()
    {
        if (_metaAwarded || _lastStatistics == null || _metaProgress == null) return;
        _metaAwarded = true;

        int earned = _metaProgress.RecordRunEnd(
            cleared: true,
            netWorth: _lastStatistics.NetWorth,
            rank: _lastStatistics.Rank,
            totalTurns: _lastStatistics.TotalTurns);

        // 村資金への変換（村と店の経営を繋ぐ唯一の橋）
        int converted = _metaProgress.ConvertRunToVillageFunds(
            cleared: true,
            netWorth: _lastStatistics.NetWorth,
            finalCash: _lastStatistics.FinalMoney);
        VillageArrivalReport.Set(cleared: true, earned: _lastStatistics.NetWorth, converted: converted);

        Debug.Log($"[ResultPresenter] メタ通貨（信用）+{earned} / 村資金 +{converted}G");
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

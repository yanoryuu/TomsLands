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
        // タイトルへ戻るボタン
        if (_resultView != null)
        {
            _resultView.OnGoToTitleClicked
                .Subscribe(_ =>
                {
                    Debug.Log("[ResultPresenter] Go to title clicked. Deleting run save data.");
                    // メタ通貨を精算してから、ラン内セーブ一式を削除する
                    // （従来は save.json しか消さず、スロットが「続きから」に残り続けるバグがあった）
                    AwardMetaCurrencyOnce();
                    RunSaveCleaner.DeleteRunFiles();
                    _sceneTransition.GoToTitle();
                })
                .AddTo(_disposables);

            // もう一度遊ぶボタン（セーブデータを削除して新規ゲームで再開）
            _resultView.OnRetryClicked
                .Subscribe(_ =>
                {
                    Debug.Log("[ResultPresenter] Retry clicked. Deleting run save data.");
                    AwardMetaCurrencyOnce();
                    RunSaveCleaner.DeleteRunFiles();
                    _sceneTransition.GoToTitle();
                })
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
    /// ランクリアのメタ通貨精算（1回のみ）。ラン内データを消す前に metaData.json へ加算保存する。
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
        Debug.Log($"[ResultPresenter] メタ通貨（信用）を獲得: +{earned}");
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

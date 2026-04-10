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
    private readonly CompositeDisposable _disposables = new();

    public ResultPresenter(
        ResultView resultView,
        ResultModel resultModel,
        SceneTransitionService sceneTransition)
    {
        _resultView = resultView;
        _resultModel = resultModel;
        _sceneTransition = sceneTransition;
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
                    Debug.Log("[ResultPresenter] Go to title clicked.");
                    _sceneTransition.GoToTitle();
                })
                .AddTo(_disposables);

            // もう一度遊ぶボタン（セーブデータを削除して新規ゲームで再開）
            _resultView.OnRetryClicked
                .Subscribe(_ =>
                {
                    Debug.Log("[ResultPresenter] Retry clicked. Starting new game...");
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

        var statistics = _resultModel.BuildStatistics();

        if (_resultView != null)
        {
            _resultView.UpdateContent(statistics);
        }

        Debug.Log($"[ResultPresenter] Result displayed: Rank={statistics.Rank}, NetWorth={statistics.NetWorth}G");
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

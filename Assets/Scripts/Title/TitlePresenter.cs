using System;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

/// <summary>
/// タイトルシーン（Start.unity）のPresenter。
/// 「初めから」「続きから」のボタンに応じてStartModeDataを設定し、TomsShopシーンへ遷移する。
/// </summary>
public class TitlePresenter : IStartable, IDisposable
{
    private readonly TitleView _titleView;
    private readonly StartModeData _startModeData;
    private readonly CompositeDisposable _disposable = new CompositeDisposable();

    public TitlePresenter(TitleView titleView, StartModeData startModeData)
    {
        _titleView = titleView;
        _startModeData = startModeData;
    }

    public void Start()
    {
        SoundManager.Instance?.PlayBGM("OP");
        _titleView.SetContinueButtonVisible(SaveSystem.Exists());

        // フロー選択UIの初期値を GameConst の既定から設定
        _titleView.InitFlowSelection(_startModeData.SelectedMode, GameConst.FlowGeneration.useAutoGeneration);

        Bind();
    }

    public void Dispose()
    {
        _disposable.Dispose();
    }

    private void Bind()
    {
        // 「初めから」ボタン：新規ゲーム開始
        _titleView.OnNewGameRequested.Subscribe(_ =>
        {
            _startModeData.SetNewGame();
            // タイトルUIで選んだフロー設定（モード・自動/手動）を反映
            _startModeData.SetFlowSelection(_titleView.SelectedMode, _titleView.UseAutoGeneration);
            Debug.Log($"[TitlePresenter] 初めから → TomsShopシーンへ遷移 (mode={_titleView.SelectedMode}, auto={_titleView.UseAutoGeneration})");
            SceneManager.LoadScene("TomsShop");
        }).AddTo(_disposable);

        // 「続きから」ボタン：セーブデータをロードして続行
        _titleView.OnLoadGameRequested.Subscribe(_ =>
        {
            _startModeData.SetContinue();
            Debug.Log("[TitlePresenter] 続きから → TomsShopシーンへ遷移");
            SceneManager.LoadScene("TomsShop");
        }).AddTo(_disposable);
    }
}

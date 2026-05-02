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
            Debug.Log("[TitlePresenter] 初めから → TomsShopシーンへ遷移");
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

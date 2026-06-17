using System;
using UnityEngine;
using R3;
using UnityEngine.SceneManagement;
using VContainer.Unity;

public class CommonPresenter:IStartable,IDisposable
{
    private const string SettingSceneName = "Setting";

    private readonly CommonView commonView;
    private readonly TomsModel tomsModel;
    private CompositeDisposable disposables = new();

    // メニュー（Setting）シーンのロード中フラグ。連打による多重加算ロードを防ぐ。
    private bool isMenuTransitioning;

    public CommonPresenter(CommonView commonView, TomsModel tomsModel)
    {
        this.commonView = commonView;
        this.tomsModel = tomsModel;
    }
    
    public void Start()
    {
        Bind();
    }
    public void Dispose()
    {
        disposables.Dispose();
    }
    
    private void Bind()
    {
        
        Debug.Log("CommonPresenter.Bind");
        // 所持金更新（ModelのデータからViewへ）
        tomsModel.PlayerMoney
            .Subscribe(money =>
            {
                Debug.Log($"PlayerMoney: {money}");
                commonView.UpdatePlayerMoney(money);
            })
            .AddTo(disposables);
        
        // 現在のターン更新（ModelのデータからViewへ）
        tomsModel.CurrentTurn.Subscribe(date =>
            {
                Debug.Log($"CurrentTurn: {date}");
                commonView.UpdateCurrentTurn(date);
            })
            .AddTo(disposables);

        commonView.OnMenuButtonClicked.Subscribe(_ => OpenSettingScene())
            .AddTo(disposables);
    }

    /// <summary>
    /// 設定（Setting）シーンを加算ロードする。
    /// 連打しても多重にロードされないよう、ロード中フラグと既ロード判定でガードする。
    /// </summary>
    private void OpenSettingScene()
    {
        // ロード処理中の連打を無視
        if (isMenuTransitioning) return;
        // すでに開いている場合は無視
        if (SceneManager.GetSceneByName(SettingSceneName).isLoaded) return;

        isMenuTransitioning = true;
        var op = SceneManager.LoadSceneAsync(SettingSceneName, LoadSceneMode.Additive);
        op.completed += _ => isMenuTransitioning = false;
    }
}

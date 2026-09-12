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
    private readonly SellOrderModel sellOrderModel;
    private readonly MarketingFacade marketingFacade;
    private CompositeDisposable disposables = new();

    // メニュー（Setting）シーンのロード中フラグ。連打による多重加算ロードを防ぐ。
    private bool isMenuTransitioning;

    public CommonPresenter(CommonView commonView, TomsModel tomsModel, SellOrderModel sellOrderModel, MarketingFacade marketingFacade)
    {
        this.commonView = commonView;
        this.tomsModel = tomsModel;
        this.sellOrderModel = sellOrderModel;
        this.marketingFacade = marketingFacade;
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
        
        // 現在のターン更新（ModelのデータからViewへ）。
        // バズ中は残りターン数も併記する（バズ演出側の表記が小さく初見で伝わりにくいため）
        tomsModel.CurrentTurn.Subscribe(date =>
            {
                Debug.Log($"CurrentTurn: {date}");
                RefreshTurnText();
            })
            .AddTo(disposables);

        var buzz = marketingFacade?.Buzz;
        if (buzz != null)
        {
            buzz.RemainingTurns.Subscribe(_ => RefreshTurnText()).AddTo(disposables);
            buzz.CurrentBuzzType.Subscribe(_ => RefreshTurnText()).AddTo(disposables);
        }

        // 未約定の売り注文の見込み入金額（所持金の隣のバッジ）
        sellOrderModel.PendingTotalEstimate
            .Subscribe(amount => commonView.UpdatePendingIncome(amount))
            .AddTo(disposables);

        commonView.OnMenuButtonClicked.Subscribe(_ => OpenSettingScene())
            .AddTo(disposables);
    }

    /// <summary>
    /// ターン表示を組み立てる。バズ中なら「バズ中！残りNターン」を併記。
    /// </summary>
    private void RefreshTurnText()
    {
        string buzzInfo = null;
        var buzz = marketingFacade?.Buzz;
        if (buzz != null && buzz.RemainingTurns.Value > 0)
        {
            string label = buzz.CurrentBuzzType.Value switch
            {
                BuzzType.Flame => "炎上中…",
                BuzzType.Big => "超バズ中！",
                _ => "バズ中！",
            };
            buzzInfo = $"{label} 残り{buzz.RemainingTurns.Value}ターン";
        }

        commonView.UpdateCurrentTurn(tomsModel.CurrentTurn.Value, buzzInfo);
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

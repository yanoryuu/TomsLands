using System;
using R3;
using VContainer.Unity;

public class CommonPresenter : IStartable, IDisposable
{
    private readonly CommonView commonView;
    private readonly TomsModel tomsModel;
    private readonly SellOrderModel sellOrderModel;
    private readonly MarketingFacade marketingFacade;
    private readonly CompositeDisposable disposables = new();

    public CommonPresenter(CommonView commonView, TomsModel tomsModel, SellOrderModel sellOrderModel, MarketingFacade marketingFacade)
    {
        this.commonView = commonView;
        this.tomsModel = tomsModel;
        this.sellOrderModel = sellOrderModel;
        this.marketingFacade = marketingFacade;
    }

    public void Start() => Bind();

    public void Dispose() => disposables.Dispose();

    private void Bind()
    {
        // 所持金更新（ModelのデータからViewへ）
        tomsModel.PlayerMoney
            .Subscribe(money => commonView.UpdatePlayerMoney(money))
            .AddTo(disposables);

        // 現在のターン更新（ModelのデータからViewへ）。
        // バズ中は残りターン数も併記する（バズ演出側の表記が小さく初見で伝わりにくいため）
        tomsModel.CurrentTurn.Subscribe(_ => RefreshTurnText())
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
    /// 連打や店ホームの Start / Esc と重なっても多重にロードされないよう、SettingSceneOpener のガードを通す。
    /// </summary>
    private static void OpenSettingScene() => SettingSceneOpener.Open();
}

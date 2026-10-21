using System;
using R3;
using VContainer.Unity;

public class CommonPresenter : IStartable, IDisposable
{
    private readonly CommonView commonView;
    private readonly TomsModel tomsModel;
    private readonly SellOrderModel sellOrderModel;
    private readonly MarketingFacade marketingFacade;
    private readonly GameFlowManager gameFlowManager;
    private readonly MetaProgressModel meta;
    private readonly StateManager stateManager;
    private readonly CompositeDisposable disposables = new();

    public CommonPresenter(CommonView commonView, TomsModel tomsModel, SellOrderModel sellOrderModel, MarketingFacade marketingFacade,
        GameFlowManager gameFlowManager = null, MetaProgressModel meta = null, StateManager stateManager = null)
    {
        this.commonView = commonView;
        this.tomsModel = tomsModel;
        this.sellOrderModel = sellOrderModel;
        this.marketingFacade = marketingFacade;
        this.gameFlowManager = gameFlowManager;
        this.meta = meta;
        this.stateManager = stateManager;
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

        // 次の配信まで（G-3: 賭けの締め切りを常に見せる。K-11: 最後の配信を知らせる）
        tomsModel.CurrentTurn.Subscribe(_ => RefreshStreamInfo()).AddTo(disposables);
        if (gameFlowManager != null)
            gameFlowManager.IsAwaitingStream.Subscribe(_ => RefreshStreamInfo()).AddTo(disposables);
        // 占い師に出会った（＝配信を知った）直後にも出せるよう、画面が変わるたびに見直す
        if (stateManager != null)
            stateManager.CurrentTomsShopPhase.Subscribe(_ => RefreshStreamInfo()).AddTo(disposables);
    }

    /// <summary>
    /// HUD の「次の配信まで」を組み立てる。1周目で占い師に出会う前（配信をまだ知らない）は出さない。
    /// </summary>
    private void RefreshStreamInfo()
    {
        if (gameFlowManager == null)
        {
            commonView.UpdateStreamInfo(null);
            return;
        }
        bool streamKnown = meta == null || meta.TotalRuns > 0 || meta.HasSeenTutorial(TutorialSteps.ProphetMeeting);
        commonView.UpdateStreamInfo(streamKnown ? BuildStreamInfo(gameFlowManager) : null);
    }

    /// <summary>「明日は配信（2/4）」「今日は最後の配信（4/4）」「次の配信まで あと3日（1/4）」。配信が残っていなければ null。</summary>
    public static string BuildStreamInfo(GameFlowManager flow)
    {
        int days = flow.GetDaysUntilNextStream();
        if (days < 0) return null;
        flow.GetStreamProgress(out int upcoming, out int total);
        bool final = upcoming > 0 && upcoming == total;
        string head = days switch
        {
            0 => final ? "今日は最後の配信" : "今日は配信",
            1 => final ? "明日は最後の配信" : "明日は配信",
            _ => final ? $"最後の配信まで あと{days}日" : $"次の配信まで あと{days}日",
        };
        return total > 0 && upcoming > 0 ? $"{head}（{upcoming}/{total}）" : head;
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
            buzzInfo = $"{label}残り{buzz.RemainingTurns.Value}日"; // HUD の1行に収める（下の行は次の配信まで）
        }

        commonView.UpdateCurrentTurn(tomsModel.CurrentTurn.Value, buzzInfo);
    }

    /// <summary>
    /// 設定（Setting）シーンを加算ロードする。
    /// 連打や店ホームの Start / Esc と重なっても多重にロードされないよう、SettingSceneOpener のガードを通す。
    /// </summary>
    private static void OpenSettingScene() => SettingSceneOpener.Open();
}

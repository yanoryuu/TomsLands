using R3;
using System;
using UnityEngine;

public class StateManager : IDisposable
{
    public ReactiveProperty<GamePhase> CurrentPhase { get; private set; }

    // --- Dependencies (null 許容: 未実装の画面は後で差し替え可能) ---
    private readonly StreamingItemPresenter streamingItemPresenter;
    private readonly ItemShopView itemShopView;
    private readonly PreparationView preparationView;
    private readonly StreamingView streamingView;
    private readonly EndPhaseView endPhaseView;
    private readonly TomsShopView tomsShopView;
    private readonly BattleManager battleManager;
    private readonly TitleView titleView;
    private readonly GamePanelManager gamePanelManager;

    private readonly CompositeDisposable disposables = new();

    // フェーズ毎の Enter 処理（必要になったら追加）
    private readonly System.Collections.Generic.Dictionary<GamePhase, Action> onEnter
        = new System.Collections.Generic.Dictionary<GamePhase, Action>();

    public StateManager(
        StreamingItemPresenter streamingItemPresenter,
        ItemShopView itemShopView,
        PreparationView preparationView,
        StreamingView streamingView,
        EndPhaseView endPhaseView,
        TomsShopView tomsShopView,
        BattleManager battleManager,
        TitleView titleView,
        GamePanelManager gamePanelManager
    )
    {
        this.streamingItemPresenter = streamingItemPresenter;
        this.itemShopView = itemShopView;
        this.preparationView = preparationView;
        this.streamingView = streamingView;
        this.endPhaseView = endPhaseView;
        this.tomsShopView = tomsShopView;
        this.battleManager = battleManager;
        this.titleView = titleView;
        this.gamePanelManager = gamePanelManager;

        CurrentPhase = new ReactiveProperty<GamePhase>(GamePhase.Title);

        ConfigureEnterHandlers();
        Bind();
    }

    private void ConfigureEnterHandlers()
    {
        onEnter[GamePhase.Title] = () =>
        {
            // 例: タイトル初期化
            if (titleView != null) { /* titleView.Initialize(); */ }
        };

        onEnter[GamePhase.Preparation] = () =>
        {
            // 価格更新や在庫反映など
            // 例: preparationView.Initialize();
        };

        onEnter[GamePhase.TomsShop] = () =>
        {
            // トムの店トップ。必要なら在庫更新など
            // tomsShopView?.Refresh();
        };

        onEnter[GamePhase.BlackSmith] = () =>
        {
            // 鍛冶屋画面の初期化
            // e.g. blacksmithPresenter?.Initialize();
        };

        onEnter[GamePhase.ToolShop] = () =>
        {
            // 道具屋画面の初期化
            // itemShopView?.Initialize();
        };

        onEnter[GamePhase.InfoBroker] = () =>
        {
            // 情報屋画面の初期化
            // infoBrokerPresenter?.Initialize();
        };

        onEnter[GamePhase.StreamingSetting] = () =>
        {
            // 配信前の品出し選択など
            // streamingSettingView?.Initialize();
        };

        onEnter[GamePhase.Streaming] = () =>
        {
            streamingItemPresenter?.Initialize();
            battleManager?.BattleStart();
            // streamingView?.Begin();
        };

        onEnter[GamePhase.StreamingResult] = () =>
        {
            // リザルト集計
            // streamingResultView?.ShowResults();
        };

        onEnter[GamePhase.Setting] = () =>
        {
            // 設定画面の初期化
            // settingView?.Open();
        };

        onEnter[GamePhase.End] = () =>
        {
            // エンドフェーズ表示
            // endPhaseView?.Show();
        };
    }

    private void Bind()
    {
        // フェーズ購読: パネル切替 → Enter ハンドラ実行
        CurrentPhase
            .Subscribe(phase =>
            {
                // まず UI 切り替え（GamePanelManager に一本化）
                gamePanelManager?.ShowPanel(phase);

                // その後フェーズ固有の処理
                if (onEnter.TryGetValue(phase, out var enter))
                {
                    try
                    {
                        enter?.Invoke();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[StateManager] OnEnter error at {phase}: {e}");
                    }
                }

                Debug.Log($"[StateManager] Phase changed to {phase}");
            })
            .AddTo(disposables);
    }

    /// <summary>
    /// 外部からフェーズ変更する入口。UI 切替と処理は購読側で実行される。
    /// </summary>
    public void ChangePhase(GamePhase phase)
    {
        if (CurrentPhase.Value == phase) return;
        CurrentPhase.Value = phase;
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
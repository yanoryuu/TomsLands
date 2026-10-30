using Cysharp.Threading.Tasks;
using R3;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// FightScene の EntryPoint。
/// StreamingSetting → Battle → Result → TomsShop の順にフェーズを制御する。
/// パネルの表示/非表示は BattlePanelManager が一元管理する。
/// </summary>
public class BattleSceneStarter : IAsyncStartable, IDisposable
{
    private readonly BattleSequencer _battleSequencer;
    private readonly BattleInputData _inputData;
    private readonly BattleOutputData _outputData;
    private readonly SceneTransitionService _sceneTransition;
    private readonly IDungeonCatalog _dungeonCatalog;
    private readonly StreamingSalesController _salesController;
    private readonly ItemModel _itemModel;
    private readonly TomsModel _tomsModel;
    private readonly StreamingSettingPresenter _settingPresenter;
    private readonly BattleResultView _resultView;
    private readonly BattlePanelManager _panelManager;
    private readonly BattleControlView _controlView;
    private readonly TutorialScenarioService _tutorialScenario;
    private readonly InterventionPresenter _interventions; // 介入（勇者側・ダンジョン側）
    private readonly SuperChatGenerator _superChats;       // 視聴者ランダムスパチャ（収入にしない）
    private readonly DemonKingPayoutView _payoutView;      // 配当通知書（未配線なら null。勇者敗北時のみ表示）
    private readonly BattleOutlookMeterView _outlookMeter; // 勝敗メーター（未配線なら null）
    private readonly ShopEconomySettings _economySettings; // 「相場への影響」の見込み（ストップ高/安）。未登録なら既定値
    private readonly StreamingInteractionSettings _interactionSettings;

    /// <summary>勝敗メーター・配信収支メーターを更新する間隔（秒）。</summary>
    private const float LiveHudInterval = 0.25f;
    /// <summary>決着の溜め（演出が無いときの待ち）の秒。</summary>
    private const float FinaleFallbackSeconds = 1.2f;
    private const string FinaleHeroLabel = "勝利！";
    private const string FinaleDemonLabel = "配当確定";
    private const string FinaleDefeatLabel = "敗北……";

    // 配信チュートリアルのシナリオラベル
    private const string TutorialStreamingPrepare = "Tutorial_StreamingPrepare";
    private const string TutorialStreamingStart   = "Tutorial_StreamingStart";

    // #9 トーストの置き場所（配信中は映像の下・売り場の棚の上の帯）。配信を終えたら・シーンを出たら既定に戻す
    private ToastPlacement _toastBand;
    private DungeonPhaseGaugeView _phaseGauge;

    // ポーズ・在庫切れ管理
    private readonly BattlePauseController _pauseController = new();
    private bool _isRestockPopupShowing = false;
    private bool _isEndConfirmShowing = false;
    private int _battleSpending = 0; // バトル中の補充購入で使った金額
    // 戦闘ループの例外で途中終了したか（勝負配信の宣言の回数を戻すかの判定。配信終了ボタンの途中終了とは分ける）
    private bool _abortedByError;
    // バトル中に補充した数量（itemId → 合計数）。売却数の集計（BuildSoldItems）で使用
    private readonly Dictionary<string, int> _restockedQuantities = new();
    // 補充時の単価（itemId → 単価）。売れ残り分の返金計算で使用
    private readonly Dictionary<string, int> _restockUnitCosts = new();
    private UniTaskCompletionSource<(BattleResult result, string weaponId, string armorId)> _battleTcs;
    // 配信前の店の値段と在庫（装備の「相場への影響」用。配信中の値動きは店に持ち帰らないので、店の値段はこれ）
    private readonly Dictionary<string, (int price, int stock)> _shopSnapshot = new();

    public BattleSceneStarter(
        BattleSequencer battleSequencer,
        BattleInputData inputData,
        BattleOutputData outputData,
        SceneTransitionService sceneTransition,
        IDungeonCatalog dungeonCatalog,
        StreamingSalesController salesController,
        ItemModel itemModel,
        TomsModel tomsModel,
        StreamingSettingPresenter settingPresenter,
        BattleResultView resultView,
        BattlePanelManager panelManager,
        BattleControlView controlView,
        TutorialScenarioService tutorialScenario,
        InterventionPresenter interventions,
        SuperChatGenerator superChats,
        IObjectResolver resolver)
    {
        // 配当通知書・勝敗メーターは任意の画面なので、未登録でも解決エラーにしない
        resolver.TryResolve(out _payoutView);
        resolver.TryResolve(out _outlookMeter);
        resolver.TryResolve(out _economySettings);
        resolver.TryResolve(out _interactionSettings);
        _interventions = interventions;
        _superChats = superChats;
        _tutorialScenario = tutorialScenario;
        _battleSequencer = battleSequencer;
        _inputData = inputData;
        _outputData = outputData;
        _sceneTransition = sceneTransition;
        _dungeonCatalog = dungeonCatalog;
        _salesController = salesController;
        _itemModel = itemModel;
        _tomsModel = tomsModel;
        _settingPresenter = settingPresenter;
        _resultView = resultView;
        _panelManager = panelManager;
        _controlView = controlView;
    }

    public async UniTask StartAsync(CancellationToken cancellation)
    {
        Debug.Log("[BattleSceneStarter] FightScene started. Beginning phase flow...");

        if (_battleSequencer == null)
        {
            Debug.LogError("[BattleSceneStarter] BattleSequencer is null!");
            return;
        }

        if (_inputData == null)
        {
            Debug.LogError("[BattleSceneStarter] BattleInputData is null!");
            return;
        }

        // --- Phase 1: StreamingSetting（品出し設定） ---
        Debug.Log("[BattleSceneStarter] Phase 1: StreamingSetting");
        if (_panelManager != null) _panelManager.ShowPanel(StreamingGamePhase.StreamingSetting);

        // 初回だけ、占い師が品出しのやり方を説明する
        if (_tutorialScenario != null)
            await _tutorialScenario.PlayOnceAsync(TutorialStreamingPrepare, cancellation);

        var selectedItems = await _settingPresenter.RunAsync();

        // 選択結果を BattleInputData に書き込み
        var battleItems = new List<BattleInputItem>();
        foreach (var kv in selectedItems)
        {
            var runtime = _itemModel.GetRuntimeItem(kv.Key);
            // 持ち込み数は今の在庫が上限（前回の品出し数が在庫より多いまま確定されても、在庫を超えて売らない）。
            // StreamingSalesController.Setup が在庫をこの数で上書きするので、必ずここで丸める
            int quantity = runtime != null ? Mathf.Min(kv.Value, runtime.Stock.Value) : 0;
            if (quantity <= 0) continue;
            battleItems.Add(new BattleInputItem
            {
                ItemId = kv.Key,
                Quantity = quantity,
                Price = runtime != null ? runtime.CurrentPrice.Value : 0
            });
        }
        _inputData.SelectedItems = new List<BattleInputItem>(battleItems);
        Debug.Log($"[BattleSceneStarter] StreamingSetting confirmed. {battleItems.Count} items selected.");

        // --- Phase 2: Battle（配信中・戦闘） ---
        Debug.Log("[BattleSceneStarter] Phase 2: Battle");
        if (_panelManager != null) _panelManager.ShowPanel(StreamingGamePhase.Streaming);

        // #9 配信中のお知らせ（売り切れ・補充）は、勇者の映像に重ならないよう映像の下・売り場の棚の上の帯に 1 行で出す
        _toastBand = ToastPlacement.Band(MeasureToastBand);
        Toast.SetPlacement(_toastBand);

        // カウントダウン前にダンジョン（背景）と品出しアイテムを反映しておく。
        // 後回しにするとカウントダウン中にシーン既定の背景・空の在庫が映ってしまう
        var targetDungeon = ResolveTargetDungeon();
        if (targetDungeon == null)
        {
            Debug.LogError($"[BattleSceneStarter] Dungeon not found: {_inputData.DungeonKey}");
            return;
        }

        _battleSequencer.SetDungeon(targetDungeon, _inputData.DungeonLevel);

        // 店の値段・在庫を控える（配信中は Setup で在庫・値段が配信用に書き換わる）。賭けの立場もここで決める
        SnapshotShopState();
        var director = _interventions?.CommentDirector;
        if (director != null) director.SetBetStance(ResolveBetStance());

        // 計測ログ（BalanceLog）: この配信の行にダンジョン・Lv・日・配信開始時の見込みの段を付ける
        BalanceLog.BeginStream(_inputData.DungeonKey.ToString(), _inputData.DungeonLevel,
            _tomsModel != null ? _tomsModel.CurrentTurn.Value : -1, _inputData.GameFlowIndex,
            _inputData.PreStreamClearPct);

        // 勇者モデルを構築
        var heroModel = new HeroModel();
        heroModel.ApplyEquippedItems(_inputData.EquippedItemIds);
        Debug.Log($"[BattleSceneStarter] Hero equipped items: {_inputData.EquippedItemIds.Count}");

        // ポーズコントローラーを各コンポーネントへ配布
        _battleSequencer.SetPauseController(_pauseController);
        if (_salesController != null) _salesController.SetPauseController(_pauseController);
        if (_salesController != null) _salesController.SetHeroTactics(heroModel.heroData?.tactics.Value ?? HeroTactics.Balanced);

        // 販売ループは Setup で動き出すので、カウントダウン・チュートリアルの間はポーズしておく。
        // 途中で例外が出てもポーズが残らないよう finally で必ず解除する
        // （残ると販売ループ・戦闘が WaitIfPausedAsync で止まったまま進行不能になる）
        _pauseController.Pause();
        try
        {
            // StreamingSalesController に選択アイテムを渡して初期化
            if (_salesController != null)
            {
                _salesController.Setup(_itemModel, _inputData.SelectedItems);
                Debug.Log($"[BattleSceneStarter] StreamingSalesController initialized with {_inputData.SelectedItems.Count} items.");
            }

            // 配信開始前の3秒カウントダウン（戦闘・販売ループはこの後に始まる）
            if (_controlView != null)
                await RunPreludeSafelyAsync(() => _controlView.PlayCountdownAsync(3, cancellation), "カウントダウン");

            // 初回だけ、配信画面の見かたを説明する（戦闘が始まる前に挟む）
            if (_tutorialScenario != null)
                await RunPreludeSafelyAsync(() => _tutorialScenario.PlayOnceAsync(TutorialStreamingStart, cancellation), "配信チュートリアル");
        }
        finally
        {
            _pauseController.Resume();
            if (_controlView != null) _controlView.UpdatePauseButtonText(false);
        }

        // バトル終了を待つための UniTaskCompletionSource
        _battleTcs = new UniTaskCompletionSource<(BattleResult result, string weaponId, string armorId)>();
        int defeatedMobCount = 0;
        int defeatedBossCount = 0;

        // バトル終了時のCancellationTokenSource（ポップアップ等を終わらせるため）
        using var battleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);

        var disposables = new CompositeDisposable();

        _battleSequencer.OnBattleWin
            .Subscribe(win => _battleTcs.TrySetResult((BattleResult.Victory, win.weaponId, win.armorId)))
            .AddTo(disposables);

        _battleSequencer.OnBattleDefeat
            .Subscribe(defeat => _battleTcs.TrySetResult((BattleResult.Defeat, defeat.weaponId, defeat.armorId)))
            .AddTo(disposables);

        // 戦闘ループの例外は敗北（防衛報酬あり）にせず途中終了として扱う
        _battleSequencer.OnBattleError
            .Subscribe(err =>
            {
                if (_battleTcs.TrySetResult((BattleResult.Abort, err.weaponId, err.armorId))) _abortedByError = true;
            })
            .AddTo(disposables);

        _battleSequencer.OnEnemyDefeated
            .Subscribe(enemy =>
            {
                if (enemy.IsBoss)
                    defeatedBossCount++;
                else
                    defeatedMobCount++;
            })
            .AddTo(disposables);

        // 一時停止ボタン
        if (_controlView != null)
        {
            _controlView.OnPauseToggled
                .Subscribe(_ => TogglePause())
                .AddTo(disposables);

            // 配信終了ボタン（確認してから途中終了）
            _controlView.OnEndBattleRequested
                .Subscribe(_ => ConfirmEndStreamingAsync(battleCts.Token).Forget())
                .AddTo(disposables);

            // ×1 / ×2（戦闘・販売ループ・介入のクールダウンを同じ比で縮める）
            _controlView.OnSpeedToggled
                .Subscribe(_ =>
                {
                    float speed = BattleSpeed.Toggle();
                    _controlView.UpdateSpeedButtonText();
                    Debug.Log($"[BattleSceneStarter] 配信の速さ ×{speed:0}");
                })
                .AddTo(disposables);
            _controlView.UpdateSpeedButtonText();
        }

        // 在庫切れ検知
        if (_salesController != null)
        {
            _salesController.OnItemStockDepleted
                .Subscribe(item =>
                {
                    // #9 補充ポップアップを出せない（別の補充・終了確認の最中）ときは、売り切れをトーストで知らせる。
                    // ポップアップ（AutoPlay が答える）はこれまでどおり。トーストは入力を受けない
                    if (_isRestockPopupShowing || _isEndConfirmShowing)
                        Toast.Show($"「{item.ItemName}」も売り切れ（売り場の枠を押すと補充）", item.ItemIcon, ToastKind.Warn);
                    TryShowRestockAsync(item, battleCts.Token).Forget();
                })
                .AddTo(disposables);
        }

        // 在庫切れスロットをクリックして補充ポップアップを再表示
        void OnDepletedItemClicked(ItemSlotView slot)
        {
            if (slot == null || slot.CurrentItem == null || slot.CurrentItem.Stock.Value > 0) return;
            TryShowRestockAsync(slot.CurrentItem, battleCts.Token).Forget();
        }
        ItemSlotView.OnItemClicked += OnDepletedItemClicked;

        // 介入の受付開始（キューを BattleSequencer に渡すので StartBattle より前）。
        // 補充費用は利用可能残高の計算に使う
        // ダンジョン側の値段はこのダンジョンLvの防衛報酬に合わせる（G-6）
        _interventions?.Start(_salesController, () => _battleSpending, InterventionPriceBase());

        // ボスの状況の点数に使う勝敗メーターは、画面のメーターと同じ見込み（介入の効果込み）
        if (_salesController != null)
            _salesController.SetOutlookProvider(() => _battleSequencer.Context != null
                ? BattleOutlookEstimator.Estimate(_battleSequencer.Context, _interventions?.Queue.Effects, _interactionSettings)
                : 0.5f);

        _battleSequencer.StartBattle(heroModel);

        // 勝敗メーター・配信収支メーター（決着まで）
        RunLiveHudAsync(battleCts.Token).Forget();

        // バトル終了待ち
        var battleResult = await _battleTcs.Task;
        Debug.Log($"[BattleSceneStarter] Battle finished: {battleResult.result}");

        // バトル終了 → ポーズ解除してからループキャンセル（WaitIfPausedAsync が抜けられるように）
        _pauseController.Resume();
        battleCts.Cancel();
        // 途中終了でも戦闘ループを止める（止めないとリザルト表示中も裏で攻撃SE・価格変動が続く）
        _battleSequencer.StopBattle();

        // 介入の受付を閉じる（未実行の指示は返金）→ 視聴者スパチャの発生を止める。
        // どちらも StopSales・スナップショットより前（§5-3 の停止順序）
        int interventionRefund = _interventions != null ? _interventions.Stop() : 0;
        _superChats?.Stop();

        ItemSlotView.OnItemClicked -= OnDepletedItemClicked;
        disposables.Dispose();
        RestoreToastPlacement(); // リザルト以降は既定の置き場所（画面上部の中央）

        // 販売ループを停止してから結果を集計する。
        // 停止しないとリザルト画面中も売れ続け、ここで取るスナップショットとの差分
        // （売上金・売却数）が精算されず消失する。
        if (_salesController != null) _salesController.StopSales();

        // BattleOutputData に結果を書き込み
        var soldItems = BuildSoldItems();
        int rawSales = _salesController != null ? _salesController.GetTotalSalesValue() : 0;
        int restockRefund = CalculateUnsoldRestockRefund(soldItems); // 補充分の売れ残りは返金
        var defeatRewardBreakdown = CalculateDefeatReward(battleResult.result); // 勇者敗北時（ダンジョン防衛成功）の報酬
        int defeatReward = defeatRewardBreakdown.Total;
        // 介入: 払った総額を引き、未実行分の返金を戻す（介入なしならどちらも 0 で従来と同じ式）
        int interventionSpent = _interventions != null ? _interventions.TotalSpent : 0;
        // 村の広場 Lv5「勝負配信の宣言」: 当たれば配信の売上と防衛報酬 ×1.5（上乗せ分を純利益に足す）。途中終了は無効
        var declarationSide = StreamDeclaration.ActiveFor(_tomsModel, _inputData.GameFlowIndex);
        var declarationOutcome = StreamDeclaration.Judge(declarationSide, battleResult.result, _abortedByError);
        int declarationBonus = declarationOutcome == StreamDeclarationOutcome.Hit ? StreamDeclaration.Bonus(rawSales, defeatReward) : 0;
        // 勇者敗北の返金騒ぎ: 勇者が負けたら、この配信で売れた武器・防具の売上の一部を視聴者に返す（勇者に張った分だけ痛い）。
        // 売上の一部なので、補充・介入と同じく配信の収支から引く（割合は 0〜1 なので売上を超えて引くことはない）
        float heroDefeatRefundRate = _salesController != null ? _salesController.HeroDefeatRefundRate : 0f;
        int heroDefeatRefund = HeroDefeatRefund.Calculate(battleResult.result, soldItems,
            HeroDefeatRefund.TypeLookup(_itemModel), heroDefeatRefundRate);
        int totalEarnings = rawSales - _battleSpending + restockRefund + defeatReward - interventionSpent + interventionRefund + declarationBonus
                            - heroDefeatRefund;
        if (heroDefeatRefund > 0)
            Debug.Log($"[BattleSceneStarter] 返金騒ぎ: 武器・防具の売上の {heroDefeatRefundRate:P0} → -{heroDefeatRefund}G");
        if (declarationSide != StreamDeclarationSide.None)
            Debug.Log($"[BattleSceneStarter] 勝負配信の宣言「{StreamDeclaration.Label(declarationSide)}」→ {declarationOutcome}（上乗せ {declarationBonus}G）");
        if (restockRefund > 0)
            Debug.Log($"[BattleSceneStarter] 補充分の売れ残りを返金: {restockRefund}G");
        _outputData.SetResult(battleResult.result, battleResult.weaponId, battleResult.armorId, soldItems, totalEarnings, defeatedMobCount, defeatedBossCount);
        _outputData.AbortedByError = battleResult.result == BattleResult.Abort && _abortedByError;
        if (_interventions != null)
        {
            _outputData.SetStreamingStats(interventionSpent, _interventions.TotalRefunded, _interventions.PeakViewers,
                _interventions.ViewerSuperChatCount, _interventions.SpecialMovesUsed);
        }
        _outputData.SetDefenseReward(defeatReward, _inputData.DungeonKey);
        _outputData.SetDeclaration(declarationSide, declarationOutcome, declarationBonus);
        _outputData.SetHeroDefeatRefund(heroDefeatRefund);

        var settlement = new BattleSettlement(battleResult.result, rawSales, _battleSpending, restockRefund,
            _outputData.InterventionNetSpending, defeatReward, totalEarnings, soldItems,
            BuildMarketImpacts(battleResult.result, battleResult.weaponId, battleResult.armorId, soldItems, defeatedBossCount))
        {
            DeclarationSide = declarationSide,
            DeclarationOutcome = declarationOutcome,
            DeclarationBonus = declarationBonus,
            HeroDefeatRefund = heroDefeatRefund,
            HeroDefeatRefundRate = heroDefeatRefundRate,
        };

        // 計測ログ: 配信 1 本の決着（stream_result）。お金の動きは変えない
        WriteStreamResultLog(battleResult.result, rawSales, restockRefund, heroDefeatRefund, interventionSpent - interventionRefund,
            declarationBonus, totalEarnings, defeatRewardBreakdown, defeatedMobCount, defeatedBossCount);

        // --- Phase 2.5: 決着の溜め（配信画面のまま。勝敗のコメント弾幕と最後の売上を見せる） ---
        if (battleResult.result != BattleResult.Abort)
            await PlayFinaleAsync(battleResult.result, settlement.IsDemonPayout, cancellation);
        // 宣言が当たったら、続けて「宣言的中！」の帯（決着の帯と同じ作り。勇者側は金・魔王側は紫）
        if (declarationOutcome == StreamDeclarationOutcome.Hit)
            await PlayDeclarationHitAsync(declarationSide, cancellation);

        // --- Phase 3: Result（配信リザルト画面） ---
        Debug.Log("[BattleSceneStarter] Phase 3: Result");
        if (_panelManager != null) _panelManager.ShowPanel(StreamingGamePhase.StreamingResult);
        if (_resultView != null)
        {
            await _resultView.ShowResultAsync(settlement);
        }
        else
        {
            Debug.LogWarning("[BattleSceneStarter] BattleResultView is null. Skipping result screen.");
        }

        // --- Phase 3.5: 配当通知書（勇者敗北＆防衛報酬あり）。防衛報酬は totalEarnings に入っているので表示だけ ---
        if (battleResult.result == BattleResult.Defeat && defeatReward > 0 && _payoutView != null)
        {
            Debug.Log("[BattleSceneStarter] Phase 3.5: DemonKingPayout");
            await _payoutView.ShowAsync(BuildPayoutData(defeatRewardBreakdown), cancellation);
        }

        // --- Phase 4: TomsShop に戻る ---
        Debug.Log("[BattleSceneStarter] Returning to TomsShop...");
        _sceneTransition.ReturnToTomsShop();
    }

    // =====================================================
    // 配信中の表示（勝敗メーター・配信収支）と決着の溜め
    // =====================================================

    /// <summary>
    /// 決着まで、勝敗メーター（G-5）と配信収支メーター（E-6）を一定間隔で更新する。
    /// 介入・補充・被弾・撃破はどれもこの間隔のうちに反映される。
    /// </summary>
    private async UniTaskVoid RunLiveHudAsync(CancellationToken token)
    {
        var salesView = _salesController != null ? _salesController.View : null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                int spending = _battleSpending + (_interventions != null ? _interventions.NetSpending : 0);
                int money = _tomsModel != null ? _tomsModel.PlayerMoney.Value : 0;
                int sales = _salesController != null ? _salesController.GetTotalSalesValue() : 0;
                int available = _interventions != null ? _interventions.AvailableBalance : money + sales - spending;
                if (salesView != null) salesView.SetLedger(spending, available);

                if (_outlookMeter != null && _battleSequencer.Context != null)
                    _outlookMeter.SetValue(BattleOutlookEstimator.Estimate(
                        _battleSequencer.Context, _interventions?.Queue.Effects, _interactionSettings));

                await UniTask.Delay(TimeSpan.FromSeconds(LiveHudInterval), ignoreTimeScale: true, cancellationToken: token);
            }
        }
        catch (OperationCanceledException)
        {
            // 決着（battleCts のキャンセル）で止める
        }
    }

    /// <summary>
    /// 決着の溜め（E-1）。勇者勝利は金の「CLEAR!」、魔王側に張って当たったら紫の「配当確定」を配信画面に重ねる。
    /// クリック・決定ボタンで飛ばせる。演出が未配線なら少し待つだけ。
    /// </summary>
    private async UniTask PlayFinaleAsync(BattleResult result, bool demonPayout, CancellationToken token)
    {
        if (_outlookMeter != null) _outlookMeter.SetValue(result == BattleResult.Victory ? 1f : 0f);
        var fx = _battleSequencer.WaveFx;
        bool heroWon = result == BattleResult.Victory;
        string label = heroWon ? FinaleHeroLabel : demonPayout ? FinaleDemonLabel : FinaleDefeatLabel;
        try
        {
            if (fx != null)
            {
                await fx.PlayFinaleAsync(heroWon, label, token, SkipPressed);
            }
            else
            {
                float wait = FinaleFallbackSeconds * GameSettings.EffectDurationScale;
                float elapsed = 0f;
                while (elapsed < wait && !SkipPressed())
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // 演出の失敗でリザルトに進めなくならないように
            Debug.LogException(e);
        }
    }

    private const string DeclarationHitLabel = "宣言的中！";

    /// <summary>勝負配信の宣言が当たったときの帯（BattleWaveFxView.PlayBannerAsync）。演出が無ければ何もしない。</summary>
    private async UniTask PlayDeclarationHitAsync(StreamDeclarationSide side, CancellationToken token)
    {
        var fx = _battleSequencer.WaveFx;
        if (fx == null) return;
        try
        {
            await fx.PlayBannerAsync(DeclarationHitLabel, side == StreamDeclarationSide.Demon, token, SkipPressed);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Debug.LogException(e);
        }
    }

    /// <summary>決着の溜めを飛ばす入力（クリック・決定・パッドの A）。</summary>
    private static bool SkipPressed()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)) return true;
        var pad = UnityEngine.InputSystem.Gamepad.current;
        return pad != null && pad.buttonSouth.wasPressedThisFrame;
    }

    // =====================================================
    // 賭けの立場・勇者側の配当（相場への影響）
    // =====================================================

    /// <summary>勇者の装備の、配信前の店の値段と在庫を控える。</summary>
    private void SnapshotShopState()
    {
        _shopSnapshot.Clear();
        foreach (var id in _inputData.EquippedItemIds)
        {
            if (string.IsNullOrEmpty(id) || _shopSnapshot.ContainsKey(id)) continue;
            var runtime = _itemModel.GetRuntimeItem(id);
            if (runtime != null) _shopSnapshot[id] = (runtime.CurrentPrice.Value, runtime.Stock.Value);
        }
    }

    /// <summary>
    /// この配信で店長がどちらに張っているか（配信コメントの出し分け用。E-8）。
    /// 勇者側 = 勇者の装備の在庫を抱えている（勝てば値上がり）。魔王側 = このダンジョンを魔王軍支援で育てた／魔王系レリックがある。
    /// 両方なら金額の大きい方。
    /// </summary>
    private StreamingBetStance ResolveBetStance()
    {
        int heroStake = 0;
        foreach (var kv in _shopSnapshot) heroStake += kv.Value.price * kv.Value.stock;

        var dungeon = _dungeonCatalog?.GetDungeon(_inputData.DungeonKey);
        int demonStake = CalculateSupportAmount(dungeon, _inputData.DungeonLevel);
        if (RelicBattleEffects.DefeatRewardMul > 1.0001f) demonStake = Mathf.Max(demonStake, BaseDefenseReward());

        if (heroStake <= 0 && demonStake <= 0) return StreamingBetStance.None;
        return heroStake >= demonStake ? StreamingBetStance.Hero : StreamingBetStance.Demon;
    }

    /// <summary>このダンジョンLvの防衛報酬の基本額（レリック補正前）。Lv0（未出資）は 0。</summary>
    private int BaseDefenseReward()
    {
        var dungeon = _dungeonCatalog?.GetDungeon(_inputData.DungeonKey);
        return dungeon != null ? dungeon.GetDefenseReward(_inputData.DungeonLevel) : 0;
    }

    /// <summary>
    /// 介入の値段の基準（防衛報酬に比例。G-6）。Lv0（未出資）は防衛報酬が 0 なので、値段が 0 にならないよう Lv1 の防衛報酬を下限にする。
    /// </summary>
    private int InterventionPriceBase()
    {
        var dungeon = _dungeonCatalog?.GetDungeon(_inputData.DungeonKey);
        if (dungeon == null) return 0;
        return dungeon.GetDefenseReward(Mathf.Max(1, _inputData.DungeonLevel));
    }

    /// <summary>
    /// 勇者の装備の相場が勝ちでどう動くか（E-4）。勝ち: ×1.8（ボス撃破 ×2.2）。
    /// 値は帰還処理（BattleResultHandler）と同じ倍率を、店の値段に掛けてストップ高/安で丸めた見込み。
    /// 負け・途中終了は店の値段を動かさない（負けの痛手は返金騒ぎ）ので空。
    /// </summary>
    private List<BattleMarketImpact> BuildMarketImpacts(BattleResult result, string weaponId, string armorId,
        List<BattleOutputSoldItem> soldItems, int defeatedBossCount)
    {
        var list = new List<BattleMarketImpact>();
        if (result != BattleResult.Victory) return list;
        float mul = ItemModel.HeroWinMultiplier(defeatedBossCount);

        foreach (var id in new[] { weaponId, armorId })
        {
            if (string.IsNullOrEmpty(id) || !_shopSnapshot.TryGetValue(id, out var shop)) continue;
            var master = _itemModel.GetMasterItem(id);
            var runtime = _itemModel.GetRuntimeItem(id);
            if (master == null || runtime == null) continue;
            int soldFromStock = 0;
            foreach (var s in soldItems) if (s.ItemId == id) soldFromStock += s.SoldFromStock;
            int after = ItemModel.PreviewPriceAfterBattle(shop.price, master.basePrice, mul, _economySettings);
            // 左は配信前の店の値段。帰還処理が日送りのあとにこの after へ揃えるので、翌朝の店の値段と一致する。
            // 含み損益の取得単価（平均取得単価）は #109 の記録が入ってから渡す（今は null＝出さない）
            list.Add(new BattleMarketImpact(runtime.ItemName, runtime.ItemIcon, shop.price, after, shop.stock - soldFromStock));
        }
        return list;
    }

    // =====================================================
    // ポーズ制御
    // =====================================================

    private void TogglePause()
    {
        _pauseController.Toggle();
        if (_controlView != null) _controlView.UpdatePauseButtonText(_pauseController.IsPaused);
        Debug.Log($"[BattleSceneStarter] {(_pauseController.IsPaused ? "一時停止" : "再開")}");
    }

    /// <summary>
    /// 配信開始前の演出（カウントダウン・会話）を実行する。演出の失敗で配信が始まらなくならないよう、
    /// キャンセル以外の例外はログに出して握りつぶす。
    /// </summary>
    private static async UniTask RunPreludeSafelyAsync(Func<UniTask> prelude, string label)
    {
        try
        {
            await prelude();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BattleSceneStarter] 配信開始前の{label}で例外が発生したため、スキップして配信を始めます: {ex}");
        }
    }

    // =====================================================
    // 配信強制終了
    // =====================================================

    /// <summary>
    /// 配信終了ボタン: 確認ポップアップを出し、「終了する」なら途中終了（Abort）で決着させる。
    /// 確認中は一時停止する（止めないと確認している間に勇者が倒れたり勝ったりする）。
    /// 確認中に決着したら battleCts のキャンセルでポップアップを閉じる。
    /// </summary>
    private async UniTaskVoid ConfirmEndStreamingAsync(CancellationToken token)
    {
        // ポップアップは同時に1つまで（補充ポップアップの最中は受け付けない）
        if (_isEndConfirmShowing || _isRestockPopupShowing || _controlView == null) return;
        _isEndConfirmShowing = true;

        bool wasPaused = _pauseController.IsPaused;
        if (!wasPaused)
        {
            _pauseController.Pause();
            _controlView.UpdatePauseButtonText(true);
        }

        bool confirmed = false;
        try
        {
            confirmed = await _controlView.ShowEndStreamingConfirmAsync(token);
        }
        finally
        {
            _isEndConfirmShowing = false;
            if (!confirmed && !wasPaused)
            {
                _pauseController.Resume();
                _controlView.UpdatePauseButtonText(false);
            }
        }

        if (confirmed && !token.IsCancellationRequested) ForceEndBattle();
    }

    private void ForceEndBattle()
    {
        // ポーズ解除してから終了（ループが WaitIfPausedAsync で詰まらないように）
        _pauseController.Resume();
        string weaponId = _inputData.EquippedItemIds.Count > 0 ? _inputData.EquippedItemIds[0] : "";
        string armorId  = _inputData.EquippedItemIds.Count > 1 ? _inputData.EquippedItemIds[1] : "";
        // 途中終了は勝ちでも負けでもない。防衛報酬・勝敗ボーナス・経験値・最低保証レベルアップは付かない
        _battleTcs?.TrySetResult((BattleResult.Abort, weaponId, armorId));
        Debug.Log("[BattleSceneStarter] 配信を途中終了しました（Abort）。");
    }

    // =====================================================
    // 在庫切れポップアップ
    // =====================================================

    private async UniTaskVoid TryShowRestockAsync(RuntimeItemData item, CancellationToken token)
    {
        if (_isRestockPopupShowing || _isEndConfirmShowing || _controlView == null) return;
        _isRestockPopupShowing = true;

        // ポップアップ中は一時停止（既にポーズ中でなければ）
        bool wasPaused = _pauseController.IsPaused;
        if (!wasPaused)
        {
            _pauseController.Pause();
            _controlView.UpdatePauseButtonText(true);
        }

        // 途中で例外が出てもフラグとポーズが固まらないようにする
        // （固まると以後の在庫切れでポップアップが二度と出なくなる）
        try
        {
            // コスト計算: その時の配信の値段 × 補充の率（元値で補充してその場で高く売れないようにする）
            float restockRate = _salesController != null ? _salesController.RestockPriceRate : 1f;
            int unitCost = Mathf.Max(1, Mathf.RoundToInt(item.CurrentPrice.Value * restockRate));

            // バトル中獲得金額も含めた利用可能残高から購入可能な最大数を算出
            // （UpdateStock が MaxStock で黙ってクランプするため、在庫の空き枠も上限に含める。
            //   超過分を請求すると精算が狂う）
            int battleEarnings = _salesController != null ? _salesController.GetTotalSalesValue() : 0;
            int interventionSpending = _interventions != null ? _interventions.NetSpending : 0; // 介入の支出も同じ残高から
            int availableForPurchase = (_tomsModel != null ? _tomsModel.PlayerMoney.Value : 0) + battleEarnings - _battleSpending - interventionSpending;
            int stockRoom = Mathf.Max(0, item.MaxStock.Value - item.Stock.Value);
            int maxQuantity = Mathf.Clamp(Mathf.Min(availableForPurchase / unitCost, stockRoom), 0, 99);

            // 数量選択ポップアップ（鍛冶屋と同じ購入UI。0 = キャンセル）
            float recommendScore = ItemModel.ExpectedRevenueOf(item);
            int quantity = await _controlView.ShowRestockQuantityPopupAsync(item, unitCost, maxQuantity, recommendScore, token);

            if (quantity > 0)
            {
                int totalCost = unitCost * quantity;
                item.UpdateStock(item.Stock.Value + quantity);
                _battleSpending += totalCost; // バトル中売上から充当（PlayerMoneyは戦闘終了時に精算）
                _restockedQuantities[item.ItemId] = (_restockedQuantities.TryGetValue(item.ItemId, out var prev) ? prev : 0) + quantity;
                _restockUnitCosts[item.ItemId] = unitCost; // 売れ残りの払い戻しも補充した時の単価で
                Toast.Show($"「{item.ItemName}」を {quantity} 個補充（-{totalCost:N0}G）", item.ItemIcon, ToastKind.Info, 2f);
                Debug.Log($"[BattleSceneStarter] 在庫補充: {item.ItemName} +{quantity}個, 費用 {totalCost}G (利用可能残高: {availableForPurchase}G → {availableForPurchase - totalCost}G)");
            }
        }
        finally
        {
            // ポーズ状態を元に戻す
            if (!wasPaused)
            {
                _pauseController.Resume();
                _controlView.UpdatePauseButtonText(false);
            }

            _isRestockPopupShowing = false;
        }
    }

    // =====================================================
    // ユーティリティ
    // =====================================================

    private DungeonInfoScriptableObj ResolveTargetDungeon()
    {
        var fromCatalog = _dungeonCatalog?.GetDungeon(_inputData.DungeonKey);
        if (fromCatalog != null)
        {
            Debug.Log($"[BattleSceneStarter] Dungeon resolved from catalog: {_inputData.DungeonKey}");
            return fromCatalog;
        }

        if (_battleSequencer.CurrentDungeon != null && _battleSequencer.CurrentDungeon.key == _inputData.DungeonKey)
        {
            Debug.LogWarning($"[BattleSceneStarter] Dungeon {_inputData.DungeonKey} was missing from catalog, using BattleSequencer fallback.");
            return _battleSequencer.CurrentDungeon;
        }

        Debug.LogWarning($"[BattleSceneStarter] Catalog lookup failed for dungeon key: {_inputData.DungeonKey}");
        return null;
    }

    /// <summary>
    /// 勇者敗北時（＝ダンジョン防衛成功時）の報酬を計算する。
    /// 現在のダンジョンレベルの rewardGold（魔王軍側からの報酬）を受け取る。勝利時は 0。
    /// 配当通知書に出すため、基本額と倍率も一緒に返す（式は SupportPayout.Calculate）。
    /// </summary>
    private SupportPayout CalculateDefeatReward(BattleResult result)
    {
        if (result != BattleResult.Defeat) return SupportPayout.None;

        var dungeon = _dungeonCatalog?.GetDungeon(_inputData.DungeonKey);
        int baseReward = dungeon != null ? dungeon.GetDefenseReward(_inputData.DungeonLevel) : 0;

        // レリック補正（魔王ビルド: 防衛報酬アップ）。配信遷移時に GameFlowManager がセットする
        var payout = SupportPayout.Calculate(baseReward, RelicBattleEffects.DefeatRewardMul);

        if (payout.Total > 0)
            Debug.Log($"[BattleSceneStarter] 勇者敗北！ダンジョン防衛報酬: {payout.Total}G ({_inputData.DungeonKey} Lv.{_inputData.DungeonLevel}, mul={payout.Multiplier:F2})");
        return payout;
    }

    /// <summary>
    /// 計測ログ stream_result（Recordings/balance_log_*.jsonl と Editor.log の「[BalanceLog]」行）。
    /// 審議 panel_1030b の stream_result（見込みの段・支援の額と配当・補充と返金・広場の土台）と panel_1030e の熱の道筋・ボスの状況。
    /// ログを書くだけで、お金の計算には触らない。
    /// </summary>
    private void WriteStreamResultLog(BattleResult result, int rawSales, int restockRefund, int heroDefeatRefund, int interventionNet,
        int declarationBonus, int totalEarnings, SupportPayout reward, int mobs, int bosses)
    {
        try
        {
            var dungeon = _dungeonCatalog?.GetDungeon(_inputData.DungeonKey);
            var tuning = StreamTuning.Data;
            int plazaBase = StreamTuning.PlazaBonusBase(rawSales, heroDefeatRefund);
            var payload = new Newtonsoft.Json.Linq.JObject
            {
                ["outcome"] = result.ToString(),
                ["abortedByError"] = result == BattleResult.Abort && _abortedByError,
                ["estimateTier"] = BalanceLog.OutlookKey(_inputData.PreStreamClearPct),
                ["estimateLabel"] = BalanceLog.OutlookLabel(_inputData.PreStreamClearPct),
                ["sales"] = rawSales,
                ["restockCost"] = _battleSpending,
                ["restockRefund"] = restockRefund,
                ["restockRefundRate"] = tuning.restockRefundRate,
                ["heroDefeatRefund"] = heroDefeatRefund,
                ["interventionNet"] = interventionNet,
                ["supportPaid"] = CalculateSupportAmount(dungeon, _inputData.DungeonLevel),
                ["payoutBase"] = reward.BaseReward,
                ["relicMul"] = Math.Round(reward.Multiplier, 3),
                ["payout"] = reward.Total,
                ["declarationBonus"] = declarationBonus,
                ["totalEarnings"] = totalEarnings,
                ["plazaBase"] = plazaBase,
                ["plazaBonus"] = VillageEffects.Current != null ? VillageEffects.Current.StreamSalesBonusGold(plazaBase) : 0,
                ["plazaAfterRefund"] = tuning.plazaBonusAfterRefund,
                ["heroWinMul"] = result == BattleResult.Victory ? Math.Round(ItemModel.HeroWinMultiplier(bosses), 3) : (double?)null,
                ["mobs"] = mobs,
                ["bosses"] = bosses,
            };
            var summary = _salesController != null ? _salesController.BuildBalanceSummary() : null;
            if (summary != null) payload["heat"] = summary;
            BalanceLog.Write("stream_result", payload);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[BattleSceneStarter] stream_result のログを書けませんでした: {e.Message}");
        }
        finally
        {
            BalanceLog.EndStream();
        }
    }

    /// <summary>配当通知書に渡す表示データを組み立てる。</summary>
    private DemonKingPayoutData BuildPayoutData(SupportPayout reward)
    {
        var dungeon = _dungeonCatalog?.GetDungeon(_inputData.DungeonKey);
        string dungeonName = dungeon != null ? dungeon.dungeonName : _inputData.DungeonKey.ToString();
        int supportAmount = CalculateSupportAmount(dungeon, _inputData.DungeonLevel);
        // 回収倍率は防衛報酬の受取累計で見る（今回分は帰還処理で記録されるので、ここでは足して表示するだけ）
        return DemonKingPayoutData.From(dungeonName, _inputData.DungeonLevel, supportAmount, reward,
            DemonPayoutHistory.GetTotal(_inputData.DungeonKey));
    }

    /// <summary>
    /// 魔王軍支援でこのダンジョンに払った累計額。
    /// 支払額はセーブしていないが、ダンジョンLvは魔王軍支援（DungeonLevelUpPresenter）でしか上がらないので、
    /// 初期Lv→現在Lv のレベルアップ費用（levelUpCost は「そのLvから次のLvへ」の費用）の合計と一致する（DungeonLevels.InvestedTotal）。
    /// </summary>
    private static int CalculateSupportAmount(DungeonInfoScriptableObj dungeon, int currentLevel)
        => dungeon != null ? dungeon.GetInvestedTotal(currentLevel) : 0;

    /// <summary>
    /// バトル中に補充したが売れ残った分の返金額を計算する。
    /// 補充分は前払い（_battleSpending）で購入済みのため、売れ残り数 × 補充単価 を戻す。
    /// </summary>
    private int CalculateUnsoldRestockRefund(List<BattleOutputSoldItem> soldItems)
    {
        int refund = 0;
        foreach (var sold in soldItems)
        {
            if (!_restockedQuantities.TryGetValue(sold.ItemId, out var restocked) || restocked <= 0) continue;

            // 補充分から売れた数 = 総売却数 - 持ち込み分から売れた数
            int soldFromRestock = Mathf.Max(0, sold.SoldQuantity - sold.SoldFromStock);
            int unsoldRestock = Mathf.Max(0, restocked - soldFromRestock);
            if (unsoldRestock <= 0) continue;

            int unitCost = _restockUnitCosts.TryGetValue(sold.ItemId, out var c) ? c : 0;
            // 払い戻しの率（streamTuning.restockRefundRate。既定 1 = 補充単価の全額で今と同じ）
            refund += StreamTuning.RestockRefund(unsoldRestock, unitCost);
        }
        return refund;
    }

    /// <summary>
    /// BattleInputData の SelectedItems から BattleOutputSoldItem リストを構築する。
    /// 実際に売れた数 = (持ち込み数 + バトル中の補充数) - バトル終了時の残在庫。
    /// SoldFromStock はショップ在庫の減算用（持ち込み数が上限。補充分は店在庫に存在しないため含めない）。
    /// </summary>
    private List<BattleOutputSoldItem> BuildSoldItems()
    {
        var soldItems = new List<BattleOutputSoldItem>();
        foreach (var item in _inputData.SelectedItems)
        {
            var runtime = _itemModel.GetRuntimeItem(item.ItemId);
            int remainingStock = runtime != null ? runtime.Stock.Value : 0;
            int restocked = _restockedQuantities.TryGetValue(item.ItemId, out var r) ? r : 0;
            int totalSold = Mathf.Max(0, item.Quantity + restocked - remainingStock);
            soldItems.Add(new BattleOutputSoldItem
            {
                ItemId = item.ItemId,
                SoldQuantity = totalSold,
                SoldFromStock = Mathf.Min(totalSold, item.Quantity),
                SoldPrice = item.Price,
                // 実際に売れたときの価格の合計（配信中は価格が動くので「品出し時価格×個数」とは一致しない）
                Revenue = _salesController != null ? _salesController.GetItemRevenue(item.ItemId) : totalSold * item.Price
            });
        }
        return soldItems;
    }
    /// <summary>シーンを出るとき（VContainer のスコープ破棄）にトーストの置き場所を既定に戻す。</summary>
    public void Dispose() => RestoreToastPlacement();

    private void RestoreToastPlacement()
    {
        if (_toastBand != null && ReferenceEquals(Toast.Placement, _toastBand))
            Toast.SetPlacement(ToastPlacement.Default);
        _toastBand = null;
    }

    /// <summary>トーストの帯を測る（出すたびに呼ばれる）。測れなければ null（既定の置き場所で出る）。</summary>
    private Rect? MeasureToastBand()
    {
        var fx = _battleSequencer != null ? _battleSequencer.WaveFx : null;
        if (_phaseGauge == null) _phaseGauge = UnityEngine.Object.FindFirstObjectByType<DungeonPhaseGaugeView>();
        var salesView = _salesController != null ? _salesController.View : null;
        return MeasureToastBand(fx != null ? (RectTransform)fx.transform : null,
            salesView != null ? salesView.ItemSlots : null,
            _phaseGauge != null ? (RectTransform)_phaseGauge.transform : null);
    }

    /// <summary>
    /// 配信画面のトーストの帯（スクリーン座標）。上端 = 配信の映像の枠の下端、下端 = 売り場の棚の上端。
    /// 映像と棚の間にあるダンジョンの進行ゲージは避けたいので、枠とゲージの間が板の下限（<see cref="Toast.BandMinHeight"/>）以上あれば
    /// ゲージの上端までにする。狭ければ（1920x1080 の配置では約 7px）ゲージには重ねて、枠の下端〜棚の上端の帯にする。
    /// 横は映像の枠の幅。どれかが無い・非表示なら null。
    /// video は配信の映像の View（BattleWaveFxView。映像の内側）で、同じ親にあってそれを囲む Image（BattleFrame）を枠とみなす。
    /// </summary>
    public static Rect? MeasureToastBand(RectTransform video, IReadOnlyList<ItemSlotView> shelfSlots, RectTransform phaseGauge)
    {
        if (video == null || !video.gameObject.activeInHierarchy || shelfSlots == null) return null;
        if (!TryScreenRect(FindVideoFrame(video), out var frame)) return null;

        float top = frame.yMin;
        float shelfTop = float.NegativeInfinity;
        foreach (var slot in shelfSlots)
        {
            if (slot == null || !slot.gameObject.activeInHierarchy) continue;
            if (!TryScreenRect((RectTransform)slot.transform, out var r) || r.yMax > top + 1f) continue; // 枠より下の棚だけ
            shelfTop = Mathf.Max(shelfTop, r.yMax);
        }
        if (float.IsNegativeInfinity(shelfTop) || top - shelfTop < 1f) return null;

        float bottom = shelfTop;
        if (phaseGauge != null && phaseGauge.gameObject.activeInHierarchy && TryScreenRect(phaseGauge, out var g)
            && g.yMax > shelfTop && g.yMax < top && Toast.FitsBand(top - g.yMax))
            bottom = g.yMax;

        return Rect.MinMaxRect(frame.xMin, bottom, frame.xMax, top);
    }

    /// <summary>映像の外枠: 映像と同じ親にあって映像を囲む、表示中の Image のうち一番小さいもの。無ければ映像そのもの。</summary>
    private static RectTransform FindVideoFrame(RectTransform video)
    {
        var parent = video.parent;
        if (parent == null || !TryScreenRect(video, out var inner)) return video;
        RectTransform best = video;
        float bestArea = float.PositiveInfinity;
        foreach (Transform child in parent)
        {
            if (child == video || !child.gameObject.activeInHierarchy) continue;
            if (!child.TryGetComponent<UnityEngine.UI.Image>(out var img) || !img.enabled) continue;
            var rt = (RectTransform)child;
            if (!TryScreenRect(rt, out var r)) continue;
            if (r.xMin > inner.xMin + 1f || r.xMax < inner.xMax - 1f || r.yMin > inner.yMin + 1f || r.yMax < inner.yMax - 1f) continue;
            float area = r.width * r.height;
            if (area < bestArea) { bestArea = area; best = rt; }
        }
        return best;
    }

    /// <summary>UI の矩形をスクリーン座標（左下原点）に直す。</summary>
    private static bool TryScreenRect(RectTransform rt, out Rect rect)
    {
        rect = default;
        if (rt == null) return false;
        var canvas = rt.GetComponentInParent<Canvas>();
        if (canvas == null) return false;
        var root = canvas.rootCanvas;
        Camera cam = root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        return rect.width > 0f && rect.height > 0f;
    }
}

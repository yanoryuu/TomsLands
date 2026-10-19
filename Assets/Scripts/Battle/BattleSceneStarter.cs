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
public class BattleSceneStarter : IAsyncStartable
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

    // 配信チュートリアルのシナリオラベル
    private const string TutorialStreamingPrepare = "Tutorial_StreamingPrepare";
    private const string TutorialStreamingStart   = "Tutorial_StreamingStart";

    // ポーズ・在庫切れ管理
    private readonly BattlePauseController _pauseController = new();
    private bool _isRestockPopupShowing = false;
    private int _battleSpending = 0; // バトル中の補充購入で使った金額
    // バトル中に補充した数量（itemId → 合計数）。売却数の集計（BuildSoldItems）で使用
    private readonly Dictionary<string, int> _restockedQuantities = new();
    // 補充時の単価（itemId → 単価）。売れ残り分の返金計算で使用
    private readonly Dictionary<string, int> _restockUnitCosts = new();
    private UniTaskCompletionSource<(BattleResult result, string weaponId, string armorId)> _battleTcs;

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
        // 配当通知書は任意の画面なので、未登録でも解決エラーにしない
        resolver.TryResolve(out _payoutView);
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
            battleItems.Add(new BattleInputItem
            {
                ItemId = kv.Key,
                Quantity = kv.Value,
                Price = runtime != null ? runtime.CurrentPrice.Value : 0
            });
        }
        _inputData.SelectedItems = new List<BattleInputItem>(battleItems);
        Debug.Log($"[BattleSceneStarter] StreamingSetting confirmed. {battleItems.Count} items selected.");

        // --- Phase 2: Battle（配信中・戦闘） ---
        Debug.Log("[BattleSceneStarter] Phase 2: Battle");
        if (_panelManager != null) _panelManager.ShowPanel(StreamingGamePhase.Streaming);

        // カウントダウン前にダンジョン（背景）と品出しアイテムを反映しておく。
        // 後回しにするとカウントダウン中にシーン既定の背景・空の在庫が映ってしまう
        var targetDungeon = ResolveTargetDungeon();
        if (targetDungeon == null)
        {
            Debug.LogError($"[BattleSceneStarter] Dungeon not found: {_inputData.DungeonKey}");
            return;
        }

        _battleSequencer.SetDungeon(targetDungeon, _inputData.DungeonLevel);

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

            // 配信終了ボタン
            _controlView.OnEndBattleRequested
                .Subscribe(_ => ForceEndBattle())
                .AddTo(disposables);
        }

        // 在庫切れ検知
        if (_salesController != null)
        {
            _salesController.OnItemStockDepleted
                .Subscribe(item => TryShowRestockAsync(item, battleCts.Token).Forget())
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
        _interventions?.Start(_salesController, () => _battleSpending);

        _battleSequencer.StartBattle(heroModel);

        // バトル終了待ち
        var battleResult = await _battleTcs.Task;
        Debug.Log($"[BattleSceneStarter] Battle finished: {battleResult.result}");

        // バトル終了 → ポーズ解除してからループキャンセル（WaitIfPausedAsync が抜けられるように）
        _pauseController.Resume();
        battleCts.Cancel();

        // 介入の受付を閉じる（未実行の指示は返金）→ 視聴者スパチャの発生を止める。
        // どちらも StopSales・スナップショットより前（§5-3 の停止順序）
        int interventionRefund = _interventions != null ? _interventions.Stop() : 0;
        _superChats?.Stop();

        ItemSlotView.OnItemClicked -= OnDepletedItemClicked;
        disposables.Dispose();

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
        int totalEarnings = rawSales - _battleSpending + restockRefund + defeatReward - interventionSpent + interventionRefund;
        if (restockRefund > 0)
            Debug.Log($"[BattleSceneStarter] 補充分の売れ残りを返金: {restockRefund}G");
        _outputData.SetResult(battleResult.result, battleResult.weaponId, battleResult.armorId, soldItems, totalEarnings, defeatedMobCount, defeatedBossCount);
        if (_interventions != null)
        {
            _outputData.SetStreamingStats(interventionSpent, _interventions.TotalRefunded, _interventions.PeakViewers,
                _interventions.ViewerSuperChatCount, _interventions.SpecialMovesUsed);
        }

        // --- Phase 3: Result（配信リザルト画面） ---
        Debug.Log("[BattleSceneStarter] Phase 3: Result");
        if (_panelManager != null) _panelManager.ShowPanel(StreamingGamePhase.StreamingResult);
        if (_resultView != null)
        {
            await _resultView.ShowResultAsync(battleResult.result, soldItems, totalEarnings, _outputData.InterventionNetSpending);
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

    private void ForceEndBattle()
    {
        // ポーズ解除してから終了（ループが WaitIfPausedAsync で詰まらないように）
        _pauseController.Resume();
        string weaponId = _inputData.EquippedItemIds.Count > 0 ? _inputData.EquippedItemIds[0] : "";
        string armorId  = _inputData.EquippedItemIds.Count > 1 ? _inputData.EquippedItemIds[1] : "";
        _battleTcs?.TrySetResult((BattleResult.Defeat, weaponId, armorId));
        Debug.Log("[BattleSceneStarter] 配信を強制終了しました。");
    }

    // =====================================================
    // 在庫切れポップアップ
    // =====================================================

    private async UniTaskVoid TryShowRestockAsync(RuntimeItemData item, CancellationToken token)
    {
        if (_isRestockPopupShowing || _controlView == null) return;
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
            // コスト計算（マスターデータの基本価格）
            var master = _itemModel.GetMasterItem(item.ItemId);
            int unitCost = master != null ? master.basePrice : item.CurrentPrice.Value;
            unitCost = Mathf.Max(1, unitCost);

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
                _restockUnitCosts[item.ItemId] = unitCost; // 単価はマスター基準価格で固定
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

    /// <summary>防衛報酬の内訳（基本額 × レリック倍率 = 受取額）</summary>
    private readonly struct DefeatRewardBreakdown
    {
        public readonly int BaseReward;
        public readonly float Multiplier;
        public readonly int Total;

        public DefeatRewardBreakdown(int baseReward, float multiplier, int total)
        {
            BaseReward = baseReward;
            Multiplier = multiplier;
            Total = total;
        }
    }

    /// <summary>
    /// 勇者敗北時（＝ダンジョン防衛成功時）の報酬を計算する。
    /// 現在のダンジョンレベルの rewardGold（魔王軍側からの報酬）を受け取る。勝利時は 0。
    /// 配当通知書に出すため、基本額と倍率も一緒に返す。
    /// </summary>
    private DefeatRewardBreakdown CalculateDefeatReward(BattleResult result)
    {
        if (result != BattleResult.Defeat) return new DefeatRewardBreakdown(0, 1f, 0);

        var dungeon = _dungeonCatalog?.GetDungeon(_inputData.DungeonKey);
        int baseReward = dungeon != null ? dungeon.GetLevelData(_inputData.DungeonLevel)?.rewardGold ?? 0 : 0;

        // レリック補正（魔王ビルド: 防衛報酬アップ）。配信遷移時に GameFlowManager がセットする
        float mul = RelicBattleEffects.DefeatRewardMul;
        int reward = Mathf.RoundToInt(baseReward * mul);

        if (reward > 0)
            Debug.Log($"[BattleSceneStarter] 勇者敗北！ダンジョン防衛報酬: {reward}G ({_inputData.DungeonKey} Lv.{_inputData.DungeonLevel}, mul={mul:F2})");
        return new DefeatRewardBreakdown(baseReward, mul, reward);
    }

    /// <summary>配当通知書に渡す表示データを組み立てる。</summary>
    private DemonKingPayoutData BuildPayoutData(DefeatRewardBreakdown reward)
    {
        var dungeon = _dungeonCatalog?.GetDungeon(_inputData.DungeonKey);
        string dungeonName = dungeon != null ? dungeon.dungeonName : _inputData.DungeonKey.ToString();
        int supportAmount = CalculateSupportAmount(dungeon, _inputData.DungeonLevel);
        return new DemonKingPayoutData(dungeonName, _inputData.DungeonLevel, supportAmount,
            reward.BaseReward, reward.Multiplier, reward.Total);
    }

    /// <summary>
    /// 魔王軍支援でこのダンジョンに払った累計額。
    /// 支払額はセーブしていないが、ダンジョンLvは魔王軍支援（DungeonLevelUpPresenter）でしか上がらないので、
    /// 初期Lv→現在Lv のレベルアップ費用（levelUpCost は「そのLvから次のLvへ」の費用）の合計と一致する。
    /// </summary>
    private static int CalculateSupportAmount(DungeonInfoScriptableObj dungeon, int currentLevel)
    {
        if (dungeon == null) return 0;
        int total = 0;
        for (int lv = Mathf.Max(1, dungeon.initDungeonLevel); lv < currentLevel; lv++)
            total += dungeon.GetLevelData(lv)?.levelUpCost ?? 0;
        return total;
    }

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
            refund += unsoldRestock * unitCost;
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
                SoldPrice = item.Price
            });
        }
        return soldItems;
    }
}

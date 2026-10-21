using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// リザルト画面のPresenter（ResultScene 専用）。
/// シーン開始時に統計データを集計し、演出（営業終了カットイン → トコの振り返り → 結果の段階表示）を流して、
/// 村へ戻るボタンを処理する。
/// </summary>
public class ResultPresenter : IPresenter, IDisposable, IStartable
{
    private readonly ResultView _resultView;
    private readonly ResultModel _resultModel;
    private readonly SceneTransitionService _sceneTransition;
    private readonly MetaProgressModel _metaProgress;
    private readonly PopUpManager _popUpManager;
    private readonly CompositeDisposable _disposables = new();

    private ResultStatisticsData _lastStatistics;
    private bool _metaAwarded;

    // ランを締めるトコの会話。再生中は村へ戻るボタンを受け付けない（会話を途中で切らないため）
    private readonly CancellationTokenSource _cts = new();
    private bool _isTalking;
    private bool _sequenceDone;
    private bool _demoUpsellShown;

#if UNITY_EDITOR
    // ResultScene 単体再生の確認用（進行中のランが無いスロットで再生したとき）。精算・削除・遷移は行わない
    private bool _isDebugDummy;
#endif

    public ResultPresenter(
        ResultView resultView,
        ResultModel resultModel,
        SceneTransitionService sceneTransition,
        MetaProgressModel metaProgress,
        PopUpManager popUpManager = null)
    {
        _resultView = resultView;
        _resultModel = resultModel;
        _sceneTransition = sceneTransition;
        _metaProgress = metaProgress;
        _popUpManager = popUpManager;
    }

    public void Start()
    {
        if (DemoBuild.IsDemo && _resultView != null) _resultView.ApplyDemoLabels();
        Bind();
        // 統計を集計し、すべて隠した状態で待機
        Entry();
        // 営業終了カットイン → トコの振り返り → 結果を1項目ずつ → ボタン有効化
        PlaySequenceAsync().Forget();
    }

    private async UniTaskVoid PlaySequenceAsync()
    {
        var ct = _cts.Token;
        try
        {
            if (_resultView != null)
                await _resultView.PlayCutInAsync(ct);

            await PlayRunEndTalkAsync(ct);

            if (_resultView != null)
            {
                _resultView.SetSkipEnabled(true);
                await _resultView.PlayRevealAsync(_lastStatistics, ct);
                if (_lastStatistics != null && _lastStatistics.Rank == "S")
                    _resultView.PlayRankSCelebration();
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            Debug.LogError($"[ResultPresenter] リザルト演出に失敗しました。\n{e}");
            if (_resultView != null) _resultView.ShowAllImmediate(_lastStatistics);
        }

        _sequenceDone = true;
        if (_resultView != null)
        {
            _resultView.SetSkipEnabled(false);
            _resultView.SetButtonsInteractable(true);
        }
    }

    private async UniTask PlayRunEndTalkAsync(CancellationToken ct)
    {
        _isTalking = true;
        // 会話中はクリックが会話送りに使われるので、演出のスキップ受付は止めて背景を暗くする
        if (_resultView != null)
        {
            _resultView.SetSkipEnabled(false);
            _resultView.SetTalkDim(true);
        }
        try
        {
            await TokoTalk.PlayAsync(TokoTalk.SelectRunClearLabel(_lastStatistics), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            Debug.LogError($"[ResultPresenter] ラン終了の会話に失敗しました。\n{e}");
        }
        finally
        {
            _isTalking = false;
            if (_resultView != null) _resultView.SetTalkDim(false);
        }
    }

    private void Bind()
    {
        // どちらのボタンも「精算 → ラン内セーブ削除 → 村へ帰還」の順を厳守する
        // （先に遷移するとランデータが残り「続きから」に破産/クリア済みランが復活するバグの再発経路になる）
        if (_resultView != null)
        {
            _resultView.OnGoToTitleClicked
                .Subscribe(_ => FinishRunAndGoVillage())
                .AddTo(_disposables);

            _resultView.OnRetryClicked
                .Subscribe(_ => FinishRunAndGoVillage())
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

        var soundManager = SoundManager.Instance;
        if (soundManager != null) soundManager.PlayBGM("リザルト画面");

        var statistics = _resultModel.BuildStatistics();
#if UNITY_EDITOR
        if (ResultDebugDummy.ShouldUse())
        {
            _isDebugDummy = true;
            statistics = ResultDebugDummy.Create();
            var sample = _resultModel.PeekItemForDebug();
            if (sample != null)
            {
                statistics.BestItemName = sample.ItemName;
                statistics.BestItemIcon = sample.ItemIcon;
            }
            Debug.LogWarning($"[ResultPresenter] 確認用のダミー統計で表示します（進行中のラン無し or ResultDebugDummy.Force）（Rank={statistics.Rank}）。");
        }
#endif
        // 村が王都なら称号を出す（村の格は施設レベルから求める）
        statistics.MerchantTitle = _metaProgress != null && VillageRank.IsTop(VillageRank.Compute(_metaProgress))
            ? VillageRank.TopTitle
            : "";
        _lastStatistics = statistics;

        if (_resultView != null)
        {
            _resultView.Prepare(statistics);
        }

        Debug.Log($"[ResultPresenter] Result displayed: Rank={statistics.Rank}, NetWorth={statistics.NetWorth}G");
    }

    /// <summary>
    /// ラン終了の後始末: メタ精算 → ラン内セーブ削除 → 村シーンへ帰還。
    /// </summary>
    private void FinishRunAndGoVillage()
    {
        if (_isTalking || !_sequenceDone) return;
#if UNITY_EDITOR
        if (_isDebugDummy)
        {
            Debug.LogWarning("[ResultPresenter] ダミー統計の表示中のため、精算・セーブ削除・村への遷移は行いません。");
            return;
        }
#endif
        if (DemoBuild.IsDemo)
        {
            // 体験版: 村は無いので精算（村資金・ランの記録）はせず、ラン内セーブを消して「製品版へ」→ タイトルへ。
            // ランの記録を残さないので、次のランも「1周目」のまま（2周目の会話は流れない）
            if (_demoUpsellShown) return;
            _demoUpsellShown = true;
            Debug.Log("[ResultPresenter] 体験版: ラン内セーブを消して「製品版へ」を出します。");
            RunSaveCleaner.DeleteRunFiles();
            var s = _lastStatistics;
            DemoUpsell.Show(_popUpManager, cleared: true, profit: s != null ? s.Profit : 0, rank: s != null ? s.Rank : "",
                goToTitle: _sceneTransition.GoToTitle);
            return;
        }
        Debug.Log("[ResultPresenter] Finishing run. Deleting run save data → Village.");
        AwardMetaCurrencyOnce();
        RunSaveCleaner.DeleteRunFiles();
        _sceneTransition.GoToVillage();
    }

    /// <summary>
    /// ランクリアの精算（1回のみ）。ラン内データを消す前に metaData.json へ加算保存する。
    /// 儲け（純資産 − 初期資金 − 持ち込み − 立て替え）から村資金を求め、持ち込みは返す（MetaProgressModel.ConvertRunToVillageFunds）。
    /// </summary>
    private void AwardMetaCurrencyOnce()
    {
        if (_metaAwarded || _lastStatistics == null || _metaProgress == null) return;
        _metaAwarded = true;

        var s = _lastStatistics;
        int converted = _metaProgress.ConvertRunToVillageFunds(
            cleared: true, profit: s.Profit, carried: s.Carried, mode: s.Mode);

        _metaProgress.RecordRunEnd(new RunRecordPlain
        {
            mode = (int)s.Mode,
            cleared = true,
            profit = s.Profit,
            netWorth = s.NetWorth,
            rank = s.Rank,
            turns = s.TotalTurns,
            streamWins = s.StreamWins,
            streamLosses = s.StreamLosses,
            streamEarnings = s.StreamEarningsTotal,
            villageFunds = converted,
            endedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm"),
        });

        VillageArrivalReport.Set(cleared: true, earned: s.Profit, converted: converted);
        Debug.Log($"[ResultPresenter] 村資金 +{converted}G（儲け {s.Profit}G）");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _disposables.Dispose();
    }
}

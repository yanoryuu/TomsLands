using System;
using R3;
using VContainer.Unity;

/// <summary>
/// TurnPhaseManager（フェーズ状態）と TurnPhaseView（UI）を仲介する。
/// フェーズ変化に応じてUIを再描画し、「次へ」操作でフェーズを前進させる。
/// ※ 開始フェーズ（イベント有無でEvent/Procurement）は TomsShopPresenter.Entry が
///    BeginTurnPhases(hasPendingEvent) で決める。ここでは購読→UI反映のみ（再入回避）。
/// </summary>
public class TurnPhasePresenter : IStartable, IDisposable
{
    private readonly TurnPhaseView _view;
    private readonly TurnPhaseManager _manager;
    private readonly CompositeDisposable _disposables = new();

    public TurnPhasePresenter(TurnPhaseView view, TurnPhaseManager manager)
    {
        _view = view;
        _manager = manager;
    }

    public void Start()
    {
        _view.OnAdvanceClicked
            .Subscribe(_ => _manager.AdvanceTurnPhase())
            .AddTo(_disposables);

        // 現在値を即時受信してUI初期化、以降の変化にも追従（自動Advanceはしない）
        _manager.CurrentTurnPhase
            .Subscribe(phase => _view.ShowForPhase(phase))
            .AddTo(_disposables);
    }

    public void Dispose() => _disposables.Dispose();
}

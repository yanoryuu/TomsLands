using System;
using R3;
using VContainer.Unity;

/// <summary>
/// TurnPhaseManager（フェーズ状態）と TurnPhaseView（UI）を仲介する。
/// フェーズ変化に応じてUIを再描画し、「次へ」操作でフェーズを前進させる。
/// イベントフェーズで保留イベントが無い場合は自動で仕入れへスキップする。
/// </summary>
public class TurnPhasePresenter : IStartable, IDisposable
{
    private readonly TurnPhaseView _view;
    private readonly TurnPhaseManager _manager;
    private readonly PendingEventData _pendingEventData;
    private readonly CompositeDisposable _disposables = new();

    public TurnPhasePresenter(TurnPhaseView view, TurnPhaseManager manager, PendingEventData pendingEventData)
    {
        _view = view;
        _manager = manager;
        _pendingEventData = pendingEventData;
    }

    public void Start()
    {
        _view.OnAdvanceClicked
            .Subscribe(_ => _manager.AdvanceTurnPhase())
            .AddTo(_disposables);

        // 現在値を即時に受け取りUIを初期化、以降の変化にも追従
        _manager.CurrentTurnPhase
            .Subscribe(OnPhaseChanged)
            .AddTo(_disposables);
    }

    private void OnPhaseChanged(TurnPhase phase)
    {
        _view.ShowForPhase(phase);

        // イベントフェーズで保留イベントが無ければ自動で仕入れへ
        if (phase == TurnPhase.Event && !_pendingEventData.HasPendingEvent)
        {
            _manager.AdvanceTurnPhase();
        }
    }

    public void Dispose() => _disposables.Dispose();
}

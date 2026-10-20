using System;
using System.Linq;
using R3;
using VContainer.Unity;

/// <summary>
/// TurnPhaseManager（フェーズ状態）と TurnPhaseView（UI）を仲介する。
/// フェーズ変化に応じてUIを再描画し、「次へ」操作でフェーズを前進させる。
/// ※ 開始フェーズ（イベント有無でEvent/Procurement）は TomsShopPresenter.Entry が
///    BeginTurnPhases(hasPendingEvent) で決める。ここでは購読→UI反映のみ（再入回避）。
///
/// 陳列フェーズの「営業開始 →」は、営業フェーズへ進めてそのまま営業を始める
/// （以前は営業フェーズの画面で「営業開始」をもう一度押す必要があった。押すだけの1手を省く）。
/// 何も並べていないときだけ確認を出す。
/// </summary>
public class TurnPhasePresenter : IStartable, IDisposable
{
    /// <summary>品出しゼロで営業するときの確認の見出し（Debug/DebugAutoPlayer が見出しで承認する）。</summary>
    public const string EmptyDisplayConfirmTitle = "品出しゼロで営業します";

    private readonly TurnPhaseView _view;
    private readonly TurnPhaseManager _manager;
    private readonly TomsShopView _shopView;
    private readonly ItemModel _itemModel;
    private readonly PopUpManager _popUpManager;
    private readonly CompositeDisposable _disposables = new();

    public TurnPhasePresenter(TurnPhaseView view, TurnPhaseManager manager, TomsShopView shopView, ItemModel itemModel, PopUpManager popUpManager)
    {
        _view = view;
        _manager = manager;
        _shopView = shopView;
        _itemModel = itemModel;
        _popUpManager = popUpManager;
    }

    public void Start()
    {
        _view.OnAdvanceClicked
            .Subscribe(_ => OnAdvance())
            .AddTo(_disposables);

        _view.OnBackClicked
            .Subscribe(_ => _manager.GoBackTurnPhase())
            .AddTo(_disposables);

        // 現在値を即時受信してUI初期化、以降の変化にも追従（自動Advanceはしない）
        _manager.CurrentTurnPhase
            .Subscribe(phase => _view.ShowForPhase(phase))
            .AddTo(_disposables);
    }

    public void Dispose() => _disposables.Dispose();

    private void OnAdvance()
    {
        if (_manager.CurrentTurnPhase.Value != TurnPhase.Display || _shopView == null)
        {
            _manager.AdvanceTurnPhase();
            return;
        }

        if (CountDisplayed() == 0 && _popUpManager != null)
        {
            _popUpManager.Show(new PopUpData
            {
                Title = EmptyDisplayConfirmTitle,
                Message = "まだ何も並べていません。\nこのまま営業すると、今日の売上はありません。",
                ConfirmButtonText = "営業する",
                CancelButtonText = "陳列にもどる",
                Size = PopupSizeEnum.Medium,
                OnConfirm = BeginSales,
            });
            return;
        }
        BeginSales();
    }

    /// <summary>営業フェーズへ進め、営業開始（暗転→サマリー）をそのまま始める。</summary>
    private void BeginSales()
    {
        if (_manager.CurrentTurnPhase.Value != TurnPhase.Display) return;
        _manager.AdvanceTurnPhase();
        // TomsShopPresenter は Sales のときだけ受け付け、連打ガードも持つ
        if (_manager.CurrentTurnPhase.Value == TurnPhase.Sales)
            _shopView.OnStartShopClicked.OnNext(Unit.Default);
    }

    private int CountDisplayed()
    {
        var items = _itemModel?.RuntimeItems;
        if (items == null) return 0;
        return items.Count(r => r.IsDisplay.Value && r.DisplayStock.Value > 0);
    }
}

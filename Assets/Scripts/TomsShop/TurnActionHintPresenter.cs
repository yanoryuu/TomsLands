using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using VContainer.Unity;

/// <summary>
/// ターン行動チェックリストのPresenter。
/// Shop フェーズ開始時にヒントを生成し、タップで対象画面へ誘導する。
/// </summary>
public class TurnActionHintPresenter : IStartable, IDisposable
{
    private readonly TurnActionHintView       _view;
    private readonly ItemModel                _itemModel;
    private readonly TomsModel                _tomsModel;
    private readonly ShopStatusModel          _shopStatus;
    private readonly GameFlowManager          _gameFlowManager;
    private readonly StateManager             _stateManager;
    private readonly ItemSelectionPresenter   _itemSelectionPresenter;
    private readonly CompositeDisposable      _disposables = new();

    private const int MaxHints = 3;

    public TurnActionHintPresenter(
        TurnActionHintView       view,
        ItemModel                itemModel,
        TomsModel                tomsModel,
        ShopStatusModel          shopStatus,
        GameFlowManager          gameFlowManager,
        StateManager             stateManager,
        ItemSelectionPresenter   itemSelectionPresenter)
    {
        _view                   = view;
        _itemModel              = itemModel;
        _tomsModel              = tomsModel;
        _shopStatus             = shopStatus;
        _gameFlowManager        = gameFlowManager;
        _stateManager           = stateManager;
        _itemSelectionPresenter = itemSelectionPresenter;

        _stateManager.RegisterOnEnter(TomsShopGamePhase.Shop, OnEnterShop);
    }

    public void Start()
    {
        _view.OnHintTapped
            .Subscribe(Navigate)
            .AddTo(_disposables);
    }

    private void OnEnterShop()
    {
        var hints = GenerateHints();
        _view.Populate(hints);
        _view.Show();
    }

    private void Navigate(ActionTarget target)
    {
        switch (target)
        {
            case ActionTarget.ItemSelection:
                _itemSelectionPresenter.OnOpenSelectionPanel();
                break;
            case ActionTarget.BlackSmith:
                _stateManager.ChangeTomsShopPhase(TomsShopGamePhase.BlackSmith);
                break;
            case ActionTarget.Advertisement:
                _stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Advertisement);
                break;
        }
        // ヒントをタップしたら再生成して最新状態に更新
        _view.Populate(GenerateHints());
    }

    private List<TurnActionHint> GenerateHints()
    {
        var hints = new List<TurnActionHint>();
        int level = _tomsModel.BlacksmithLevel.Value;

        var available = _itemModel.RuntimeItems
            .Where(r => r.RequiredLevel.Value <= level)
            .ToList();

        var displayed = available
            .Where(r => r.IsDisplay.Value && r.DisplayStock.Value > 0)
            .ToList();

        // --- Critical ---

        // 品出しがゼロ
        if (displayed.Count == 0)
        {
            hints.Add(new TurnActionHint
            {
                Priority = HintPriority.Critical,
                Message  = "品出しがゼロです！陳列を設定してください",
                Target   = ActionTarget.ItemSelection,
            });
        }

        // 品出し中アイテムの在庫切れ間近（DisplayStock <= 1）
        var nearlyOutItems = displayed.Where(r => r.DisplayStock.Value <= 1).ToList();
        if (nearlyOutItems.Count >= 2)
        {
            hints.Add(new TurnActionHint
            {
                Priority = HintPriority.Critical,
                Message  = $"{nearlyOutItems.Count}種が在庫切れ間近です。補充してください",
                Target   = ActionTarget.BlackSmith,
            });
        }
        else if (nearlyOutItems.Count == 1)
        {
            hints.Add(new TurnActionHint
            {
                Priority = HintPriority.Critical,
                Message  = $"「{nearlyOutItems[0].ItemName}」の在庫が切れそうです",
                Target   = ActionTarget.BlackSmith,
            });
        }

        if (hints.Count >= MaxHints) return hints.Take(MaxHints).ToList();

        // --- Warning ---

        // 高需要なのに陳列されていないアイテム
        var highDemandNotDisplayed = available
            .Where(r => !r.IsDisplay.Value && r.Demand.Value >= 0.65f && r.Stock.Value > 0)
            .OrderByDescending(r => r.Demand.Value)
            .FirstOrDefault();

        if (highDemandNotDisplayed != null)
        {
            hints.Add(new TurnActionHint
            {
                Priority = HintPriority.Warning,
                Message  = $"「{highDemandNotDisplayed.ItemName}」は需要が高いです！陳列を検討してください",
                Target   = ActionTarget.ItemSelection,
            });
        }

        // 購入余地があり鍛冶屋で補充できる商品がある
        var purchasable = available
            .Where(r => r.Stock.Value < r.MaxStock.Value && r.RemainToMax() > 0)
            .ToList();

        if (purchasable.Count > 0 && _tomsModel.PlayerMoney.Value >= 300)
        {
            hints.Add(new TurnActionHint
            {
                Priority = HintPriority.Warning,
                Message  = "補充できる商品があります",
                Target   = ActionTarget.BlackSmith,
            });
        }

        if (hints.Count >= MaxHints) return hints.Take(MaxHints).ToList();

        // --- Info ---

        // フォロワーが少なく広告未活用
        if (_shopStatus.Followers.Value < 50 && _shopStatus.Trust.Value < 30 && _tomsModel.PlayerMoney.Value >= 200)
        {
            hints.Add(new TurnActionHint
            {
                Priority = HintPriority.Info,
                Message  = "広告でフォロワーを増やすと売上が伸びます",
                Target   = ActionTarget.Advertisement,
            });
        }

        // 前のターン売上がなかった品出し中アイテム
        var notSelling = displayed.Where(r => !r.WasSoldLastTurn).ToList();
        if (notSelling.Count > 0 && displayed.Count > 0)
        {
            hints.Add(new TurnActionHint
            {
                Priority = HintPriority.Info,
                Message  = $"{notSelling.Count}種が前ターン売れませんでした。陳列を見直してみてください",
                Target   = ActionTarget.ItemSelection,
            });
        }

        return hints.Take(MaxHints).ToList();
    }

    public void Dispose() => _disposables.Dispose();
}

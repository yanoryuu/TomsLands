using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using VContainer.Unity;

/// <summary>
/// 需要一覧ダッシュボードのPresenter。
/// TomsShopViewのダッシュボードボタンを購読してパネルを開く。
/// 全アイテムの需要・価格・在庫をまとめて確認でき、陳列トグルも操作できる。
/// </summary>
public class DemandDashboardPresenter : IStartable, IDisposable
{
    private readonly DemandDashboardView _view;
    private readonly ItemModel _itemModel;
    private readonly TomsModel _tomsModel;
    private readonly TomsShopView _tomsShopView;
    private readonly CompositeDisposable _disposables = new();

    private DashboardSortMode _currentSort = DashboardSortMode.Revenue;
    private DashboardFilterMode _currentFilter = DashboardFilterMode.All;

    public DemandDashboardPresenter(
        DemandDashboardView view,
        ItemModel itemModel,
        TomsModel tomsModel,
        TomsShopView tomsShopView)
    {
        _view = view;
        _itemModel = itemModel;
        _tomsModel = tomsModel;
        _tomsShopView = tomsShopView;
    }

    public void Start()
    {
        _view.Hide();
        Bind();
    }

    private void Bind()
    {
        // TomsShopのダッシュボードボタンで開く
        _tomsShopView.OnDashboardClicked
            .Subscribe(_ => Open())
            .AddTo(_disposables);

        _view.OnCloseRequested
            .Subscribe(_ => _view.Hide())
            .AddTo(_disposables);

        _view.OnSortRequested
            .Subscribe(mode =>
            {
                _currentSort = mode;
                Refresh();
            })
            .AddTo(_disposables);

        _view.OnFilterRequested
            .Subscribe(mode =>
            {
                _currentFilter = mode;
                Refresh();
            })
            .AddTo(_disposables);

    }

    private void Open()
    {
        Refresh();
        _view.Show();
    }

    private void Refresh()
    {
        var items = GetFilteredSorted();
        _view.Populate(items);
    }

    private List<RuntimeItemData> GetFilteredSorted()
    {
        int level = _tomsModel.BlacksmithLevel.Value;

        IEnumerable<RuntimeItemData> filtered = _itemModel.RuntimeItems
            .Where(r => r.RequiredLevel.Value <= level);

        filtered = _currentFilter switch
        {
            DashboardFilterMode.Weapon => filtered.Where(r => r.ItemType == ItemTypeData.ItemType.Weapon),
            DashboardFilterMode.Armor  => filtered.Where(r => r.ItemType == ItemTypeData.ItemType.Armor),
            _ => filtered
        };

        filtered = _currentSort switch
        {
            DashboardSortMode.Demand  => filtered.OrderByDescending(r => r.Demand.Value),
            DashboardSortMode.Price   => filtered.OrderByDescending(r => r.CurrentPrice.Value),
            _ => filtered.OrderByDescending(r => r.Demand.Value * r.CurrentPrice.Value * r.SalesRate)
        };

        return filtered.ToList();
    }

    public void Dispose() => _disposables.Dispose();
}

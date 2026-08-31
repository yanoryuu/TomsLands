using System;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// 店の改装（店レベルアップ）画面の Presenter。
/// 仕入れフェーズの「店の改装」ボタンから開き、ゴールドで店レベルを上げる。
/// 店レベルは同時陳列銘柄数・1銘柄あたり陳列個数・マシン設置枠を規定する。
/// </summary>
public class ShopUpgradePresenter : IStartable, IDisposable
{
    private readonly ShopUpgradeView view;
    private readonly TomsModel tomsModel;
    private readonly StateManager stateManager;
    private readonly ShopLevelSettings settings;
    private readonly CompositeDisposable disposables = new();

    public ShopUpgradePresenter(
        ShopUpgradeView view,
        TomsModel tomsModel,
        StateManager stateManager,
        ShopLevelSettings settings)
    {
        this.view = view;
        this.tomsModel = tomsModel;
        this.stateManager = stateManager;
        this.settings = settings;

        stateManager.RegisterOnEnter(TomsShopGamePhase.ShopUpgrade, Entry);
    }

    public void Start()
    {
        if (view == null) return;

        view.OnCloseRequested
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposables);

        view.OnUpgradeClicked
            .Subscribe(_ => HandleUpgrade())
            .AddTo(disposables);

        // 所持金・店レベルの変化で表示を更新（画面表示中以外の更新は無害）
        tomsModel.PlayerMoney
            .Subscribe(_ => Refresh())
            .AddTo(disposables);
        tomsModel.ShopLevel
            .Subscribe(_ => Refresh())
            .AddTo(disposables);
    }

    public void Entry()
    {
        view?.ShowMessage("店を改装して、より多くの商品を並べよう。");
        Refresh();
    }

    private void HandleUpgrade()
    {
        int before = tomsModel.ShopLevel.Value;

        if (!tomsModel.UpgradeShop(settings))
        {
            bool isMax = before >= settings.MaxLevel;
            view?.ShowMessage(isMax
                ? "これ以上の改装はできない（最大レベル）。"
                : "資金が足りない……。");
            return;
        }

        SoundManager.Instance?.PlaySE("営業/SE_仕入れ完了");
        view?.ShowMessage($"改装完了！ 店レベルが {tomsModel.ShopLevel.Value} になった。");
        Refresh();
    }

    private void Refresh()
    {
        if (view == null || settings == null) return;

        int level = tomsModel.ShopLevel.Value;
        var current = settings.GetEntry(level);
        var next = level < settings.MaxLevel ? settings.GetEntry(level + 1) : null;
        int cost = settings.GetLevelUpCost(level);
        bool canAfford = cost >= 0 && tomsModel.PlayerMoney.Value >= cost;

        view.UpdateContent(level, settings.MaxLevel, current, next, cost, canAfford);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

using System;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// マシンショップ（店カスタマイズ）画面の Presenter。
/// 仕入れフェーズの「店の設備」ボタンから開き、ゴールドでマシンを購入・設置する。
/// 設置枠は店レベル（ShopLevelSettings.machineSlots）が規定する。
/// </summary>
public class ShopMachinePresenter : IStartable, IDisposable
{
    private const float RemoveRefundRate = 0.5f;

    private readonly ShopMachineView view;
    private readonly ShopMachineModel machineModel;
    private readonly TomsModel tomsModel;
    private readonly StateManager stateManager;
    private readonly ShopLevelSettings shopLevelSettings;
    private readonly CompositeDisposable disposables = new();
    private CompositeDisposable slotDisposables = new();

    private string selectedMachineId;

    private int MaxSlots => shopLevelSettings != null
        ? shopLevelSettings.GetEntry(tomsModel.ShopLevel.Value).machineSlots
        : 3;

    public ShopMachinePresenter(
        ShopMachineView view,
        ShopMachineModel machineModel,
        TomsModel tomsModel,
        StateManager stateManager,
        ShopLevelSettings shopLevelSettings)
    {
        this.view = view;
        this.machineModel = machineModel;
        this.tomsModel = tomsModel;
        this.stateManager = stateManager;
        this.shopLevelSettings = shopLevelSettings;

        stateManager.RegisterOnEnter(TomsShopGamePhase.MachineShop, Entry);
    }

    public void Start()
    {
        if (view == null) return;

        view.OnCloseRequested
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposables);

        view.OnPurchaseClicked
            .Subscribe(_ => HandlePurchase())
            .AddTo(disposables);

        view.OnRemoveClicked
            .Subscribe(_ => HandleRemove())
            .AddTo(disposables);
    }

    public void Entry()
    {
        selectedMachineId = null;
        view?.ShowMessage("マシンを設置して店を強化しよう。設置枠は店の改装で増える。");
        view?.ClearDetail();
        RefreshCatalog();
    }

    private void RefreshCatalog()
    {
        if (view == null) return;

        slotDisposables.Dispose();
        slotDisposables = new CompositeDisposable();

        var machines = machineModel.AllMachines;
        var slots = view.PopulateCatalog(machines.Count);

        for (int i = 0; i < slots.Count && i < machines.Count; i++)
        {
            var machine = machines[i];
            var slot = slots[i];
            bool placed = machineModel.IsPlaced(machine.machineId);
            bool levelLocked = machine.requiredShopLevel > tomsModel.ShopLevel.Value;
            slot.Setup(machine, placed, levelLocked);

            slot.OnSelected
                .Subscribe(id => SelectMachine(id))
                .AddTo(slotDisposables);
        }

        view.UpdateSlotCounter(machineModel.PlacedCount, MaxSlots);

        if (!string.IsNullOrEmpty(selectedMachineId))
            ShowDetail();
    }

    private void SelectMachine(string machineId)
    {
        selectedMachineId = machineId;
        ShowDetail();
    }

    private void ShowDetail()
    {
        var machine = machineModel.GetMachine(selectedMachineId);
        if (machine == null || view == null) return;

        bool placed = machineModel.IsPlaced(machine.machineId);
        bool canPurchase = !placed
            && machineModel.PlacedCount < MaxSlots
            && machine.requiredShopLevel <= tomsModel.ShopLevel.Value
            && tomsModel.PlayerMoney.Value >= machine.cost;
        int refund = Mathf.RoundToInt(machine.cost * RemoveRefundRate);

        view.ShowDetail(machine, placed, canPurchase, refund);
    }

    private void HandlePurchase()
    {
        var machine = machineModel.GetMachine(selectedMachineId);
        if (machine == null) return;

        if (machine.requiredShopLevel > tomsModel.ShopLevel.Value)
        {
            view?.ShowMessage($"店レベル {machine.requiredShopLevel} が必要だ。改装しよう。");
            return;
        }
        if (machineModel.PlacedCount >= MaxSlots)
        {
            view?.ShowMessage("設置枠がいっぱいだ。撤去するか、店を改装して枠を増やそう。");
            return;
        }

        if (machineModel.TryPurchaseAndPlace(machine, tomsModel, MaxSlots))
        {
            SoundManager.Instance?.PlaySE("営業/SE_仕入れ完了");
            view?.ShowMessage($"{machine.machineName} を設置した！ 効果は明日の朝から。");
            RefreshCatalog();
        }
        else
        {
            view?.ShowMessage("資金が足りない……。");
        }
    }

    private void HandleRemove()
    {
        var machine = machineModel.GetMachine(selectedMachineId);
        if (machine == null) return;

        if (machineModel.TryRemove(machine.machineId, tomsModel))
        {
            view?.ShowMessage($"{machine.machineName} を撤去した（{Mathf.RoundToInt(machine.cost * RemoveRefundRate):N0}G 返金）。");
            RefreshCatalog();
        }
    }

    public void Dispose()
    {
        slotDisposables.Dispose();
        disposables.Dispose();
    }
}

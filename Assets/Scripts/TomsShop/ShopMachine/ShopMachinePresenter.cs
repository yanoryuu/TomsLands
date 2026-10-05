using System;
using System.Linq;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// マシンショップ（店カスタマイズ）画面の Presenter。
/// 仕入れフェーズの「店の設備」ボタンから開き、ゴールドでマシンを購入・設置する。
/// - 設置枠は店レベル（ShopLevelSettings.machineSlots）が規定
/// - 同一マシンでも枠が空いていれば複数台設置できる（設置済み一覧から1台ずつ選択・撤去）
/// - 選択式製造機は設置エントリ（台）ごとに生産アイテムを選べる
/// </summary>
public class ShopMachinePresenter : IStartable, IDisposable
{
    private const float RemoveRefundRate = 0.5f;

    private readonly ShopMachineView view;
    private readonly ShopMachineModel machineModel;
    private readonly TomsModel tomsModel;
    private readonly ItemModel itemModel;
    private readonly StateManager stateManager;
    private readonly ShopLevelSettings shopLevelSettings;
    private readonly CompositeDisposable disposables = new();
    private CompositeDisposable slotDisposables = new();
    private CompositeDisposable placementDisposables = new();

    private string selectedMachineId;
    private string selectedPlacementId;

    /// <summary>生産アイテムドロップダウンの候補（表示順と同じ itemId 列）。</summary>
    private readonly System.Collections.Generic.List<string> producedCandidates = new();

    private int MaxSlots => shopLevelSettings != null
        ? shopLevelSettings.GetEntry(tomsModel.ShopLevel.Value).machineSlots
        : 3;

    public ShopMachinePresenter(
        ShopMachineView view,
        ShopMachineModel machineModel,
        TomsModel tomsModel,
        ItemModel itemModel,
        StateManager stateManager,
        ShopLevelSettings shopLevelSettings)
    {
        this.view = view;
        this.machineModel = machineModel;
        this.tomsModel = tomsModel;
        this.itemModel = itemModel;
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

        // 選択式製造機の生産アイテム変更
        view.OnProducedItemChanged
            .Subscribe(index => HandleProducedItemChanged(index))
            .AddTo(disposables);
    }

    public void Entry()
    {
        selectedMachineId = null;
        selectedPlacementId = null;
        view?.ShowMessage("マシンを設置して店を強化しよう。同じマシンも枠が空いていれば複数台置ける。");
        view?.ClearDetail();
        view?.HideProducedItemSelector();
        view?.PopulatePlacements(0);
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
            bool levelLocked = machine.requiredShopLevel > tomsModel.ShopLevel.Value;
            slot.Setup(machine, machineModel.CountOf(machine.machineId), levelLocked);

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
        // 既定の選択台 = その機種の最後に置いた1台
        selectedPlacementId = machineModel.PlacementsOf(machineId).LastOrDefault()?.PlacementId;
        ShowDetail();
    }

    private void ShowDetail()
    {
        var machine = machineModel.GetMachine(selectedMachineId);
        if (machine == null || view == null) return;

        int placedCount = machineModel.CountOf(machine.machineId);

        // 選択中の台が撤去済みなどで無効なら最後の台に付け替える
        if (machineModel.GetPlacement(selectedPlacementId) == null ||
            machineModel.GetPlacement(selectedPlacementId).MachineId != machine.machineId)
        {
            selectedPlacementId = machineModel.PlacementsOf(machine.machineId).LastOrDefault()?.PlacementId;
        }

        bool canPurchase = machineModel.PlacedCount < MaxSlots
            && machine.requiredShopLevel <= tomsModel.ShopLevel.Value
            && tomsModel.PlayerMoney.Value >= machine.cost;
        int refund = Mathf.RoundToInt(machine.cost * RemoveRefundRate);

        view.ShowDetail(machine, placedCount, canPurchase, refund);
        RefreshPlacementList(machine);
        RefreshProducedItemSelector(machine);
    }

    /// <summary>選択中マシンの設置済み一覧（台ごとの行）を更新する。</summary>
    private void RefreshPlacementList(ShopMachineData machine)
    {
        placementDisposables.Dispose();
        placementDisposables = new CompositeDisposable();

        var placements = machineModel.PlacementsOf(machine.machineId);
        var rows = view.PopulatePlacements(placements.Count);

        for (int i = 0; i < rows.Count && i < placements.Count; i++)
        {
            var placement = placements[i];
            string detail = "";
            if (machine.dailyItemSelectable)
            {
                var runtime = string.IsNullOrEmpty(placement.SelectedItemId) ? null : itemModel.GetRuntimeItem(placement.SelectedItemId);
                var master = runtime != null ? itemModel.GetMasterItem(runtime.ItemId) : null;
                detail = runtime == null
                    ? "生産: 未選択"
                    : $"生産: {runtime.ItemName}（{placement.ProductionProgress:N0}/{(master != null ? master.basePrice : 0):N0}G）";
            }
            else
            {
                detail = machine.EffectSummary;
            }

            rows[i].SetupPlacementRow(placement.PlacementId, $"{i + 1}台目", detail, placement.PlacementId == selectedPlacementId);

            rows[i].OnSelected
                .Subscribe(pid =>
                {
                    selectedPlacementId = pid;
                    ShowDetail();
                })
                .AddTo(placementDisposables);
        }
    }

    /// <summary>
    /// 選択式製造機（選択中の台）の生産アイテムドロップダウンを更新する。
    /// 候補 = 鍛冶屋レベルで解放済みの全アイテム（基準価格の安い順）。
    /// 高い武器ほど製造が遅い（進捗が basePrice に達するごとに1個）のでどれを選んでもよい。
    /// </summary>
    private void RefreshProducedItemSelector(ShopMachineData machine)
    {
        producedCandidates.Clear();

        var placement = machineModel.GetPlacement(selectedPlacementId);
        if (placement == null || machine.effectType != ShopMachineEffectType.DailyItem || !machine.dailyItemSelectable)
        {
            view.HideProducedItemSelector();
            return;
        }

        var candidates = itemModel.RuntimeItems
            .Where(r => r.RequiredLevel.Value <= tomsModel.BlacksmithLevel.Value)
            .Select(r => (runtime: r, master: itemModel.GetMasterItem(r.ItemId)))
            .Where(x => x.master != null && x.master.basePrice > 0)
            .OrderBy(x => x.master.basePrice)
            .ToList();

        if (candidates.Count == 0)
        {
            view.HideProducedItemSelector();
            return;
        }

        // 未選択なら最初の候補（最安）を自動選択して製造を止めない
        if (string.IsNullOrEmpty(placement.SelectedItemId))
            machineModel.SetProducedItem(placement.PlacementId, candidates[0].runtime.ItemId);

        var labels = new System.Collections.Generic.List<string>();
        int selectedIndex = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            var (runtime, master) = candidates[i];
            producedCandidates.Add(runtime.ItemId);
            labels.Add($"{runtime.ItemName}（{master.basePrice:N0}G）");
            if (placement.SelectedItemId == runtime.ItemId)
                selectedIndex = i;
        }

        view.ShowProducedItemSelector(labels, selectedIndex);
    }

    private void HandleProducedItemChanged(int index)
    {
        if (index < 0 || index >= producedCandidates.Count) return;
        var machine = machineModel.GetMachine(selectedMachineId);
        if (machine == null || !machine.dailyItemSelectable) return;

        string itemId = producedCandidates[index];
        if (machineModel.SetProducedItem(selectedPlacementId, itemId))
        {
            var runtime = itemModel.GetRuntimeItem(itemId);
            view?.ShowMessage($"{machine.machineName} の生産を「{runtime?.ItemName}」に切り替えた。明日の朝から製造される。");
            RefreshPlacementList(machine);
        }
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

        var placed = machineModel.TryPurchaseAndPlace(machine, tomsModel, MaxSlots);
        if (placed != null)
        {
            SoundManager.Instance?.PlaySE("営業/SE_仕入れ完了");
            selectedPlacementId = placed.PlacementId;
            view?.ShowMessage($"{machine.machineName}（{machineModel.CountOf(machine.machineId)}台目）を設置した！ 効果は明日の朝から。");
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

        // 選択中の台（無効なら最後の台）を撤去する
        string targetId = machineModel.GetPlacement(selectedPlacementId) != null
            ? selectedPlacementId
            : machineModel.PlacementsOf(machine.machineId).LastOrDefault()?.PlacementId;
        if (string.IsNullOrEmpty(targetId)) return;

        if (machineModel.RemovePlacement(targetId, tomsModel))
        {
            view?.ShowMessage($"{machine.machineName} を1台撤去した（{Mathf.RoundToInt(machine.cost * RemoveRefundRate):N0}G 返金）。");
            selectedPlacementId = machineModel.PlacementsOf(machine.machineId).LastOrDefault()?.PlacementId;
            RefreshCatalog();
        }
    }

    public void Dispose()
    {
        slotDisposables.Dispose();
        placementDisposables.Dispose();
        disposables.Dispose();
    }
}

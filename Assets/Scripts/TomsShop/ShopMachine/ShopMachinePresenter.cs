using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// マシンショップ（店カスタマイズ）画面の Presenter。
/// 仕入れフェーズの「店の設備」ボタンから開き、ゴールドでマシンを購入・設置する。
/// - 設置枠は店レベル（ShopLevelSettings.machineSlots）が規定
/// - 同じマシンは 1 台まで。置いたマシンは強化（Lv+1）・修理（故障中か、明朝から壊れる恐れがあるとき）・撤去ができる
/// - 選択式製造機は生産アイテムを選べる
/// </summary>
public class ShopMachinePresenter : IStartable, IDisposable
{
    // 壊れやすさの色（既存の赤 #A8332B は濃い茶の上で沈むので明るめ）
    private const string DangerColor = "#E05A4A";
    private const string SafeColor = "#9BD37A";
    private const string WarnColor = "#E8A33D";

    private readonly ShopMachineView view;
    private readonly ShopMachineModel machineModel;
    private readonly TomsModel tomsModel;
    private readonly ItemModel itemModel;
    private readonly StateManager stateManager;
    private readonly ShopLevelSettings shopLevelSettings;
    private readonly CompositeDisposable disposables = new();
    private CompositeDisposable slotDisposables = new();

    private string selectedMachineId;

    /// <summary>生産アイテムドロップダウンの候補（表示順と同じ itemId 列）。</summary>
    private readonly List<string> producedCandidates = new();

    private int MaxSlots => (shopLevelSettings != null
        ? shopLevelSettings.GetEntry(tomsModel.ShopLevel.Value).machineSlots
        : 3) + machineModel.ExtraSlots;

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

        // 「店」画面のタブ: 改装へ
        view.OnUpgradeTabRequested
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.ShopUpgrade))
            .AddTo(disposables);

        view.OnPurchaseClicked
            .Subscribe(_ => HandlePurchase())
            .AddTo(disposables);

        view.OnRemoveClicked
            .Subscribe(_ => HandleRemove())
            .AddTo(disposables);

        view.OnUpgradeClicked
            .Subscribe(_ => HandleUpgrade())
            .AddTo(disposables);

        view.OnRepairClicked
            .Subscribe(_ => HandleRepair())
            .AddTo(disposables);

        // 選択式製造機の生産アイテム変更
        view.OnProducedItemChanged
            .Subscribe(HandleProducedItemChanged)
            .AddTo(disposables);
    }

    public void Entry()
    {
        selectedMachineId = null;
        view?.ShowMessage("設備を置いて店を強化しよう。置いた設備はお金を払うと強化できる。");
        view?.ClearDetail();
        view?.HideProducedItemSelector();
        // 同じマシンは 1 台までになったので、台ごとの「設置済み一覧」は使わない（空にして隠す）
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
            var placed = machineModel.PlacementOf(machine.machineId);
            slot.Setup(machine, placed?.Level ?? 0, placed != null && placed.Broken, levelLocked, machineModel.GetPurchaseCost(machine));

            slot.OnSelected
                .Subscribe(SelectMachine)
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

    private PlacedMachine SelectedPlacement => machineModel.PlacementOf(selectedMachineId);

    private void ShowDetail()
    {
        var machine = machineModel.GetMachine(selectedMachineId);
        if (machine == null || view == null) return;

        var placed = SelectedPlacement;
        view.ShowDetail(placed != null ? PlacedDetail(machine, placed) : CatalogDetail(machine));
        RefreshProducedItemSelector(machine);
    }

    /// <summary>まだ置いていないマシンの詳細（Lv1 の効果と購入費）。</summary>
    private ShopMachineDetailInfo CatalogDetail(ShopMachineData machine)
    {
        int cost = machineModel.GetPurchaseCost(machine);
        bool canPurchase = machineModel.PlacedCount < MaxSlots
            && machine.requiredShopLevel <= tomsModel.ShopLevel.Value
            && tomsModel.PlayerMoney.Value >= cost;
        return new ShopMachineDetailInfo
        {
            Machine = machine,
            Placed = false,
            Level = 0,
            EffectLine = machine.EffectSummaryAt(1),
            CostLine = $"{cost:N0}G",
            CanPurchase = canPurchase,
        };
    }

    /// <summary>置いてあるマシンの詳細（今の効果・次のレベル・壊れやすさ・強化と修理のボタン）。</summary>
    private ShopMachineDetailInfo PlacedDetail(ShopMachineData machine, PlacedMachine placed)
    {
        int upgradeCost = machineModel.GetUpgradeCost(placed);
        bool isMax = upgradeCost < 0;
        int repairCost = machineModel.GetRepairCost(placed);
        bool showRepair = machineModel.CanRepair(placed);

        string next = isMax
            ? "最大レベル"
            : $"次 Lv{placed.Level + 1}: {machine.EffectSummaryAt(placed.Level + 1)}";
        return new ShopMachineDetailInfo
        {
            Machine = machine,
            Placed = true,
            Level = placed.Level,
            EffectLine = placed.Broken
                ? $"<color={DangerColor}>故障中</color>　{machine.EffectSummaryAt(placed.Level)} が止まっている"
                : machine.EffectSummaryAt(placed.Level),
            CostLine = $"{next}\n{DurabilityText(placed)}",
            CanUpgrade = !isMax && tomsModel.PlayerMoney.Value >= upgradeCost,
            UpgradeLabel = isMax ? "MAX" : $"強化 {upgradeCost:N0}G",
            ShowRepair = showRepair,
            CanRepair = showRepair && tomsModel.PlayerMoney.Value >= repairCost,
            RepairLabel = $"修理 {repairCost:N0}G",
        };
    }

    /// <summary>壊れやすさの一言（「あと 2 日は安心」「そろそろ危ない（明朝 30%）」「故障中」）。</summary>
    private string DurabilityText(PlacedMachine placed)
    {
        if (placed.Broken) return $"<color={DangerColor}>修理するまで動かない</color>";
        int safe = machineModel.SafeMorningsLeft(placed);
        if (safe == int.MaxValue) return $"<color={SafeColor}>壊れない</color>";
        if (safe > 0) return $"<color={SafeColor}>あと {safe} 日は安心</color>";
        float chance = machineModel.NextBreakChance(placed);
        return $"<color={WarnColor}>そろそろ危ない（明朝 {Mathf.RoundToInt(chance * 100f)}%）</color>";
    }

    /// <summary>
    /// 選択式製造機（置いてあるもの）の生産アイテムドロップダウンを更新する。
    /// 候補 = 鍛冶屋レベルで解放済みの全アイテム（基準価格の安い順）。
    /// 高い武器ほど製造が遅い（進捗が basePrice に達するごとに1個）のでどれを選んでもよい。
    /// </summary>
    private void RefreshProducedItemSelector(ShopMachineData machine)
    {
        producedCandidates.Clear();

        var placement = SelectedPlacement;
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

        var labels = new List<string>();
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
        var placement = SelectedPlacement;
        if (machine == null || placement == null || !machine.dailyItemSelectable) return;

        string itemId = producedCandidates[index];
        if (machineModel.SetProducedItem(placement.PlacementId, itemId))
        {
            var runtime = itemModel.GetRuntimeItem(itemId);
            view?.ShowMessage($"{machine.machineName} の生産を「{runtime?.ItemName}」に切り替えた。明日の朝から製造される。");
        }
    }

    private void HandlePurchase()
    {
        var machine = machineModel.GetMachine(selectedMachineId);
        if (machine == null) return;

        if (machineModel.IsPlaced(machine.machineId))
        {
            view?.ShowMessage("同じ設備は1台までだ。強化して効果を上げよう。");
            return;
        }
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
            view?.ShowMessage($"{machine.machineName}を設置した！ 効果は明日の朝から。");
            RefreshCatalog();
        }
        else
        {
            view?.ShowMessage("資金が足りない……。");
        }
    }

    private void HandleUpgrade()
    {
        var machine = machineModel.GetMachine(selectedMachineId);
        var placed = SelectedPlacement;
        if (machine == null || placed == null) return;

        if (machineModel.GetUpgradeCost(placed) < 0)
        {
            view?.ShowMessage("これ以上は強化できない。");
            return;
        }
        if (machineModel.TryUpgrade(placed.PlacementId, tomsModel))
        {
            SoundManager.Instance?.PlaySE("営業/SE_仕入れ完了");
            view?.ShowMessage($"{machine.machineName}を Lv{placed.Level} に強化した！ 新品同様だ。効果は明日の朝から。");
            RefreshCatalog();
        }
        else
        {
            view?.ShowMessage("資金が足りない……。");
        }
    }

    private void HandleRepair()
    {
        var machine = machineModel.GetMachine(selectedMachineId);
        var placed = SelectedPlacement;
        if (machine == null || placed == null || !machineModel.CanRepair(placed)) return;

        bool wasBroken = placed.Broken;
        if (machineModel.TryRepair(placed.PlacementId, tomsModel))
        {
            SoundManager.Instance?.PlaySE("営業/SE_仕入れ完了");
            view?.ShowMessage(wasBroken
                ? $"{machine.machineName}を修理した。明日の朝からまた動く。"
                : $"{machine.machineName}を点検した。しばらくは安心だ。");
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
        var placed = SelectedPlacement;
        if (machine == null || placed == null) return;

        int refund = machineModel.GetRemoveRefund(placed);
        if (machineModel.RemovePlacement(placed.PlacementId, tomsModel))
        {
            view?.ShowMessage($"{machine.machineName} を撤去した（{refund:N0}G 返金）。");
            RefreshCatalog();
        }
    }

    public void Dispose()
    {
        slotDisposables.Dispose();
        disposables.Dispose();
    }
}

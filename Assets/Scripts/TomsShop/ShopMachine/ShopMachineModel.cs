using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using R3;
using UnityEngine;

/// <summary>
/// マシンの設置状態と効果集計を担当するモデル。
/// - 設置枠数は店レベル（ShopLevelSettings.machineSlots）が規定
/// - 同一マシンは各1台まで
/// - 撤去は購入額の50%返金
/// - 毎日発動型の効果は翌朝（GameFlowManager.NextTurn）に発動し、朝レポートで通知される
/// - 常時バフ型は集計プロパティ（TotalRevenueMultiplier 等）を消費側が参照する
/// - 選択式製造機（dailyItemSelectable）は設置ごとに「生産アイテム」と「生産進捗」を持つ。
///   毎朝 dailyProductionBudget ぶん進捗が貯まり、選択アイテムの基準価格に達するごとに1個生産する
///   （高い武器ほどゆっくり＝何を選んでもバランスが崩れない）
/// ラン内リセット（ニューゲームで全撤去）。セーブは shopMachineData.json。
/// </summary>
public class ShopMachineModel
{
    private const string FileName = "shopMachineData.json";
    private const float RemoveRefundRate = 0.5f;

    private readonly List<ShopMachineData> machines;

    /// <summary>設置済みマシン（順序=設置順）。</summary>
    public List<PlacedMachine> Placed { get; } = new();

    /// <summary>設置状態の変化通知（UI再描画用）。</summary>
    public Subject<Unit> OnPlacementChanged { get; } = new();

    public ShopMachineModel(List<ShopMachineData> machines)
    {
        this.machines = machines ?? new List<ShopMachineData>();
        LoadData();
    }

    public List<ShopMachineData> AllMachines => machines;

    public ShopMachineData GetMachine(string machineId) =>
        machines.FirstOrDefault(m => m.machineId == machineId);

    /// <summary>設置IDから設置エントリを取得する。</summary>
    public PlacedMachine GetPlacement(string placementId) =>
        Placed.FirstOrDefault(p => p.PlacementId == placementId);

    /// <summary>指定マシンの設置エントリ一覧（複数設置可）。</summary>
    public List<PlacedMachine> PlacementsOf(string machineId) =>
        Placed.Where(p => p.MachineId == machineId).ToList();

    public bool IsPlaced(string machineId) => Placed.Any(p => p.MachineId == machineId);

    /// <summary>指定マシンの設置台数。</summary>
    public int CountOf(string machineId) => Placed.Count(p => p.MachineId == machineId);

    public int PlacedCount => Placed.Count;

    /// <summary>設置済みマシンのID列（見た目反映用。同一マシンは台数ぶん並ぶ）。</summary>
    public IEnumerable<string> PlacedMachineIds => Placed.Select(p => p.MachineId);

    // ========================================
    // 購入・撤去・生産アイテム選択
    // ========================================

    /// <summary>
    /// マシンを購入して設置する。設置枠（maxSlots=店レベル由来）・資金・店レベルを検証。
    /// 同一マシンでも枠が空いていれば複数台設置できる。
    /// </summary>
    public PlacedMachine TryPurchaseAndPlace(ShopMachineData machine, TomsModel tomsModel, int maxSlots)
    {
        if (machine == null || tomsModel == null) return null;
        if (PlacedCount >= maxSlots) return null;                             // 設置枠
        if (machine.requiredShopLevel > tomsModel.ShopLevel.Value) return null;
        if (tomsModel.PlayerMoney.Value < machine.cost) return null;

        tomsModel.PurchaseItem(machine.cost);
        var placed = new PlacedMachine
        {
            PlacementId = Guid.NewGuid().ToString("N"),
            MachineId = machine.machineId,
            SelectedItemId = machine.dailyItemSelectable ? machine.dailyItemId : "",
            ProductionProgress = 0,
        };
        Placed.Add(placed);

        SaveData();
        tomsModel.SavePlayerMoney();
        OnPlacementChanged.OnNext(Unit.Default);
        Debug.Log($"[ShopMachine] 設置: {machine.machineName} {CountOf(machine.machineId)}台目 (-{machine.cost}G)");
        return placed;
    }

    /// <summary>指定の設置エントリを撤去する（購入額の50%返金）。</summary>
    public bool RemovePlacement(string placementId, TomsModel tomsModel)
    {
        var placed = GetPlacement(placementId);
        var machine = placed != null ? GetMachine(placed.MachineId) : null;
        if (machine == null || placed == null) return false;

        int refund = Mathf.RoundToInt(machine.cost * RemoveRefundRate);
        Placed.Remove(placed);
        tomsModel.AddRevenue(refund);

        SaveData();
        tomsModel.SavePlayerMoney();
        OnPlacementChanged.OnNext(Unit.Default);
        Debug.Log($"[ShopMachine] 撤去: {machine.machineName} (+{refund}G 返金)");
        return true;
    }

    /// <summary>
    /// 選択式製造機（設置エントリ単位）の生産アイテムを変更する（生産進捗は引き継ぐ）。
    /// </summary>
    public bool SetProducedItem(string placementId, string itemId)
    {
        var placed = GetPlacement(placementId);
        var machine = placed != null ? GetMachine(placed.MachineId) : null;
        if (machine == null || placed == null || !machine.dailyItemSelectable) return false;
        if (placed.SelectedItemId == itemId) return false;

        placed.SelectedItemId = itemId;
        SaveData();
        OnPlacementChanged.OnNext(Unit.Default);
        Debug.Log($"[ShopMachine] {machine.machineName} の生産アイテムを {itemId} に変更");
        return true;
    }

    // ========================================
    // 常時バフの集計（消費側はこれだけを見る）
    // ========================================

    /// <summary>営業売上への倍率（1 + Σ revenueMultiplierBonus）。</summary>
    public float TotalRevenueMultiplier =>
        1f + PlacedDefinitions().Where(m => m.effectType == ShopMachineEffectType.RevenueMultiplier)
                                .Sum(m => m.revenueMultiplierBonus);

    /// <summary>需要下限への加算量合計。</summary>
    public float TotalDemandFloorBonus =>
        PlacedDefinitions().Where(m => m.effectType == ShopMachineEffectType.DemandFloorBonus)
                           .Sum(m => m.demandFloorBonus);

    private IEnumerable<ShopMachineData> PlacedDefinitions() =>
        Placed.Select(p => GetMachine(p.MachineId)).Where(m => m != null);

    // ========================================
    // 毎日発動型（翌朝の日送りで呼ばれる）
    // ========================================

    /// <summary>
    /// 毎日発動型マシンの効果を実行する。お金はここでは触らず結果で返す
    /// （呼び出し側が入金し朝レポートへ載せる）。アイテム生成はここで在庫へ反映する。
    /// </summary>
    public ShopMachineDailyResult ExecuteDailyEffects(ItemModel itemModel, RelicEffectResolver relicResolver = null)
    {
        var result = new ShopMachineDailyResult();
        bool progressChanged = false;

        foreach (var placed in Placed)
        {
            var machine = GetMachine(placed.MachineId);
            if (machine == null) continue;

            switch (machine.effectType)
            {
                case ShopMachineEffectType.DailyMoney:
                    result.TotalMoney += machine.dailyMoney;
                    result.Lines.Add($"{machine.machineName}: +{machine.dailyMoney:N0}G");
                    break;

                case ShopMachineEffectType.DailyItem when machine.dailyItemSelectable:
                    ExecuteSelectableProduction(machine, placed, itemModel, result, relicResolver);
                    progressChanged = true;
                    break;

                case ShopMachineEffectType.DailyItem:
                    ExecuteFixedProduction(machine, itemModel, result);
                    break;
            }
        }

        if (progressChanged) SaveData();
        return result;
    }

    /// <summary>固定生産（従来仕様）: dailyItemId を dailyItemCount 個生成する。</summary>
    private void ExecuteFixedProduction(ShopMachineData machine, ItemModel itemModel, ShopMachineDailyResult result)
    {
        var runtime = itemModel?.GetRuntimeItem(machine.dailyItemId);
        if (runtime == null)
        {
            Debug.LogWarning($"[ShopMachine] 生成対象アイテムが見つかりません: {machine.dailyItemId}");
            return;
        }
        int room = runtime.RemainToMax();
        int produced = Mathf.Min(machine.dailyItemCount, room);
        int discarded = machine.dailyItemCount - produced;
        if (produced > 0)
            runtime.UpdateStock(runtime.Stock.Value + produced);
        string line = $"{machine.machineName}: {runtime.ItemName} ×{produced}";
        if (discarded > 0) line += $"（在庫満杯のため{discarded}個破棄）";
        result.Lines.Add(line);
        result.ProducedAnyItem |= produced > 0;
    }

    /// <summary>
    /// 選択式生産: 毎朝 budget ぶん進捗が貯まり、選択アイテムの basePrice に達するごとに1個生産する。
    /// </summary>
    private void ExecuteSelectableProduction(ShopMachineData machine, PlacedMachine placed, ItemModel itemModel, ShopMachineDailyResult result, RelicEffectResolver relicResolver)
    {
        if (string.IsNullOrEmpty(placed.SelectedItemId))
        {
            result.Lines.Add($"{machine.machineName}: 生産アイテム未選択（店の設備画面で選ぼう）");
            return;
        }

        var runtime = itemModel?.GetRuntimeItem(placed.SelectedItemId);
        var master = itemModel?.GetMasterItem(placed.SelectedItemId);
        if (runtime == null || master == null || master.basePrice <= 0)
        {
            Debug.LogWarning($"[ShopMachine] 生産対象アイテムが見つかりません: {placed.SelectedItemId}");
            return;
        }

        // レリック補正（工房ビルド: ProductionBudgetAdd）
        int budget = machine.dailyProductionBudget;
        if (relicResolver != null)
            budget = Mathf.Max(0, relicResolver.ModifyInt(RelicStatId.ProductionBudgetAdd, budget));

        placed.ProductionProgress += budget;

        int producible = placed.ProductionProgress / master.basePrice;
        if (producible <= 0)
        {
            result.Lines.Add($"{machine.machineName}: {runtime.ItemName} を製造中（{placed.ProductionProgress:N0}/{master.basePrice:N0}G）");
            return;
        }

        int room = runtime.RemainToMax();
        int produced = Mathf.Min(producible, room);
        if (produced > 0)
        {
            runtime.UpdateStock(runtime.Stock.Value + produced);
            placed.ProductionProgress -= produced * master.basePrice;
            result.ProducedAnyItem = true;
        }

        int discarded = producible - produced;
        if (discarded > 0)
        {
            // 在庫満杯ぶんは進捗ごと破棄（貯め込み放題を防ぐ）
            placed.ProductionProgress -= discarded * master.basePrice;
        }

        string line = $"{machine.machineName}: {runtime.ItemName} ×{produced}";
        if (discarded > 0) line += $"（在庫満杯のため{discarded}個破棄）";
        result.Lines.Add(line);
    }

    // ========================================
    // 永続化
    // ========================================

    public void SaveData()
    {
        var data = new ShopMachineSaveData
        {
            placed = Placed.Select(p => new PlacedMachinePlain
            {
                placementId = p.PlacementId,
                machineId = p.MachineId,
                selectedItemId = p.SelectedItemId ?? "",
                productionProgress = p.ProductionProgress,
            }).ToList(),
            // 旧フィールドも書いておく（旧ビルドで読んでも壊れないように）
            placedMachineIds = Placed.Select(p => p.MachineId).ToList(),
        };
        File.WriteAllText(SaveSlotManager.GetPath(FileName), JsonUtility.ToJson(data, true));
    }

    public void LoadData()
    {
        Placed.Clear();

        string path = SaveSlotManager.GetPath(FileName);
        if (File.Exists(path))
        {
            var data = JsonUtility.FromJson<ShopMachineSaveData>(File.ReadAllText(path));
            if (data?.placed != null && data.placed.Count > 0)
            {
                foreach (var plain in data.placed)
                {
                    if (string.IsNullOrEmpty(plain.machineId) || GetMachine(plain.machineId) == null) continue;
                    Placed.Add(new PlacedMachine
                    {
                        // 旧セーブ（placementId なし）は新規発番
                        PlacementId = string.IsNullOrEmpty(plain.placementId) ? Guid.NewGuid().ToString("N") : plain.placementId,
                        MachineId = plain.machineId,
                        SelectedItemId = plain.selectedItemId ?? "",
                        ProductionProgress = Mathf.Max(0, plain.productionProgress),
                    });
                }
            }
            else if (data?.placedMachineIds != null)
            {
                // 旧セーブ互換（machineId のみのリスト）
                foreach (var id in data.placedMachineIds)
                {
                    var machine = GetMachine(id);
                    if (string.IsNullOrEmpty(id) || machine == null) continue;
                    Placed.Add(new PlacedMachine
                    {
                        PlacementId = Guid.NewGuid().ToString("N"),
                        MachineId = id,
                        SelectedItemId = machine.dailyItemSelectable ? machine.dailyItemId : "",
                        ProductionProgress = 0,
                    });
                }
            }
        }
        OnPlacementChanged.OnNext(Unit.Default);
    }

    /// <summary>ニューゲーム用リセット。</summary>
    public void Clear()
    {
        Placed.Clear();
        OnPlacementChanged.OnNext(Unit.Default);
    }
}

/// <summary>設置済みマシン1台ぶんの状態（同一マシンの複数設置に対応するため設置IDを持つ）。</summary>
public class PlacedMachine
{
    public string PlacementId;
    public string MachineId;
    /// <summary>選択式製造機の生産アイテムID（未選択は空）。</summary>
    public string SelectedItemId = "";
    /// <summary>選択式製造機の生産進捗（G換算。basePrice に達するごとに1個生産）。</summary>
    public int ProductionProgress;
}

[Serializable]
public class PlacedMachinePlain
{
    public string placementId;
    public string machineId;
    public string selectedItemId;
    public int productionProgress;
}

[Serializable]
public class ShopMachineSaveData
{
    public List<PlacedMachinePlain> placed = new();
    /// <summary>旧セーブ互換用（machineId のみ）。</summary>
    public List<string> placedMachineIds = new();
}

/// <summary>毎日発動型マシンの実行結果。</summary>
public class ShopMachineDailyResult
{
    /// <summary>マシンが産んだゴールド合計（入金は呼び出し側）。</summary>
    public int TotalMoney;
    /// <summary>朝レポート用の明細行。</summary>
    public List<string> Lines = new();
    public bool ProducedAnyItem;
}

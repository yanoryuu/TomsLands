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
/// ラン内リセット（ニューゲームで全撤去）。セーブは shopMachineData.json。
/// </summary>
public class ShopMachineModel
{
    private const string FileName = "shopMachineData.json";
    private const float RemoveRefundRate = 0.5f;

    private readonly List<ShopMachineData> machines;

    /// <summary>設置済みマシンID（順序=設置順）。</summary>
    public List<string> PlacedMachineIds { get; } = new();

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

    public bool IsPlaced(string machineId) => PlacedMachineIds.Contains(machineId);

    public int PlacedCount => PlacedMachineIds.Count;

    // ========================================
    // 購入・撤去
    // ========================================

    /// <summary>
    /// マシンを購入して設置する。設置枠（maxSlots=店レベル由来）・資金・重複・店レベルを検証。
    /// </summary>
    public bool TryPurchaseAndPlace(ShopMachineData machine, TomsModel tomsModel, int maxSlots)
    {
        if (machine == null || tomsModel == null) return false;
        if (IsPlaced(machine.machineId)) return false;                        // 各1台まで
        if (PlacedCount >= maxSlots) return false;                            // 設置枠
        if (machine.requiredShopLevel > tomsModel.ShopLevel.Value) return false;
        if (tomsModel.PlayerMoney.Value < machine.cost) return false;

        tomsModel.PurchaseItem(machine.cost);
        PlacedMachineIds.Add(machine.machineId);

        SaveData();
        tomsModel.SavePlayerMoney();
        OnPlacementChanged.OnNext(Unit.Default);
        Debug.Log($"[ShopMachine] 設置: {machine.machineName} (-{machine.cost}G)");
        return true;
    }

    /// <summary>マシンを撤去する（購入額の50%返金）。</summary>
    public bool TryRemove(string machineId, TomsModel tomsModel)
    {
        var machine = GetMachine(machineId);
        if (machine == null || !IsPlaced(machineId)) return false;

        int refund = Mathf.RoundToInt(machine.cost * RemoveRefundRate);
        PlacedMachineIds.Remove(machineId);
        tomsModel.AddRevenue(refund);

        SaveData();
        tomsModel.SavePlayerMoney();
        OnPlacementChanged.OnNext(Unit.Default);
        Debug.Log($"[ShopMachine] 撤去: {machine.machineName} (+{refund}G 返金)");
        return true;
    }

    // ========================================
    // 常時バフの集計（消費側はこれだけを見る）
    // ========================================

    /// <summary>営業売上への倍率（1 + Σ revenueMultiplierBonus）。</summary>
    public float TotalRevenueMultiplier =>
        1f + PlacedMachines().Where(m => m.effectType == ShopMachineEffectType.RevenueMultiplier)
                             .Sum(m => m.revenueMultiplierBonus);

    /// <summary>需要下限への加算量合計。</summary>
    public float TotalDemandFloorBonus =>
        PlacedMachines().Where(m => m.effectType == ShopMachineEffectType.DemandFloorBonus)
                        .Sum(m => m.demandFloorBonus);

    private IEnumerable<ShopMachineData> PlacedMachines() =>
        PlacedMachineIds.Select(GetMachine).Where(m => m != null);

    // ========================================
    // 毎日発動型（翌朝の日送りで呼ばれる）
    // ========================================

    /// <summary>
    /// 毎日発動型マシンの効果を実行する。お金はここでは触らず結果で返す
    /// （呼び出し側が入金し朝レポートへ載せる）。アイテム生成はここで在庫へ反映する。
    /// </summary>
    public ShopMachineDailyResult ExecuteDailyEffects(ItemModel itemModel)
    {
        var result = new ShopMachineDailyResult();

        foreach (var machine in PlacedMachines())
        {
            switch (machine.effectType)
            {
                case ShopMachineEffectType.DailyMoney:
                    result.TotalMoney += machine.dailyMoney;
                    result.Lines.Add($"{machine.machineName}: +{machine.dailyMoney:N0}G");
                    break;

                case ShopMachineEffectType.DailyItem:
                    var runtime = itemModel?.GetRuntimeItem(machine.dailyItemId);
                    if (runtime == null)
                    {
                        Debug.LogWarning($"[ShopMachine] 生成対象アイテムが見つかりません: {machine.dailyItemId}");
                        break;
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
                    break;
            }
        }

        return result;
    }

    // ========================================
    // 永続化
    // ========================================

    public void SaveData()
    {
        var data = new ShopMachineSaveData { placedMachineIds = new List<string>(PlacedMachineIds) };
        File.WriteAllText(SaveSlotManager.GetPath(FileName), JsonUtility.ToJson(data, true));
    }

    public void LoadData()
    {
        PlacedMachineIds.Clear();

        string path = SaveSlotManager.GetPath(FileName);
        if (File.Exists(path))
        {
            var data = JsonUtility.FromJson<ShopMachineSaveData>(File.ReadAllText(path));
            if (data?.placedMachineIds != null)
            {
                foreach (var id in data.placedMachineIds)
                {
                    if (!string.IsNullOrEmpty(id) && GetMachine(id) != null && !PlacedMachineIds.Contains(id))
                        PlacedMachineIds.Add(id);
                }
            }
        }
        OnPlacementChanged.OnNext(Unit.Default);
    }

    /// <summary>ニューゲーム用リセット。</summary>
    public void Clear()
    {
        PlacedMachineIds.Clear();
        OnPlacementChanged.OnNext(Unit.Default);
    }
}

[Serializable]
public class ShopMachineSaveData
{
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

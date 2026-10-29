using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;

/// <summary>
/// マシンの設置状態と効果集計を担当するモデル。
/// - 設置枠数は店レベル（ShopLevelSettings.machineSlots）が規定
/// - 同じマシンは 1 台まで（2026-10-29 から。旧セーブの 2 台目以降は読み込み時にレベルへまとめる）
/// - レベル（Lv1〜表の行数）: 強化費を払うと 1 つ上がる。効果・壊れやすさは ShopMachineData.levels の行
/// - 壊れる: 置いて（強化・修理して）から safeDays 回の朝は壊れない。その後は朝ごとに
///   breakChanceStep × (超えた朝の数) の確率で壊れ、修理するまで効果が止まる（レベルは残る）
/// - 修理費 = GameConst.Data.machine.repairCostRate × 今のレベルの行の費用。強化・修理で寿命の数えは 0 に戻る
/// - 購入・強化・修理の費用には村の職人組合の割引（PurchaseCostRate）が掛かる
/// - 撤去は払った額（購入＋強化。割引込み）の50%返金
/// - 毎日発動型の効果は翌朝（GameFlowManager.NextTurn）に発動し、朝レポートで通知される
/// - 常時バフ型は集計プロパティ（TotalRevenueMultiplier 等）を消費側が参照する
/// - 選択式製造機（dailyItemSelectable）は「生産アイテム」と「生産進捗」を持つ。
///   毎朝 製造の進み（レベルの effectValue）ぶん進捗が貯まり、選択アイテムの基準価格に達するごとに1個生産する
///   （高い武器ほどゆっくり＝何を選んでもバランスが崩れない）
/// ラン内リセット（ニューゲームで全撤去）。セーブは shopMachineData.json。
/// </summary>
public class ShopMachineModel
{
    private const string FileName = "shopMachineData.json";
    private const float RemoveRefundRate = 0.5f;
    private const float DefaultRepairCostRate = 0.5f;

    private readonly List<ShopMachineData> machines;

    /// <summary>設置済みマシン（順序=設置順。同じマシンは 1 台まで）。</summary>
    public List<PlacedMachine> Placed { get; } = new();

    /// <summary>設置状態の変化通知（UI再描画用）。</summary>
    public Subject<Unit> OnPlacementChanged { get; } = new();

    /// <summary>購入・強化・修理の費用に掛ける率（1 = 定価。村の職人組合の割引をラン開始時・シーン読み込み時に渡す）。</summary>
    public float PurchaseCostRate { get; set; } = 1f;

    /// <summary>店レベルの設置枠に足す枠数（村の職人組合）。</summary>
    public int ExtraSlots { get; set; }

    /// <summary>修理費の率（今のレベルの行の費用に掛ける。gameconst の machine.repairCostRate）。</summary>
    public static float RepairCostRate => Mathf.Max(0f, GameConst.Data?.machine?.repairCostRate ?? DefaultRepairCostRate);

    /// <summary>定価に割引を掛けた額。</summary>
    private int Discounted(int baseCost) => Mathf.Max(0, Mathf.RoundToInt(baseCost * Mathf.Max(0f, PurchaseCostRate)));

    /// <summary>実際に払う購入費（割引込み）。表示・支払いはすべてこの額を基準にする。</summary>
    public int GetPurchaseCost(ShopMachineData machine) => machine == null ? 0 : Discounted(machine.BaseCost);

    /// <summary>まだ置いていないマシンの撤去の返金額の目安（購入費の半額）。</summary>
    public int GetRemoveRefund(ShopMachineData machine) => Mathf.RoundToInt(GetPurchaseCost(machine) * RemoveRefundRate);

    /// <summary>置いてあるマシンを撤去したときの返金額（購入＋強化で払った額の半額）。</summary>
    public int GetRemoveRefund(PlacedMachine placed)
    {
        if (placed == null) return 0;
        int paid = placed.TotalPaid > 0 ? placed.TotalPaid : GetPurchaseCost(GetMachine(placed.MachineId));
        return Mathf.RoundToInt(paid * RemoveRefundRate);
    }

    /// <summary>次のレベルへの強化費（割引込み）。最大レベル・未設置なら -1。</summary>
    public int GetUpgradeCost(PlacedMachine placed)
    {
        var machine = placed != null ? GetMachine(placed.MachineId) : null;
        if (machine == null || placed.Level >= machine.MaxLevel) return -1;
        return Discounted(machine.LevelCost(placed.Level + 1));
    }

    /// <summary>修理費（割引込み）= repairCostRate × 今のレベルの行の費用（Lv1 = 購入費）。</summary>
    public int GetRepairCost(PlacedMachine placed)
    {
        var machine = placed != null ? GetMachine(placed.MachineId) : null;
        if (machine == null) return 0;
        return Discounted(Mathf.RoundToInt(machine.LevelCost(placed.Level) * RepairCostRate));
    }

    /// <summary>明朝に壊れる確率（壊れている・壊れないマシンは 0）。</summary>
    public float NextBreakChance(PlacedMachine placed)
    {
        var machine = placed != null ? GetMachine(placed.MachineId) : null;
        if (machine == null || placed.Broken) return 0f;
        return machine.BreakChanceOnMorning(placed.Level, placed.DaysSinceService + 1);
    }

    /// <summary>あと何回の朝は壊れないか（壊れないマシンは int.MaxValue）。</summary>
    public int SafeMorningsLeft(PlacedMachine placed)
    {
        var machine = placed != null ? GetMachine(placed.MachineId) : null;
        if (machine == null || !machine.CanBreakAt(placed.Level)) return int.MaxValue;
        return Mathf.Max(0, machine.SafeDaysAt(placed.Level) - placed.DaysSinceService);
    }

    /// <summary>
    /// 修理できるか: 壊れているとき、または明朝から壊れる恐れがあるとき
    /// （壊れない間に払って寿命を延ばすのは無駄なので、そのときは出さない）。
    /// </summary>
    public bool CanRepair(PlacedMachine placed) => placed != null && (placed.Broken || NextBreakChance(placed) > 0f);

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

    /// <summary>指定マシンの設置エントリ（置いていなければ null）。</summary>
    public PlacedMachine PlacementOf(string machineId) =>
        Placed.FirstOrDefault(p => p.MachineId == machineId);

    /// <summary>指定マシンの設置エントリ一覧（1 台までなので 0 か 1 件）。</summary>
    public List<PlacedMachine> PlacementsOf(string machineId) =>
        Placed.Where(p => p.MachineId == machineId).ToList();

    public bool IsPlaced(string machineId) => Placed.Any(p => p.MachineId == machineId);

    /// <summary>指定マシンの設置台数（0 か 1）。</summary>
    public int CountOf(string machineId) => Placed.Count(p => p.MachineId == machineId);

    /// <summary>指定マシンのレベル（置いていなければ 0）。</summary>
    public int LevelOf(string machineId) => PlacementOf(machineId)?.Level ?? 0;

    public int PlacedCount => Placed.Count;

    /// <summary>設置済みマシンのID列（見た目反映用）。</summary>
    public IEnumerable<string> PlacedMachineIds => Placed.Select(p => p.MachineId);

    // ========================================
    // 購入・撤去・強化・修理・生産アイテム選択
    // ========================================

    /// <summary>
    /// マシンを購入して設置する。設置枠（maxSlots=店レベル由来）・資金・店レベル・同じマシンの有無を検証。
    /// </summary>
    public PlacedMachine TryPurchaseAndPlace(ShopMachineData machine, TomsModel tomsModel, int maxSlots)
    {
        if (machine == null || tomsModel == null) return null;
        if (IsPlaced(machine.machineId)) return null;                         // 同じマシンは 1 台まで
        if (PlacedCount >= maxSlots) return null;                             // 設置枠
        if (machine.requiredShopLevel > tomsModel.ShopLevel.Value) return null;
        int cost = GetPurchaseCost(machine);
        if (tomsModel.PlayerMoney.Value < cost) return null;

        tomsModel.PurchaseItem(cost);
        var placed = new PlacedMachine
        {
            PlacementId = Guid.NewGuid().ToString("N"),
            MachineId = machine.machineId,
            SelectedItemId = machine.dailyItemSelectable ? machine.dailyItemId : "",
            ProductionProgress = 0,
            Level = 1,
            TotalPaid = cost,
        };
        Placed.Add(placed);

        SaveData();
        tomsModel.SavePlayerMoney();
        OnPlacementChanged.OnNext(Unit.Default);
        Debug.Log($"[ShopMachine] 設置: {machine.machineName} (-{cost}G)");
        return placed;
    }

    /// <summary>指定の設置エントリを撤去する（払った額の50%返金）。</summary>
    public bool RemovePlacement(string placementId, TomsModel tomsModel)
    {
        var placed = GetPlacement(placementId);
        var machine = placed != null ? GetMachine(placed.MachineId) : null;
        if (machine == null || placed == null) return false;

        int refund = GetRemoveRefund(placed);
        Placed.Remove(placed);
        tomsModel.AddRevenue(refund);

        SaveData();
        tomsModel.SavePlayerMoney();
        OnPlacementChanged.OnNext(Unit.Default);
        Debug.Log($"[ShopMachine] 撤去: {machine.machineName} Lv{placed.Level} (+{refund}G 返金)");
        return true;
    }

    /// <summary>
    /// 強化する（レベル +1）。強化すると寿命の数えが 0 に戻り、壊れていれば直る（中身が新しくなるため）。
    /// </summary>
    public bool TryUpgrade(string placementId, TomsModel tomsModel)
    {
        var placed = GetPlacement(placementId);
        var machine = placed != null ? GetMachine(placed.MachineId) : null;
        if (machine == null || tomsModel == null) return false;
        int cost = GetUpgradeCost(placed);
        if (cost < 0 || tomsModel.PlayerMoney.Value < cost) return false;

        tomsModel.PurchaseItem(cost);
        placed.Level++;
        placed.TotalPaid += cost;
        placed.DaysSinceService = 0;
        placed.Broken = false;

        SaveData();
        tomsModel.SavePlayerMoney();
        OnPlacementChanged.OnNext(Unit.Default);
        Debug.Log($"[ShopMachine] 強化: {machine.machineName} Lv{placed.Level} (-{cost}G)");
        return true;
    }

    /// <summary>修理する（壊れているか、明朝から壊れる恐れがあるときだけ）。寿命の数えが 0 に戻る。</summary>
    public bool TryRepair(string placementId, TomsModel tomsModel)
    {
        var placed = GetPlacement(placementId);
        var machine = placed != null ? GetMachine(placed.MachineId) : null;
        if (machine == null || tomsModel == null || !CanRepair(placed)) return false;
        int cost = GetRepairCost(placed);
        if (tomsModel.PlayerMoney.Value < cost) return false;

        tomsModel.PurchaseItem(cost);
        placed.Broken = false;
        placed.DaysSinceService = 0;

        SaveData();
        tomsModel.SavePlayerMoney();
        OnPlacementChanged.OnNext(Unit.Default);
        Debug.Log($"[ShopMachine] 修理: {machine.machineName} Lv{placed.Level} (-{cost}G)");
        return true;
    }

    /// <summary>
    /// 選択式製造機の生産アイテムを変更する（生産進捗は引き継ぐ）。
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
    // 常時バフの集計（消費側はこれだけを見る。壊れているマシンは数えない）
    // ========================================

    /// <summary>営業売上への倍率（1 + Σ 売上への加算）。上限なし。</summary>
    public float TotalRevenueMultiplier =>
        1f + Working(ShopMachineEffectType.RevenueMultiplier).Sum(x => x.machine.EffectValueAt(x.placed.Level));

    /// <summary>需要下限への加算量合計（旧・冷蔵ケースの効果。同梱のマシンでは使っていない）。</summary>
    public float TotalDemandFloorBonus =>
        Working(ShopMachineEffectType.DemandFloorBonus).Sum(x => x.machine.EffectValueAt(x.placed.Level));

    /// <summary>
    /// 並べていない品の毎朝の需要の落ち込みに掛ける率（1 = そのまま）。冷蔵ケースの「減らす割合」を
    /// (1 - 割合) の掛け算で重ね、0〜1 に収める。
    /// </summary>
    public float NotDisplayedDemandDropRate
    {
        get
        {
            float rate = 1f;
            foreach (var (placed, machine) in Working(ShopMachineEffectType.UndisplayedDemandKeep))
                rate *= 1f - Mathf.Clamp01(machine.EffectValueAt(placed.Level));
            return Mathf.Clamp01(rate);
        }
    }

    private IEnumerable<(PlacedMachine placed, ShopMachineData machine)> Working(ShopMachineEffectType type)
    {
        foreach (var p in Placed)
        {
            if (p.Broken) continue;
            var m = GetMachine(p.MachineId);
            if (m != null && m.effectType == type) yield return (p, m);
        }
    }

    // ========================================
    // 毎朝の故障（日送りの最初に呼ばれる）
    // ========================================

    /// <summary>
    /// 朝の故障の判定。動いているマシンの寿命の数えを 1 進め、その朝の故障率で壊す。
    /// 乱数はランのシード×日×マシンID から決める（相場の揺らぎ ItemModel.SeededUnit と同じ考え方。
    /// 同じシードなら同じ朝に壊れ、セーブ/ロードしても変わらない）。シードが 0 のときだけ UnityEngine.Random。
    /// </summary>
    /// <returns>この朝に壊れたマシン（朝レポートの行は呼び出し側が BreakdownLine で組む）。</returns>
    public List<PlacedMachine> AdvanceDurability(int seed, int turn) =>
        AdvanceDurability(p => seed != 0 ? BreakRoll(seed, turn, p.MachineId) : UnityEngine.Random.value);

    /// <summary>朝の故障の判定（乱数を差し替えられる版。テスト用）。roll は 0 以上 1 未満を返すこと。</summary>
    public List<PlacedMachine> AdvanceDurability(Func<PlacedMachine, float> roll)
    {
        var broken = new List<PlacedMachine>();
        bool changed = false;
        foreach (var placed in Placed)
        {
            var machine = GetMachine(placed.MachineId);
            if (machine == null || placed.Broken) continue;
            placed.DaysSinceService++;
            changed = true;
            float chance = machine.BreakChanceOnMorning(placed.Level, placed.DaysSinceService);
            if (chance <= 0f || roll == null || roll(placed) >= chance) continue;
            placed.Broken = true;
            broken.Add(placed);
            Debug.Log($"[ShopMachine] 故障: {machine.machineName} Lv{placed.Level}（{placed.DaysSinceService}朝目・{chance:P0}）");
        }
        if (changed) SaveData();
        if (broken.Count > 0) OnPlacementChanged.OnNext(Unit.Default);
        return broken;
    }

    /// <summary>故障の乱数（0 以上 1 未満）。シード×日×マシンID の FNV-1a に murmur3 の finalizer。</summary>
    public static float BreakRoll(int seed, int turn, string machineId)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)seed) * 16777619u;
            h = (h ^ (uint)turn) * 16777619u;
            h = (h ^ 0x9E3779B9u) * 16777619u; // 相場の揺らぎ（同じ式）と系列をずらす
            foreach (char c in machineId ?? string.Empty) h = (h ^ c) * 16777619u;
            h ^= h >> 16; h *= 0x85ebca6bu; h ^= h >> 13; h *= 0xc2b2ae35u; h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>朝レポートの「故障した」行。</summary>
    public string BreakdownLine(PlacedMachine placed)
    {
        var machine = placed != null ? GetMachine(placed.MachineId) : null;
        return machine == null ? "" : $"{machine.machineName}が故障した（修理 {GetRepairCost(placed):N0}G）";
    }

    // ========================================
    // 毎日発動型（翌朝の日送りで呼ばれる）
    // ========================================

    /// <summary>
    /// 毎日発動型マシンの効果を実行する（壊れているマシンは動かない）。お金はここでは触らず結果で返す
    /// （呼び出し側が入金し朝レポートへ載せる）。アイテム生成はここで在庫へ反映する。
    /// </summary>
    public ShopMachineDailyResult ExecuteDailyEffects(ItemModel itemModel, RelicEffectResolver relicResolver = null)
    {
        var result = new ShopMachineDailyResult();
        bool progressChanged = false;

        foreach (var placed in Placed)
        {
            var machine = GetMachine(placed.MachineId);
            if (machine == null || placed.Broken) continue;

            switch (machine.effectType)
            {
                case ShopMachineEffectType.DailyMoney:
                    int money = machine.DailyMoneyAt(placed.Level);
                    result.TotalMoney += money;
                    // 朝レポートではアイコン付きの入金の行にするので、文の行にはしない
                    result.MoneySources.Add(new ShopMachineIncome(machine, money));
                    break;

                case ShopMachineEffectType.DailyItem when machine.dailyItemSelectable:
                    ExecuteSelectableProduction(machine, placed, itemModel, result, relicResolver);
                    progressChanged = true;
                    break;

                case ShopMachineEffectType.DailyItem:
                    ExecuteFixedProduction(machine, placed, itemModel, result);
                    break;
            }
        }

        if (progressChanged) SaveData();
        return result;
    }

    /// <summary>固定生産（従来仕様）: dailyItemId をレベルの個数だけ生成する。</summary>
    private void ExecuteFixedProduction(ShopMachineData machine, PlacedMachine placed, ItemModel itemModel, ShopMachineDailyResult result)
    {
        var runtime = itemModel?.GetRuntimeItem(machine.dailyItemId);
        if (runtime == null)
        {
            Debug.LogWarning($"[ShopMachine] 生成対象アイテムが見つかりません: {machine.dailyItemId}");
            return;
        }
        int count = machine.ItemCountAt(placed.Level);
        int room = runtime.RemainToMax();
        int produced = Mathf.Min(count, room);
        int discarded = count - produced;
        if (produced > 0)
        {
            runtime.RecordAcquisition(produced, 0); // 製造機の生成は取得単価 0G
            runtime.UpdateStock(runtime.Stock.Value + produced);
        }
        string line = $"{machine.machineName}: {runtime.ItemName} ×{produced}";
        if (discarded > 0) line += $"（在庫満杯のため{discarded}個破棄）";
        result.Lines.Add(line);
        result.ProducedAnyItem |= produced > 0;
    }

    /// <summary>
    /// 選択式生産: 毎朝 製造の進み（レベルの値）ぶん進捗が貯まり、選択アイテムの basePrice に達するごとに1個生産する。
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
        int budget = machine.ProductionBudgetAt(placed.Level);
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
            runtime.RecordAcquisition(produced, 0); // 製造機の生成は取得単価 0G
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

    /// <summary>今の設置状態をセーブの形にする。</summary>
    public ShopMachineSaveData ToSave() => new ShopMachineSaveData
    {
        placed = Placed.Select(p => new PlacedMachinePlain
        {
            placementId = p.PlacementId,
            machineId = p.MachineId,
            selectedItemId = p.SelectedItemId ?? "",
            productionProgress = p.ProductionProgress,
            level = p.Level,
            daysSinceService = p.DaysSinceService,
            broken = p.Broken,
            totalPaid = p.TotalPaid,
        }).ToList(),
        // 旧フィールドも書いておく（旧ビルドで読んでも壊れないように）
        placedMachineIds = Placed.Select(p => p.MachineId).ToList(),
    };

    public void SaveData() =>
        SaveFile.WriteAtomic(SaveSlotManager.GetPath(FileName), JsonUtility.ToJson(ToSave(), true));

    public void LoadData()
    {
        Placed.Clear();

        string path = SaveSlotManager.GetPath(FileName);
        // 壊れていれば .bak から復旧。どちらも読めなければ設置なしで続行（SaveFile がログを出す）
        if (SaveFile.TryReadJson<ShopMachineSaveData>(path, out var data))
            Placed.AddRange(FromSave(data));
        OnPlacementChanged.OnNext(Unit.Default);
    }

    /// <summary>
    /// セーブの中身から設置エントリを組む（旧セーブ互換込み。レベル・寿命・故障の無い旧セーブは Lv1・0・動いている）。
    /// 同じマシンが 2 台以上ある旧セーブは 1 台にまとめ、台数ぶんのレベル（最大レベルまで）にする
    /// （読み込みでお金を動かさずに、置いていた価値をほぼそのまま残すため。払った額はそのレベルまでの費用の合計とみなす）。
    /// </summary>
    public List<PlacedMachine> FromSave(ShopMachineSaveData data)
    {
        var plains = new List<PlacedMachinePlain>();
        if (data?.placed != null && data.placed.Count > 0)
            plains.AddRange(data.placed);
        else if (data?.placedMachineIds != null)
            plains.AddRange(data.placedMachineIds.Select(id => new PlacedMachinePlain { machineId = id }));   // 旧セーブ互換（machineId のみのリスト）

        var result = new List<PlacedMachine>();
        foreach (var plain in plains)
        {
            var machine = plain != null && !string.IsNullOrEmpty(plain.machineId) ? GetMachine(plain.machineId) : null;
            if (machine == null) continue;

            var same = result.FirstOrDefault(p => p.MachineId == plain.machineId);
            if (same != null)
            {
                // 旧仕様の 2 台目以降: レベルにまとめる
                if (same.Level < machine.MaxLevel)
                {
                    same.Level++;
                    same.TotalPaid = Discounted(Enumerable.Range(1, same.Level).Sum(machine.LevelCost));
                    Debug.Log($"[ShopMachine] 旧セーブの {machine.machineName} の2台目以降を Lv{same.Level} にまとめた");
                }
                else Debug.LogWarning($"[ShopMachine] 旧セーブの {machine.machineName} が最大レベルを超える台数あったので、超えた分は捨てた");
                continue;
            }

            result.Add(new PlacedMachine
            {
                // 旧セーブ（placementId なし）は新規発番
                PlacementId = string.IsNullOrEmpty(plain.placementId) ? Guid.NewGuid().ToString("N") : plain.placementId,
                MachineId = plain.machineId,
                SelectedItemId = plain.selectedItemId ?? (machine.dailyItemSelectable ? machine.dailyItemId : ""),
                ProductionProgress = Mathf.Max(0, plain.productionProgress),
                Level = Mathf.Clamp(plain.level, 1, machine.MaxLevel),
                DaysSinceService = Mathf.Max(0, plain.daysSinceService),
                Broken = plain.broken,
                TotalPaid = Mathf.Max(0, plain.totalPaid),
            });
        }
        return result;
    }

    /// <summary>ニューゲーム用リセット。</summary>
    public void Clear()
    {
        Placed.Clear();
        OnPlacementChanged.OnNext(Unit.Default);
    }
}

/// <summary>設置済みマシン1台ぶんの状態。</summary>
public class PlacedMachine
{
    public string PlacementId;
    public string MachineId;
    /// <summary>選択式製造機の生産アイテムID（未選択は空）。</summary>
    public string SelectedItemId = "";
    /// <summary>選択式製造機の生産進捗（G換算。basePrice に達するごとに1個生産）。</summary>
    public int ProductionProgress;
    /// <summary>レベル（1 始まり）。</summary>
    public int Level = 1;
    /// <summary>置いて（強化・修理して）から過ぎた朝の数。</summary>
    public int DaysSinceService;
    /// <summary>故障中（修理するまで効果が止まる）。</summary>
    public bool Broken;
    /// <summary>購入と強化で払った額（割引込み。撤去の返金の基準。0 なら購入費とみなす）。</summary>
    public int TotalPaid;
}

[Serializable]
public class PlacedMachinePlain
{
    public string placementId;
    public string machineId;
    public string selectedItemId;
    public int productionProgress;
    // 2026-10-29 追加（旧セーブでは 0 / false。level 0 は Lv1 として読む）
    public int level;
    public int daysSinceService;
    public bool broken;
    public int totalPaid;
}

[Serializable]
public class ShopMachineSaveData
{
    public List<PlacedMachinePlain> placed = new();
    /// <summary>旧セーブ互換用（machineId のみ）。</summary>
    public List<string> placedMachineIds = new();
}

/// <summary>お金を産んだマシン1台ぶん（朝レポートの入金の行）。</summary>
public readonly struct ShopMachineIncome
{
    public readonly ShopMachineData Machine;
    public readonly int Amount;

    public ShopMachineIncome(ShopMachineData machine, int amount)
    {
        Machine = machine;
        Amount = amount;
    }
}

/// <summary>毎日発動型マシンの実行結果。</summary>
public class ShopMachineDailyResult
{
    /// <summary>マシンが産んだゴールド合計（入金は呼び出し側）。</summary>
    public int TotalMoney;
    /// <summary>お金を産んだマシン（1台につき1件。額はそのレベルの毎朝のゴールド）。</summary>
    public List<ShopMachineIncome> MoneySources = new();
    /// <summary>朝レポート用の明細行（お金以外: 生成アイテム・製造中・未選択）。</summary>
    public List<string> Lines = new();
    public bool ProducedAnyItem;
}

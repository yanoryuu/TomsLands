using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using VContainer;

/// <summary>
/// 占い師の「上げる枠・下げる枠」の仕組み（IProphetIntervention の本物。Docs/Prophet_Intervention.md）。
/// 数値は ProphetTuning の 1 か所。画面（ProphetPresenter）・本物のゲーム（GameFlowManager / BattleResultHandler）・
/// ヘッドレスの AutoPlay（AutoPlayHeadlessGame は本物の GameFlowManager と BattleResultHandler を回す）が同じこのクラスを使う。
///
/// 効き方（需要の計算は ItemModel.ApplyShopTurnEconomy が <see cref="TrendBias"/> / <see cref="DemandKick"/> /
/// <see cref="FairRate"/> / <see cref="PreviousFairRate"/> を読む）:
///   ・入れた日（TurnsLeft = TotalTurns）は何も動かない。翌朝から TotalTurns 回の朝、需要の均衡点（と適正値）がずれる
///   ・最初に効く朝だけ需要へ小さく直撃（ProphetTuning.DemandKick）
///   ・朝の経済更新のあとで <see cref="AdvanceDay"/> が残り日数を 1 減らし、0 になった朝に枠から外れる。外れたらバイアスが消えるだけ（反動なし）
/// 力: 入れるたびに 1 使う。配信を終えると（途中終了でも）<see cref="OnStreamFinished"/> で +1、最大を超えない。ラン開始時（<see cref="Clear"/>）は StartPower。
/// 保存: ラン内のセーブ（prophetIntervention.json。RunSaveCleaner が新しいランで消す）。ファイルが無い旧セーブは「枠は空・力 StartPower」。
/// </summary>
public sealed class ProphetInterventionModel : IProphetIntervention, IDisposable
{
    public const string FileName = "prophetIntervention.json";

    private readonly TomsModel toms;
    private readonly ItemModel items;
    private readonly bool persist;
    private readonly Subject<Unit> changed = new();
    private ProphetSlotState? up;
    private ProphetSlotState? down;
    // 前の朝に外れた枠。翌朝の「前日の適正値」に前日の効きを入れる（ABM のファンダメンタル勢が「適正値が戻った」日を正しく見る）ため
    private readonly List<ProphetSlotState> expiredLastMorning = new();

    [Inject]
    public ProphetInterventionModel(TomsModel toms, ItemModel items) : this(toms, items, true)
    {
    }

    private ProphetInterventionModel(TomsModel toms, ItemModel items, bool persist)
    {
        this.toms = toms;
        this.items = items;
        this.persist = persist;
        Power = ProphetTuning.StartPower;
        if (persist) LoadData();
    }

    /// <summary>
    /// 保存しない（読みも書きもしない）ものを作る。撮影ツール・テスト用。
    /// toms・items は null でもよい（そのときはお金・解放を見ない）。
    /// </summary>
    public static ProphetInterventionModel CreateInMemory(TomsModel toms = null, ItemModel items = null) => new(toms, items, false);

    public int Power { get; private set; }
    public int MaxPower => ProphetTuning.MaxPower;
    public int Cost => ProphetTuning.Cost > 0 ? VillageEffects.Current.ApplyProphetPrice(ProphetTuning.Cost) : 0;
    public int EffectTurns => ProphetTuning.EffectTurns;
    public Observable<Unit> Changed => changed;

    public ProphetSlotState? GetSlot(ProphetDirection direction) => direction == ProphetDirection.Up ? up : down;

    public ProphetPlaceResult CanPlace(string itemId, ProphetDirection direction)
    {
        if (string.IsNullOrEmpty(itemId)) return ProphetPlaceResult.UnknownItem;
        if (items != null)
        {
            var r = items.GetRuntimeItem(itemId);
            if (r == null) return ProphetPlaceResult.UnknownItem;
            if (toms != null && r.RequiredLevel.Value > toms.BlacksmithLevel.Value) return ProphetPlaceResult.NotUnlocked;
        }
        if (GetSlot(direction).HasValue) return ProphetPlaceResult.SlotInUse;
        var other = GetSlot(Opposite(direction));
        if (other.HasValue && other.Value.ItemId == itemId) return ProphetPlaceResult.SameItemInOtherSlot;
        if (Power <= 0) return ProphetPlaceResult.NoPower;
        if (toms != null && toms.PlayerMoney.Value < Cost) return ProphetPlaceResult.NotEnoughMoney;
        return ProphetPlaceResult.Ok;
    }

    public ProphetPlaceResult TryPlace(string itemId, ProphetDirection direction)
    {
        var result = CanPlace(itemId, direction);
        if (result != ProphetPlaceResult.Ok) return result;

        int cost = Cost;
        if (toms != null && cost > 0)
        {
            toms.PurchaseItem(cost);
            toms.SavePlayerMoney();
        }
        SetSlot(direction, new ProphetSlotState(itemId, direction, EffectTurns, EffectTurns));
        Power--;
        SaveData();
        Debug.Log($"[Prophet] {(direction == ProphetDirection.Up ? "上げる" : "下げる")}枠に {itemId}（{EffectTurns} 日・-{cost}G・力 {Power}/{MaxPower}）");
        changed.OnNext(Unit.Default);
        return ProphetPlaceResult.Ok;
    }

    // =====================================================================
    // 需要・値段への効き（ItemModel.ApplyShopTurnEconomy が朝の更新で読む）
    // =====================================================================

    /// <summary>今朝この品の需要の均衡点に足すバイアス（効いている枠の向き × ProphetTuning.TrendBias）。</summary>
    public float TrendBias(string itemId) => SumOver(itemId, s => true) * ProphetTuning.TrendBias;

    /// <summary>今朝この品の需要へ 1 回だけ乗る直撃分（入れて最初の朝だけ）。</summary>
    public float DemandKick(string itemId) => SumOver(itemId, s => s.TurnsLeft >= s.TotalTurns) * ProphetTuning.DemandKick;

    /// <summary>今朝の適正値に掛ける倍率（効いていなければ 1）。</summary>
    public float FairRate(string itemId) => FairRateOf(TrendBias(itemId));

    /// <summary>
    /// 前日の適正値に掛ける倍率（前の朝に効いていた分）。入れて最初の朝は 1、効いている 2 日目以降と外れた翌朝は前日の効きを返す。
    /// </summary>
    public float PreviousFairRate(string itemId)
    {
        float dir = SumOver(itemId, s => s.TurnsLeft < s.TotalTurns);
        foreach (var s in expiredLastMorning)
            if (s.ItemId == itemId) dir += (int)s.Direction;
        return FairRateOf(dir * ProphetTuning.TrendBias);
    }

    private static float FairRateOf(float bias) =>
        bias == 0f ? 1f : Mathf.Max(0.1f, 1f + ProphetTuning.FairPerBias * bias);

    private float SumOver(string itemId, Func<ProphetSlotState, bool> pick)
    {
        if (string.IsNullOrEmpty(itemId)) return 0f;
        float sum = 0f;
        if (up.HasValue && up.Value.ItemId == itemId && pick(up.Value)) sum += (int)ProphetDirection.Up;
        if (down.HasValue && down.Value.ItemId == itemId && pick(down.Value)) sum += (int)ProphetDirection.Down;
        return sum;
    }

    // =====================================================================
    // 日送り・配信・ラン開始（本物のゲームとヘッドレスの両方から呼ばれる）
    // =====================================================================

    /// <summary>朝の経済更新のあと（GameFlowManager.NextTurn）。残り日数を 1 減らし、0 になった枠を外す。</summary>
    public void AdvanceDay()
    {
        bool hadAny = up.HasValue || down.HasValue || expiredLastMorning.Count > 0;
        expiredLastMorning.Clear();
        up = Tick(up);
        down = Tick(down);
        if (!hadAny) return;
        SaveData();
        changed.OnNext(Unit.Default);
    }

    /// <summary>配信を終えた（途中終了でも）。力 +PowerPerStream、最大を超えない（BattleResultHandler）。</summary>
    public void OnStreamFinished()
    {
        int before = Power;
        Power = Mathf.Min(MaxPower, Power + ProphetTuning.PowerPerStream);
        if (Power == before) return;
        Debug.Log($"[Prophet] 配信を終えて力が戻った（{before} → {Power}/{MaxPower}）");
        SaveData();
        changed.OnNext(Unit.Default);
    }

    /// <summary>新しいラン（GameLifecycleHandler.InitializeNewGame / ヘッドレスの StartNewRun）。枠を空に、力を StartPower に。</summary>
    public void Clear()
    {
        up = null;
        down = null;
        expiredLastMorning.Clear();
        Power = ProphetTuning.StartPower;
        SaveData();
        changed.OnNext(Unit.Default);
    }

    /// <summary>力を直接決める（デバッグ・撮影用。決まりは通さない）。</summary>
    public void SetPower(int power)
    {
        Power = Mathf.Clamp(power, 0, MaxPower);
        SaveData();
        changed.OnNext(Unit.Default);
    }

    /// <summary>決まりを通さずに枠へ入れる・空にする（デバッグ・撮影用。itemId が空なら空にする）。</summary>
    public void ForceSlot(ProphetDirection direction, string itemId, int turnsLeft)
    {
        SetSlot(direction, string.IsNullOrEmpty(itemId)
            ? (ProphetSlotState?)null
            : new ProphetSlotState(itemId, direction, Mathf.Max(1, turnsLeft), Mathf.Max(EffectTurns, turnsLeft)));
        SaveData();
        changed.OnNext(Unit.Default);
    }

    private void SetSlot(ProphetDirection direction, ProphetSlotState? slot)
    {
        if (direction == ProphetDirection.Up) up = slot; else down = slot;
    }

    private ProphetSlotState? Tick(ProphetSlotState? s)
    {
        if (!s.HasValue) return null;
        int left = s.Value.TurnsLeft - 1;
        if (left > 0) return new ProphetSlotState(s.Value.ItemId, s.Value.Direction, left, s.Value.TotalTurns);
        expiredLastMorning.Add(s.Value);
        Debug.Log($"[Prophet] {s.Value.ItemId} の{(s.Value.Direction == ProphetDirection.Up ? "上げる" : "下げる")}枠の効果が切れた");
        return null;
    }

    private static ProphetDirection Opposite(ProphetDirection d) =>
        d == ProphetDirection.Up ? ProphetDirection.Down : ProphetDirection.Up;

    // =====================================================================
    // 保存（ラン内のセーブ。各 Model が自分のファイルを持つ既存の流儀）
    // =====================================================================

    [Serializable]
    private class SlotPlain
    {
        public string itemId;
        /// <summary>ProphetDirection の int 値（+1 上げる / -1 下げる）。</summary>
        public int direction;
        public int turnsLeft;
        public int totalTurns;
    }

    [Serializable]
    private class ProphetSave
    {
        public int version = 1;
        /// <summary>-1 = 欠けている（StartPower として読む）。</summary>
        public int power = -1;
        public List<SlotPlain> slots = new();
        public List<SlotPlain> expired = new();
    }

    public void SaveData()
    {
        if (!persist) return;
        var data = new ProphetSave { power = Power };
        foreach (var s in new[] { up, down })
            if (s.HasValue) data.slots.Add(ToPlain(s.Value));
        foreach (var s in expiredLastMorning) data.expired.Add(ToPlain(s));
        SaveFile.WriteAtomic(SaveSlotManager.GetPath(FileName), JsonUtility.ToJson(data, true));
    }

    public void LoadData()
    {
        up = null;
        down = null;
        expiredLastMorning.Clear();
        Power = ProphetTuning.StartPower;
        // 旧セーブ（ファイルなし）・壊れたファイルは「枠は空・力 StartPower」で続ける（SaveFile がログを出す）
        if (!SaveFile.TryReadJson<ProphetSave>(SaveSlotManager.GetPath(FileName), out var data) || data == null) return;

        Power = data.power < 0 ? ProphetTuning.StartPower : Mathf.Clamp(data.power, 0, MaxPower);
        if (data.slots != null)
            foreach (var p in data.slots)
            {
                if (!TryFromPlain(p, out var s) || GetSlot(s.Direction).HasValue) continue;
                var other = GetSlot(Opposite(s.Direction));
                if (other.HasValue && other.Value.ItemId == s.ItemId) continue;
                SetSlot(s.Direction, s);
            }
        if (data.expired != null)
            foreach (var p in data.expired)
                if (TryFromPlain(p, out var s)) expiredLastMorning.Add(s);
    }

    private static SlotPlain ToPlain(ProphetSlotState s) => new()
    {
        itemId = s.ItemId, direction = (int)s.Direction, turnsLeft = s.TurnsLeft, totalTurns = s.TotalTurns,
    };

    private static bool TryFromPlain(SlotPlain p, out ProphetSlotState slot)
    {
        slot = default;
        if (p == null || string.IsNullOrEmpty(p.itemId) || p.turnsLeft <= 0) return false;
        if (p.direction != (int)ProphetDirection.Up && p.direction != (int)ProphetDirection.Down) return false;
        slot = new ProphetSlotState(p.itemId, (ProphetDirection)p.direction, p.turnsLeft, Mathf.Max(p.turnsLeft, p.totalTurns));
        return true;
    }

    public void Dispose() => changed.Dispose();
}

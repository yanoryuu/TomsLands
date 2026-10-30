using System;
using R3;

/// <summary>
/// 占い師の枠の<b>仮の実装</b>（メモリ上だけ）。仕組み（wt-04）が本物を入れるまで、画面を動かすためと撮影のために使う。
///   ・セーブしない（シーンを読み直すと力 2・枠は空に戻る）・需要に効かない・お金を引かない（足りないかどうかは見る）
///   ・日送り・配信での回復は自分では起きない（撮影・確認では <see cref="AdvanceDay"/> / <see cref="OnStreamFinished"/> / <see cref="SetPower"/> を呼ぶ）
/// 登録は GameLifetimeScope の 1 か所（IProphetIntervention → ProphetInterventionStub）。本物に替えるときはその 1 行を差し替える。
/// </summary>
public sealed class ProphetInterventionStub : IProphetIntervention, IDisposable
{
    public const int DefaultMaxPower = 2;
    public const int DefaultCost = 15000;
    public const int DefaultEffectTurns = 3;

    private readonly TomsModel toms;
    private readonly ItemModel items;
    private readonly Subject<Unit> changed = new();
    private ProphetSlotState? up;
    private ProphetSlotState? down;

    /// <summary>toms・items は null でもよい（撮影用。そのときはお金・解放を見ない）。</summary>
    public ProphetInterventionStub(TomsModel toms = null, ItemModel items = null)
    {
        this.toms = toms;
        this.items = items;
        Power = DefaultMaxPower;
    }

    public int Power { get; private set; }
    public int MaxPower => DefaultMaxPower;
    public int Cost => DefaultCost;
    public int EffectTurns => DefaultEffectTurns;
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
        var other = GetSlot(direction == ProphetDirection.Up ? ProphetDirection.Down : ProphetDirection.Up);
        if (other.HasValue && other.Value.ItemId == itemId) return ProphetPlaceResult.SameItemInOtherSlot;
        if (Power <= 0) return ProphetPlaceResult.NoPower;
        if (toms != null && toms.PlayerMoney.Value < Cost) return ProphetPlaceResult.NotEnoughMoney;
        return ProphetPlaceResult.Ok;
    }

    public ProphetPlaceResult TryPlace(string itemId, ProphetDirection direction)
    {
        var r = CanPlace(itemId, direction);
        if (r != ProphetPlaceResult.Ok) return r;
        var slot = new ProphetSlotState(itemId, direction, EffectTurns, EffectTurns);
        if (direction == ProphetDirection.Up) up = slot; else down = slot;
        Power--;
        changed.OnNext(Unit.Default);
        return ProphetPlaceResult.Ok;
    }

    // ───────── 仮の実装だけの操作（撮影・確認用） ─────────

    /// <summary>1 日進める（残り日数を 1 減らし、0 で外す）。</summary>
    public void AdvanceDay()
    {
        up = Tick(up);
        down = Tick(down);
        changed.OnNext(Unit.Default);
    }

    /// <summary>配信を終えた（力 +1、最大を超えない）。</summary>
    public void OnStreamFinished() => SetPower(Power + 1);

    public void SetPower(int power)
    {
        Power = Math.Clamp(power, 0, MaxPower);
        changed.OnNext(Unit.Default);
    }

    /// <summary>決まりを通さずに枠へ入れる（撮影で状態を作る用）。</summary>
    public void ForceSlot(ProphetDirection direction, string itemId, int turnsLeft)
    {
        var slot = string.IsNullOrEmpty(itemId) ? (ProphetSlotState?)null
            : new ProphetSlotState(itemId, direction, Math.Max(1, turnsLeft), EffectTurns);
        if (direction == ProphetDirection.Up) up = slot; else down = slot;
        changed.OnNext(Unit.Default);
    }

    private static ProphetSlotState? Tick(ProphetSlotState? s)
    {
        if (!s.HasValue) return null;
        int left = s.Value.TurnsLeft - 1;
        return left <= 0 ? null : new ProphetSlotState(s.Value.ItemId, s.Value.Direction, left, s.Value.TotalTurns);
    }

    public void Dispose() => changed.Dispose();
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 特殊効果（フェーズフック型）レリックの C# 実装インターフェース。
/// RelicDefinition.behaviours の behaviourKey で RelicBehaviourRegistry から解決される。
/// </summary>
public interface IRelicBehaviour
{
    /// <summary>ターン開始（日送り）時に呼ばれる。戻り値は朝レポートに載せる行（null可）。</summary>
    string OnTurnStart(RelicHookContext context, float param);
}

/// <summary>フックに渡す文脈。必要になったらフィールドを足す。</summary>
public class RelicHookContext
{
    public int CurrentTurn;
    public TomsModel TomsModel;
    public ItemModel ItemModel;
    public RelicInventoryModel Inventory;
    public string RelicId;
}

/// <summary>
/// behaviourKey → IRelicBehaviour の解決表。
/// 新しい特殊効果は実装クラスを作って Register するだけで、データ（behaviourKey）から参照できる。
/// </summary>
public class RelicBehaviourRegistry
{
    private readonly Dictionary<string, IRelicBehaviour> behaviours = new();

    public RelicBehaviourRegistry()
    {
        // 組み込みビヘイビアの登録
        Register("dailyGold", new DailyGoldRelicBehaviour());
        Register(LedgerMissPenaltyRelicBehaviour.Key, new LedgerMissPenaltyRelicBehaviour());
    }

    public void Register(string key, IRelicBehaviour behaviour)
    {
        if (string.IsNullOrEmpty(key) || behaviour == null) return;
        behaviours[key] = behaviour;
    }

    public IRelicBehaviour Resolve(string key) =>
        !string.IsNullOrEmpty(key) && behaviours.TryGetValue(key, out var b) ? b : null;
}

/// <summary>
/// 所持レリックのフック型効果を各タイミングで発火させるディスパッチャ。
/// GameFlowManager.NextTurn から OnTurnStart が呼ばれる。
/// </summary>
public class RelicHookDispatcher
{
    private readonly RelicInventoryModel inventory;
    private readonly RelicBehaviourRegistry registry;

    public RelicHookDispatcher(RelicInventoryModel inventory, RelicBehaviourRegistry registry)
    {
        this.inventory = inventory;
        this.registry = registry;
    }

    /// <summary>
    /// ターン開始フックを全所持レリックへ配る。朝レポート用の行を返す。
    /// 効果内でお金を直接触るビヘイビアは TomsModel 経由で行う。
    /// onIncome を渡すと、所持金を増やしたフック（子豚の貯金箱など）は行にせず (レリック, 増えた額) で知らせる
    /// （朝レポートがアイコン付きの入金の行にするため）。減らしたフック（呪われた帳簿の取り立て）は今までどおり行で返す。
    /// </summary>
    public List<string> OnTurnStart(int currentTurn, TomsModel tomsModel, ItemModel itemModel,
        Action<RelicDefinition, int> onIncome = null)
    {
        var lines = new List<string>();
        if (inventory == null || registry == null) return lines;

        foreach (var def in inventory.OwnedDefinitions())
        {
            if (def.behaviours == null) continue;
            foreach (var behaviourRef in def.behaviours)
            {
                var behaviour = registry.Resolve(behaviourRef.behaviourKey);
                if (behaviour == null)
                {
                    Debug.LogWarning($"[Relic] 未登録の behaviourKey: {behaviourRef.behaviourKey} ({def.relicName})");
                    continue;
                }

                var context = new RelicHookContext
                {
                    CurrentTurn = currentTurn,
                    TomsModel = tomsModel,
                    ItemModel = itemModel,
                    Inventory = inventory,
                    RelicId = def.relicId,
                };

                try
                {
                    int moneyBefore = tomsModel != null ? tomsModel.PlayerMoney.Value : 0;
                    string line = behaviour.OnTurnStart(context, behaviourRef.param);
                    int gained = tomsModel != null ? tomsModel.PlayerMoney.Value - moneyBefore : 0;
                    if (onIncome != null && gained > 0)
                        onIncome(def, gained);
                    else if (!string.IsNullOrEmpty(line))
                        lines.Add($"{def.relicName}: {line}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Relic] フック実行エラー ({def.relicName}/{behaviourRef.behaviourKey}): {ex}");
                }
            }
        }
        return lines;
    }
}

/// <summary>組み込み: 毎朝 param G を得る（フック型のリファレンス実装）。</summary>
public class DailyGoldRelicBehaviour : IRelicBehaviour
{
    public string OnTurnStart(RelicHookContext context, float param)
    {
        int amount = Mathf.RoundToInt(param);
        if (amount == 0 || context.TomsModel == null) return null;
        context.TomsModel.AddRevenue(amount);
        return $"+{amount:N0}G";
    }
}

/// <summary>
/// 呪われた帳簿: 配信が外れた（勇者が負けて配当も無い・途中終了）次の朝に、所持金の param（0.2 = 20%）を取り立てる。
/// 外れの印は帰還処理（BattleResultHandler）が TomsModel.LedgerPenaltyPending に立てる
/// （配信の次がイベントの日でも、日が進む朝まで持ち越す）。
/// </summary>
public class LedgerMissPenaltyRelicBehaviour : IRelicBehaviour
{
    public const string Key = "ledgerMissPenalty";

    /// <summary>所持品に、この効果を持つレリックがあるか。</summary>
    public static bool IsOwned(RelicInventoryModel inventory)
    {
        if (inventory == null) return false;
        foreach (var def in inventory.OwnedDefinitions())
        {
            if (def?.behaviours == null) continue;
            foreach (var b in def.behaviours)
                if (b != null && b.behaviourKey == Key) return true;
        }
        return false;
    }

    /// <summary>取り立てる額（所持金が 0 以下なら 0）。</summary>
    public static int PenaltyOf(int money, float rate)
    {
        if (money <= 0 || rate <= 0f) return 0;
        return Mathf.Clamp(Mathf.FloorToInt(money * Mathf.Min(rate, 1f)), 0, money);
    }

    public string OnTurnStart(RelicHookContext context, float param)
    {
        var toms = context.TomsModel;
        if (toms == null || !toms.LedgerPenaltyPending) return null;
        toms.LedgerPenaltyPending = false;
        int amount = PenaltyOf(toms.PlayerMoney.Value, param);
        if (amount > 0) toms.PurchaseItem(amount);
        toms.SavePlayerMoney();
        return amount > 0 ? $"外れた配信の取り立て -{amount:N0}G" : null;
    }
}

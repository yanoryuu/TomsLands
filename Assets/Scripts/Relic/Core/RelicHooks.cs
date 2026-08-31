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
    /// </summary>
    public List<string> OnTurnStart(int currentTurn, TomsModel tomsModel, ItemModel itemModel)
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
                    string line = behaviour.OnTurnStart(context, behaviourRef.param);
                    if (!string.IsNullOrEmpty(line))
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

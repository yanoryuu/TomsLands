using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 所持レリックの数値補正を集計して基準値に適用する純関数レイヤー。
/// 計算順: (base + ΣAdd) × ΠMul。副作用なし・呼び出し順不同。
/// 【差し込み規律】消費側は必ずオプショナル null 許容で受け取り、
/// null または補正なしのとき従来挙動と完全一致すること。
/// </summary>
public class RelicEffectResolver
{
    private readonly RelicInventoryModel inventory;

    public RelicEffectResolver(RelicInventoryModel inventory)
    {
        this.inventory = inventory;
    }

    /// <summary>基準値にレリック補正を適用した値を返す。</summary>
    public float Modify(RelicStatId stat, float baseValue)
    {
        if (inventory == null) return baseValue;

        float add = 0f;
        float mul = 1f;

        foreach (var def in inventory.OwnedDefinitions())
        {
            if (def.modifiers == null) continue;
            foreach (var modifier in def.modifiers)
            {
                if (modifier.stat != stat) continue;
                if (modifier.op == RelicOp.Add) add += modifier.value;
                else mul *= modifier.value;
            }
        }

        return (baseValue + add) * mul;
    }

    public int ModifyInt(RelicStatId stat, int baseValue) =>
        Mathf.RoundToInt(Modify(stat, baseValue));

    /// <summary>
    /// この StatId に効いている所持レリックと、そのレリック単体の倍率（Mul はその値、Add は 1+値）を返す（UI表示用。F-8）。
    /// 計算は Modify と同じ値を使うので、表示と実際の効き目はずれない。
    /// </summary>
    public List<(RelicDefinition relic, float factor)> GetContributors(RelicStatId stat)
    {
        var result = new List<(RelicDefinition, float)>();
        if (inventory == null) return result;
        foreach (var def in inventory.OwnedDefinitions())
        {
            if (def?.modifiers == null) continue;
            float factor = 1f;
            bool hit = false;
            foreach (var modifier in def.modifiers)
            {
                if (modifier.stat != stat) continue;
                hit = true;
                factor *= modifier.op == RelicOp.Mul ? modifier.value : 1f + modifier.value;
            }
            if (hit) result.Add((def, factor));
        }
        return result;
    }

    /// <summary>この StatId に効いているレリックがあるか（UI表示用）。</summary>
    public bool HasAnyModifier(RelicStatId stat)
    {
        if (inventory == null) return false;
        foreach (var def in inventory.OwnedDefinitions())
        {
            if (def.modifiers == null) continue;
            foreach (var modifier in def.modifiers)
                if (modifier.stat == stat) return true;
        }
        return false;
    }
}

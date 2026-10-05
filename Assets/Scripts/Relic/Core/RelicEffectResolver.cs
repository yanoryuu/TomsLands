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

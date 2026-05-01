using System.Linq;
using UnityEngine;

public static class ClearProbabilityCalculator
{
    public static float Calculate(
        RuntimeHeroData hero,
        ItemModel itemModel,
        DungeonData dungeon,
        int dungeonLevel)
    {
        if (hero == null || dungeon == null) return 50f;

        // ── 1) レベル差による基礎確率
        // 推奨レベルと同じ → 60%、1下がるごとに -10%、1上がるごとに +10%
        float recLv      = Mathf.Max(1, dungeon.recommendedLevel);
        float levelDiff  = hero.level.Value - recLv;
        float baseProbability = Mathf.Clamp(60f + levelDiff * 10f, 20f, 90f);

        // ── 2) 難易度補正（difficulty 5 → ×1.0、10 → ×0.67、1 → ×1.43）
        float diffFactor = 10f / Mathf.Max(1f, dungeon.difficulty + 5f);
        baseProbability *= diffFactor;

        // ── 3) 装備属性ボーナス
        float attributeBonus = CalculateAttributeBonus(hero, itemModel, dungeon, dungeonLevel);

        // ── 4) 作戦ボーナス
        float tacticsBonus = GetTacticsBonus(hero.tactics.Value);

        return Mathf.Clamp(baseProbability + attributeBonus + tacticsBonus, 5f, 95f);
    }

    private static float CalculateAttributeBonus(
        RuntimeHeroData hero,
        ItemModel itemModel,
        DungeonData dungeon,
        int dungeonLevel)
    {
        float bonus = 0f;

        var weaponItem = itemModel?.GetRuntimeItem(hero.weaponId.Value);
        var armorItem  = itemModel?.GetRuntimeItem(hero.armorId.Value);

        if (weaponItem != null && weaponItem.ItemAttribute == dungeon.requiredAttribute)
            bonus += 8f;
        if (armorItem != null && armorItem.ItemAttribute == dungeon.requiredAttribute)
            bonus += 5f;

        var levelData = dungeon.GetLevelData(dungeonLevel);
        if (levelData?.monsters == null || levelData.monsters.Count == 0)
            return bonus;

        int total = levelData.monsters.Count(e => e != null);
        if (total == 0) return bonus;

        int effectiveCount = 0;
        int weakCount      = 0;

        foreach (var enemy in levelData.monsters)
        {
            if (enemy == null || weaponItem == null) continue;
            if (ElementAttributeMapper.IsEffective(weaponItem.ItemAttribute, enemy.elementType))
                effectiveCount++;
            if (ElementAttributeMapper.IsWeak(weaponItem.ItemAttribute, enemy.elementType))
                weakCount++;
        }

        bonus += (effectiveCount / (float)total) * 15f;
        bonus -= (weakCount      / (float)total) * 10f;

        return bonus;
    }

    private static float GetTacticsBonus(HeroTactics tactics)
    {
        return tactics switch
        {
            HeroTactics.Aggressive   => 3f,
            HeroTactics.Defensive    => 2f,
            HeroTactics.Stealth      => 1f,
            HeroTactics.MagicFocused => 2f,
            _                        => 0f
        };
    }
}

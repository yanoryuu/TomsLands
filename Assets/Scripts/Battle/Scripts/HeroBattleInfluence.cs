using UnityEngine;

public static class HeroBattleInfluence
{
    public static float GetWeaponPerformanceMultiplier(HeroTactics tactics)
    {
        return tactics switch
        {
            HeroTactics.Aggressive => 1.20f,
            HeroTactics.Defensive => 0.90f,
            HeroTactics.Stealth => 0.90f,
            HeroTactics.MagicFocused => 0.95f,
            _ => 1.00f
        };
    }

    public static float GetArmorPerformanceMultiplier(HeroTactics tactics)
    {
        return tactics switch
        {
            HeroTactics.Defensive => 1.20f,
            HeroTactics.Aggressive => 0.90f,
            HeroTactics.Stealth => 1.10f,
            HeroTactics.MagicFocused => 0.95f,
            _ => 1.00f
        };
    }

    public static float GetAttributeMultiplier(HeroTactics tactics)
    {
        return tactics switch
        {
            HeroTactics.MagicFocused => 1.20f,
            HeroTactics.Aggressive => 1.05f,
            HeroTactics.Stealth => 0.90f,
            _ => 1.00f
        };
    }

    public static float GetHeatMultiplier(HeroTactics tactics)
    {
        return tactics switch
        {
            HeroTactics.Aggressive => 1.15f,
            HeroTactics.Defensive => 0.95f,
            HeroTactics.Stealth => 0.85f,
            HeroTactics.MagicFocused => 1.05f,
            _ => 1.00f
        };
    }

    public static string GetDisplayName(HeroTactics tactics)
    {
        return tactics switch
        {
            HeroTactics.Aggressive => "攻撃重視",
            HeroTactics.Defensive => "防御重視",
            HeroTactics.Stealth => "慎重",
            HeroTactics.MagicFocused => "属性重視",
            _ => "バランス"
        };
    }

    public static string BuildSummary(
        RuntimeHeroData hero,
        string weaponName,
        string armorName,
        BattlePriceSettings settings)
    {
        if (hero == null) return "";

        HeroTactics tactics = hero.tactics.Value;
        float weaponKill = GetWeaponPriceUpOnKill(settings, tactics);
        float weaponNonKill = GetWeaponPriceDownOnNonKill(settings, tactics);
        float armorBlock = GetArmorPriceUpOnBlock(settings, tactics);
        float armorHit = GetArmorPriceDownOnHit(settings, tactics);
        float effective = GetEffectiveAttributeRate(settings, tactics);
        float weak = GetWeakAttributeRate(settings, tactics);

        string weapon = string.IsNullOrEmpty(weaponName) ? "なし" : weaponName;
        string armor = string.IsNullOrEmpty(armorName) ? "なし" : armorName;

        return
            $"装備: 武器 {weapon} / 防具 {armor}\n" +
            "戦闘後: 勝利で装備品の評価が上がり、敗北で下がります。\n" +
            $"戦闘中: 撃破 -> 武器 {FormatRate(weaponKill)} / 撃破できず -> 武器 {FormatRate(weaponNonKill)}\n" +
            $"戦闘中: 防御成功 -> 防具 {FormatRate(armorBlock)} / 被弾 -> 防具 {FormatRate(armorHit)}\n" +
            $"属性: 有利 {FormatRate(effective)} / 不利 {FormatRate(weak)}\n" +
            $"作戦: {GetDisplayName(tactics)}（武器 x{GetWeaponPerformanceMultiplier(tactics):0.00}, 防具 x{GetArmorPerformanceMultiplier(tactics):0.00}, 属性 x{GetAttributeMultiplier(tactics):0.00}, 話題性 x{GetHeatMultiplier(tactics):0.00}）";
    }

    public static float GetWeaponPriceUpOnKill(BattlePriceSettings settings, HeroTactics tactics)
    {
        return ApplyPositiveMultiplier(settings != null ? settings.weaponPriceUpOnHit : 1.10f, GetWeaponPerformanceMultiplier(tactics));
    }

    public static float GetWeaponPriceDownOnNonKill(BattlePriceSettings settings, HeroTactics tactics)
    {
        return ApplyNegativeMultiplier(settings != null ? settings.weaponPriceDownOnNonKill : 0.95f, GetWeaponPerformanceMultiplier(tactics));
    }

    public static float GetArmorPriceUpOnBlock(BattlePriceSettings settings, HeroTactics tactics)
    {
        return ApplyPositiveMultiplier(settings != null ? settings.armorPriceUpOnBlock : 1.10f, GetArmorPerformanceMultiplier(tactics));
    }

    public static float GetArmorPriceDownOnHit(BattlePriceSettings settings, HeroTactics tactics)
    {
        return ApplyNegativeMultiplier(settings != null ? settings.armorPriceDownOnHit : 0.90f, GetArmorPerformanceMultiplier(tactics));
    }

    public static float GetEffectiveAttributeRate(BattlePriceSettings settings, HeroTactics tactics)
    {
        return ApplyPositiveMultiplier(settings != null ? settings.effectiveAttributeRate : 1.30f, GetAttributeMultiplier(tactics));
    }

    public static float GetWeakAttributeRate(BattlePriceSettings settings, HeroTactics tactics)
    {
        return ApplyNegativeMultiplier(settings != null ? settings.weakAttributeRate : 0.85f, GetAttributeMultiplier(tactics));
    }

    private static float ApplyPositiveMultiplier(float baseRate, float multiplier)
    {
        return 1f + ((baseRate - 1f) * multiplier);
    }

    private static float ApplyNegativeMultiplier(float baseRate, float multiplier)
    {
        return Mathf.Clamp(1f - ((1f - baseRate) * multiplier), 0f, 10f);
    }

    private static string FormatRate(float rate)
    {
        int percent = Mathf.RoundToInt((rate - 1f) * 100f);
        return percent >= 0 ? $"+{percent}%" : $"{percent}%";
    }
}

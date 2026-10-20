using UnityEngine;

public static class HeroBattleInfluence
{
    public static float GetWeaponPerformanceMultiplier(HeroTactics tactics)
        => tactics switch
        {
            HeroTactics.Aggressive => 1.20f,
            HeroTactics.Defensive => 0.90f,
            HeroTactics.Stealth => 0.90f,
            HeroTactics.MagicFocused => 0.95f,
            _ => 1.00f
        };

    public static float GetArmorPerformanceMultiplier(HeroTactics tactics)
        => tactics switch
        {
            HeroTactics.Defensive => 1.20f,
            HeroTactics.Aggressive => 0.90f,
            HeroTactics.Stealth => 1.10f,
            HeroTactics.MagicFocused => 0.95f,
            _ => 1.00f
        };

    public static float GetAttributeMultiplier(HeroTactics tactics)
        => tactics switch
        {
            HeroTactics.MagicFocused => 1.20f,
            HeroTactics.Aggressive => 1.05f,
            HeroTactics.Stealth => 0.90f,
            _ => 1.00f
        };

    public static float GetHeatMultiplier(HeroTactics tactics)
        => tactics switch
        {
            HeroTactics.Aggressive => 1.15f,
            HeroTactics.Defensive => 0.95f,
            HeroTactics.Stealth => 0.85f,
            HeroTactics.MagicFocused => 1.05f,
            _ => 1.00f
        };

    public static string GetDisplayName(HeroTactics tactics)
        => tactics switch
        {
            HeroTactics.Aggressive => "攻撃重視",
            HeroTactics.Defensive => "防御重視",
            HeroTactics.Stealth => "慎重",
            HeroTactics.MagicFocused => "属性重視",
            _ => "バランス"
        };

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
            $"作戦: {GetDisplayName(tactics)}（{BuildTacticsMultiplierText(tactics)}）";
    }

    /// <summary>
    /// 作戦の倍率の1行（「武器 x1.20, 防具 x0.90, 属性 x1.05, 話題性 x1.15」）。
    /// 作戦は配信中の値動き（相場）だけに効き、勝ち負け（クリア見込み）には効かない。勇者画面と BuildSummary で共有する。
    /// </summary>
    public static string BuildTacticsMultiplierText(HeroTactics tactics)
        => $"武器 x{GetWeaponPerformanceMultiplier(tactics):0.00}, 防具 x{GetArmorPerformanceMultiplier(tactics):0.00}, " +
           $"属性 x{GetAttributeMultiplier(tactics):0.00}, 話題性 x{GetHeatMultiplier(tactics):0.00}";

    /// <summary>
    /// いちばん強く上がる値動き（作戦のプルダウンに添える一言）。例: 攻撃重視 → 「武器↑」。上がるものが無ければ空。
    /// </summary>
    public static string GetStrongestEffectTag(HeroTactics tactics)
    {
        float weapon = GetWeaponPerformanceMultiplier(tactics);
        float armor = GetArmorPerformanceMultiplier(tactics);
        float attribute = GetAttributeMultiplier(tactics);
        float heat = GetHeatMultiplier(tactics);
        float max = Mathf.Max(Mathf.Max(weapon, armor), Mathf.Max(attribute, heat));
        if (max <= 1f) return "";
        if (Mathf.Approximately(max, weapon)) return "武器↑";
        if (Mathf.Approximately(max, armor)) return "防具↑";
        if (Mathf.Approximately(max, attribute)) return "属性↑";
        return "話題性↑";
    }

    public static float GetWeaponPriceUpOnKill(BattlePriceSettings settings, HeroTactics tactics)
        => ApplyPositiveMultiplier(settings != null ? settings.weaponPriceUpOnHit : 1.10f, GetWeaponPerformanceMultiplier(tactics));

    public static float GetWeaponPriceDownOnNonKill(BattlePriceSettings settings, HeroTactics tactics)
        => ApplyNegativeMultiplier(settings != null ? settings.weaponPriceDownOnNonKill : 0.95f, GetWeaponPerformanceMultiplier(tactics));

    public static float GetArmorPriceUpOnBlock(BattlePriceSettings settings, HeroTactics tactics)
        => ApplyPositiveMultiplier(settings != null ? settings.armorPriceUpOnBlock : 1.10f, GetArmorPerformanceMultiplier(tactics));

    public static float GetArmorPriceDownOnHit(BattlePriceSettings settings, HeroTactics tactics)
        => ApplyNegativeMultiplier(settings != null ? settings.armorPriceDownOnHit : 0.90f, GetArmorPerformanceMultiplier(tactics));

    public static float GetEffectiveAttributeRate(BattlePriceSettings settings, HeroTactics tactics)
        => ApplyPositiveMultiplier(settings != null ? settings.effectiveAttributeRate : 1.30f, GetAttributeMultiplier(tactics));

    public static float GetWeakAttributeRate(BattlePriceSettings settings, HeroTactics tactics)
        => ApplyNegativeMultiplier(settings != null ? settings.weakAttributeRate : 0.85f, GetAttributeMultiplier(tactics));

    private static float ApplyPositiveMultiplier(float baseRate, float multiplier)
        => 1f + ((baseRate - 1f) * multiplier);

    private static float ApplyNegativeMultiplier(float baseRate, float multiplier)
        => Mathf.Clamp(1f - ((1f - baseRate) * multiplier), 0f, 10f);

    private static string FormatRate(float rate)
    {
        int percent = Mathf.RoundToInt((rate - 1f) * 100f);
        return percent >= 0 ? $"+{percent}%" : $"{percent}%";
    }
}

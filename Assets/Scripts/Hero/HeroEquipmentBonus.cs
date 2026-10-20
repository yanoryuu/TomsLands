using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 勇者の装備（武器・防具）による戦闘ステータス補正。
/// 店で扱う装備のランク（ItemData.requiredLevel＝鍛冶屋レベル帯）が高いほど勇者が強くなる。
/// 値は GameConst（heroEquipment*）で調整する。
/// 実戦闘（CharacterModel）とクリア確率（ClearProbabilityCalculator）の両方がここを通るので、
/// 表示確率と実際の強さがズレない。
/// </summary>
public static class HeroEquipmentBonus
{
    public readonly struct Multipliers
    {
        public readonly float Hp;
        public readonly float Attack;
        public readonly float Defense;

        public Multipliers(float hp, float attack, float defense)
        {
            Hp = hp;
            Attack = attack;
            Defense = defense;
        }

        public static Multipliers None => new Multipliers(1f, 1f, 1f);
    }

    private static Dictionary<string, int> _masterTierCache;
    private static Dictionary<string, ItemTypeData.ItemAttribute> _masterAttributeCache;

    /// <summary>
    /// 勇者の武器の属性（未装備・不明なら null）。属性の戦闘効果（Q7）に使う。
    /// </summary>
    public static ItemTypeData.ItemAttribute? GetWeaponAttribute(RuntimeHeroData hero, ItemModel itemModel = null)
    {
        string id = hero?.weaponId.Value;
        if (string.IsNullOrEmpty(id)) return null;
        if (itemModel != null)
        {
            var item = itemModel.GetRuntimeItem(id);
            if (item != null) return ItemTypeData.NormalizeAttribute(item.ItemAttribute);
        }
        if (_masterAttributeCache == null)
        {
            _masterAttributeCache = new Dictionary<string, ItemTypeData.ItemAttribute>();
            var masters = ItemMaster.ApplyOverrides(AddressableLoader.LoadAll<ItemData>("ItemData"));
            if (masters != null)
                foreach (var m in masters)
                    if (m != null && !string.IsNullOrEmpty(m.itemId))
                        _masterAttributeCache[m.itemId] = ItemTypeData.NormalizeAttribute(m.itemAttribute);
        }
        return _masterAttributeCache.TryGetValue(id, out var attr) ? attr : (ItemTypeData.ItemAttribute?)null;
    }

    /// <summary>
    /// 属性の戦闘効果（Q7）: 武器の属性が敵の弱点（ElementAttributeMapper.IsEffective）なら、その敵への攻撃倍率
    /// 1 + GameConst.HeroAttributeAttackBonus（既定 +100%＝弱点で2倍）。それ以外は 1。
    /// 実戦闘（CharacterPresenter.PerformAttack）だけで使い、クリア確率（ClearProbabilityCalculator / Get）には入れない
    /// （2026-10 ユーザー決定。表示の確率は属性を考えない「控えめな見込み」になる）。
    /// </summary>
    public static float AttributeAttackMul(ItemTypeData.ItemAttribute? weaponAttribute, ElementType enemyElement)
    {
        if (!weaponAttribute.HasValue || enemyElement == ElementType.None) return 1f;
        return ElementAttributeMapper.IsEffective(weaponAttribute.Value, enemyElement)
            ? Mathf.Max(0f, 1f + GameConst.HeroAttributeAttackBonus)
            : 1f;
    }

    /// <summary>
    /// 装備から倍率を求める。itemModel があればそのランク情報を使い、
    /// 無ければ（FightScene の CharacterModel など）マスターデータから引く。
    /// </summary>
    public static Multipliers Get(RuntimeHeroData hero, ItemModel itemModel = null)
    {
        if (hero == null) return Multipliers.None;

        int weaponTier = GetTier(hero.weaponId.Value, itemModel);
        int armorTier = GetTier(hero.armorId.Value, itemModel);

        float attack = 1f;
        if (weaponTier > 0)
            attack += GameConst.HeroWeaponAttackBonusBase + GameConst.HeroWeaponAttackBonusPerTier * (weaponTier - 1);

        float defense = 1f;
        float hp = 1f;
        if (armorTier > 0)
        {
            defense += GameConst.HeroArmorDefenseBonusBase + GameConst.HeroArmorDefenseBonusPerTier * (armorTier - 1);
            hp += GameConst.HeroArmorHpBonusBase + GameConst.HeroArmorHpBonusPerTier * (armorTier - 1);
        }

        return new Multipliers(Mathf.Max(0.1f, hp), Mathf.Max(0.1f, attack), Mathf.Max(0.1f, defense));
    }

    /// <summary>装備のランク（requiredLevel）。未装備・不明なら 0。</summary>
    private static int GetTier(string itemId, ItemModel itemModel)
    {
        if (string.IsNullOrEmpty(itemId)) return 0;

        if (itemModel != null)
        {
            var item = itemModel.GetRuntimeItem(itemId);
            if (item != null) return Mathf.Max(1, item.RequiredLevel.Value);
        }

        if (_masterTierCache == null)
        {
            _masterTierCache = new Dictionary<string, int>();
            var masters = ItemMaster.ApplyOverrides(AddressableLoader.LoadAll<ItemData>("ItemData"));
            if (masters != null)
            {
                foreach (var m in masters)
                {
                    if (m != null && !string.IsNullOrEmpty(m.itemId))
                        _masterTierCache[m.itemId] = Mathf.Max(1, m.requiredLevel);
                }
            }
        }

        return _masterTierCache.TryGetValue(itemId, out int tier) ? tier : 0;
    }
}

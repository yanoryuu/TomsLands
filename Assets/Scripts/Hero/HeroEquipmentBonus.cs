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

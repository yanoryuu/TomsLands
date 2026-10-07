using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// レベルデザイン用ツール（エディタ専用）。ダンジョン×レベルごとの敵ステータス・防衛報酬・魔王軍支援の費用を
/// 「強さの目安（勇者レベル換算の P）」から式で作って SO に書き込み、勇者Lv×ダンジョンLvの勝敗表を出す。
/// 経緯と各サイクルの結果は Docs/Level_Design_Log.md。
///
/// P(ダンジョン, Lv) = tierBase[ダンジョン] + levelStep × (Lv − 1)。
/// 敵は「勇者レベル P の勇者（装備なし）」の攻撃 A(P)・防御 D(P)・HP H(P) から作る:
///   通常: HP = A×normalHp / 防御 = A×normalDef / 攻撃 = D + H×attackK
///   ボス: HP = A×bossHp   / 防御 = A×bossDef   / 攻撃 = D + H×attackK×bossAttackMul
/// （ダメージ式 max(1, 攻撃−防御) なので、攻撃は「勇者の防御 + 1発で削る HP」で作るのが素直）
/// </summary>
public static class AutoPlayLevelDesignTool
{
    [Serializable]
    public sealed class Params
    {
        public float forest = 1.0f, ice = 1.4f, volcano = 1.8f, mausoleum = 2.3f, mechanical = 2.8f, demonKing = 3.4f;
        public float levelStep = 0.8f;
        public float normalHp = 1.4f, normalDef = 0.08f;
        public float bossHp = 4.5f, bossDef = 0.12f, bossAttackMul = 2f;
        public float attackK = 0.03f;
        /// <summary>防衛報酬（Lv1〜5）。</summary>
        public int[] rewards = { 10000, 25000, 50000, 100000, 160000 };
        /// <summary>魔王軍支援の費用（Lv1→2, 2→3, 3→4, 4→5。Lv5 の値は使われない）。</summary>
        public int[] supportCosts = { 5000, 15000, 30000, 60000, 0 };

        public float TierBase(DungeonName d) => d switch
        {
            DungeonName.DeepGreenBeastForest => forest,
            DungeonName.IceMistCave => ice,
            DungeonName.ScorchingVolcanoPrison => volcano,
            DungeonName.MausoleumOblivion => mausoleum,
            DungeonName.AncientMechanicalCastle => mechanical,
            _ => demonKing,
        };

        public float Power(DungeonName d, int level) => TierBase(d) + levelStep * (level - 1);
    }

    // -----------------------------------------------------------------
    // 勇者のステータス（HeroStatusData ＋ RemoteBalance.heroLevels）を小数レベルで補間
    // -----------------------------------------------------------------

    private static List<HeroLevelData> HeroTable()
    {
        var loader = new HeroLevelDataLoader();
        loader.LoadFromCSV("HeroStatusData");
        var list = new List<HeroLevelData>();
        for (int lv = 1; lv <= loader.GetMaxLevel(); lv++)
        {
            var d = loader.GetLevelData(lv);
            if (d != null) list.Add(d);
        }
        return list;
    }

    private static (float hp, float atk, float def) HeroAt(List<HeroLevelData> table, float p)
    {
        p = Mathf.Clamp(p, 1f, table.Count);
        int lo = Mathf.FloorToInt(p), hi = Mathf.Min(table.Count, lo + 1);
        float t = p - lo;
        var a = table[lo - 1];
        var b = table[hi - 1];
        return (Mathf.Lerp(a.MaxHp, b.MaxHp, t), Mathf.Lerp(a.Attack, b.Attack, t), Mathf.Lerp(a.Defense, b.Defense, t));
    }

    // -----------------------------------------------------------------
    // 適用
    // -----------------------------------------------------------------

    public static List<DungeonInfoScriptableObj> LoadDungeons() =>
        AssetDatabase.FindAssets("t:DungeonInfoScriptableObj")
            .Select(g => AssetDatabase.LoadAssetAtPath<DungeonInfoScriptableObj>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null)
            .GroupBy(d => d.key).Select(g => g.First())
            .ToList();

    /// <summary>式で敵・報酬・支援費を作って SO に書き込む。戻り値は変更内容の一覧。</summary>
    public static string Apply(Params p, bool dryRun = false)
    {
        var table = HeroTable();
        var sb = new StringBuilder();
        foreach (var so in LoadDungeons())
        {
            var levels = GetLevelDataList(so);
            for (int lv = 1; lv <= levels.Count; lv++)
            {
                var data = levels[lv - 1];
                if (data == null) continue;
                float power = p.Power(so.key, lv);
                var (hp, atk, def) = HeroAt(table, power);
                sb.Append($"{so.key} Lv{lv} P={power:F1}: ");
                foreach (var e in data.monsters.Where(e => e != null).Distinct())
                {
                    int eHp, eAtk, eDef;
                    if (e.isBoss)
                    {
                        eHp = Mathf.RoundToInt(atk * p.bossHp);
                        eDef = Mathf.RoundToInt(atk * p.bossDef);
                        eAtk = Mathf.RoundToInt(def + hp * p.attackK * p.bossAttackMul);
                    }
                    else
                    {
                        eHp = Mathf.RoundToInt(atk * p.normalHp);
                        eDef = Mathf.RoundToInt(atk * p.normalDef);
                        eAtk = Mathf.RoundToInt(def + hp * p.attackK);
                    }
                    sb.Append($"{e.name}({e.hp}/{e.attackPower}/{e.defensePower}→{eHp}/{eAtk}/{eDef}) ");
                    if (!dryRun)
                    {
                        Undo.RecordObject(e, "Level design");
                        e.hp = eHp;
                        e.attackPower = eAtk;
                        e.defensePower = eDef;
                        EditorUtility.SetDirty(e);
                    }
                }
                int reward = p.rewards[Mathf.Clamp(lv - 1, 0, p.rewards.Length - 1)];
                int cost = p.supportCosts[Mathf.Clamp(lv - 1, 0, p.supportCosts.Length - 1)];
                sb.AppendLine($"reward {data.rewardGold}→{reward} cost {data.levelUpCost}→{cost}");
                if (!dryRun)
                {
                    data.rewardGold = reward;
                    data.levelUpCost = cost;
                }
            }
            if (!dryRun) EditorUtility.SetDirty(so);
        }
        if (!dryRun) AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    private static List<DungeonLevelData> GetLevelDataList(DungeonInfoScriptableObj so)
    {
        return so.levelDataList != null ? so.levelDataList.ToList() : new List<DungeonLevelData>();
    }

    // -----------------------------------------------------------------
    // 勝敗表（勇者Lv × ダンジョンLv、ダンジョン別）
    // -----------------------------------------------------------------

    /// <summary>
    /// 勇者Lv1〜maxHero（装備 tier = gearTier、0 で装備なし）× ダンジョンLv1〜5 の勝敗と残りHP。
    /// セル = 残りHP%（負けは "x"）。介入なし。
    /// </summary>
    public static string Matrix(int maxHero = 7, int gearTier = 1, AutoPlayInterventionOrder[] orders = null)
    {
        var settings = StreamingInteractionSettings.Load();
        var loader = new HeroLevelDataLoader();
        loader.LoadFromCSV("HeroStatusData");
        var sb = new StringBuilder();
        sb.AppendLine($"gearTier={gearTier} orders={(orders == null ? "none" : string.Join("+", orders.Select(o => o.Kind)))}  cell=残りHP% / x=負け");
        foreach (var so in LoadDungeons().OrderBy(d => d.difficulty))
        {
            var d = new DungeonData(so);
            sb.AppendLine($"## {so.key}");
            for (int lv = 1; lv <= 5; lv++)
            {
                sb.Append($"  Lv{lv}:");
                for (int hl = 1; hl <= maxHero; hl++)
                {
                    var ld = loader.GetLevelData(hl);
                    if (ld == null) { sb.Append("   -"); continue; }
                    var hero = RuntimeHeroData.CreateFromLevelData(ld);
                    var o = SimulateWithGear(hero, d, lv, gearTier, settings, orders);
                    sb.Append(o.Victory ? $" {Mathf.RoundToInt(o.HeroHpRatio * 100),3}" : "   x");
                }
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    /// <summary>装備 tier を倍率で反映したドライラン（HeroEquipmentBonus の式を GameConst から）。</summary>
    public static AutoPlayBattleSurrogate.Outcome SimulateWithGear(RuntimeHeroData hero, DungeonData d, int level, int gearTier,
        StreamingInteractionSettings settings, AutoPlayInterventionOrder[] orders)
    {
        if (gearTier > 0)
        {
            float atkMul = 1f + GameConst.HeroWeaponAttackBonusBase + GameConst.HeroWeaponAttackBonusPerTier * (gearTier - 1);
            float defMul = 1f + GameConst.HeroArmorDefenseBonusBase + GameConst.HeroArmorDefenseBonusPerTier * (gearTier - 1);
            float hpMul = 1f + GameConst.HeroArmorHpBonusBase + GameConst.HeroArmorHpBonusPerTier * (gearTier - 1);
            hero.attackPower.Value = Mathf.RoundToInt(hero.attackPower.Value * atkMul);
            hero.defensePower.Value = Mathf.RoundToInt(hero.defensePower.Value * defMul);
            hero.hp.Value = Mathf.RoundToInt(hero.hp.Value * hpMul);
        }
        return AutoPlayBattleSurrogate.Simulate(new AutoPlayBattleSurrogate.StreamInput
        {
            Hero = hero, Dungeon = d, Level = level, Rng = new System.Random(1),
            Settings = settings, Orders = orders ?? new AutoPlayInterventionOrder[0],
            StartMoney = 10000000, SecondsPerTurn = 3f,
        });
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 配信中の値動き（勇者の攻撃・被弾、敵撃破、配信熱）を、実際の BattlePriceSettings の値で1配信ぶん回して確かめる。
/// イベントごとの価格の動かし方は StreamingSalesController の購読処理と同じ順に呼ぶ。
/// </summary>
public class StreamingPriceDriftTests
{
    private const string SettingsPath = "Assets/Resources_moved/BattlePriceSettings.asset";
    private const int StartPrice = 1000;

    private BattlePriceSettings _settings;

    [SetUp]
    public void SetUp()
    {
        _settings = AssetDatabase.LoadAssetAtPath<BattlePriceSettings>(SettingsPath);
        Assert.IsNotNull(_settings, SettingsPath + " が読めない");
    }

    /// <summary>1配信の値動きだけを再現する（売買・需要は扱わない）。</summary>
    private class StreamSim
    {
        private readonly BattlePriceSettings _s;
        private readonly StreamingSalesModel _model = new(15f, 0f);
        public readonly StreamingHeatModel Heat;
        public readonly RuntimeItemData Weapon = NewItem("weapon", ItemTypeData.ItemType.Weapon);
        public readonly RuntimeItemData Armor = NewItem("armor", ItemTypeData.ItemType.Armor);
        public readonly RuntimeItemData Tool = NewItem("tool", ItemTypeData.ItemType.Tool);
        public readonly List<int> HeatTiers = new();
        public int Blocks, Hits;
        private readonly System.Random _rng = new(12345);

        public StreamSim(BattlePriceSettings s)
        {
            _s = s;
            Heat = new StreamingHeatModel(s);
            _model.SetItemsForSale(new List<RuntimeItemData> { Weapon, Armor, Tool });
        }

        private static RuntimeItemData NewItem(string id, ItemTypeData.ItemType type) =>
            new(id, id, StartPrice, 10, 10, 1, null, null, type, ItemTypeData.ItemAttribute.Fire, 1, initialTrend: 0f);

        public int EffectiveHits, IneffectiveHits;

        /// <summary>
        /// 勇者の攻撃。倒したら武器↑（ピンチ中なら逆転の見せ場、そうでなければ楽勝の撃破）。
        /// 倒せなければ、与えたダメージの割合から出した確率で「効いた」武器↑ /「効かなかった」武器↓。
        /// </summary>
        public void HeroAttacks(bool kill, bool heroInPinch = false, int damage = 50, int enemyMaxHp = 100)
        {
            float rate;
            if (kill) rate = HeroBattleInfluence.GetWeaponPriceUpOnKill(_s, HeroTactics.Balanced);
            else if (HeroBattleInfluence.IsWeaponEffectiveHit(damage, enemyMaxHp, _s, (float)_rng.NextDouble()))
            {
                EffectiveHits++;
                rate = HeroBattleInfluence.GetWeaponPriceUpOnEffectiveHit(_s, HeroTactics.Balanced);
            }
            else
            {
                IneffectiveHits++;
                rate = HeroBattleInfluence.GetWeaponPriceDownOnNonKill(_s, HeroTactics.Balanced);
            }
            _model.AdjustPricesByType(ItemTypeData.ItemType.Weapon, rate, null);
            if (!kill) return;
            if (heroInPinch)
            {
                Heat.AddHeat(_s.comebackKillHeat);
                Heat.MarkHighlight();
            }
            else Heat.AddEasyKillHeat(_s.easyKillHeat);
        }

        /// <summary>敵の攻撃を勇者が受けた。防げたら防具↑、被弾なら防具↓。</summary>
        public void HeroIsAttacked(int enemyAttack, int heroDefense)
        {
            bool block = HeroBattleInfluence.IsArmorBlock(enemyAttack, heroDefense, _s, (float)_rng.NextDouble());
            if (block) Blocks++; else Hits++;
            float rate = block
                ? HeroBattleInfluence.GetArmorPriceUpOnBlock(_s, HeroTactics.Balanced)
                : HeroBattleInfluence.GetArmorPriceDownOnHit(_s, HeroTactics.Balanced);
            _model.AdjustPricesByType(ItemTypeData.ItemType.Armor, rate, null);
        }

        /// <summary>ピンチ（HP が境目を下回った瞬間）。</summary>
        public void HeroPinch()
        {
            Heat.AddHeat(_s.pinchHeat);
            Heat.MarkHighlight();
        }

        /// <summary>ターン終わり（ExecuteTurnSales の配信熱の部分）。</summary>
        public void EndTurn()
        {
            Heat.ApplyTurnDecay();
            float mult = Heat.GetPriceMultiplier();
            if (mult != 1f) _model.AdjustAllPrices(mult, null);
            HeatTiers.Add(Heat.GetTierIndex());
        }

        public string Describe() =>
            $"武器 {Weapon.CurrentPrice.Value} / 防具 {Armor.CurrentPrice.Value} / 道具 {Tool.CurrentPrice.Value}（開始 {StartPrice}）, " +
            $"効いた {EffectiveHits} / 効かず {IneffectiveHits}, 防御成功 {Blocks} / 被弾 {Hits}, 熱 {Heat.Heat.Value:F1}, ティア推移 [{string.Join(",", HeatTiers)}]";
    }

    /// <summary>
    /// 勇者がピンチにならずに順当に勝つ配信（2ターンに1体倒す、毎ターン3回殴られる、12ターン）。
    /// 勇者に張った品（武器）は上がり、配信熱のせいで全部の品が下がり続けることはない。
    /// </summary>
    [Test]
    public void SteadyWin_PricesDoNotSinkIntoColdTier()
    {
        var sim = new StreamSim(_settings);
        for (int turn = 1; turn <= 12; turn++)
        {
            sim.HeroAttacks(kill: turn % 2 == 0);
            for (int i = 0; i < 3; i++) sim.HeroIsAttacked(enemyAttack: 25, heroDefense: 16);
            sim.EndTurn();
        }
        Debug.Log("[SteadyWin] " + sim.Describe());

        CollectionAssert.DoesNotContain(sim.HeatTiers, 0, "敵を倒し続けているのに配信熱が冷め気味に落ちた: " + sim.Describe());
        Assert.GreaterOrEqual(sim.Tool.CurrentPrice.Value, StartPrice, "配信熱で道具が値下がりした: " + sim.Describe());
        Assert.Greater(sim.Weapon.CurrentPrice.Value, StartPrice, "順当に勝っているのに武器が上がらない: " + sim.Describe());
    }

    /// <summary>防御成功の確率は 防御 ÷ 攻撃 を下限・上限に収めたもの。</summary>
    [Test]
    public void ArmorBlockChance_FollowsDefenseOverAttack()
    {
        Assert.AreEqual(16f / 25f, HeroBattleInfluence.GetArmorBlockChance(25, 16, _settings), 1e-4f);
        Assert.AreEqual(_settings.armorBlockChanceMax, HeroBattleInfluence.GetArmorBlockChance(10, 16, _settings), 1e-4f, "弱い敵でも上限で止まる");
        Assert.AreEqual(_settings.armorBlockChanceMin, HeroBattleInfluence.GetArmorBlockChance(1000, 16, _settings), 1e-4f, "強い敵でも下限は残る");
        Assert.IsTrue(HeroBattleInfluence.IsArmorBlock(25, 16, _settings, 0.5f));
        Assert.IsFalse(HeroBattleInfluence.IsArmorBlock(25, 16, _settings, 0.7f));
    }

    /// <summary>倒しきれなかった一撃が「効いた」確率は 与ダメ ÷ 最大HP × 係数 を下限・上限に収めたもの。</summary>
    [Test]
    public void WeaponEffectiveHitChance_FollowsDamageShare()
    {
        float scale = _settings.weaponEffectiveHitChanceScale;
        Assert.AreEqual(Mathf.Clamp(20f / 100f * scale, _settings.weaponEffectiveHitChanceMin, _settings.weaponEffectiveHitChanceMax),
            HeroBattleInfluence.GetWeaponEffectiveHitChance(20, 100, _settings), 1e-4f);
        Assert.AreEqual(_settings.weaponEffectiveHitChanceMax, HeroBattleInfluence.GetWeaponEffectiveHitChance(90, 100, _settings), 1e-4f, "大きく削っても上限で止まる");
        Assert.AreEqual(_settings.weaponEffectiveHitChanceMin, HeroBattleInfluence.GetWeaponEffectiveHitChance(1, 1000, _settings), 1e-4f, "硬い敵でも下限は残る");
        Assert.IsTrue(HeroBattleInfluence.IsWeaponEffectiveHit(20, 100, _settings, 0f));
        Assert.IsFalse(HeroBattleInfluence.IsWeaponEffectiveHit(20, 100, _settings, 0.99f));
    }

    /// <summary>硬いボス（最大HP763 に 35 ずつ、22発）でも「効いた」一撃はときどき出て、武器は下がる一方にはならない。</summary>
    [Test]
    public void ToughBoss_WeaponSometimesRises()
    {
        var sim = new StreamSim(_settings);
        int ups = 0;
        for (int i = 0; i < 21; i++)
        {
            int before = sim.Weapon.CurrentPrice.Value;
            sim.HeroAttacks(kill: false, damage: 35, enemyMaxHp: 763);
            if (sim.Weapon.CurrentPrice.Value > before) ups++;
        }
        sim.HeroAttacks(kill: true);
        Debug.Log("[ToughBoss] " + sim.Describe());
        Assert.Greater(ups, 0, "硬いボス相手に武器が一度も上がらない: " + sim.Describe());
        Assert.Greater(sim.IneffectiveHits, sim.EffectiveHits, "硬いボスなのに効いた一撃の方が多い: " + sim.Describe());
    }

    /// <summary>強い敵（攻撃94 vs 防御16）でも防御成功はときどき起き、防具は下がる一方にはならない。</summary>
    [Test]
    public void StrongEnemy_ArmorSometimesBlocks()
    {
        var sim = new StreamSim(_settings);
        int ups = 0;
        for (int i = 0; i < 60; i++)
        {
            int before = sim.Armor.CurrentPrice.Value;
            sim.HeroIsAttacked(enemyAttack: 94, heroDefense: 16);
            if (sim.Armor.CurrentPrice.Value > before) ups++;
        }
        Debug.Log("[StrongEnemy] " + sim.Describe());
        Assert.Greater(sim.Blocks, 0, "強い敵に一度も防御成功しない: " + sim.Describe());
        Assert.Greater(ups, 0, "強い敵相手に防具が一度も上がらない: " + sim.Describe());
        Assert.Greater(sim.Hits, sim.Blocks, "強い敵なのに被弾より防御成功が多い: " + sim.Describe());
    }

    /// <summary>勇者の防御が敵の攻撃に近い（敵19 vs 防御16）なら、防御成功が多く防具は上がる。</summary>
    [Test]
    public void HeroShrugsOffWeakHits_ArmorPriceRises()
    {
        var sim = new StreamSim(_settings);
        for (int turn = 1; turn <= 6; turn++)
        {
            sim.HeroAttacks(kill: turn % 2 == 0);
            for (int i = 0; i < 3; i++) sim.HeroIsAttacked(enemyAttack: 19, heroDefense: 16);
            sim.EndTurn();
        }
        Debug.Log("[ArmorBlock] " + sim.Describe());
        Assert.Greater(sim.Armor.CurrentPrice.Value, StartPrice, "攻撃をほぼ防いでいるのに防具が上がらない: " + sim.Describe());
    }

    /// <summary>膠着（誰も倒せず見せ場もない）が続けば、#134 のとおり冷め気味まで落ちて値が下がる。</summary>
    [Test]
    public void Stalemate_StillCoolsDown()
    {
        var sim = new StreamSim(_settings);
        for (int turn = 1; turn <= 8; turn++)
        {
            sim.HeroAttacks(kill: false);
            sim.EndTurn();
        }
        Debug.Log("[Stalemate] " + sim.Describe());
        Assert.AreEqual(0, sim.Heat.GetTierIndex(), "膠着が続いても冷めない: " + sim.Describe());
        Assert.Less(sim.Tool.CurrentPrice.Value, StartPrice);
    }

    /// <summary>ピンチから逆転する配信は、盛り上がって全部の品が上がる。</summary>
    [Test]
    public void PinchAndComeback_PricesRise()
    {
        var sim = new StreamSim(_settings);
        sim.HeroAttacks(kill: false); sim.EndTurn();
        sim.HeroPinch(); sim.HeroAttacks(kill: false); sim.EndTurn();
        sim.HeroAttacks(kill: true, heroInPinch: true); sim.EndTurn();
        sim.HeroAttacks(kill: true, heroInPinch: true); sim.EndTurn();
        Debug.Log("[Comeback] " + sim.Describe());
        Assert.Greater(sim.Tool.CurrentPrice.Value, StartPrice, sim.Describe());
        Assert.Greater(sim.Weapon.CurrentPrice.Value, StartPrice, sim.Describe());
    }
}

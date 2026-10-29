using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 店の設備のレベル・強化・故障・修理（2026-10-29）。
/// レベルの効果 / 強化費と村の割引 / 故障率の予定（固定の乱数）/ 修理で寿命が戻る / セーブの往復 / 同じ設備は 1 台まで。
/// セーブは SaveSlotManager.DevRootOverride で使い捨てのフォルダへ逃がす（ユーザーのスロットを汚さない）。
/// </summary>
public class ShopMachineLevelTests
{
    private string _prevRoot;
    private string _root;
    private readonly List<Object> _created = new();

    [SetUp]
    public void SetUp()
    {
        _prevRoot = SaveSlotManager.DevRootOverride;
        _root = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Temp", "ShopMachineLevelTests");
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        Directory.CreateDirectory(_root);
        SaveSlotManager.DevRootOverride = _root;
    }

    [TearDown]
    public void TearDown()
    {
        SaveSlotManager.DevRootOverride = _prevRoot;
        foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
        _created.Clear();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private ShopMachineData Machine(string id, ShopMachineEffectType type, int[] costs, float[] effects, int[] safe = null, float[] step = null)
    {
        var m = ScriptableObject.CreateInstance<ShopMachineData>();
        _created.Add(m);
        m.machineId = id;
        m.machineName = id;
        m.effectType = type;
        m.requiredShopLevel = 1;
        m.dailyItemSelectable = type == ShopMachineEffectType.DailyItem;
        m.levels = new List<MachineLevelEntry>();
        for (int i = 0; i < costs.Length; i++)
            m.levels.Add(new MachineLevelEntry
            {
                cost = costs[i], effectValue = effects[i],
                safeDays = safe != null ? safe[i] : 0, breakChanceStep = step != null ? step[i] : 0f,
            });
        return m;
    }

    private ShopMachineData MoneyS() => Machine("money_s", ShopMachineEffectType.DailyMoney,
        new[] { 6000, 4200, 4900, 5600, 6300 }, new[] { 1200f, 2400, 3800, 5400, 7200 },
        new[] { 4, 5, 6, 7, 8 }, new[] { 0.15f, 0.12f, 0.10f, 0.08f, 0.06f });

    private ShopMachineData Fridge(string id = "fridge", float lv1 = 0.3f) => Machine(id, ShopMachineEffectType.UndisplayedDemandKeep,
        new[] { 5000, 3000 }, new[] { lv1, 0.4f });

    private static TomsModel Toms(int money)
    {
        var t = new TomsModel();
        t.PlayerMoney.Value = money;
        t.ShopLevel.Value = 5;
        return t;
    }

    [Test]
    public void EffectFollowsLevel()
    {
        var money = MoneyS();
        var attract = Machine("attract", ShopMachineEffectType.RevenueMultiplier, new[] { 12000, 5000 }, new[] { 0.06f, 0.09f });
        var model = new ShopMachineModel(new List<ShopMachineData> { money, attract });
        var toms = Toms(100000);
        var placed = model.TryPurchaseAndPlace(money, toms, 5);
        var a = model.TryPurchaseAndPlace(attract, toms, 5);

        Assert.AreEqual(1200, model.ExecuteDailyEffects(null).TotalMoney);
        Assert.AreEqual(1.06f, model.TotalRevenueMultiplier, 1e-4f);

        Assert.IsTrue(model.TryUpgrade(placed.PlacementId, toms));
        Assert.IsTrue(model.TryUpgrade(a.PlacementId, toms));
        Assert.AreEqual(2, placed.Level);
        var result = model.ExecuteDailyEffects(null);
        Assert.AreEqual(2400, result.TotalMoney);
        Assert.AreEqual(2400, result.MoneySources[0].Amount);
        Assert.AreEqual(1.09f, model.TotalRevenueMultiplier, 1e-4f);
    }

    [Test]
    public void FridgeReductionsCombineMultiplicatively()
    {
        var f1 = Fridge("fridge", 0.3f);
        var f2 = Fridge("fridge_l", 0.5f);
        var model = new ShopMachineModel(new List<ShopMachineData> { f1, f2 });
        var toms = Toms(100000);
        Assert.AreEqual(1f, model.NotDisplayedDemandDropRate, 1e-5f);
        model.TryPurchaseAndPlace(f1, toms, 5);
        Assert.AreEqual(0.7f, model.NotDisplayedDemandDropRate, 1e-5f);
        model.TryPurchaseAndPlace(f2, toms, 5);
        Assert.AreEqual(0.35f, model.NotDisplayedDemandDropRate, 1e-5f);
    }

    [Test]
    public void UpgradeCost_UsesNextRow_WithArtisanDiscount_AndMaxIsMinusOne()
    {
        var money = MoneyS();
        var model = new ShopMachineModel(new List<ShopMachineData> { money }) { PurchaseCostRate = 0.75f };
        var toms = Toms(100000);
        var placed = model.TryPurchaseAndPlace(money, toms, 5);
        Assert.AreEqual(100000 - 4500, toms.PlayerMoney.Value, "購入費 6000 × 0.75");
        Assert.AreEqual(3150, model.GetUpgradeCost(placed), "Lv2 の行 4200 × 0.75");

        Assert.IsTrue(model.TryUpgrade(placed.PlacementId, toms));
        Assert.AreEqual(100000 - 4500 - 3150, toms.PlayerMoney.Value);
        Assert.AreEqual(4500 + 3150, placed.TotalPaid);
        Assert.AreEqual(Mathf.RoundToInt((4500 + 3150) * 0.5f), model.GetRemoveRefund(placed), "撤去は払った額（購入＋強化）の半分");

        for (int i = 0; i < 3; i++) Assert.IsTrue(model.TryUpgrade(placed.PlacementId, toms));
        Assert.AreEqual(5, placed.Level);
        Assert.AreEqual(-1, model.GetUpgradeCost(placed));
        Assert.IsFalse(model.TryUpgrade(placed.PlacementId, toms));
    }

    [Test]
    public void RepairCost_IsRateTimesCurrentLevelRow_WithDiscount()
    {
        var money = MoneyS();
        var model = new ShopMachineModel(new List<ShopMachineData> { money }) { PurchaseCostRate = 0.85f };
        var toms = Toms(100000);
        var placed = model.TryPurchaseAndPlace(money, toms, 5);
        float rate = ShopMachineModel.RepairCostRate;
        Assert.AreEqual(Mathf.RoundToInt(Mathf.RoundToInt(6000 * rate) * 0.85f), model.GetRepairCost(placed));
        model.TryUpgrade(placed.PlacementId, toms);
        Assert.AreEqual(Mathf.RoundToInt(Mathf.RoundToInt(4200 * rate) * 0.85f), model.GetRepairCost(placed));
    }

    [Test]
    public void BreakChanceSchedule_SafeDaysThenStep()
    {
        var money = MoneyS();
        Assert.AreEqual(0f, money.BreakChanceOnMorning(1, 4));
        Assert.AreEqual(0.15f, money.BreakChanceOnMorning(1, 5), 1e-5f);
        Assert.AreEqual(0.30f, money.BreakChanceOnMorning(1, 6), 1e-5f);
        Assert.AreEqual(1f, money.BreakChanceOnMorning(1, 30), 1e-5f, "1 で頭打ち");
        Assert.AreEqual(0f, money.BreakChanceOnMorning(2, 5), "Lv2 は 5 朝まで安心");
        Assert.AreEqual(0.12f, money.BreakChanceOnMorning(2, 6), 1e-5f);
        Assert.AreEqual(0f, Fridge().BreakChanceOnMorning(1, 100), "safeDays 0・step 0 は壊れない");
    }

    [Test]
    public void Breakdown_WithFixedRoll_StopsEffectUntilRepaired()
    {
        var money = MoneyS();
        var model = new ShopMachineModel(new List<ShopMachineData> { money });
        var toms = Toms(100000);
        var placed = model.TryPurchaseAndPlace(money, toms, 5);

        // 乱数を 0.2 に固定: 5 朝目は 15% < 0.2 で壊れず、6 朝目の 30% で壊れる
        for (int morning = 1; morning <= 5; morning++)
        {
            Assert.AreEqual(0, model.AdvanceDurability(_ => 0.2f).Count, $"{morning} 朝目");
            Assert.IsFalse(placed.Broken);
        }
        Assert.AreEqual(0, model.SafeMorningsLeft(placed));
        Assert.AreEqual(0.30f, model.NextBreakChance(placed), 1e-5f);
        Assert.IsTrue(model.CanRepair(placed), "明朝から危ないので点検できる");

        var broken = model.AdvanceDurability(_ => 0.2f);
        Assert.AreEqual(1, broken.Count);
        Assert.IsTrue(placed.Broken);
        Assert.AreEqual(0, model.ExecuteDailyEffects(null).TotalMoney, "壊れている間は何も産まない");
        Assert.AreEqual(0, model.ExecuteDailyEffects(null).MoneySources.Count, "入金の行も出さない");
        StringAssert.Contains("故障した", model.BreakdownLine(placed));
        Assert.AreEqual(1, placed.Level, "レベルは残る");

        int before = toms.PlayerMoney.Value;
        Assert.IsTrue(model.TryRepair(placed.PlacementId, toms));
        Assert.AreEqual(before - model.GetRepairCost(placed), toms.PlayerMoney.Value);
        Assert.IsFalse(placed.Broken);
        Assert.AreEqual(0, placed.DaysSinceService, "修理で寿命が戻る");
        Assert.AreEqual(4, model.SafeMorningsLeft(placed));
        Assert.IsFalse(model.CanRepair(placed), "安心な間は修理できない");
        Assert.AreEqual(1200, model.ExecuteDailyEffects(null).TotalMoney);
    }

    [Test]
    public void Upgrade_ResetsLifespan_AndFixesBrokenMachine()
    {
        var money = MoneyS();
        var model = new ShopMachineModel(new List<ShopMachineData> { money });
        var toms = Toms(100000);
        var placed = model.TryPurchaseAndPlace(money, toms, 5);
        for (int i = 0; i < 10 && !placed.Broken; i++) model.AdvanceDurability(_ => 0f);
        Assert.IsTrue(placed.Broken);
        Assert.IsTrue(model.TryUpgrade(placed.PlacementId, toms));
        Assert.IsFalse(placed.Broken);
        Assert.AreEqual(0, placed.DaysSinceService);
        Assert.AreEqual(5, model.SafeMorningsLeft(placed), "Lv2 の safeDays");
    }

    [Test]
    public void BreakRoll_IsDeterministic()
    {
        float a = ShopMachineModel.BreakRoll(1234, 7, "machine_money_s");
        Assert.AreEqual(a, ShopMachineModel.BreakRoll(1234, 7, "machine_money_s"));
        Assert.AreNotEqual(a, ShopMachineModel.BreakRoll(1234, 8, "machine_money_s"));
        Assert.That(a, Is.InRange(0f, 1f));
    }

    [Test]
    public void SaveRoundTrip_KeepsLevelDaysBrokenAndPaid()
    {
        var money = MoneyS();
        var machines = new List<ShopMachineData> { money };
        var model = new ShopMachineModel(machines);
        var toms = Toms(100000);
        var placed = model.TryPurchaseAndPlace(money, toms, 5);
        model.TryUpgrade(placed.PlacementId, toms);
        for (int i = 0; i < 20 && !placed.Broken; i++) model.AdvanceDurability(_ => 0f);

        var loaded = new ShopMachineModel(machines);   // コンストラクタがセーブを読む
        Assert.AreEqual(1, loaded.Placed.Count);
        var p = loaded.Placed[0];
        Assert.AreEqual(placed.PlacementId, p.PlacementId);
        Assert.AreEqual(2, p.Level);
        Assert.AreEqual(placed.DaysSinceService, p.DaysSinceService);
        Assert.IsTrue(p.Broken);
        Assert.AreEqual(placed.TotalPaid, p.TotalPaid);
    }

    [Test]
    public void OldSave_DefaultsToLv1_AndMergesDuplicatesIntoLevels()
    {
        var money = MoneyS();
        var model = new ShopMachineModel(new List<ShopMachineData> { money, Fridge() });

        // 旧セーブ: レベル・寿命・故障の項目が無い JSON
        var old = JsonUtility.FromJson<ShopMachineSaveData>(
            "{\"placed\":[{\"placementId\":\"a\",\"machineId\":\"money_s\",\"selectedItemId\":\"\",\"productionProgress\":0}," +
            "{\"placementId\":\"b\",\"machineId\":\"money_s\",\"selectedItemId\":\"\",\"productionProgress\":0}," +
            "{\"placementId\":\"c\",\"machineId\":\"fridge\",\"selectedItemId\":\"\",\"productionProgress\":0}]}");
        var list = model.FromSave(old);
        Assert.AreEqual(2, list.Count, "同じ設備は 1 台にまとめる");
        var m = list.Find(x => x.MachineId == "money_s");
        Assert.AreEqual("a", m.PlacementId, "最初の 1 台を残す");
        Assert.AreEqual(2, m.Level, "2 台ぶん → Lv2");
        Assert.AreEqual(6000 + 4200, m.TotalPaid);
        Assert.IsFalse(m.Broken);
        Assert.AreEqual(0, m.DaysSinceService);
        var f = list.Find(x => x.MachineId == "fridge");
        Assert.AreEqual(1, f.Level);
    }

    [Test]
    public void SameMachine_CannotBeBoughtTwice()
    {
        var money = MoneyS();
        var model = new ShopMachineModel(new List<ShopMachineData> { money });
        var toms = Toms(100000);
        Assert.IsNotNull(model.TryPurchaseAndPlace(money, toms, 5));
        int after = toms.PlayerMoney.Value;
        Assert.IsNull(model.TryPurchaseAndPlace(money, toms, 5));
        Assert.AreEqual(after, toms.PlayerMoney.Value, "2 台目はお金も取らない");
        Assert.AreEqual(1, model.CountOf("money_s"));
    }

    [Test]
    public void NoLevelTable_FallsBackToLegacyFields()
    {
        var m = ScriptableObject.CreateInstance<ShopMachineData>();
        _created.Add(m);
        m.machineId = "legacy";
        m.cost = 5600;
        m.dailyMoney = 800;
        Assert.AreEqual(1, m.MaxLevel);
        Assert.AreEqual(5600, m.BaseCost);
        Assert.AreEqual(800, m.DailyMoneyAt(1));
        Assert.IsFalse(m.CanBreakAt(1));
    }

    [Test]
    public void RemoteMachineLevels_ReplaceLevelTable()
    {
        var money = MoneyS();
        // GAS の readMachineLevels_ が出す形（シート machineLevels の 2 行ぶん）
        const string json = "{\"version\":99,\"schemaVersion\":1,\"machineLevels\":[{\"id\":\"money_s\",\"levels\":[" +
                            "{\"cost\":7000,\"effectValue\":1500,\"safeDays\":3,\"breakChanceStep\":0.2}," +
                            "{\"cost\":5000,\"effectValue\":3000,\"safeDays\":4,\"breakChanceStep\":0.1}]}]}";
        try
        {
            Assert.AreEqual(99, RemoteBalance.OverrideFromBundle(json));
            var applied = ShopMachineData.ApplyRemoteBalance(new List<ShopMachineData> { money })[0];
            Assert.AreNotSame(money, applied, "配信値は複製に当てる（アセットを汚さない）");
            Assert.AreEqual(2, applied.MaxLevel);
            Assert.AreEqual(7000, applied.BaseCost);
            Assert.AreEqual(3000, applied.DailyMoneyAt(2));
            Assert.AreEqual(0.2f, applied.BreakChanceOnMorning(1, 4), 1e-5f);
            Assert.AreEqual(5, money.MaxLevel, "元のアセットはそのまま");
        }
        finally { RemoteBalance.ResetToDefault(); }
    }

    [Test]
    public void MachineRules_PaybackWithinHalfOfRemainingMornings()
    {
        Assert.IsTrue(AutoPlayMachineRules.ShouldUpgrade(4200, 1200f, 8), "3.5 日 ≤ 4 日");
        Assert.IsFalse(AutoPlayMachineRules.ShouldUpgrade(4200, 1200f, 6), "3.5 日 > 3 日");
        Assert.IsTrue(AutoPlayMachineRules.ShouldRepair(ShopMachineEffectType.DailyMoney, 3000, 1200f, 6));
        Assert.IsFalse(AutoPlayMachineRules.ShouldRepair(ShopMachineEffectType.RevenueMultiplier, 3000, 1200f, 60), "お金・製造の設備だけ");
    }
}

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using R3;
using UnityEngine;

/// <summary>
/// 占い師の「上げる枠・下げる枠」（Docs/Prophet_Intervention.md）の仕組み: 入れられるかの判定の順・力・日数・需要への効き・保存・AI の決まり。
/// セーブは隔離フォルダ（SaveSlotManager.DevRootOverride）に逃がし、村の効果は VillageEffects.Override で固定する（ユーザーのセーブは読まない）。
/// </summary>
public class ProphetInterventionTests
{
    private const string Sword = "test_sword";
    private const string Shield = "test_shield";
    private const string Locked = "test_locked";

    private string _root;
    private string _prevRoot;
    private readonly List<Object> _created = new();

    [SetUp]
    public void SetUp()
    {
        _prevRoot = SaveSlotManager.DevRootOverride;
        _root = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")), "_scratch_wt04", "tests", "prophet_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        SaveSlotManager.DevRootOverride = _root;
        VillageEffects.Override(VillageEffects.None);
    }

    [TearDown]
    public void TearDown()
    {
        SaveSlotManager.DevRootOverride = _prevRoot;
        VillageEffects.Override(null);
        foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
        _created.Clear();
        try { Directory.Delete(_root, true); }
        catch (IOException e) { Debug.LogWarning($"[ProphetInterventionTests] 後片付けに失敗: {e.Message}"); }
    }

    // ───────── 組み立て ─────────

    private ItemData Item(string id, int basePrice, int requiredLevel = 1)
    {
        var d = ScriptableObject.CreateInstance<ItemData>();
        d.itemId = id;
        d.itemName = id;
        d.basePrice = basePrice;
        d.maxStock = 99;
        d.requiredLevel = requiredLevel;
        d.itemType = ItemTypeData.ItemType.Weapon;
        d.itemAttribute = ItemTypeData.ItemAttribute.Fire;
        _created.Add(d);
        return d;
    }

    private ItemModel Items() => new(new List<ItemData> { Item(Sword, 1000), Item(Shield, 2000), Item(Locked, 5000, 3) });

    private static TomsModel Toms(int money)
    {
        var toms = new TomsModel();
        toms.Initialize(money);
        return toms;
    }

    private ShopEconomySettings CalmEconomy()
    {
        var s = ScriptableObject.CreateInstance<ShopEconomySettings>();
        // 流行の揺らぎを止めて、枠の効きだけを見る
        s.trendDriftMax = 0f;
        s.displayDemandUp = 0f;
        s.notDisplayDemandDown = 0f;
        _created.Add(s);
        return s;
    }

    // ───────── 入れられるか（判定の順と各答え） ─────────

    [Test]
    public void CanPlace_ReturnsEachResult()
    {
        var toms = Toms(100000);
        var p = ProphetInterventionModel.CreateInMemory(toms, Items());

        Assert.AreEqual(ProphetPlaceResult.UnknownItem, p.CanPlace("", ProphetDirection.Up));
        Assert.AreEqual(ProphetPlaceResult.UnknownItem, p.CanPlace(null, ProphetDirection.Up));
        Assert.AreEqual(ProphetPlaceResult.UnknownItem, p.CanPlace("no_such_item", ProphetDirection.Up));
        Assert.AreEqual(ProphetPlaceResult.NotUnlocked, p.CanPlace(Locked, ProphetDirection.Up));
        Assert.AreEqual(ProphetPlaceResult.Ok, p.CanPlace(Sword, ProphetDirection.Up));

        Assert.AreEqual(ProphetPlaceResult.Ok, p.TryPlace(Sword, ProphetDirection.Up));
        Assert.AreEqual(ProphetPlaceResult.SlotInUse, p.CanPlace(Shield, ProphetDirection.Up));
        Assert.AreEqual(ProphetPlaceResult.SameItemInOtherSlot, p.CanPlace(Sword, ProphetDirection.Down));
        Assert.AreEqual(ProphetPlaceResult.Ok, p.CanPlace(Shield, ProphetDirection.Down));

        toms.PlayerMoney.Value = p.Cost - 1;
        Assert.AreEqual(ProphetPlaceResult.NotEnoughMoney, p.CanPlace(Shield, ProphetDirection.Down));

        p.SetPower(0);
        Assert.AreEqual(ProphetPlaceResult.NoPower, p.CanPlace(Shield, ProphetDirection.Down));
    }

    [Test]
    public void CanPlace_FollowsDocumentedOrder()
    {
        var toms = Toms(0);
        var p = ProphetInterventionModel.CreateInMemory(toms, Items());
        p.ForceSlot(ProphetDirection.Up, Sword, 3);
        p.SetPower(0);

        // 未解放は「枠が使用中」より先
        Assert.AreEqual(ProphetPlaceResult.NotUnlocked, p.CanPlace(Locked, ProphetDirection.Up));
        // 使用中は「力 0」「お金」より先
        Assert.AreEqual(ProphetPlaceResult.SlotInUse, p.CanPlace(Shield, ProphetDirection.Up));
        // もう片方と同じ品は「力 0」より先
        Assert.AreEqual(ProphetPlaceResult.SameItemInOtherSlot, p.CanPlace(Sword, ProphetDirection.Down));
        // 力 0 は「お金」より先
        Assert.AreEqual(ProphetPlaceResult.NoPower, p.CanPlace(Shield, ProphetDirection.Down));
    }

    [Test]
    public void TryPlace_TakesPowerAndMoney_OnlyWhenOk()
    {
        var toms = Toms(100000);
        var p = ProphetInterventionModel.CreateInMemory(toms, Items());
        int changes = 0;
        using var sub = p.Changed.Subscribe(_ => changes++);

        Assert.AreEqual(ProphetTuning.Cost, p.Cost);
        Assert.AreEqual(ProphetPlaceResult.Ok, p.TryPlace(Sword, ProphetDirection.Up));
        Assert.AreEqual(100000 - ProphetTuning.Cost, toms.PlayerMoney.Value);
        Assert.AreEqual(ProphetTuning.MaxPower - 1, p.Power);
        Assert.AreEqual(1, changes);
        var slot = p.GetSlot(ProphetDirection.Up);
        Assert.IsTrue(slot.HasValue);
        Assert.AreEqual(Sword, slot.Value.ItemId);
        Assert.AreEqual(ProphetTuning.EffectTurns, slot.Value.TurnsLeft);
        Assert.AreEqual(ProphetTuning.EffectTurns, slot.Value.TotalTurns);

        // だめなときは何も変えない
        Assert.AreEqual(ProphetPlaceResult.SlotInUse, p.TryPlace(Shield, ProphetDirection.Up));
        Assert.AreEqual(100000 - ProphetTuning.Cost, toms.PlayerMoney.Value);
        Assert.AreEqual(ProphetTuning.MaxPower - 1, p.Power);
        Assert.AreEqual(1, changes);
    }

    [Test]
    public void Cost_UsesTavernPrice()
    {
        VillageEffects.Override(VillageEffects.ForLevels(0, 0, 4, 0, 0, 0));
        var toms = Toms(100000);
        var p = ProphetInterventionModel.CreateInMemory(toms, Items());
        int expected = VillageEffects.Current.ApplyProphetPrice(ProphetTuning.Cost);
        Assert.AreEqual(expected, p.Cost);
        p.TryPlace(Sword, ProphetDirection.Up);
        Assert.AreEqual(100000 - expected, toms.PlayerMoney.Value);
    }

    // ───────── 力 ─────────

    [Test]
    public void Power_StartsFull_ConsumedPerPlacement_RecoversPerStream_Capped()
    {
        var toms = Toms(100000);
        var p = ProphetInterventionModel.CreateInMemory(toms, Items());
        Assert.AreEqual(ProphetTuning.StartPower, p.Power);
        Assert.AreEqual(2, p.MaxPower);

        p.TryPlace(Sword, ProphetDirection.Up);
        p.TryPlace(Shield, ProphetDirection.Down);
        Assert.AreEqual(0, p.Power);
        p.ForceSlot(ProphetDirection.Up, null, 0);   // 上げる枠を空けても力が無い
        Assert.AreEqual(ProphetPlaceResult.NoPower, p.CanPlace(Sword, ProphetDirection.Up));

        p.OnStreamFinished();
        Assert.AreEqual(1, p.Power);
        p.OnStreamFinished();
        Assert.AreEqual(2, p.Power);
        p.OnStreamFinished();
        Assert.AreEqual(2, p.Power, "最大を超えない");

        p.SetPower(0);
        p.Clear();
        Assert.AreEqual(ProphetTuning.StartPower, p.Power, "新しいランは満タン");
        Assert.IsFalse(p.GetSlot(ProphetDirection.Up).HasValue);
        Assert.IsFalse(p.GetSlot(ProphetDirection.Down).HasValue);
    }

    // ───────── 日数と効き ─────────

    [Test]
    public void Duration_CountsDownPerMorning_AndBiasRemovedAfter()
    {
        var p = ProphetInterventionModel.CreateInMemory(Toms(100000), Items());
        p.TryPlace(Sword, ProphetDirection.Up);
        p.TryPlace(Shield, ProphetDirection.Down);

        for (int morning = 1; morning <= ProphetTuning.EffectTurns; morning++)
        {
            // 朝の経済更新で読む値（枠が残っている間は効く）
            Assert.AreEqual(ProphetTuning.TrendBias, p.TrendBias(Sword), 1e-6f, $"朝 {morning}");
            Assert.AreEqual(-ProphetTuning.TrendBias, p.TrendBias(Shield), 1e-6f, $"朝 {morning}");
            Assert.AreEqual(morning == 1 ? ProphetTuning.DemandKick : 0f, p.DemandKick(Sword), 1e-6f, "直撃は最初の朝だけ");
            Assert.Greater(p.FairRate(Sword), 1f);
            Assert.Less(p.FairRate(Shield), 1f);
            Assert.AreEqual(morning == 1 ? 1f : p.FairRate(Sword), p.PreviousFairRate(Sword), 1e-6f, "前日の適正値は前の朝の効き");

            p.AdvanceDay();
            int left = ProphetTuning.EffectTurns - morning;
            var slot = p.GetSlot(ProphetDirection.Up);
            if (left > 0)
            {
                Assert.IsTrue(slot.HasValue);
                Assert.AreEqual(left, slot.Value.TurnsLeft);
            }
            else
            {
                Assert.IsFalse(slot.HasValue, "0 になった朝に外れる");
                Assert.IsFalse(p.GetSlot(ProphetDirection.Down).HasValue);
            }
        }

        Assert.AreEqual(0f, p.TrendBias(Sword));
        Assert.AreEqual(0f, p.TrendBias(Shield));
        Assert.AreEqual(1f, p.FairRate(Sword));
        Assert.Greater(p.PreviousFairRate(Sword), 1f, "外れた翌朝の前日分は前の朝の効き");
        p.AdvanceDay();
        Assert.AreEqual(1f, p.PreviousFairRate(Sword));
        // 外れたら同じ枠にまた入れられる（力は残っていない）
        Assert.AreEqual(ProphetPlaceResult.NoPower, p.CanPlace(Sword, ProphetDirection.Up));
    }

    [Test]
    public void Demand_DriftsTowardBiasedEquilibrium_ThenBack_WithoutRebound()
    {
        var settings = CalmEconomy();
        var toms = Toms(100000);
        var withCard = Items();
        var control = Items();
        foreach (var m in new[] { withCard, control })
        foreach (var r in m.RuntimeItems)
        {
            r.Trend = 0f;
            r.Demand.Value = 0.5f;
        }
        var p = ProphetInterventionModel.CreateInMemory(toms, withCard);
        Assert.AreEqual(ProphetPlaceResult.Ok, p.TryPlace(Sword, ProphetDirection.Up));
        Assert.AreEqual(ProphetPlaceResult.Ok, p.TryPlace(Shield, ProphetDirection.Down));
        Assert.AreEqual(0.5f, withCard.GetRuntimeItem(Sword).Demand.Value, 1e-6f, "入れた日のうちは需要を動かさない");

        float Diff(string id) => withCard.GetRuntimeItem(id).Demand.Value - control.GetRuntimeItem(id).Demand.Value;
        var up = new List<float>();
        var down = new List<float>();
        for (int turn = 2; turn <= 10; turn++)
        {
            withCard.ApplyShopTurnEconomy(settings, 1, turnIndex: turn, flowSeed: 1, prophet: p);
            control.ApplyShopTurnEconomy(settings, 1, turnIndex: turn, flowSeed: 1);
            p.AdvanceDay();
            up.Add(Diff(Sword));
            down.Add(Diff(Shield));
        }

        // 1 朝目: 直撃 + 均衡点への接近
        float firstExpected = ProphetTuning.DemandKick + settings.trendConvergenceRate * ProphetTuning.TrendBias * settings.trendAmplitude;
        Assert.AreEqual(firstExpected, up[0], 1e-4f);
        Assert.AreEqual(-firstExpected, down[0], 1e-4f);

        int n = ProphetTuning.EffectTurns;
        for (int i = 1; i < n; i++)
        {
            Assert.Greater(up[i], up[i - 1], $"効いている間は上へ（朝 {i + 1}）");
            Assert.Less(down[i], down[i - 1], $"効いている間は下へ（朝 {i + 1}）");
        }
        for (int i = n; i < up.Count; i++)
        {
            Assert.Less(up[i], up[i - 1], $"切れたら戻る（朝 {i + 1}）");
            Assert.Greater(down[i], down[i - 1], $"切れたら戻る（朝 {i + 1}）");
            Assert.GreaterOrEqual(up[i], 0f, "反動で逆へ行かない");
            Assert.LessOrEqual(down[i], 0f, "反動で逆へ行かない");
        }
        // ふだんの収束（15%/日）で戻る: 切れてからの 1 朝で差が (1 - 収束率) 倍になる
        Assert.AreEqual(up[n - 1] * (1f - settings.trendConvergenceRate), up[n], 1e-4f);
    }

    [Test]
    public void Economy_WithoutProphet_IsUnchanged()
    {
        var settings = CalmEconomy();
        var a = Items();
        var b = Items();
        foreach (var m in new[] { a, b })
        foreach (var r in m.RuntimeItems) { r.Trend = 0.2f; r.Demand.Value = 0.4f; }
        var empty = ProphetInterventionModel.CreateInMemory(Toms(100000), a);
        a.ApplyShopTurnEconomy(settings, 1, turnIndex: 2, flowSeed: 1, prophet: empty);
        b.ApplyShopTurnEconomy(settings, 1, turnIndex: 2, flowSeed: 1);
        Assert.AreEqual(b.GetRuntimeItem(Sword).Demand.Value, a.GetRuntimeItem(Sword).Demand.Value, 1e-6f);
    }

    // ───────── 保存 ─────────

    [Test]
    public void Save_RoundTrips_SlotsAndPower()
    {
        var toms = Toms(100000);
        var items = Items();
        var p = new ProphetInterventionModel(toms, items);
        p.TryPlace(Sword, ProphetDirection.Up);
        p.AdvanceDay();
        p.TryPlace(Shield, ProphetDirection.Down);
        Assert.IsTrue(File.Exists(Path.Combine(_root, ProphetInterventionModel.FileName)));

        var loaded = new ProphetInterventionModel(toms, items);
        Assert.AreEqual(0, loaded.Power);
        var up = loaded.GetSlot(ProphetDirection.Up);
        var down = loaded.GetSlot(ProphetDirection.Down);
        Assert.IsTrue(up.HasValue && down.HasValue);
        Assert.AreEqual(Sword, up.Value.ItemId);
        Assert.AreEqual(ProphetTuning.EffectTurns - 1, up.Value.TurnsLeft);
        Assert.AreEqual(ProphetTuning.EffectTurns, up.Value.TotalTurns);
        Assert.AreEqual(Shield, down.Value.ItemId);
        Assert.AreEqual(ProphetDirection.Down, down.Value.Direction);
        Assert.AreEqual(ProphetTuning.EffectTurns, down.Value.TurnsLeft);
        Assert.AreEqual(p.PreviousFairRate(Sword), loaded.PreviousFairRate(Sword), 1e-6f);

        // 新しいラン
        loaded.Clear();
        var again = new ProphetInterventionModel(toms, items);
        Assert.AreEqual(ProphetTuning.StartPower, again.Power);
        Assert.IsFalse(again.GetSlot(ProphetDirection.Up).HasValue);
    }

    [Test]
    public void OldSave_LoadsAsEmptySlotsAndFullPower()
    {
        // 旧セーブ: このファイルが無く、廃止した占いのファイルだけ残っている
        File.WriteAllText(Path.Combine(_root, "prophetFortune.json"), "{\"runSeed\":1,\"boughtTurn\":3}");
        var p = new ProphetInterventionModel(Toms(100000), Items());
        Assert.AreEqual(ProphetTuning.StartPower, p.Power);
        Assert.IsFalse(p.GetSlot(ProphetDirection.Up).HasValue);
        Assert.IsFalse(p.GetSlot(ProphetDirection.Down).HasValue);

        // 力の欄が欠けたファイル・おかしな枠は読み飛ばす
        File.WriteAllText(Path.Combine(_root, ProphetInterventionModel.FileName),
            "{\"slots\":[{\"itemId\":\"test_sword\",\"direction\":1,\"turnsLeft\":2,\"totalTurns\":3},{\"itemId\":\"\",\"direction\":-1,\"turnsLeft\":2},{\"itemId\":\"test_shield\",\"direction\":5,\"turnsLeft\":2}]}");
        var partial = new ProphetInterventionModel(Toms(100000), Items());
        Assert.AreEqual(ProphetTuning.StartPower, partial.Power);
        Assert.AreEqual(Sword, partial.GetSlot(ProphetDirection.Up)?.ItemId);
        Assert.AreEqual(2, partial.GetSlot(ProphetDirection.Up)?.TurnsLeft);
        Assert.IsFalse(partial.GetSlot(ProphetDirection.Down).HasValue);
    }

    [Test]
    public void InMemory_DoesNotWriteSave()
    {
        var p = ProphetInterventionModel.CreateInMemory(Toms(100000), Items());
        p.TryPlace(Sword, ProphetDirection.Up);
        p.AdvanceDay();
        Assert.IsFalse(File.Exists(Path.Combine(_root, ProphetInterventionModel.FileName)));
    }

    // ───────── AI の決まり ─────────

    private static ProphetAutoCandidate C(string id, int price, int stock, bool newsUp = false, bool wantToBuy = false) =>
        new() { ItemId = id, Price = price, Stock = stock, NewsUp = newsUp, WantToBuy = wantToBuy };

    [Test]
    public void AutoRule_UsesPowerOnlyWhenFull_AndKeepsReserve()
    {
        var list = new List<ProphetAutoCandidate> { C("a", 10000, 10, newsUp: true) };
        Assert.IsTrue(ProphetAutoRule.Choose(1, 2, 15000, 1000000, list, (_, __) => true).IsNone, "満タンでなければ使わない");
        Assert.IsTrue(ProphetAutoRule.Choose(2, 2, 15000, 15000 + ProphetTuning.AutoReserveMoney - 1, list, (_, __) => true).IsNone, "所持金に余裕がない");
        Assert.IsFalse(ProphetAutoRule.Choose(2, 2, 15000, 15000 + ProphetTuning.AutoReserveMoney, list, (_, __) => true).IsNone);
    }

    [Test]
    public void AutoRule_UpSlot_PicksNewsUpItemWithLargestStockValue()
    {
        var list = new List<ProphetAutoCandidate>
        {
            C("no_news", 10000, 50),                    // 在庫は多いが新聞▲でない
            C("small", 1000, 20, newsUp: true),        // 2 万G: 代金の 2 倍に届かない
            C("mid", 4000, 10, newsUp: true),          // 4 万G
            C("big", 3000, 20, newsUp: true),          // 6 万G
            C("blocked", 5000, 30, newsUp: true),      // 入れられない
        };
        var pick = ProphetAutoRule.Choose(2, 2, 15000, 500000, list, (id, _) => id != "blocked");
        Assert.AreEqual("big", pick.ItemId);
        Assert.AreEqual(ProphetDirection.Up, pick.Direction);
    }

    [Test]
    public void AutoRule_DownSlot_PicksPriciestWantedItemNotHeld()
    {
        var list = new List<ProphetAutoCandidate>
        {
            C("held", 9000, 10, wantToBuy: true),     // もう持っている（9 万G）
            C("cheap", 1000, 0, wantToBuy: true),
            C("pricey", 5000, 1, wantToBuy: true),
            C("other", 8000, 0),                      // 仕入れたい品ではない
            C("up_small", 1000, 3, newsUp: true),     // 上げるには在庫が足りない
        };
        var pick = ProphetAutoRule.Choose(2, 2, 15000, 500000, list, (_, __) => true);
        Assert.AreEqual("pricey", pick.ItemId);
        Assert.AreEqual(ProphetDirection.Down, pick.Direction);

        // 下げる枠が使用中なら何もしない
        var none = ProphetAutoRule.Choose(2, 2, 15000, 500000, list, (_, d) => d == ProphetDirection.Up);
        Assert.IsTrue(none.IsNone);
    }

    [Test]
    public void AutoRule_WithRealModel_RespectsCanPlace()
    {
        var toms = Toms(1000000);
        var items = Items();
        var p = ProphetInterventionModel.CreateInMemory(toms, items);
        var list = new List<ProphetAutoCandidate> { C(Locked, 5000, 99, newsUp: true), C(Sword, 1000, 99, newsUp: true) };
        var pick = ProphetAutoRule.Choose(p, toms.PlayerMoney.Value, list);
        Assert.AreEqual(Sword, pick.ItemId, "未解放の品は選ばない");
        p.TryPlace(pick.ItemId, pick.Direction);
        Assert.IsTrue(ProphetAutoRule.Choose(p, toms.PlayerMoney.Value, list).IsNone, "力が満タンでなくなった");
    }
}

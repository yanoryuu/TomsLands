using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// ヘッドレスの AutoPlay（AutoPlayHeadlessGame）が、本物のゲームと同じ占い師の枠の仕組みを通っているか:
/// 本物の GameFlowManager が同じ ProphetInterventionModel で朝の需要を動かし、本物の BattleResultHandler が配信後に力を戻す。
/// 同じシードの 2 ラン（枠を使う／使わない）を並べて、入れた品の需要だけが動くことを見る。
/// セーブは隔離フォルダ（AutoPlayRunContext が SaveSlotManager.DevRootOverride を差し替える）。
/// </summary>
public class ProphetHeadlessTests
{
    private const int Seed = 4242;
    private string _root;
    private System.Func<string, bool> _prevInterceptor;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")), "_scratch_wt04", "tests", "prophet_headless_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        VillageEffects.Override(VillageEffects.None);
        // 配信（FightScene）へのシーン遷移は横取りする（AutoPlayBatch と同じ。ラン外の遷移は素通し）
        _prevInterceptor = SceneTransitionService.DevSceneLoadInterceptor;
        SceneTransitionService.DevSceneLoadInterceptor = scene =>
        {
            var ctx = AutoPlayRunContext.Current;
            if (ctx == null) return false;
            ctx.LastSceneRequest = scene;
            return true;
        };
    }

    [TearDown]
    public void TearDown()
    {
        SceneTransitionService.DevSceneLoadInterceptor = _prevInterceptor;
        VillageEffects.Override(null);
        try { Directory.Delete(_root, true); }
        catch (IOException e) { Debug.LogWarning($"[ProphetHeadlessTests] 後片付けに失敗: {e.Message}"); }
    }

    private (AutoPlayHeadlessGame game, AutoPlayRunContext ctx) NewRun(AutoPlayAssets assets, string id)
    {
        var ctx = new AutoPlayRunContext(id, Path.Combine(_root, id), Seed);
        var game = new AutoPlayHeadlessGame(new AutoPlayHeadlessGame.Settings { Seed = Seed, InitMoneyOverride = 500000 }, assets, ctx);
        using (ctx.Enter()) game.StartNewRun();
        return (game, ctx);
    }

    [Test]
    public void Headless_UsesSameModel_ForDemandAndPowerRecovery()
    {
        var assets = AutoPlayAssets.Load(out var error);
        if (assets == null) Assert.Ignore("AutoPlay のアセットを読めない環境: " + error);

        var (withCard, ctxA) = NewRun(assets, "with_card");
        var (control, ctxB) = NewRun(assets, "control");
        try
        {
            string itemId;
            using (ctxA.Enter())
            {
                // 本物の GameFlowManager が同じインスタンスを持っている（朝の需要の計算と日送りはここから）
                var field = typeof(GameFlowManager).GetField("_prophet", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(field, "GameFlowManager._prophet が無い");
                Assert.AreSame(withCard.Prophet, field.GetValue(withCard.Flow));
                Assert.AreEqual(ProphetTuning.StartPower, withCard.Prophet.Power);

                var r = withCard.ItemModel.RuntimeItems.First(x => x.RequiredLevel.Value <= withCard.TomsModel.BlacksmithLevel.Value);
                itemId = r.ItemId;
                int money = withCard.TomsModel.PlayerMoney.Value;
                var placed = withCard.PlaceProphet(itemId, ProphetDirection.Up);
                Assert.IsTrue(placed.Ok, placed.Message);
                Assert.AreEqual(money - withCard.Prophet.Cost, withCard.TomsModel.PlayerMoney.Value);
                Assert.AreEqual(1, withCard.ProphetPlacements);
                Assert.AreEqual(1, withCard.Snapshot().ProphetSlotOf(itemId));
            }

            // 1 日進める（両方とも何も並べない）
            Step(withCard, ctxA);
            Step(control, ctxB);

            float demandA, demandB;
            using (ctxA.Enter())
            {
                demandA = withCard.ItemModel.GetRuntimeItem(itemId).Demand.Value;
                var slot = withCard.Prophet.GetSlot(ProphetDirection.Up);
                Assert.IsTrue(slot.HasValue);
                Assert.AreEqual(ProphetTuning.EffectTurns - 1, slot.Value.TurnsLeft, "朝の更新で 1 減る");
            }
            using (ctxB.Enter()) demandB = control.ItemModel.GetRuntimeItem(itemId).Demand.Value;
            Assert.Greater(demandA, demandB, "入れた品の需要が上へ動く（同じシードの対照ランより高い）");

            // 配信を終えると（本物の BattleResultHandler）力が 1 戻る
            using (ctxA.Enter())
            {
                for (int i = 0; i < 30 && withCard.Stage == AutoPlayStage.ShopDay; i++)
                {
                    withCard.StartSales();
                    withCard.EndDay();
                }
                if (withCard.Stage != AutoPlayStage.StreamDay) Assert.Inconclusive($"配信日に届かなかった（{withCard.Stage}）");
                int before = withCard.Prophet.Power;
                var stream = withCard.StartStream(new List<AutoPlayStreamItem>());
                Assert.IsTrue(stream.Ok, stream.Message);
                Assert.AreEqual(Mathf.Min(ProphetTuning.MaxPower, before + ProphetTuning.PowerPerStream), withCard.Prophet.Power);
            }
        }
        finally
        {
            using (ctxA.Enter()) withCard.Dispose();
            using (ctxB.Enter()) control.Dispose();
        }
    }

    private static void Step(AutoPlayHeadlessGame game, AutoPlayRunContext ctx)
    {
        using (ctx.Enter())
        {
            Assert.AreEqual(AutoPlayStage.ShopDay, game.Stage);
            Assert.IsTrue(game.StartSales().Ok);
            var end = game.EndDay();
            Assert.IsTrue(end.Ok, end.Message);
        }
    }
}

internal static class ProphetSnapshotTestExtensions
{
    public static int ProphetSlotOf(this AutoPlaySnapshot s, string itemId) =>
        s.Items.FirstOrDefault(i => i.Id == itemId)?.ProphetSlot ?? 0;
}

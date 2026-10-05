using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 配信（FightScene）のサロゲート。ヘッドレスでは実際の戦闘シーンを動かさないため、
/// - 勝敗: ClearProbabilityCalculator.Simulate と同じ決定論ルールでドライランする
///   （＋レリックの HeroPowerMul を CharacterModel と同じく掛ける。表示確率は掛けていない＝差が出たら異常として報告）
/// - 配信販売: StreamingSalesModel.ProcessSingleItemSale と同じ式（需要 × SalesRate × max(1,陳列数)、端数は確率）を
///   戦闘ターン数ぶん回す。配信熱・価格変動・BattleDemandTracker・補充ポップアップは未再現（Docs/Jev_AutoPlay_Design.md §2.4）。
/// 在庫は減らさない（減算は BattleResultHandler が SoldFromStock で行う＝本番と同じ経路）。
/// </summary>
public static class AutoPlayBattleSurrogate
{
    private const int MaxConcurrentEnemies = 3;
    private const int MaxSimTurns = 500;

    public sealed class Outcome
    {
        public bool Victory;
        public int Turns;
        public int MobsDefeated;
        public int BossesDefeated;
        public float HeroHpRatio;
        /// <summary>画面に出ているクリア確率（ClearProbabilityCalculator）。</summary>
        public float DisplayedClearPct;
        /// <summary>表示確率（50%以上＝勝てる見込み）とサロゲートの勝敗が一致したか。</summary>
        public bool ConsistentWithDisplay;
    }

    public static Outcome Resolve(RuntimeHeroData hero, ItemModel itemModel, DungeonData dungeon, int level,
        float heroPowerMul, bool probabilistic, System.Random rng)
    {
        var outcome = new Outcome();
        if (hero == null || dungeon == null)
        {
            outcome.Victory = false;
            outcome.Turns = 1;
            outcome.ConsistentWithDisplay = true;
            return outcome;
        }

        outcome.DisplayedClearPct = ClearProbabilityCalculator.Calculate(hero, itemModel, dungeon, level);

        var phases = BuildPhases(dungeon, level);
        var equip = HeroEquipmentBonus.Get(hero, itemModel);
        int Scale(int v) => Mathf.Max(1, Mathf.RoundToInt(v * heroPowerMul));

        int heroMaxHp = Scale(Mathf.RoundToInt(hero.hp.Value * equip.Hp));
        int heroHp = heroMaxHp;
        int heroAtk = Scale(Mathf.RoundToInt(hero.attackPower.Value * equip.Attack));
        // 実戦闘（CharacterModel のコンストラクタ）と同じ丸め順
        int heroDef = Scale(Mathf.RoundToInt(hero.defensePower.Value * equip.Defense));

        if (phases.Count == 0)
        {
            // フェーズ構成が取れない → 表示確率で決める
            outcome.Victory = rng.NextDouble() * 100.0 < outcome.DisplayedClearPct;
            outcome.Turns = 10;
            outcome.ConsistentWithDisplay = true;
            return outcome;
        }

        int phaseIndex = 0;
        var queue = new Queue<EnemyData>(phases[0].enemies.Where(e => e != null));
        var field = new List<(int hp, int atk, int def, bool boss)>();
        int turns = 0;

        void Refill()
        {
            while (field.Count < MaxConcurrentEnemies && queue.Count > 0)
            {
                var e = queue.Dequeue();
                field.Add((Mathf.Max(1, e.hp), Mathf.Max(0, e.attackPower), Mathf.Max(0, e.defensePower), e.isBoss));
            }
        }

        Refill();
        for (int t = 0; t < MaxSimTurns; t++)
        {
            turns++;
            if (field.Count > 0)
            {
                var target = field[0];
                target.hp -= Mathf.Max(1, heroAtk - target.def);
                field[0] = target;
            }

            foreach (var enemy in field)
            {
                if (enemy.hp <= 0) continue;
                heroHp -= Mathf.Max(1, enemy.atk - heroDef);
                if (heroHp <= 0) break;
            }
            if (heroHp <= 0) break;

            foreach (var e in field.Where(e => e.hp <= 0))
            {
                if (e.boss) outcome.BossesDefeated++;
                else outcome.MobsDefeated++;
            }
            field.RemoveAll(e => e.hp <= 0);

            if (field.Count == 0 && queue.Count == 0)
            {
                phaseIndex++;
                if (phaseIndex >= phases.Count) break;
                foreach (var e in phases[phaseIndex].enemies.Where(e => e != null)) queue.Enqueue(e);
            }
            Refill();
        }

        bool dryRunWin = heroHp > 0 && phaseIndex >= phases.Count;
        outcome.Turns = Mathf.Max(1, turns);
        outcome.HeroHpRatio = Mathf.Clamp01(heroHp / (float)Mathf.Max(1, heroMaxHp));
        outcome.ConsistentWithDisplay = dryRunWin == (outcome.DisplayedClearPct >= 50f);

        outcome.Victory = probabilistic
            ? rng.NextDouble() * 100.0 < outcome.DisplayedClearPct
            : dryRunWin;
        return outcome;
    }

    /// <summary>ClearProbabilityCalculator.BuildPhases の写し（private のため。本体が変わったら追従する）。</summary>
    private static List<DungeonPhaseData> BuildPhases(DungeonData dungeon, int level)
    {
        var data = dungeon.GetLevelData(level);
        if (data == null) return new List<DungeonPhaseData>();
        var phases = data.phases != null && data.phases.Any(p => p?.enemies != null && p.enemies.Any(e => e != null))
            ? data.phases
            : DungeonPhaseBuilder.BuildFromLegacy(data);
        // 実戦闘・表示確率と同じく通常ウェーブの周回（normalWaveRepeat）を展開する
        return BattleWavePlan.Expand(phases.Where(p => p?.enemies != null && p.enemies.Any(e => e != null)).ToList());
    }

    /// <summary>
    /// 配信販売を ticks 回まわす。持ち込み数を上限に、売れた数と単価（配信開始時の価格）を返す。
    /// </summary>
    public static List<BattleOutputSoldItem> SimulateStreamSales(List<BattleInputItem> selected, ItemModel itemModel,
        int ticks, System.Random rng)
    {
        var result = new List<BattleOutputSoldItem>();
        foreach (var sel in selected)
        {
            var r = itemModel.GetRuntimeItem(sel.ItemId);
            if (r == null) continue;
            int remaining = sel.Quantity;
            int sold = 0;
            float perTick = Mathf.Clamp01(r.Demand.Value) * r.SalesRate * Mathf.Max(1, r.DisplayStock.Value);
            for (int t = 0; t < ticks && remaining > 0; t++)
            {
                int q = perTick >= 1f ? Mathf.FloorToInt(perTick) : (rng.NextDouble() < perTick ? 1 : 0);
                q = Mathf.Clamp(q, 0, remaining);
                remaining -= q;
                sold += q;
            }
            result.Add(new BattleOutputSoldItem
            {
                ItemId = sel.ItemId,
                SoldQuantity = sold,
                SoldFromStock = sold,
                SoldPrice = sel.Price,
            });
        }
        return result;
    }
}

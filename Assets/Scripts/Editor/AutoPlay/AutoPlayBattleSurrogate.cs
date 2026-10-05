using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 配信（FightScene）のサロゲート。ヘッドレスでは実際の戦闘シーンを動かさないため、
/// - 勝敗: ClearProbabilityCalculator.Simulate と同じ決定論ルールでドライランする
///   （＋レリックの HeroPowerMul を CharacterModel と同じく掛ける。表示確率は掛けていない＝差が出たら異常として報告）
/// - 配信販売: StreamingSalesModel.ProcessSingleItemSale と同じ式（需要 × SalesRate × max(1,陳列数)、端数は確率）を
///   戦闘ターンごとに回す。配信熱・価格変動・BattleDemandTracker・補充ポップアップは未再現（Docs/Jev_AutoPlay_Design.md §2.4）。
/// - 介入（スパチャ）: InterventionCommandQueue / InterventionCommands / CharacterPresenter.PerformAttack の写し（§2.5）。
///   数値はすべて StreamingInteractionSettings から読む。
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
        /// <summary>表示確率（50%以上＝勝てる見込み）と「介入なし」のドライランの勝敗が一致したか。</summary>
        public bool ConsistentWithDisplay;
        /// <summary>介入なしでドライランした場合の勝敗とターン数（介入の効果測定用）。</summary>
        public bool BaselineVictory;
        public int BaselineTurns;

        // --- 配信販売 ---
        public List<BattleOutputSoldItem> Sold = new List<BattleOutputSoldItem>();
        public int RawSales;

        // --- 介入 ---
        public int InterventionSpent;
        public int InterventionRefund;
        public int HeroSideSpent;
        public int DungeonSideSpent;
        /// <summary>予約ベースの必殺技回数（プレイヤー＋視聴者）。</summary>
        public int SpecialMoves;
        public int ViewerSpecials;
        /// <summary>実行された介入（種類@ターン）。</summary>
        public List<string> Executed = new List<string>();
        /// <summary>出せなかった予定（理由つき）。</summary>
        public List<string> Skipped = new List<string>();
    }

    /// <summary>配信1回分の入力。</summary>
    public sealed class StreamInput
    {
        public RuntimeHeroData Hero;
        public ItemModel ItemModel;
        public DungeonData Dungeon;
        public int Level;
        public float HeroPowerMul = 1f;
        public bool Probabilistic;
        public System.Random Rng;

        /// <summary>持ち込み（null なら販売しない＝勝敗だけの what-if）。</summary>
        public List<BattleInputItem> Selected;
        /// <summary>1戦闘ターンあたりの販売判定回数（StreamSalesScale）。</summary>
        public float SalesTicksPerTurn = 1f;

        // --- 介入 ---
        public StreamingInteractionSettings Settings;
        /// <summary>表示クリア確率にレリックの HeroPowerMul を掛けるため（null = 掛けない）。</summary>
        public RelicEffectResolver Relic;
        public IReadOnlyList<AutoPlayInterventionOrder> Orders;
        /// <summary>配信開始時の所持金（利用可能残高 = 所持金 + 戦闘中売上 − 介入支出）。</summary>
        public int StartMoney;
        /// <summary>クールダウン（秒）を戦闘ターンに直す換算。1ターン ≒ この秒数。</summary>
        public float SecondsPerTurn = 3f;
        /// <summary>視聴者の赤スパ（必殺技）が1ターンに起きる確率（0 = 無効）。</summary>
        public float ViewerRedChancePerTurn;
    }

    /// <summary>介入なし・販売なしの勝敗だけ（既存の呼び出し口）。</summary>
    public static Outcome Resolve(RuntimeHeroData hero, ItemModel itemModel, DungeonData dungeon, int level,
        float heroPowerMul, bool probabilistic, System.Random rng)
    {
        return Simulate(new StreamInput
        {
            Hero = hero, ItemModel = itemModel, Dungeon = dungeon, Level = level,
            HeroPowerMul = heroPowerMul, Probabilistic = probabilistic, Rng = rng,
        });
    }

    /// <summary>敵の総数とボスの有無（BattleWavePlan 展開後）。</summary>
    public static (int count, bool hasBoss) DescribeEnemies(DungeonData dungeon, int level)
    {
        if (dungeon == null) return (0, false);
        var phases = BuildPhases(dungeon, level);
        var all = phases.SelectMany(p => p.enemies).Where(e => e != null).ToList();
        return (all.Count, all.Any(e => e.isBoss));
    }

    /// <summary>
    /// 配信1回を解決する。介入なしのドライランも内部で回し、BaselineVictory に入れる。
    /// </summary>
    public static Outcome Simulate(StreamInput input)
    {
        var outcome = new Outcome();
        var rng = input.Rng ?? new System.Random(0);
        if (input.Hero == null || input.Dungeon == null)
        {
            outcome.Turns = 1;
            outcome.BaselineTurns = 1;
            outcome.ConsistentWithDisplay = true;
            return outcome;
        }

        outcome.DisplayedClearPct = ClearProbabilityCalculator.Calculate(input.Hero, input.ItemModel, input.Dungeon, input.Level, input.Relic);
        var phases = BuildPhases(input.Dungeon, input.Level);
        if (phases.Count == 0)
        {
            // フェーズ構成が取れない → 表示確率で決める
            outcome.Victory = outcome.BaselineVictory = rng.NextDouble() * 100.0 < outcome.DisplayedClearPct;
            outcome.Turns = outcome.BaselineTurns = 10;
            outcome.ConsistentWithDisplay = true;
            return outcome;
        }

        // 介入なしのドライラン（表示確率との整合チェック・介入の効果測定）
        var baseline = new BattleRun(input, phases, null, new System.Random(rng.Next()));
        baseline.Run(null);
        outcome.BaselineVictory = baseline.HeroWon;
        outcome.BaselineTurns = baseline.Turns;
        outcome.ConsistentWithDisplay = baseline.HeroWon == (outcome.DisplayedClearPct >= 50f);

        // 本番（介入・視聴者赤スパ・配信販売つき）
        bool hasInterventions = input.Settings != null && input.Orders != null && input.Orders.Count > 0;
        var run = new BattleRun(input, phases, input.Settings, rng);
        run.Run(outcome);

        outcome.Turns = Mathf.Max(1, run.Turns);
        outcome.MobsDefeated = run.MobsDefeated;
        outcome.BossesDefeated = run.BossesDefeated;
        outcome.HeroHpRatio = run.HeroHpRatio;

        // 抽選モード: 介入が効いた（勝敗がドライランから変わった）ときはその結果を採用し、
        // そうでなければ表示確率で抽選する（Docs §2.5）
        if (input.Probabilistic && !(hasInterventions && run.HeroWon != baseline.HeroWon))
            outcome.Victory = rng.NextDouble() * 100.0 < outcome.DisplayedClearPct;
        else
            outcome.Victory = run.HeroWon;
        return outcome;
    }

    /// <summary>
    /// ドライラン本体。ClearProbabilityCalculator.Simulate の写しに、
    /// InterventionCommandQueue（指示1件・勇者側は勇者の手番の頭／ダンジョン側は魔物の手番の頭で実行・ResolveAttack の補正）と
    /// CharacterPresenter.PerformAttack / CharacterModel.ApplyDamage・ApplyBonusDamage・Heal の式を足したもの。
    /// </summary>
    private sealed class BattleRun
    {
        private struct Foe { public int Hp, Atk, Def; public bool Boss; }

        private readonly StreamInput _in;
        private readonly List<DungeonPhaseData> _phases;
        private readonly StreamingInteractionSettings _s; // null = 介入なし
        private readonly System.Random _rng;

        public int Turns;
        public int MobsDefeated;
        public int BossesDefeated;
        public bool HeroWon;
        public float HeroHpRatio;

        public BattleRun(StreamInput input, List<DungeonPhaseData> phases, StreamingInteractionSettings settings, System.Random rng)
        {
            _in = input;
            _phases = phases;
            _s = settings;
            _rng = rng;
        }

        public void Run(Outcome o)
        {
            var hero = _in.Hero;
            var equip = HeroEquipmentBonus.Get(hero, _in.ItemModel);
            float pm = _in.HeroPowerMul;
            int Scale(int v) => Mathf.Max(1, Mathf.RoundToInt(v * pm));
            // CharacterModel のコンストラクタと同じ丸め順
            int heroMaxHp = Scale(Mathf.RoundToInt(hero.hp.Value * equip.Hp));
            int heroHp = heroMaxHp;
            int heroAtk = Scale(Mathf.RoundToInt(hero.attackPower.Value * equip.Attack));
            int heroDef = Scale(Mathf.RoundToInt(hero.defensePower.Value * equip.Defense));

            int phaseIndex = 0;
            var queue = new Queue<EnemyData>(_phases[0].enemies.Where(e => e != null));
            var field = new List<Foe>();

            // --- 介入の状態（InterventionEffects / InterventionCommandQueue の写し） ---
            bool interventions = _s != null && o != null;
            var orders = interventions && _in.Orders != null ? _in.Orders.ToList() : new List<AutoPlayInterventionOrder>();
            int orderIndex = 0;
            AutoPlayInterventionKind? pending = null;
            int pendingPaid = 0;
            bool viewerSpecialPending = false;
            float? armedStrike = null;
            bool trapArmed = false;
            int curseLeft = 0;
            bool bossBuff = false;
            int specialsReserved = 0;
            int bossBuffsUsed = 0;
            int cooldownUntil = 0;
            int cooldownTurns = interventions ? Mathf.CeilToInt(_s.cooldownSeconds / Mathf.Max(0.1f, _in.SecondsPerTurn)) : 0;
            // timing(0〜1) → 戦闘ターンへの換算は「介入なしで何ターンかかるか」を物差しにする
            int horizon = 1;
            if (interventions && orders.Count > 0)
            {
                var probe = new BattleRun(_in, _phases, null, new System.Random(1));
                probe.Run(null);
                horizon = Mathf.Max(1, probe.Turns);
            }

            // --- 配信販売の状態 ---
            var selling = o != null && _in.Selected != null
                ? _in.Selected.Select(sel => (sel, r: _in.ItemModel.GetRuntimeItem(sel.ItemId), left: sel.Quantity, sold: 0)).ToList()
                : null;
            float tickAcc = 0f;
            int ticksDone = 0;
            int salesSoFar = 0;

            void Refill()
            {
                while (field.Count < MaxConcurrentEnemies && queue.Count > 0)
                {
                    var e = queue.Dequeue();
                    field.Add(new Foe { Hp = Mathf.Max(1, e.hp), Atk = Mathf.Max(0, e.attackPower), Def = Mathf.Max(0, e.defensePower), Boss = e.isBoss });
                }
            }

            void SellTick()
            {
                if (selling == null) return;
                for (int i = 0; i < selling.Count; i++)
                {
                    var x = selling[i];
                    if (x.r == null || x.left <= 0) continue;
                    // StreamingSalesModel.ProcessSingleItemSale の写し（BattleDemandTracker なしのフォールバック式）
                    float perTick = Mathf.Clamp01(x.r.Demand.Value) * x.r.SalesRate * Mathf.Max(1, x.r.DisplayStock.Value);
                    int q = perTick >= 1f ? Mathf.FloorToInt(perTick) : (_rng.NextDouble() < perTick ? 1 : 0);
                    q = Mathf.Clamp(q, 0, x.left);
                    x.left -= q;
                    x.sold += q;
                    salesSoFar += q * x.sel.Price;
                    selling[i] = x;
                }
            }

            // 指示を出す（InterventionPresenter のボタン押下＋キューへの積み込みに相当）
            void TryIssue(int turn)
            {
                while (orderIndex < orders.Count)
                {
                    var order = orders[orderIndex];
                    int at = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(order.Timing) * (horizon - 1)), 0, horizon - 1);
                    if (turn < at) return;                                  // まだその場面ではない
                    if (pending.HasValue || turn < cooldownUntil) return;  // 同時に1件・クールダウン中は待つ

                    int price = AutoPlayInterventionKinds.Price(order.Kind, _s);
                    int available = _in.StartMoney + salesSoFar - o.InterventionSpent;
                    string skip = null;
                    if (order.Kind == AutoPlayInterventionKind.Special && specialsReserved >= _s.specialMaxPerStream) skip = "必殺技の上限";
                    else if (order.Kind == AutoPlayInterventionKind.BossBuff && bossBuffsUsed >= _s.bossBuffMaxPerStream) skip = "ボス強化の上限";
                    else if (available < price) skip = $"残高不足({available}<{price})";

                    orderIndex++;
                    if (skip != null)
                    {
                        o.Skipped.Add($"{AutoPlayInterventionKinds.Key(order.Kind)}@{turn}:{skip}");
                        continue; // 次の予定を見る
                    }

                    o.InterventionSpent += price;
                    if (AutoPlayInterventionKinds.IsHeroSide(order.Kind)) o.HeroSideSpent += price; else o.DungeonSideSpent += price;
                    if (order.Kind == AutoPlayInterventionKind.Special) { specialsReserved++; o.SpecialMoves++; }
                    if (order.Kind == AutoPlayInterventionKind.BossBuff) bossBuffsUsed++;
                    pending = order.Kind;
                    pendingPaid = price;
                    cooldownUntil = turn + Mathf.Max(1, cooldownTurns);
                    return;
                }
            }

            void Execute(AutoPlayInterventionKind kind, int turn, ref int hp)
            {
                switch (kind)
                {
                    case AutoPlayInterventionKind.Heal:
                        hp = Mathf.Min(heroMaxHp, hp + Mathf.RoundToInt(heroMaxHp * _s.healRatio));
                        break;
                    case AutoPlayInterventionKind.Skill:
                        armedStrike = armedStrike.HasValue ? Mathf.Max(armedStrike.Value, _s.skillMultiplier) : _s.skillMultiplier;
                        break;
                    case AutoPlayInterventionKind.Special:
                        armedStrike = armedStrike.HasValue ? Mathf.Max(armedStrike.Value, _s.specialMultiplier) : _s.specialMultiplier;
                        break;
                    case AutoPlayInterventionKind.Trap:
                        trapArmed = true;
                        break;
                    case AutoPlayInterventionKind.Curse:
                        curseLeft = Mathf.Max(curseLeft, _s.curseHeroHits);
                        break;
                    case AutoPlayInterventionKind.Reinforce:
                    {
                        // ReinforceCommand.CollectCandidates の写し（現在フェーズの通常魔物 → 全フェーズの通常魔物）
                        var pool = _phases[Mathf.Min(phaseIndex, _phases.Count - 1)].enemies.Where(e => e != null && !e.isBoss).ToList();
                        if (pool.Count == 0) pool = _phases.SelectMany(p => p.enemies).Where(e => e != null && !e.isBoss).ToList();
                        for (int i = 0; i < _s.reinforceCount && pool.Count > 0; i++) queue.Enqueue(pool[_rng.Next(pool.Count)]);
                        break;
                    }
                    case AutoPlayInterventionKind.BossBuff:
                        bossBuff = true;
                        break;
                }
                o.Executed.Add($"{AutoPlayInterventionKinds.Key(kind)}@{turn}");
            }

            Refill();
            for (int t = 0; t < MaxSimTurns; t++)
            {
                Turns++;
                if (interventions)
                {
                    TryIssue(t);
                    // 視聴者の赤スパ（SuperChatGenerator → TryEnqueueViewerSpecial）。必殺技の上限に含む
                    if (_in.ViewerRedChancePerTurn > 0f && !viewerSpecialPending && specialsReserved < _s.specialMaxPerStream
                        && _rng.NextDouble() < _in.ViewerRedChancePerTurn)
                    {
                        viewerSpecialPending = true;
                        specialsReserved++;
                        o.SpecialMoves++;
                        o.ViewerSpecials++;
                    }
                }

                // --- 勇者の手番（頭で勇者側の指示、無ければ視聴者の必殺技を実行） ---
                if (interventions)
                {
                    if (pending.HasValue && AutoPlayInterventionKinds.IsHeroSide(pending.Value))
                    {
                        var k = pending.Value;
                        pending = null;
                        Execute(k, t, ref heroHp);
                    }
                    else if (viewerSpecialPending)
                    {
                        viewerSpecialPending = false;
                        armedStrike = armedStrike.HasValue ? Mathf.Max(armedStrike.Value, _s.specialMultiplier) : _s.specialMultiplier;
                        o.Executed.Add($"viewer_special@{t}");
                    }
                }

                if (field.Count > 0)
                {
                    var target = field[0];
                    float mul = 1f;
                    if (interventions)
                    {
                        // InterventionCommandQueue.ResolveAttack（勇者が攻撃する側）の写し
                        if (armedStrike.HasValue) { mul *= armedStrike.Value; armedStrike = null; }
                        if (bossBuff && target.Boss) mul *= _s.bossBuffDamageTakenMul;
                    }
                    // CharacterPresenter.PerformAttack → CharacterModel.ApplyDamage の写し
                    int atk = mul == 1f ? heroAtk : Mathf.Max(1, Mathf.RoundToInt(heroAtk * mul));
                    target.Hp -= Mathf.Max(1, atk - target.Def);
                    field[0] = target;
                }

                // --- 魔物の手番（頭でダンジョン側の指示を実行） ---
                for (int i = 0; i < field.Count; i++)
                {
                    var enemy = field[i];
                    if (enemy.Hp <= 0) continue;
                    if (interventions && pending.HasValue && !AutoPlayInterventionKinds.IsHeroSide(pending.Value))
                    {
                        var k = pending.Value;
                        pending = null;
                        Execute(k, t, ref heroHp);
                    }

                    // InterventionCommandQueue.ResolveAttack（魔物が勇者を攻撃する側）の写し:
                    // 倍率（ボス強化）→ 通常ダメージ max(1, 攻撃−防御) → 防御を削った分の差（呪い・ボスの防御貫通）と罠を
                    // 防御無視の追加ダメージとして上乗せ（ApplyBonusDamage は倒れた相手には入らない）
                    bool buffedBoss = interventions && bossBuff && enemy.Boss;
                    float emul = buffedBoss ? _s.bossBuffAttackMul : 1f;
                    int eatk = emul == 1f ? enemy.Atk : Mathf.Max(1, Mathf.RoundToInt(enemy.Atk * emul));
                    int normal = Mathf.Max(1, eatk - heroDef);
                    int bonus = 0;
                    if (interventions)
                    {
                        float defFactor = 1f;
                        if (curseLeft > 0) { defFactor *= _s.curseDefenseMul; curseLeft--; }
                        if (buffedBoss) defFactor *= 1f - Mathf.Clamp01(_s.bossBuffDefensePierce);
                        if (defFactor < 1f)
                            bonus += Mathf.Max(0, Mathf.Max(1, eatk - Mathf.RoundToInt(heroDef * defFactor)) - normal);
                        if (trapArmed)
                        {
                            trapArmed = false;
                            bonus += Mathf.Max(1, Mathf.RoundToInt(heroMaxHp * _s.trapBonusDamageRatio));
                        }
                    }
                    heroHp -= normal;
                    if (heroHp > 0 && bonus > 0) heroHp -= bonus;
                    if (heroHp <= 0) break;
                }

                // 配信販売（戦闘ターンごと。StreamSalesScale ぶん）
                tickAcc += Mathf.Max(0f, _in.SalesTicksPerTurn);
                while (tickAcc >= 1f) { SellTick(); ticksDone++; tickAcc -= 1f; }

                if (heroHp <= 0) break;

                foreach (var e in field.Where(e => e.Hp <= 0))
                {
                    if (e.Boss) BossesDefeated++;
                    else MobsDefeated++;
                }
                field.RemoveAll(e => e.Hp <= 0);

                if (field.Count == 0 && queue.Count == 0)
                {
                    phaseIndex++;
                    if (phaseIndex >= _phases.Count) break;
                    foreach (var e in _phases[phaseIndex].enemies.Where(e => e != null)) queue.Enqueue(e);
                }
                Refill();
            }

            HeroWon = heroHp > 0 && phaseIndex >= _phases.Count;
            HeroHpRatio = Mathf.Clamp01(heroHp / (float)Mathf.Max(1, heroMaxHp));

            if (o == null) return;

            // 決着で実行されなかった指示は返金（InterventionPresenter.Stop の写し）
            if (pending.HasValue)
            {
                o.InterventionRefund += pendingPaid;
                o.Skipped.Add($"{AutoPlayInterventionKinds.Key(pending.Value)}:未実行→返金");
            }

            if (selling != null)
            {
                // 少なくとも1回は販売判定する（従来の「ターン数×倍率、最低1回」と同じ）
                if (ticksDone == 0 && _in.SalesTicksPerTurn > 0f) SellTick();
                foreach (var x in selling)
                {
                    o.Sold.Add(new BattleOutputSoldItem
                    {
                        ItemId = x.sel.ItemId,
                        SoldQuantity = x.sold,
                        SoldFromStock = x.sold,
                        SoldPrice = x.sel.Price,
                    });
                    o.RawSales += x.sold * x.sel.Price;
                }
            }
        }
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
}

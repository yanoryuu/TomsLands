using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>介入コマンドの実行時に渡す戦闘側の参照。</summary>
public sealed class InterventionExecutionScope
{
    public BattleContext Context;
    public InterventionEffects Effects;
    public StreamingInteractionSettings Settings;
    public ISpecialMoveCutIn CutIn;
}

/// <summary>
/// 介入の指示1件。InterventionCommandQueue に1件だけ積まれ、勇者側は勇者の手番、
/// ダンジョン側は魔物の手番の頭で ExecuteAsync される（§4-4）。
/// </summary>
public interface IInterventionCommand
{
    InterventionFace Face { get; }
    /// <summary>プレイヤーが払った額（視聴者起点は 0）。未実行のまま配信が終われば返金する。</summary>
    int PaidAmount { get; }
    /// <summary>true = プレイヤー払い / false = 視聴者の赤スパ起点。</summary>
    bool IsPlayer { get; }
    string Label { get; }
    UniTask ExecuteAsync(InterventionExecutionScope scope, CancellationToken ct);
}

public abstract class HeroInterventionCommand : IInterventionCommand
{
    protected HeroInterventionCommand(int paid, bool isPlayer) { PaidAmount = paid; IsPlayer = isPlayer; }
    public abstract HeroInterventionType Type { get; }
    public InterventionFace Face => InterventionFace.Hero;
    public int PaidAmount { get; }
    public bool IsPlayer { get; }
    public virtual string Label => Type.ToString();
    public abstract UniTask ExecuteAsync(InterventionExecutionScope scope, CancellationToken ct);
}

public abstract class DungeonInterventionCommand : IInterventionCommand
{
    protected DungeonInterventionCommand(int paid) { PaidAmount = paid; }
    public abstract DungeonInterventionType Type { get; }
    public InterventionFace Face => InterventionFace.Dungeon;
    public int PaidAmount { get; }
    public bool IsPlayer => true;
    public virtual string Label => Type.ToString();

    public UniTask ExecuteAsync(InterventionExecutionScope scope, CancellationToken ct)
    {
        Execute(scope);
        return UniTask.CompletedTask;
    }

    protected abstract void Execute(InterventionExecutionScope scope);
}

// ═════════════════════════════════════════
//  勇者側（表）
// ═════════════════════════════════════════

/// <summary>緑スパ: 勇者の HP を最大HP × healRatio 回復（最大HPまで）。</summary>
public sealed class HealCommand : HeroInterventionCommand
{
    public HealCommand(int paid) : base(paid, true) { }
    public override HeroInterventionType Type => HeroInterventionType.Heal;

    /// <summary>実際に回復した量（実行後に参照）。</summary>
    public int HealedAmount { get; private set; }

    public override UniTask ExecuteAsync(InterventionExecutionScope scope, CancellationToken ct)
    {
        var hero = scope.Context?.HeroPresenter?.GetModel();
        if (hero != null && !hero.IsDead)
            HealedAmount = hero.Heal(Mathf.RoundToInt(hero.MaxHp * scope.Settings.healRatio));
        return UniTask.CompletedTask;
    }
}

/// <summary>青スパ: 次の勇者の攻撃を ×skillMultiplier。敵タップ（context.SelectedTarget）があればその敵を狙う。</summary>
public sealed class SkillCommand : HeroInterventionCommand
{
    public SkillCommand(int paid) : base(paid, true) { }
    public override HeroInterventionType Type => HeroInterventionType.Skill;

    public override UniTask ExecuteAsync(InterventionExecutionScope scope, CancellationToken ct)
    {
        scope.Effects.ArmHeroStrike(scope.Settings.skillMultiplier);
        return UniTask.CompletedTask;
    }
}

/// <summary>赤スパ: カットイン（未配線なら省略）→ 次の勇者の攻撃を ×specialMultiplier。</summary>
public sealed class SpecialMoveCommand : HeroInterventionCommand
{
    public SpecialMoveCommand(int paid, bool isPlayer) : base(paid, isPlayer) { }
    public override HeroInterventionType Type => HeroInterventionType.Special;
    public override string Label => IsPlayer ? "Special" : "Special(Viewer)";

    public override async UniTask ExecuteAsync(InterventionExecutionScope scope, CancellationToken ct)
    {
        if (scope.CutIn != null)
        {
            try
            {
                await scope.CutIn.PlayAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // 演出の失敗で技が出ないのは避ける
                Debug.LogWarning($"[SpecialMoveCommand] カットインで例外: {e.Message}");
            }
        }
        scope.Effects.ArmHeroStrike(scope.Settings.specialMultiplier);
    }
}

// ═════════════════════════════════════════
//  ダンジョン側（裏）
// ═════════════════════════════════════════

/// <summary>罠: 勇者が次に受ける攻撃に追加ダメージ（最大HP × trapBonusDamageRatio、防御無視）。</summary>
public sealed class TrapCommand : DungeonInterventionCommand
{
    public TrapCommand(int paid) : base(paid) { }
    public override DungeonInterventionType Type => DungeonInterventionType.Trap;
    protected override void Execute(InterventionExecutionScope scope) => scope.Effects.TrapArmed = true;
}

/// <summary>呪い: 勇者の攻撃力を curseHeroAttacks 回ぶん ×curseAttackMul。</summary>
public sealed class CurseCommand : DungeonInterventionCommand
{
    public CurseCommand(int paid) : base(paid) { }
    public override DungeonInterventionType Type => DungeonInterventionType.Curse;
    protected override void Execute(InterventionExecutionScope scope)
        => scope.Effects.CurseAttacksLeft = Mathf.Max(scope.Effects.CurseAttacksLeft, scope.Settings.curseHeroAttacks);
}

/// <summary>
/// 増援: 現在フェーズの未出現キューに通常魔物を reinforceCount 体足す。
/// 出現は既存の SpawnFromPhaseQueueAsync（ターン終了評価）が行う。
/// </summary>
public sealed class ReinforceCommand : DungeonInterventionCommand
{
    public ReinforceCommand(int paid) : base(paid) { }
    public override DungeonInterventionType Type => DungeonInterventionType.Reinforce;

    public int AddedCount { get; private set; }

    protected override void Execute(InterventionExecutionScope scope)
    {
        var ctx = scope.Context;
        if (ctx == null || ctx.AllPhasesCleared) return;

        var pool = CollectCandidates(ctx);
        if (pool.Count == 0) return;
        for (int i = 0; i < scope.Settings.reinforceCount; i++)
        {
            ctx.EnqueueReinforcement(pool[UnityEngine.Random.Range(0, pool.Count)]);
            AddedCount++;
        }
    }

    /// <summary>現在フェーズの通常魔物 → 全フェーズの通常魔物 の順で候補を集める（ボスは出さない）。</summary>
    private static List<EnemyData> CollectCandidates(BattleContext ctx)
    {
        var current = ctx.CurrentPhase?.enemies?.Where(e => e != null && !e.isBoss).ToList();
        if (current != null && current.Count > 0) return current;
        return ctx.Phases
            .Where(p => p?.enemies != null)
            .SelectMany(p => p.enemies)
            .Where(e => e != null && !e.isBoss)
            .ToList();
    }
}

/// <summary>ボス強化: この配信中、ボスの攻撃力UP・受けるダメージDOWN（出現前に掛けても有効）。1配信1回。</summary>
public sealed class BossBuffCommand : DungeonInterventionCommand
{
    public BossBuffCommand(int paid) : base(paid) { }
    public override DungeonInterventionType Type => DungeonInterventionType.BossBuff;
    protected override void Execute(InterventionExecutionScope scope) => scope.Effects.BossBuffActive = true;
}

using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

/// <summary>
/// 実行済みの介入が戦闘に残す効果（次の攻撃の倍率・罠・呪い・ボス強化）。
/// 介入を一度も実行しなければ全て初期値のままで、ResolveAttack は (1, 0) を返す。
/// </summary>
public sealed class InterventionEffects
{
    /// <summary>次の勇者の攻撃に掛ける倍率（青・赤スパ）。null = なし。</summary>
    public float? ArmedStrikeMultiplier { get; private set; }
    public bool TrapArmed { get; set; }
    /// <summary>呪い: 勇者の防御が下がったまま受ける残り被弾回数。</summary>
    public int CurseHitsLeft { get; set; }
    public bool BossBuffActive { get; set; }

    public bool HasArmedStrike => ArmedStrikeMultiplier.HasValue;

    /// <summary>次の攻撃を強化する。既に強化済みなら大きい方を残す。</summary>
    public void ArmHeroStrike(float multiplier)
    {
        ArmedStrikeMultiplier = ArmedStrikeMultiplier.HasValue ? Mathf.Max(ArmedStrikeMultiplier.Value, multiplier) : multiplier;
    }

    internal void ConsumeArmedStrike() => ArmedStrikeMultiplier = null;
}

/// <summary>1回の攻撃に掛ける補正。</summary>
public readonly struct InterventionAttackModifier
{
    public static readonly InterventionAttackModifier None = new(1f, 0, false);

    public readonly float Multiplier;
    public readonly int BonusDamage;
    /// <summary>この攻撃で青・赤スパの強化を使った。</summary>
    public readonly bool UsedArmedStrike;

    public InterventionAttackModifier(float multiplier, int bonusDamage, bool usedArmedStrike)
    {
        Multiplier = multiplier;
        BonusDamage = bonusDamage;
        UsedArmedStrike = usedArmedStrike;
    }
}

/// <summary>
/// 介入の指示キュー（plain C#）。同時に有効な指示は1件（§4-2）。
/// 勇者側の指示は勇者の手番、ダンジョン側の指示は魔物の手番の頭で BattleActionExecutor が消費する。
/// 視聴者の赤スパ起点の必殺技はプレイヤーの枠とは別に1件だけ保持する（プレイヤーの指示を塞がない）。
/// Close() 後は何も実行しない（配信終了ボタンで戦闘ループが裏で回り続けても効果が出ない）。
/// </summary>
public sealed class InterventionCommandQueue : IDisposable
{
    private readonly StreamingInteractionSettings _settings;
    private readonly InterventionExecutionScope _scope;
    private readonly Subject<IInterventionCommand> _onExecuted = new();
    private IInterventionCommand _pending;
    private SpecialMoveCommand _viewerSpecial;

    public InterventionEffects Effects { get; } = new();
    public ISpecialMoveCutIn CutIn { get; set; }
    public bool IsClosed { get; private set; }

    /// <summary>プレイヤーの指示が実行待ちか。</summary>
    public bool HasPending => _pending != null;
    public IInterventionCommand Pending => _pending;
    public bool HasViewerSpecialPending => _viewerSpecial != null;

    /// <summary>指示が実行された（戦闘に効果を入れた直後）。熱・コメント・需要の反映は購読側で行う。</summary>
    public Observable<IInterventionCommand> OnExecuted => _onExecuted;

    public InterventionCommandQueue(StreamingInteractionSettings settings)
    {
        _settings = settings != null ? settings : ScriptableObject.CreateInstance<StreamingInteractionSettings>();
        _scope = new InterventionExecutionScope { Effects = Effects, Settings = _settings };
    }

    public bool TryEnqueue(IInterventionCommand command)
    {
        if (IsClosed || command == null || _pending != null) return false;
        _pending = command;
        return true;
    }

    /// <summary>視聴者の赤スパ起点の必殺技を予約する（既に予約があれば false）。</summary>
    public bool TryEnqueueViewerSpecial()
    {
        if (IsClosed || _viewerSpecial != null) return false;
        _viewerSpecial = new SpecialMoveCommand(0, isPlayer: false);
        return true;
    }

    /// <summary>
    /// 受付を閉じ、未実行の指示を取り出す（返金用）。以後 OnHeroTurnAsync 等は何もしない。
    /// </summary>
    public IInterventionCommand Close()
    {
        IsClosed = true;
        var unexecuted = _pending;
        _pending = null;
        _viewerSpecial = null;
        return unexecuted;
    }

    // ─────────────────────────────────────────
    //  BattleActionExecutor から呼ぶ
    // ─────────────────────────────────────────

    /// <summary>勇者の手番の頭: 勇者側の指示（無ければ視聴者赤スパの必殺技）を1件実行する。</summary>
    public async UniTask OnHeroTurnAsync(BattleContext context, CancellationToken ct)
    {
        if (IsClosed) return;

        IInterventionCommand command = null;
        if (_pending != null && _pending.Face == InterventionFace.Hero)
        {
            command = _pending;
            _pending = null;
        }
        else if (_viewerSpecial != null)
        {
            command = _viewerSpecial;
            _viewerSpecial = null;
        }
        if (command == null) return;

        _scope.Context = context;
        _scope.CutIn = CutIn;
        await command.ExecuteAsync(_scope, ct);
        Debug.Log($"[Intervention] 実行: {command.Label} (paid={command.PaidAmount})");
        _onExecuted.OnNext(command);
    }

    /// <summary>魔物の手番の頭: ダンジョン側の指示を1件実行する。</summary>
    public void OnEnemyTurn(BattleContext context)
    {
        if (IsClosed || _pending == null || _pending.Face != InterventionFace.Dungeon) return;
        var command = _pending;
        _pending = null;

        _scope.Context = context;
        _scope.CutIn = CutIn;
        command.ExecuteAsync(_scope, CancellationToken.None).Forget(); // ダンジョン側は同期で完了する
        Debug.Log($"[Intervention] 実行: {command.Label} (paid={command.PaidAmount})");
        _onExecuted.OnNext(command);
    }

    /// <summary>
    /// 勇者の攻撃対象。青・赤スパの強化中で、タップ指定（context.SelectedTarget）の敵が生きていればその敵。
    /// それ以外は null（＝従来どおり先頭の生存敵）。
    /// </summary>
    public CharacterPresenter PickHeroTarget(BattleContext context)
    {
        if (IsClosed || !Effects.HasArmedStrike || context == null) return null;
        var selected = context.SelectedTarget.Value;
        if (selected == null) return null;
        if (selected.GetModel().IsDead || !context.EnemyPresenters.Contains(selected))
        {
            context.SelectedTarget.Value = null;
            return null;
        }
        return selected;
    }

    /// <summary>
    /// 攻撃1回分の補正を求め、使い切りの効果（強化・罠・呪いの回数）を消費する。
    /// 介入が無ければ InterventionAttackModifier.None（倍率1・追加0）。
    /// </summary>
    public InterventionAttackModifier ResolveAttack(BattleContext context, CharacterModel attacker, CharacterModel target)
    {
        if (IsClosed || attacker == null || target == null) return InterventionAttackModifier.None;

        if (attacker.Type == CharacterType.Hero)
        {
            float mul = 1f;
            bool usedStrike = false;
            if (Effects.HasArmedStrike)
            {
                mul *= Effects.ArmedStrikeMultiplier.Value;
                Effects.ConsumeArmedStrike();
                usedStrike = true;
                if (context != null) context.SelectedTarget.Value = null; // 指定は1回で解除
            }
            if (Effects.BossBuffActive && target.IsBoss)
                mul *= _settings.bossBuffDamageTakenMul;
            return new InterventionAttackModifier(mul, 0, usedStrike);
        }

        if (target.Type == CharacterType.Hero)
        {
            bool bossBuffed = Effects.BossBuffActive && attacker.IsBoss;
            float mul = bossBuffed ? _settings.bossBuffAttackMul : 1f;

            // 防御を削る効果（呪い・ボス強化の防御貫通）。ダメージ式は CharacterModel.ApplyDamage の
            // max(1, 攻撃 − 防御) なので、防御を下げた場合との差分を「防御無視の追加ダメージ」として上乗せする
            // （CharacterModel / CharacterPresenter を変えずに済ませるため）。
            float defenseFactor = 1f;
            if (Effects.CurseHitsLeft > 0)
            {
                defenseFactor *= _settings.curseDefenseMul;
                Effects.CurseHitsLeft--;
            }
            if (bossBuffed)
                defenseFactor *= 1f - Mathf.Clamp01(_settings.bossBuffDefensePierce);

            int bonus = 0;
            if (defenseFactor < 1f)
            {
                // CharacterPresenter.PerformAttack と同じ攻撃値の丸め
                int attack = mul == 1f ? attacker.AttackPower : Mathf.Max(1, Mathf.RoundToInt(attacker.AttackPower * mul));
                int normal = Mathf.Max(1, attack - target.DefensePower);
                int reduced = Mathf.Max(1, attack - Mathf.RoundToInt(target.DefensePower * defenseFactor));
                bonus += Mathf.Max(0, reduced - normal);
            }

            if (Effects.TrapArmed)
            {
                Effects.TrapArmed = false;
                bonus += Mathf.Max(1, Mathf.RoundToInt(target.MaxHp * _settings.trapBonusDamageRatio));
            }
            return new InterventionAttackModifier(mul, bonus, false);
        }

        return InterventionAttackModifier.None;
    }

    public void Dispose() => _onExecuted.Dispose();
}

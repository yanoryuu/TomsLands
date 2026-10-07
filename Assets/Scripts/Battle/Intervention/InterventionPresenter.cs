using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

/// <summary>
/// 介入の配線役（Presenter）。Docs/Streaming_Redesign.md §4・§5。
///   介入カード（IInterventionCardView）のボタン → 残高・上限・クールダウン判定 → 支払い → InterventionCommandQueue
///   視聴者スパチャ（SuperChatGenerator）→ チャット欄・上固定コメ・熱・同接・赤スパの必殺技
///   実行された指示 → 熱・武器需要・コメント
///
/// お金: 払った瞬間に TotalSpent に積む（PlayerMoney は戦闘終了時に BattleSceneStarter が一括精算）。
/// 利用可能残高 = 所持金 + 戦闘中売上 − 補充費用 − 介入の純支出。
/// Stop() で受付を閉じ、未実行の指示を返金する（戻り値）。
///
/// カード・カットイン・コメントが未配線（null）でもエラーにならない（ロジックだけ動く／何もしない）。
/// </summary>
public sealed class InterventionPresenter : IDisposable
{
    private static readonly HeroInterventionType[] HeroTypes =
        { HeroInterventionType.Heal, HeroInterventionType.Skill, HeroInterventionType.Special };
    private static readonly DungeonInterventionType[] DungeonTypes =
        { DungeonInterventionType.Trap, DungeonInterventionType.Curse, DungeonInterventionType.Reinforce, DungeonInterventionType.BossBuff };

    private const float RefreshInterval = 0.2f;

    private readonly StreamingInteractionSettings _settings;
    private readonly InterventionSceneRefs _refs;
    private readonly TomsModel _tomsModel;
    private readonly BattleSequencer _sequencer;
    private readonly SuperChatGenerator _superChats;
    private readonly CompositeDisposable _disposables = new();
    private readonly Dictionary<HeroInterventionType, InterventionButtonState> _lastHero = new();
    private readonly Dictionary<DungeonInterventionType, InterventionButtonState> _lastDungeon = new();

    private StreamingSalesController _sales;
    private Func<int> _restockSpending;
    private CancellationTokenSource _cts;
    private float _cooldown;
    private float _lastCooldown01 = -1f;
    private float _refreshTimer;
    private string _lastEnemyKey;
    private bool _live;
    private bool _stopped;

    public InterventionCommandQueue Queue { get; }

    /// <summary>この配信で払った介入の総額（返金前）。</summary>
    public int TotalSpent { get; private set; }
    /// <summary>未実行のまま配信が終わった指示の返金額。</summary>
    public int TotalRefunded { get; private set; }
    /// <summary>介入の純支出（利用可能残高・精算に使う）。介入なしなら 0。</summary>
    public int NetSpending => TotalSpent - TotalRefunded;
    /// <summary>必殺技の使用（予約）回数。プレイヤー・視聴者の赤スパの合計。</summary>
    public int SpecialMovesUsed { get; private set; }
    public int BossBuffUsed { get; private set; }
    public bool IsLive => _live;

    public InterventionPresenter(
        StreamingInteractionSettings settings,
        InterventionSceneRefs refs,
        TomsModel tomsModel,
        BattleSequencer sequencer,
        SuperChatGenerator superChats)
    {
        _settings = settings != null ? settings : ScriptableObject.CreateInstance<StreamingInteractionSettings>();
        _refs = refs ?? new InterventionSceneRefs(null, null, null);
        _tomsModel = tomsModel;
        _sequencer = sequencer;
        _superChats = superChats;
        Queue = new InterventionCommandQueue(_settings);
    }

    private IInterventionCardView View => _refs.CardView;
    private StreamingCommentFeed Feed => _refs.CommentDirector != null ? _refs.CommentDirector.Feed : null;
    private StreamingAudienceModel Audience => _refs.CommentDirector != null ? _refs.CommentDirector.Audience : null;

    /// <summary>現在の同接（コメント側が未配線なら基礎人数）。</summary>
    public int CurrentViewers => Audience != null ? Audience.Viewers.Value : (GameConst.Data?.streamingAudience?.baseViewers ?? 60);
    /// <summary>この配信の最大同接（コメント側が未配線なら 0）。</summary>
    public int PeakViewers => Audience != null ? Audience.PeakViewers : 0;
    public int ViewerSuperChatCount => _superChats != null ? _superChats.Count : 0;

    // ─────────────────────────────────────────
    //  開始・終了
    // ─────────────────────────────────────────

    /// <summary>
    /// 配信開始時（StartBattle の直前）に呼ぶ。キューを BattleSequencer に渡し、カード・視聴者スパチャを動かす。
    /// restockSpending は補充費用（BattleSceneStarter._battleSpending）の取得関数。
    /// </summary>
    public void Start(StreamingSalesController sales, Func<int> restockSpending)
    {
        if (_live || _stopped) return;
        _live = true;
        _sales = sales;
        _restockSpending = restockSpending;

        Queue.CutIn = _refs.CutIn;
        _sequencer?.SetInterventionQueue(Queue);

        if (View != null)
        {
            View.OnHeroRequested.Subscribe(OnHeroRequested).AddTo(_disposables);
            View.OnDungeonRequested.Subscribe(OnDungeonRequested).AddTo(_disposables);
            View.ShowFaceImmediate(InterventionFace.Hero); // 配信開始は表（面は配信中だけ保持）
            View.SetPending(false);
            View.SetCooldown(0f);
            View.SetInteractable(true);
        }
        if (_sequencer != null)
            _sequencer.OnCharacterClicked.Subscribe(OnCharacterClicked).AddTo(_disposables);
        Queue.OnExecuted.Subscribe(OnExecuted).AddTo(_disposables);

        if (_superChats != null)
        {
            _superChats.OnSuperChat.Subscribe(OnViewerSuperChat).AddTo(_disposables);
            _superChats.Start(
                () => CurrentViewers,
                () => _sales != null ? _sales.CurrentHeat : 30f,
                IsPaused,
                () => StreamingAudienceBridge.IsBuzzActive && StreamingAudienceBridge.BuzzType == BuzzType.Big,
                HeroName);
        }

        RefreshButtons(force: true);
        _cts = new CancellationTokenSource();
        TickLoopAsync(_cts.Token).Forget();
    }

    /// <summary>
    /// 決着後（battleCts.Cancel の後・StopSales の前）に呼ぶ。新しい支払いを止め、
    /// 未実行の指示を返金する。戻り値 = 今回の返金額。2回目以降は 0。
    /// </summary>
    public int Stop()
    {
        if (_stopped) return 0;
        _stopped = true;
        _live = false;

        var unexecuted = Queue.Close();
        int refund = unexecuted != null && unexecuted.IsPlayer ? unexecuted.PaidAmount : 0;
        TotalRefunded += refund;
        if (refund > 0) Debug.Log($"[InterventionPresenter] 未実行の指示 {unexecuted.Label} を返金: {refund}G");

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (View != null)
        {
            View.SetPending(false);
            View.SetCooldown(0f);
            View.SetInteractable(false);
        }
        Debug.Log($"[InterventionPresenter] 停止: 支出 {TotalSpent}G / 返金 {TotalRefunded}G / 必殺技 {SpecialMovesUsed}回 / 視聴者スパチャ {ViewerSuperChatCount}件");
        return refund;
    }

    // ─────────────────────────────────────────
    //  残高・ボタン状態
    // ─────────────────────────────────────────

    /// <summary>利用可能残高 = 所持金 + 戦闘中売上 − 補充費用 − 介入の純支出。</summary>
    public int AvailableBalance
    {
        get
        {
            int money = _tomsModel != null ? _tomsModel.PlayerMoney.Value : 0;
            int sales = _sales != null ? _sales.GetTotalSalesValue() : 0;
            int restock = _restockSpending != null ? _restockSpending() : 0;
            return money + sales - restock - NetSpending;
        }
    }

    public InterventionButtonState GetState(HeroInterventionType type)
    {
        int price = _settings.GetPrice(type);
        int uses = type == HeroInterventionType.Special ? Mathf.Max(0, _settings.specialMaxPerStream - SpecialMovesUsed) : -1;
        return new InterventionButtonState(price, price <= AvailableBalance, uses);
    }

    public InterventionButtonState GetState(DungeonInterventionType type)
    {
        int price = _settings.GetPrice(type);
        int uses = type == DungeonInterventionType.BossBuff ? Mathf.Max(0, _settings.bossBuffMaxPerStream - BossBuffUsed) : -1;
        return new InterventionButtonState(price, price <= AvailableBalance, uses);
    }

    /// <summary>今この介入を出せるか（受付中・指示なし・クールダウン明け・回数・残高）。</summary>
    public bool CanRequest(InterventionButtonState state)
        => _live && !Queue.IsClosed && !Queue.HasPending && _cooldown <= 0f && !IsPaused() && state.Interactable;

    private void RefreshButtons(bool force = false)
    {
        if (View == null) return;
        foreach (var t in HeroTypes)
        {
            var s = GetState(t);
            if (force || !_lastHero.TryGetValue(t, out var prev) || !Same(prev, s))
            {
                _lastHero[t] = s;
                View.SetHeroButton(t, s);
            }
        }
        foreach (var t in DungeonTypes)
        {
            var s = GetState(t);
            if (force || !_lastDungeon.TryGetValue(t, out var prev) || !Same(prev, s))
            {
                _lastDungeon[t] = s;
                View.SetDungeonButton(t, s);
            }
        }
    }

    private static bool Same(InterventionButtonState a, InterventionButtonState b)
        => a.Price == b.Price && a.Affordable == b.Affordable && a.UsesLeft == b.UsesLeft;

    // ─────────────────────────────────────────
    //  毎フレーム（クールダウン・ボタン・前列魔物）
    // ─────────────────────────────────────────

    private async UniTaskVoid TickLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (!_live) continue;
                if (IsPaused()) continue; // 一時停止中（補充ポップアップ含む）はクールダウンも止める

                float dt = Time.deltaTime;
                if (_cooldown > 0f) _cooldown = Mathf.Max(0f, _cooldown - dt);
                float cd01 = _settings.cooldownSeconds > 0f ? _cooldown / _settings.cooldownSeconds : 0f;
                if (View != null && !Mathf.Approximately(cd01, _lastCooldown01))
                {
                    _lastCooldown01 = cd01;
                    View.SetCooldown(cd01);
                }

                _refreshTimer -= dt;
                if (_refreshTimer <= 0f)
                {
                    _refreshTimer = RefreshInterval;
                    RefreshButtons();
                    RefreshEnemies();
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void RefreshEnemies()
    {
        if (View == null) return;
        var ctx = _sequencer != null ? _sequencer.Context : null;
        var list = new List<EnemyBadgeInfo>();
        var key = new StringBuilder();
        if (ctx != null)
        {
            foreach (var p in ctx.EnemyPresenters)
            {
                var m = p?.GetModel();
                if (m == null || m.IsDead) continue;
                float hp01 = m.MaxHp > 0 ? Mathf.Clamp01((float)m.CurrentHp.CurrentValue / m.MaxHp) : 0f;
                list.Add(new EnemyBadgeInfo(m.CharacterSprite, hp01, m.IsBoss));
                key.Append(m.GetHashCode()).Append(':').Append(m.CurrentHp.CurrentValue).Append(m.IsBoss ? 'B' : 'n').Append('|');
            }
        }
        string k = key.ToString();
        if (k == _lastEnemyKey) return;
        _lastEnemyKey = k;
        View.SetEnemies(list);
    }

    // ─────────────────────────────────────────
    //  入力
    // ─────────────────────────────────────────

    private void OnHeroRequested(HeroInterventionType type)
    {
        var state = GetState(type);
        if (!CanRequest(state)) return;

        IInterventionCommand cmd = type switch
        {
            HeroInterventionType.Heal => new HealCommand(state.Price),
            HeroInterventionType.Skill => new SkillCommand(state.Price),
            _ => new SpecialMoveCommand(state.Price, isPlayer: true),
        };
        if (!Pay(cmd)) return;
        if (type == HeroInterventionType.Special) SpecialMovesUsed++;

        // チャット欄に自分のスパチャを積み、流れコメに同色の上固定コメ
        string message = type switch
        {
            HeroInterventionType.Heal => _settings.playerHealMessage,
            HeroInterventionType.Skill => _settings.playerSkillMessage,
            _ => _settings.playerSpecialMessage,
        };
        var info = new SuperChatInfo(StreamingInteractionSettings.ColorOf(type), state.Price, message, isPlayer: true);
        View?.PushSuperChat(info);
        PushSuperChatComment(info);
        RefreshButtons();
    }

    private void OnDungeonRequested(DungeonInterventionType type)
    {
        var state = GetState(type);
        if (!CanRequest(state)) return;

        IInterventionCommand cmd = type switch
        {
            DungeonInterventionType.Trap => new TrapCommand(state.Price),
            DungeonInterventionType.Curse => new CurseCommand(state.Price),
            DungeonInterventionType.Reinforce => new ReinforceCommand(state.Price),
            _ => new BossBuffCommand(state.Price),
        };
        if (!Pay(cmd)) return;
        if (type == DungeonInterventionType.BossBuff) BossBuffUsed++;
        RefreshButtons();
    }

    private bool Pay(IInterventionCommand cmd)
    {
        if (!Queue.TryEnqueue(cmd)) return false;
        TotalSpent += cmd.PaidAmount; // 払った瞬間に積む（PlayerMoney は戦闘終了時に精算）
        _cooldown = _settings.cooldownSeconds;
        View?.SetPending(true);
        Debug.Log($"[InterventionPresenter] 指示: {cmd.Label} {cmd.PaidAmount}G（利用可能残高 → {AvailableBalance}G）");
        return true;
    }

    private void OnCharacterClicked(CharacterPresenter presenter)
    {
        if (!_live || presenter == null) return;
        var m = presenter.GetModel();
        if (m == null || m.Type != CharacterType.Enemy || m.IsDead) return;
        var ctx = _sequencer != null ? _sequencer.Context : null;
        if (ctx != null) ctx.SelectedTarget.Value = presenter; // 青・赤スパの攻撃がこの敵を狙う
    }

    // ─────────────────────────────────────────
    //  実行された指示の副作用（熱・需要・コメント）
    // ─────────────────────────────────────────

    private void OnExecuted(IInterventionCommand cmd)
    {
        if (cmd.IsPlayer && !Queue.HasPending) View?.SetPending(false);

        switch (cmd)
        {
            case HeroInterventionCommand hero:
                switch (hero.Type)
                {
                    case HeroInterventionType.Heal:
                        _sales?.AddHeat(_settings.healHeat);
                        Feed?.Push(StreamingCommentTrigger.InterventionHeal, Vars());
                        break;
                    case HeroInterventionType.Skill:
                        _sales?.AddHeat(_settings.skillHeat);
                        _sales?.BoostItemType(ItemTypeData.ItemType.Weapon, _settings.skillWeaponDemandUp, 1f);
                        Feed?.Push(StreamingCommentTrigger.InterventionSkill, Vars());
                        break;
                    case HeroInterventionType.Special:
                        _sales?.AddHeat(_settings.specialHeat);
                        _sales?.BoostItemType(ItemTypeData.ItemType.Weapon, _settings.specialWeaponDemandUp, _settings.specialWeaponPriceMul);
                        Feed?.Push(StreamingCommentTrigger.SpecialMove, Vars());
                        break;
                }
                break;
            case DungeonInterventionCommand dungeon:
                _sales?.AddHeat(_settings.dungeonInterventionHeat);
                var trigger = dungeon.Type switch
                {
                    DungeonInterventionType.Trap => StreamingCommentTrigger.DungeonTrap,
                    DungeonInterventionType.Curse => StreamingCommentTrigger.DungeonCurse,
                    DungeonInterventionType.Reinforce => StreamingCommentTrigger.DungeonReinforce,
                    _ => StreamingCommentTrigger.DungeonBossBuff,
                };
                Feed?.Push(trigger, Vars());
                break;
        }
        RefreshButtons();
    }

    // ─────────────────────────────────────────
    //  視聴者スパチャ（収入にしない）
    // ─────────────────────────────────────────

    private void OnViewerSuperChat(SuperChatInfo info)
    {
        if (!_live) return;
        View?.PushSuperChat(info);
        PushSuperChatComment(info);

        int tier = (int)info.Color + 1; // 1〜7
        _sales?.AddHeat(_settings.heatPerTier * tier);
        Audience?.Spike(_settings.viewerSpikePerTier * tier);

        if (info.Color == SuperChatColor.Red)
        {
            Feed?.Push(StreamingCommentTrigger.RedSuperChat, Vars());
            // 視聴者の赤スパは勇者の必殺技を自動で出す（上限内）。上限後は大弾幕＋熱のみ
            if (_settings.viewerRedTriggersSpecial && SpecialMovesUsed < _settings.specialMaxPerStream && Queue.TryEnqueueViewerSpecial())
            {
                SpecialMovesUsed++;
                View?.PlayAutoTrigger(HeroInterventionType.Special); // 赤ボタンを光らせて「視聴者の赤スパで必殺技が出る」を見せる
            }
            else
                _sales?.AddHeat(_settings.redOverLimitHeat);
            RefreshButtons();
        }
        else
        {
            Feed?.Push(StreamingCommentTrigger.SuperChat, Vars());
        }
    }

    private void PushSuperChatComment(SuperChatInfo info)
    {
        var feed = Feed;
        if (feed == null) return;
        string text = string.IsNullOrEmpty(info.Message) ? $"{info.Amount:N0}G" : $"{info.Amount:N0}G {info.Message}";
        var size = info.Color == SuperChatColor.Red ? NicoCommentSize.Big : NicoCommentSize.Medium;
        feed.PushText(text, ToCommentColor(info.Color), size, NicoCommentPosition.Top, info.IsPlayer ? 5 : 4);
    }

    public static Color ToCommentColor(SuperChatColor color) => StreamingCommentCatalog.ParseColor(color switch
    {
        SuperChatColor.Blue => "blue",
        SuperChatColor.Cyan => "cyan",
        SuperChatColor.Green => "green",
        SuperChatColor.Yellow => "yellow",
        SuperChatColor.Orange => "orange",
        SuperChatColor.Magenta => "pink",
        _ => "red",
    });

    // ─────────────────────────────────────────
    //  ユーティリティ
    // ─────────────────────────────────────────

    private bool IsPaused() => _sequencer != null && _sequencer.PauseController != null && _sequencer.PauseController.IsPaused;

    private string HeroName()
    {
        var ctx = _sequencer != null ? _sequencer.Context : null;
        var name = ctx?.HeroPresenter?.GetModel()?.Name;
        return string.IsNullOrEmpty(name) ? "勇者" : name;
    }

    private Dictionary<string, string> Vars() => new() { { "hero", HeroName() } };

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _disposables.Dispose();
        Queue.Dispose();
    }
}

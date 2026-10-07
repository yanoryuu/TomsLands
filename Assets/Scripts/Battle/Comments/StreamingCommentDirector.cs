using System.Collections.Generic;
using R3;
using UnityEngine;

/// <summary>
/// 配信コメント（ニコニコ式）と同接表示の配線役（Presenter）。
/// 戦闘・販売・配信熱のイベントを購読して StreamingCommentFeed に流し、出てきたコメントを
/// NicoCommentLayerView に表示する。同接（StreamingAudienceModel）もここで回す。
///
/// 開始/終了は StreamingSalesController の販売状態（StartStreamingPhase〜StopSales）に揃える。
/// → BattleSceneStarter 側の変更は不要。精算とは無関係（見た目のみ）。
/// どの参照が欠けてもエラーにならない（欠けた部分の演出が出ないだけ）。
/// </summary>
public class StreamingCommentDirector : MonoBehaviour
{
    [Header("参照（未設定ならシーンから自動取得）")]
    [SerializeField] private BattleSequencer sequencer;
    [SerializeField] private StreamingSalesController salesController;
    [SerializeField] private NicoCommentLayerView layerView;
    [SerializeField] private StreamingViewerCountView viewerCountView;

    [Header("データ")]
    [Tooltip("未設定なら Addressable \"StreamingCommentCatalog\"、それも無ければ組み込み文言")]
    [SerializeField] private StreamingCommentCatalog catalog;

    [Header("同接の瞬間流入（割合）")]
    [SerializeField] private float bossAppearSpike = 0.15f;
    [SerializeField] private float bossDefeatSpike = 0.12f;
    [SerializeField] private float enemyDefeatSpike = 0.03f;

    private const float CriticalDamageRatio = 0.5f;
    private const float PinchHpRatio = 0.3f;

    private StreamingCommentFeed _feed;
    private StreamingAudienceModel _audience;
    private readonly CompositeDisposable _disposables = new();
    private readonly Dictionary<CharacterModel, int> _lastHp = new();
    private bool _live;
    private bool _ended; // 1シーン1配信。終了後は再開しない
    private bool _pinchFired;
    private int _lastTier = -1;
    private string _heroName = "勇者";

    public StreamingCommentFeed Feed => _feed;
    public StreamingAudienceModel Audience => _audience;

    private void Awake()
    {
        if (sequencer == null) sequencer = FindFirstObjectByType<BattleSequencer>(FindObjectsInactive.Include);
        if (salesController == null) salesController = FindFirstObjectByType<StreamingSalesController>(FindObjectsInactive.Include);
        if (layerView == null) layerView = FindFirstObjectByType<NicoCommentLayerView>(FindObjectsInactive.Include);
        if (viewerCountView == null) viewerCountView = FindFirstObjectByType<StreamingViewerCountView>(FindObjectsInactive.Include);
        if (catalog == null) catalog = AddressableLoader.Load<StreamingCommentCatalog>("Streaming/StreamingCommentCatalog");

        _feed = new StreamingCommentFeed(catalog);
        if (layerView != null)
            _feed.OnComment.Subscribe(req => layerView.Show(req)).AddTo(_disposables);

        // 同接は配信開始まで隠す
        if (viewerCountView != null) viewerCountView.gameObject.SetActive(false);
    }

    private void Start()
    {
        if (sequencer != null)
        {
            sequencer.OnCharacterDamaged.Subscribe(OnDamaged).AddTo(_disposables);
            sequencer.OnEnemyDefeated.Subscribe(OnEnemyDefeated).AddTo(_disposables);
            sequencer.OnBossAppeared.Subscribe(_ =>
            {
                _audience?.Spike(bossAppearSpike);
                _feed.Push(StreamingCommentTrigger.BossAppeared);
            }).AddTo(_disposables);
            sequencer.OnBattleWin.Subscribe(_ => EndLive(StreamingCommentTrigger.Victory)).AddTo(_disposables);
            sequencer.OnBattleDefeat.Subscribe(_ => EndLive(StreamingCommentTrigger.Defeat)).AddTo(_disposables);
        }

        if (salesController != null)
        {
            salesController.OnSaleOccurred += OnSale;
            salesController.OnItemStockDepleted
                .Subscribe(item => _feed.Push(StreamingCommentTrigger.StockDepleted, Vars("item", item?.ItemName)))
                .AddTo(_disposables);
        }
    }

    private void OnDestroy()
    {
        if (salesController != null) salesController.OnSaleOccurred -= OnSale;
        _disposables.Dispose();
        _feed?.Dispose();
        _audience?.Dispose();
    }

    private void Update()
    {
        bool paused = sequencer != null && sequencer.PauseController != null && sequencer.PauseController.IsPaused;
        layerView?.SetPaused(paused);
        if (paused) return;

        bool salesActive = salesController != null && salesController.IsSalesActive;
        if (!_live && !_ended && salesActive) BeginLive();
        else if (_live && !salesActive) EndLive(null);

        float heat = salesController != null ? salesController.CurrentHeat : 30f;
        if (_audience != null)
        {
            _audience.Tick(Time.deltaTime, heat);
            _feed.DensityScale = DensityFromViewers(_audience.Viewers.Value);
            _feed.Viewers = _audience.Viewers.Value;
        }
        _feed.Heat = heat;

        if (_live && salesController != null)
        {
            int tier = salesController.CurrentHeatTier;
            if (_lastTier >= 0 && tier >= 0 && tier != _lastTier)
                _feed.Push(tier > _lastTier ? StreamingCommentTrigger.HeatUp : StreamingCommentTrigger.HeatDown);
            _lastTier = tier;
        }

        _feed.Tick(Time.deltaTime);
    }

    // ─────────────────────────────────────────
    //  開始・終了
    // ─────────────────────────────────────────

    private void BeginLive()
    {
        _live = true;
        _pinchFired = false;
        _lastHp.Clear();
        float heat = salesController != null ? salesController.CurrentHeat : 30f;
        _lastTier = salesController != null ? salesController.CurrentHeatTier : -1;

        _audience?.Dispose();
        _audience = new StreamingAudienceModel(
            GameConst.Data?.streamingAudience,
            StreamingAudienceBridge.ResolveFollowers(),
            StreamingAudienceBridge.IsBuzzActive,
            StreamingAudienceBridge.BuzzType,
            heat);
        _feed.Buzz = StreamingAudienceBridge.ToCommentCondition();
        _feed.Heat = heat;
        _feed.Viewers = _audience.Viewers.Value;
        _feed.DensityScale = DensityFromViewers(_audience.Viewers.Value);

        if (viewerCountView != null)
        {
            viewerCountView.gameObject.SetActive(true);
            viewerCountView.SetImmediate(_audience.Viewers.Value);
            UIFx.Pop(viewerCountView.transform, 0.8f, 0.2f);
            _audience.Viewers.Subscribe(v => viewerCountView.SetViewers(v)).AddTo(_disposables);
        }

        _feed.Start();
        _feed.Push(StreamingCommentTrigger.BattleStart);
    }

    private void EndLive(StreamingCommentTrigger? finalTrigger)
    {
        if (!_live) return;
        if (finalTrigger.HasValue) _feed.Push(finalTrigger.Value, Vars("hero", _heroName));
        _feed.Stop();
        _live = false;
        _ended = true;
    }

    private float DensityFromViewers(int viewers)
    {
        var c = catalog;
        float reference = c != null ? Mathf.Max(1f, c.referenceViewers) : 300f;
        float min = c != null ? c.minDensityScale : 0.5f;
        float max = c != null ? c.maxDensityScale : 2.5f;
        // 同接の伸びに対して量は緩やかに増やす（平方根）
        return Mathf.Clamp(Mathf.Sqrt(viewers / reference), min, max);
    }

    // ─────────────────────────────────────────
    //  イベント → トリガー
    // ─────────────────────────────────────────

    private void OnDamaged((CharacterModel attacker, CharacterModel target) e)
    {
        if (!_live || e.target == null) return;
        int prev = _lastHp.TryGetValue(e.target, out var hp) ? hp : e.target.MaxHp;
        int cur = e.target.CurrentHp.CurrentValue;
        _lastHp[e.target] = cur;
        int dealt = Mathf.Max(0, prev - cur);

        if (e.target.Type == CharacterType.Hero)
        {
            _heroName = string.IsNullOrEmpty(e.target.Name) ? _heroName : e.target.Name;
            float ratio = e.target.MaxHp > 0 ? (float)cur / e.target.MaxHp : 1f;
            if (!_pinchFired && cur > 0 && ratio < PinchHpRatio)
            {
                _pinchFired = true;
                _feed.Push(StreamingCommentTrigger.HeroPinch, Vars("hero", _heroName, "enemy", e.attacker?.Name));
            }
            else
            {
                _feed.Push(StreamingCommentTrigger.HeroDamaged, Vars("hero", _heroName, "enemy", e.attacker?.Name));
            }
        }
        else if (e.attacker != null && e.attacker.Type == CharacterType.Hero)
        {
            _heroName = string.IsNullOrEmpty(e.attacker.Name) ? _heroName : e.attacker.Name;
            bool critical = e.target.MaxHp > 0 && dealt >= e.target.MaxHp * CriticalDamageRatio;
            // 撃破時は EnemyDefeated 側で弾幕を出すので、ここでは控えめに
            if (cur <= 0) return;
            _feed.Push(critical ? StreamingCommentTrigger.HeroCritical : StreamingCommentTrigger.HeroAttack,
                Vars("hero", _heroName, "enemy", e.target.Name));
        }
    }

    private void OnEnemyDefeated(CharacterModel enemy)
    {
        if (!_live || enemy == null) return;
        _lastHp.Remove(enemy);
        if (enemy.IsBoss)
        {
            _audience?.Spike(bossDefeatSpike);
            _feed.Push(StreamingCommentTrigger.BossDefeated, Vars("hero", _heroName, "enemy", enemy.Name));
        }
        else
        {
            _audience?.Spike(enemyDefeatSpike);
            _feed.Push(StreamingCommentTrigger.EnemyDefeated, Vars("hero", _heroName, "enemy", enemy.Name));
        }
    }

    private void OnSale(RuntimeItemData item)
    {
        if (!_live || item == null) return;
        _feed.Push(StreamingCommentTrigger.ItemSold, Vars("item", item.ItemName, "price", item.CurrentPrice.Value.ToString("N0")));
    }

    private static Dictionary<string, string> Vars(params string[] kv)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i + 1 < kv.Length; i += 2)
            if (!string.IsNullOrEmpty(kv[i + 1])) d[kv[i]] = kv[i + 1];
        return d;
    }

    // ─────────────────────────────────────────
    //  外部（将来のスパチャ・必殺技）からの差し込み口
    // ─────────────────────────────────────────

    /// <summary>任意のトリガーでコメントを出す（スパチャ・必殺技など P2 以降の演出用）。</summary>
    public void Push(StreamingCommentTrigger trigger, Dictionary<string, string> vars = null) => _feed?.Push(trigger, vars);

    /// <summary>コメント量の設定（ON / 少なめ / OFF）。</summary>
    public void SetDensity(NicoDensity density) => layerView?.SetDensity(density);
}

using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

/// <summary>
/// 配信コメントの発生源（Model）。イベント（Push）と雑談（Tick）からコメント要求を作り、OnComment に流す。
/// ゲーム性への影響はなし（見た目のみ）。時間は Tick で外から進める（ポーズ中は Tick を止めればよい）。
///
/// コメント量の主入力:
///   Heat          … 配信熱 0〜100（雑談間隔・minHeat フィルタ）
///   DensityScale  … 同接から求めた倍率（雑談間隔を割る・弾幕件数に掛ける）
///   Buzz          … バズ状態（buzz 列のフィルタ）
/// </summary>
public sealed class StreamingCommentFeed : IDisposable
{
    private readonly StreamingCommentCatalog _catalog;
    private readonly Subject<StreamingCommentRequest> _onComment = new();
    private readonly List<(float time, StreamingCommentRequest req)> _scheduled = new();
    private readonly Dictionary<StreamingCommentTrigger, float> _lastFired = new();
    private readonly Dictionary<StreamingCommentTrigger, List<StreamingCommentCatalog.Entry>> _byTrigger = new();
    private readonly Dictionary<StreamingCommentTrigger, string> _lastText = new();
    private readonly System.Random _rng;

    private float _now;

    /// <summary>弾幕件数に掛ける同接倍率の上限。</summary>
    public const float MaxBurstScale = 1.6f;
    private float _nextIdle;

    public Observable<StreamingCommentRequest> OnComment => _onComment;

    /// <summary>新規生成中か（Start〜Stop の間）。</summary>
    public bool IsRunning { get; private set; }

    public float Heat { get; set; } = 30f;
    public float DensityScale { get; set; } = 1f;
    public StreamingCommentBuzzCondition Buzz { get; set; } = StreamingCommentBuzzCondition.Any;
    /// <summary>{viewers} の置換値。</summary>
    public int Viewers { get; set; }

    public StreamingCommentFeed(StreamingCommentCatalog catalog, int? seed = null)
    {
        _catalog = catalog != null ? catalog : StreamingCommentCatalog.CreateFallback();
        _rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        foreach (var e in _catalog.GetEntries())
        {
            if (e == null || string.IsNullOrEmpty(e.text)) continue;
            if (!_byTrigger.TryGetValue(e.trigger, out var list)) _byTrigger[e.trigger] = list = new List<StreamingCommentCatalog.Entry>();
            list.Add(e);
        }
    }

    public void Start()
    {
        IsRunning = true;
        _nextIdle = _now + 0.8f;
    }

    /// <summary>新規生成を止める。予約済みの弾幕（最大 spreadSeconds 先まで）は Tick で流し切る。</summary>
    public void Stop(bool clearScheduled = false)
    {
        IsRunning = false;
        if (clearScheduled) _scheduled.Clear();
    }

    /// <summary>時間を進める。予約済みコメントの放出と雑談の生成を行う。</summary>
    public void Tick(float deltaTime)
    {
        _now += Mathf.Max(0f, deltaTime);

        for (int i = _scheduled.Count - 1; i >= 0; i--)
        {
            if (_scheduled[i].time > _now) continue;
            var req = _scheduled[i].req;
            _scheduled.RemoveAt(i);
            _onComment.OnNext(req);
        }

        if (!IsRunning) return;
        if (_now >= _nextIdle)
        {
            Push(StreamingCommentTrigger.Idle);
            _nextIdle = _now + NextIdleInterval();
        }
    }

    /// <summary>
    /// イベントを差し込む（戦闘イベント・将来のスパチャ等）。vars は {key} 置換用。
    /// </summary>
    public void Push(StreamingCommentTrigger trigger, IReadOnlyDictionary<string, string> vars = null, float countMultiplier = 1f)
    {
        if (!IsRunning) return;
        var burst = _catalog.GetBurst(trigger);
        if (burst != null)
        {
            if (burst.cooldownSeconds > 0f && _lastFired.TryGetValue(trigger, out var last) && _now - last < burst.cooldownSeconds) return;
            if (burst.chance < 1f && _rng.NextDouble() > burst.chance) return;
        }
        _lastFired[trigger] = _now;

        int min = burst != null ? Mathf.Max(0, burst.minCount) : 1;
        int max = burst != null ? Mathf.Max(min, burst.maxCount) : 1;
        int baseCount = _rng.Next(min, max + 1);
        // 弾幕は同接倍率で増減（1件のイベントは1件のまま）。増やしすぎると読めなくなるので上限を設け、
        // 増えた分だけ散らす秒数も伸ばして「1秒あたりの量」を保つ
        float scale = Mathf.Clamp(DensityScale * countMultiplier, 0.6f, MaxBurstScale);
        int count = baseCount <= 1 ? baseCount : Mathf.Max(1, Mathf.RoundToInt(baseCount * scale));
        float spread = (burst != null ? burst.spreadSeconds : 0f) * Mathf.Max(1f, scale);
        int priority = burst != null ? burst.priority : 1;

        bool fixedUsed = false; // 上下固定コメントは1イベント1件まで（同じ見出しが縦に並ぶのを防ぐ）
        for (int i = 0; i < count; i++)
        {
            var entry = Pick(trigger);
            if (entry == null) return;
            var position = entry.position;
            if (position != NicoCommentPosition.Flow)
            {
                if (fixedUsed)
                {
                    for (int retry = 0; retry < 3 && entry.position != NicoCommentPosition.Flow; retry++)
                        entry = Pick(trigger) ?? entry;
                    position = NicoCommentPosition.Flow;
                }
                else fixedUsed = true;
            }
            var req = new StreamingCommentRequest(Fill(entry.text, vars), entry.color, entry.size, position,
                position == NicoCommentPosition.Flow ? priority : priority + 1);
            float at = _now + (count <= 1 || i == 0 ? 0f : (float)_rng.NextDouble() * spread);
            _scheduled.Add((at, req));
        }
    }

    private float NextIdleInterval()
    {
        float t = Mathf.Clamp01(Heat / 100f);
        float interval = Mathf.Lerp(_catalog.idleIntervalAtHeat0, _catalog.idleIntervalAtHeat100, t);
        interval /= Mathf.Max(0.1f, DensityScale);
        // ゆらぎ（±40%）
        interval *= 0.6f + (float)_rng.NextDouble() * 0.8f;
        return Mathf.Max(0.12f, interval);
    }

    private StreamingCommentCatalog.Entry Pick(StreamingCommentTrigger trigger)
    {
        if (!_byTrigger.TryGetValue(trigger, out var list) || list.Count == 0) return null;

        float total = 0f;
        foreach (var e in list) if (Eligible(e)) total += e.weight;
        if (total <= 0f) return null;

        // 同じ文言が連続しないよう最大3回引き直す
        _lastText.TryGetValue(trigger, out var lastText);
        StreamingCommentCatalog.Entry picked = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            float r = (float)_rng.NextDouble() * total;
            foreach (var e in list)
            {
                if (!Eligible(e)) continue;
                r -= e.weight;
                if (r <= 0f) { picked = e; break; }
            }
            if (picked != null && picked.text != lastText) break;
        }
        if (picked != null) _lastText[trigger] = picked.text;
        return picked;
    }

    private bool Eligible(StreamingCommentCatalog.Entry e)
    {
        if (e.weight <= 0f || Heat < e.minHeat) return false;
        switch (e.buzz)
        {
            case StreamingCommentBuzzCondition.Any: return true;
            case StreamingCommentBuzzCondition.Buzz: return Buzz == StreamingCommentBuzzCondition.Buzz || Buzz == StreamingCommentBuzzCondition.SuperBuzz;
            default: return Buzz == e.buzz;
        }
    }

    private string Fill(string text, IReadOnlyDictionary<string, string> vars)
    {
        if (text.IndexOf('{') < 0) return text;
        if (vars != null)
            foreach (var kv in vars)
                text = text.Replace("{" + kv.Key + "}", kv.Value ?? string.Empty);
        text = text.Replace("{viewers}", Viewers.ToString("N0"));
        // 置換されなかったプレースホルダは消す
        text = text.Replace("{hero}", "勇者").Replace("{enemy}", "敵").Replace("{item}", "それ").Replace("{price}", "");
        return text;
    }

    public void Dispose()
    {
        _scheduled.Clear();
        _onComment.Dispose();
    }
}

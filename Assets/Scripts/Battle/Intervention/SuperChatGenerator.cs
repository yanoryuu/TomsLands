using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer;

/// <summary>
/// 視聴者ランダムスパチャの発生源（Docs/Streaming_Redesign.md §5-1）。
/// 1分あたりの期待件数 = 同接 / viewersPerUnit × (heatBase + 熱/100)（上限 maxPerMinute）。
/// 色の重みは同接が多いほど赤寄り、超バズ中は赤の重みをさらに上げる。
/// 金額は表示上のみ（収入・精算には一切入らない）。効果（熱・同接・赤スパの必殺技）は購読側（InterventionPresenter）が反映する。
/// </summary>
public sealed class SuperChatGenerator : IDisposable
{
    private readonly StreamingInteractionSettings _settings;
    private readonly Subject<SuperChatInfo> _onSuperChat = new();
    private readonly System.Random _rng;
    private CancellationTokenSource _cts;
    private Func<int> _viewers;
    private Func<float> _heat;
    private Func<bool> _isPaused;
    private Func<bool> _isSuperBuzz;
    private Func<string> _heroName;

    public bool IsRunning { get; private set; }
    /// <summary>この配信で来た視聴者スパチャの件数（将来のフォロワー増加計算用）。</summary>
    public int Count { get; private set; }
    public int RedCount { get; private set; }

    public Observable<SuperChatInfo> OnSuperChat => _onSuperChat;

    [Inject]
    public SuperChatGenerator(StreamingInteractionSettings settings) : this(settings, null) { }

    public SuperChatGenerator(StreamingInteractionSettings settings, int? seed)
    {
        _settings = settings != null ? settings : ScriptableObject.CreateInstance<StreamingInteractionSettings>();
        _rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
    }

    /// <summary>発生を始める。入力は関数で受け取り、毎フレーム読む（参照が欠けても既定値で動く）。</summary>
    public void Start(Func<int> viewers, Func<float> heat, Func<bool> isPaused = null, Func<bool> isSuperBuzz = null, Func<string> heroName = null)
    {
        if (IsRunning) return;
        if (!_settings.viewerSuperChatEnabled) return;
        _viewers = viewers;
        _heat = heat;
        _isPaused = isPaused;
        _isSuperBuzz = isSuperBuzz;
        _heroName = heroName;
        Count = 0;
        RedCount = 0;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        LoopAsync(_cts.Token).Forget();
    }

    /// <summary>発生を止める（精算前に呼ぶ。視聴者スパチャは精算に影響しないが、終了後の演出を止める）。</summary>
    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async UniTaskVoid LoopAsync(CancellationToken ct)
    {
        float delay = _settings.startDelaySeconds;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (_isPaused != null && _isPaused()) continue;
                float dt = Time.deltaTime;
                if (delay > 0f) { delay -= dt; continue; }

                float perMinute = ExpectedPerMinute(SafeViewers(), SafeHeat());
                // ポアソン過程: dt の間に1件以上来る確率
                double p = 1.0 - Math.Exp(-(perMinute / 60.0) * dt);
                if (_rng.NextDouble() < p) Emit();
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>1分あたりの期待件数（上限つき）。</summary>
    public float ExpectedPerMinute(int viewers, float heat)
    {
        float v = Mathf.Max(0, viewers) / Mathf.Max(1f, _settings.viewersPerUnit);
        float h = _settings.heatBase + Mathf.Clamp(heat, 0f, 100f) / 100f;
        return Mathf.Clamp(v * Mathf.Max(0f, h), 0f, _settings.maxPerMinute);
    }

    private void Emit()
    {
        var color = PickColor(SafeViewers(), _isSuperBuzz != null && _isSuperBuzz());
        int amount = PickAmount(color);
        string message = PickMessage(color);
        Count++;
        if (color == SuperChatColor.Red) RedCount++;
        _onSuperChat.OnNext(new SuperChatInfo(color, amount, message, isPlayer: false));
    }

    public SuperChatColor PickColor(int viewers, bool superBuzz)
    {
        var w = _settings.colorWeights;
        int n = 7;
        float[] weights = new float[n];
        float viewerFactor = Mathf.Max(0, viewers) / Mathf.Max(1f, _settings.redViewerReference);
        for (int i = 0; i < n; i++)
        {
            float baseW = w != null && i < w.Length ? Mathf.Max(0f, w[i]) : 1f;
            if (i == (int)SuperChatColor.Red)
            {
                baseW *= 1f + viewerFactor;
                if (superBuzz) baseW *= _settings.superBuzzRedMul;
            }
            else if (i == (int)SuperChatColor.Magenta || i == (int)SuperChatColor.Orange)
            {
                baseW *= 1f + viewerFactor * _settings.warmColorViewerScale;
            }
            weights[i] = baseW;
        }

        float total = 0f;
        foreach (var x in weights) total += x;
        if (total <= 0f) return SuperChatColor.Blue;
        double r = _rng.NextDouble() * total;
        for (int i = 0; i < n; i++)
        {
            r -= weights[i];
            if (r <= 0) return (SuperChatColor)i;
        }
        return SuperChatColor.Red;
    }

    private int PickAmount(SuperChatColor color)
    {
        var ranges = _settings.amountRanges;
        int i = (int)color;
        if (ranges == null || i >= ranges.Length) return 100 * (i + 1);
        var r = ranges[i];
        int min = Mathf.Min(r.x, r.y), max = Mathf.Max(r.x, r.y);
        int raw = _rng.Next(min, max + 1);
        // 見た目のキリをよくする（100G 未満は 10 刻み、それ以上は 100 刻み）
        int step = raw >= 1000 ? 100 : 10;
        return Mathf.Max(min, raw / step * step);
    }

    private string PickMessage(SuperChatColor color)
    {
        string[] pool = color switch
        {
            SuperChatColor.Red => _settings.redMessages,
            SuperChatColor.Magenta or SuperChatColor.Orange or SuperChatColor.Yellow => _settings.midMessages,
            _ => _settings.lowMessages,
        };
        if (pool == null || pool.Length == 0) return string.Empty;
        string text = pool[_rng.Next(pool.Length)] ?? string.Empty;
        string hero = _heroName != null ? _heroName() : null;
        return text.Replace("{hero}", string.IsNullOrEmpty(hero) ? "勇者" : hero);
    }

    private int SafeViewers()
    {
        try { return _viewers != null ? _viewers() : 0; } catch { return 0; }
    }

    private float SafeHeat()
    {
        try { return _heat != null ? _heat() : 30f; } catch { return 30f; }
    }

    public void Dispose()
    {
        Stop();
        _onSuperChat.Dispose();
    }
}

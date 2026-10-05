using System;
using System.IO;
using R3;
using UnityEngine;

/// <summary>
/// 配信開始時の同接の入力（フォロワー数・バズ状態）。
/// 店側（GameFlowManager の配信遷移）からセットする想定の橋渡し。未設定なら
/// フォロワー数はセーブ（shopStatusData.json）から読み、バズは「なし」として扱う。
/// </summary>
public static class StreamingAudienceBridge
{
    /// <summary>配信開始時のフォロワー数。null = 未設定（セーブから読む）。</summary>
    public static int? Followers;
    /// <summary>配信開始時にバズ中か。</summary>
    public static bool IsBuzzActive;
    /// <summary>バズの種類（IsBuzzActive のときだけ有効）。</summary>
    public static BuzzType BuzzType = BuzzType.Normal;

    public static void Set(int followers, bool isBuzzActive, BuzzType buzzType)
    {
        Followers = followers;
        IsBuzzActive = isBuzzActive;
        BuzzType = buzzType;
    }

    /// <summary>コメントの buzz 列と照合する形に変換する。</summary>
    public static StreamingCommentBuzzCondition ToCommentCondition()
    {
        if (!IsBuzzActive) return StreamingCommentBuzzCondition.Any;
        return BuzzType switch
        {
            BuzzType.Big => StreamingCommentBuzzCondition.SuperBuzz,
            BuzzType.Flame => StreamingCommentBuzzCondition.Flame,
            _ => StreamingCommentBuzzCondition.Buzz,
        };
    }

    /// <summary>フォロワー数を解決する（未設定ならセーブを読む。読めなければ 0）。</summary>
    public static int ResolveFollowers()
    {
        if (Followers.HasValue) return Mathf.Max(0, Followers.Value);
        try
        {
            var path = SaveSlotManager.GetPath("shopStatusData.json");
            if (!File.Exists(path)) return 0;
            var data = JsonUtility.FromJson<ShopStatusData>(File.ReadAllText(path));
            return data != null ? Mathf.Max(0, data.followers) : 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[StreamingAudienceBridge] フォロワー数を読めませんでした: {e.Message}");
            return 0;
        }
    }
}

/// <summary>
/// 配信の同時接続数（同接）。表示とコメント量の入力に使う（売上・精算には影響しない）。
/// 同接の目標 = (基礎 + フォロワー×係数) × バズ倍率 × 配信熱倍率。表示値は目標へ徐々に近づき、少し揺れる。
/// </summary>
public sealed class StreamingAudienceModel : IDisposable
{
    private readonly StreamingAudienceData _settings;
    private readonly int _followers;
    private readonly float _buzzMultiplier;
    private readonly System.Random _rng = new();
    private float _value;
    private float _jitterTimer;
    private float _jitterFactor = 1f;

    /// <summary>現在の同接（表示用の整数）。</summary>
    public ReactiveProperty<int> Viewers { get; }

    /// <summary>最大同接（将来のフォロワー増加計算用）。</summary>
    public int PeakViewers { get; private set; }

    public StreamingAudienceModel(StreamingAudienceData settings, int followers, bool isBuzzActive, BuzzType buzzType, float initialHeat)
    {
        _settings = settings ?? new StreamingAudienceData();
        _followers = Mathf.Max(0, followers);
        _buzzMultiplier = !isBuzzActive ? 1f : buzzType switch
        {
            BuzzType.Big => _settings.superBuzzMultiplier,
            BuzzType.Flame => _settings.flameMultiplier,
            _ => _settings.buzzMultiplier,
        };
        // 立ち上がりは目標の6割から（配信開始直後に人が集まってくる）
        _value = Target(initialHeat) * 0.6f;
        Viewers = new ReactiveProperty<int>(Mathf.RoundToInt(_value));
        PeakViewers = Viewers.Value;
    }

    public float Target(float heat)
    {
        float baseCount = _settings.baseViewers + _followers * _settings.viewersPerFollower;
        float heatMul = Mathf.Lerp(_settings.heatMultiplierAt0, _settings.heatMultiplierAt100, Mathf.Clamp01(heat / 100f));
        return Mathf.Max(1f, baseCount * _buzzMultiplier * heatMul);
    }

    /// <summary>毎フレーム呼ぶ（ポーズ中は呼ばない）。</summary>
    public void Tick(float deltaTime, float heat)
    {
        _jitterTimer -= deltaTime;
        if (_jitterTimer <= 0f)
        {
            _jitterTimer = 0.8f + (float)_rng.NextDouble() * 0.6f;
            _jitterFactor = 1f + ((float)_rng.NextDouble() * 2f - 1f) * _settings.jitter;
        }

        float target = Target(heat) * _jitterFactor;
        float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(_settings.approachPerSecond), deltaTime);
        _value = Mathf.Lerp(_value, target, k);
        Viewers.Value = Mathf.Max(1, Mathf.RoundToInt(_value));
        if (Viewers.Value > PeakViewers) PeakViewers = Viewers.Value;
    }

    /// <summary>瞬間的な流入（ボス出現・撃破など）。ratio=0.1 で +10%。</summary>
    public void Spike(float ratio) => _value *= 1f + Mathf.Max(0f, ratio);

    public void Dispose() => Viewers.Dispose();
}

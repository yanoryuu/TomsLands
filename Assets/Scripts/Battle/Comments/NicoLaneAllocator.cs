using System;

/// <summary>
/// ニコニコ式の流れコメントのレーン割り当て（衝突回避）。純粋ロジック。
/// 全コメントは同じ時間 D で画面を横断する（長文ほど速い）。
/// レーン L に置ける条件:
///   (a) 前コメの尾がもう画面内に入っている:  now >= prev.spawn + prev.width / prev.speed
///   (b) 新コメが前コメに追いつかない:        now + W / speedNew >= prev.spawn + D
/// 条件を満たすレーンが無ければ「最も早く空くレーン」に重ねる（ニコニコ同様に重なりを許容）。
/// </summary>
public sealed class NicoLaneAllocator
{
    private struct LaneState
    {
        public bool Used;
        public float SpawnTime;
        public float Width;
        public float Speed;
    }

    private LaneState[] _lanes;
    private float _width;
    private readonly float _duration;
    private int _cursor;
    private readonly Random _rng;

    public int LaneCount => _lanes.Length;

    public NicoLaneAllocator(int laneCount, float width, float duration, int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _duration = Math.Max(0.1f, duration);
        Resize(laneCount, width);
    }

    public void Resize(int laneCount, float width)
    {
        _lanes = new LaneState[Math.Max(1, laneCount)];
        _width = Math.Max(1f, width);
        _cursor = 0;
    }

    /// <summary>この幅のコメントの速度（px/秒）。</summary>
    public float SpeedFor(float textWidth) => (_width + textWidth) / _duration;

    /// <summary>
    /// レーン番号（0 = 最上段）を返す。置けるレーンが複数あるときは、その中からランダムに選ぶ
    /// （本家は上から詰めるが、コメントが少ない時間帯に最上段だけが流れる見た目を避けるため）。
    /// </summary>
    public int Allocate(float now, float textWidth)
    {
        float speedNew = SpeedFor(textWidth);
        int best = -1;
        float bestFree = float.MaxValue;
        int fitCount = 0;
        int chosenFit = -1;

        for (int i = 0; i < _lanes.Length; i++)
        {
            var p = _lanes[i];
            bool fits;
            if (!p.Used) fits = true;
            else
            {
                float tailIn = p.SpawnTime + p.Width / p.Speed;
                float prevGone = p.SpawnTime + _duration;
                fits = now >= tailIn && now + _width / speedNew >= prevGone;
                if (!fits)
                {
                    // 空く時刻（条件 a,b を両方満たす最短時刻）
                    float freeAt = Math.Max(tailIn, prevGone - _width / speedNew);
                    if (freeAt < bestFree) { bestFree = freeAt; best = i; }
                }
            }
            if (fits)
            {
                // リザーバサンプリングで一様に1本選ぶ
                fitCount++;
                if (_rng.Next(fitCount) == 0) chosenFit = i;
            }
        }

        if (chosenFit >= 0) best = chosenFit;
        if (best < 0)
        {
            best = _cursor;
            _cursor = (_cursor + 1) % _lanes.Length;
        }

        _lanes[best] = new LaneState { Used = true, SpawnTime = now, Width = textWidth, Speed = speedNew };
        return best;
    }
}

/// <summary>上下固定コメント用。各レーンは表示時間だけ占有する。</summary>
public sealed class NicoFixedLaneAllocator
{
    private float[] _busyUntil;
    private readonly float _duration;

    public NicoFixedLaneAllocator(int laneCount, float duration)
    {
        _busyUntil = new float[Math.Max(1, laneCount)];
        _duration = duration;
    }

    public int Allocate(float now)
    {
        int best = 0;
        for (int i = 0; i < _busyUntil.Length; i++)
        {
            if (_busyUntil[i] <= now) { best = i; break; }
            if (_busyUntil[i] < _busyUntil[best]) best = i;
        }
        _busyUntil[best] = now + _duration;
        return best;
    }
}

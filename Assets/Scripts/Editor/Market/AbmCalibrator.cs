using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// =====================================================================
// ローカルABM のパラメータを「お手本の統計量」へ合わせ込むキャリブレータ。
//
// 使い方:
//   1. JevReferenceRun で Jev のお手本系列を作り、その MarketStats を取る
//   2. その統計量を目標として AbmCalibrator.Fit を回す
//   3. 得られた LocalAbmSettings を ScriptableObject へ焼いて出荷する
//
// 探索は山登り法（ランダム摂動 + 温度を下げながら受理）。
// ABM の1評価は数ミリ秒なので、数百回まわしても一瞬で終わる。
// =====================================================================

/// <summary>合わせ込みたい統計量と、その重み。</summary>
public sealed class AbmCalibrationTarget
{
    /// <summary>尖度。実市場は 5〜10。</summary>
    public float Kurtosis = 6f;

    /// <summary>|リターン| のラグ1自己相関（ボラティリティ・クラスタリング）。実市場は 0.1〜0.3。</summary>
    public float AbsReturnAutocorr1 = 0.2f;

    /// <summary>リターンのラグ1自己相関（予測可能性）。実市場はほぼ 0。</summary>
    public float ReturnAutocorr1;

    /// <summary>対数リターンの標準偏差（ボラティリティ水準）。</summary>
    public float StdDevReturn = 0.012f;

    /// <summary>
    /// 最大ドローダウン（0〜1）。ストップ高／安への張り付きを直接抑えるための項。
    /// 統計量だけを合わせると「価格が上下限まで走って凍結する」解が選ばれうるため、
    /// 値動きの到達範囲そのものに上限を与える。<see cref="WeightMaxDrawdown"/> が 0 なら無視される。
    /// </summary>
    public float MaxDrawdown = 0.15f;

    // --- 各項の重み。0 にすればその指標を無視する ---
    public float WeightKurtosis = 1.0f;
    public float WeightAbsAutocorr = 1.5f;
    public float WeightAutocorr = 1.5f;
    public float WeightStdDev = 1.0f;

    /// <summary>最大ドローダウン項の重み。既定 0 = 無視（従来挙動と一致）。</summary>
    public float WeightMaxDrawdown;

    /// <summary>Jev のお手本結果から目標を作る。</summary>
    public static AbmCalibrationTarget FromStats(MarketStats stats)
    {
        return new AbmCalibrationTarget
        {
            Kurtosis = stats.Kurtosis,
            AbsReturnAutocorr1 = stats.AbsReturnAutocorr1,
            ReturnAutocorr1 = stats.ReturnAutocorr1,
            StdDevReturn = stats.StdDevReturn,
        };
    }

    /// <summary>
    /// 統計量との距離。各指標を「意味のあるスケール」で正規化してから二乗誤差を取る。
    /// 尖度は数値が大きいので 3 で、自己相関は 0.2 で、σ は目標値自身で割る。
    /// </summary>
    public float Loss(MarketStats s)
    {
        float lk = Sq((s.Kurtosis - Kurtosis) / 3f) * WeightKurtosis;
        float la = Sq((s.AbsReturnAutocorr1 - AbsReturnAutocorr1) / 0.2f) * WeightAbsAutocorr;
        float lr = Sq((s.ReturnAutocorr1 - ReturnAutocorr1) / 0.2f) * WeightAutocorr;
        float ls = Sq((s.StdDevReturn - StdDevReturn) / Mathf.Max(1e-4f, StdDevReturn)) * WeightStdDev;

        // ドローダウンは「超過分だけ」を罰する片側ペナルティ。
        // 目標より浅いのは問題にならず、深い（＝上下限へ走る）方だけを抑えたい。
        float excess = Mathf.Max(0f, s.MaxDrawdown - MaxDrawdown);
        float ld = Sq(excess / Mathf.Max(1e-4f, MaxDrawdown)) * WeightMaxDrawdown;

        float loss = lk + la + lr + ls + ld;
        return (float.IsNaN(loss) || float.IsInfinity(loss)) ? float.MaxValue : loss;
    }

    private static float Sq(float x) => x * x;
}

/// <summary>
/// 探索するパラメータの範囲。既定値は「物理的に意味のある範囲」全体。
///
/// 範囲を絞ることで、統計量は合っていても挙動が破綻する解を排除できる。
/// 実例: valueGain（逆張り勢の基準価格アンカー）に下限を設けないと、
/// 最適化が racf1 を稼ぐためにアンカーを 0 近くまで切ってしまい、
/// 価格を基準値へ引き戻す力が消えて上下限に張り付く解が選ばれる。
/// </summary>
public sealed class AbmSearchBounds
{
    public Vector2 MomentumGain = new Vector2(0f, 30f);

    /// <summary>逆張りの感度。下限を 0 にすると基準価格アンカーが失われる。2 以上を推奨。</summary>
    public Vector2 ValueGain = new Vector2(0f, 12f);

    public Vector2 DemandGain = new Vector2(0f, 30f);
    public Vector2 MarketMakerGain = new Vector2(0f, 20f);
    public Vector2 HerdingGain = new Vector2(0f, 1f);
    public Vector2 InactionBandMax = new Vector2(0f, 0.99f);

    /// <summary>戦略スイッチングの強さ（ロジットの逆温度 β）。0 で切り替えなし。</summary>
    public Vector2 SwitchingIntensity = new Vector2(0f, 60f);

    /// <summary>戦略の成績を評価する期間（ターン）。</summary>
    public Vector2 SwitchingMemory = new Vector2(2f, 12f);

    /// <summary>価格インパクトの指数。0.5=平方根則、1.0=線形。上げるほどテールが太る。</summary>
    public Vector2 ImpactExponent = new Vector2(0.5f, 1.3f);
    public Vector2 Lambda = new Vector2(0.002f, 0.6f);
    public Vector2 BaseDepth = new Vector2(20f, 2000f);
    public Vector2 CapitalParetoAlpha = new Vector2(0.6f, 3f);

    /// <summary>
    /// 出発点の各値を範囲内へ収める。範囲外の開始値から探索するのを防ぐ。
    /// </summary>
    public void Clamp(LocalAbmSettings s)
    {
        if (s == null) return;
        s.momentumGain = Mathf.Clamp(s.momentumGain, MomentumGain.x, MomentumGain.y);
        s.valueGain = Mathf.Clamp(s.valueGain, ValueGain.x, ValueGain.y);
        s.demandGain = Mathf.Clamp(s.demandGain, DemandGain.x, DemandGain.y);
        s.marketMakerGain = Mathf.Clamp(s.marketMakerGain, MarketMakerGain.x, MarketMakerGain.y);
        s.herdingGain = Mathf.Clamp(s.herdingGain, HerdingGain.x, HerdingGain.y);
        s.inactionBandMax = Mathf.Clamp(s.inactionBandMax, InactionBandMax.x, InactionBandMax.y);
        s.switchingIntensity = Mathf.Clamp(s.switchingIntensity, SwitchingIntensity.x, SwitchingIntensity.y);
        s.switchingMemory = Mathf.RoundToInt(Mathf.Clamp(s.switchingMemory, SwitchingMemory.x, SwitchingMemory.y));
        s.impactExponent = Mathf.Clamp(s.impactExponent, ImpactExponent.x, ImpactExponent.y);
        s.lambda = Mathf.Clamp(s.lambda, Lambda.x, Lambda.y);
        s.baseDepth = Mathf.Clamp(s.baseDepth, BaseDepth.x, BaseDepth.y);
        s.capitalParetoAlpha = Mathf.Clamp(s.capitalParetoAlpha, CapitalParetoAlpha.x, CapitalParetoAlpha.y);
    }
}

public sealed class AbmCalibrationResult
{
    public LocalAbmSettings Best;
    public MarketStats Stats;
    public float Loss;
    public int Evaluations;
    public MarketStats InitialStats;
    public float InitialLoss;
}

public static class AbmCalibrator
{
    /// <summary>
    /// 山登り法でパラメータを探索する。
    /// </summary>
    /// <param name="target">合わせ込む目標統計量。</param>
    /// <param name="start">探索の出発点。null なら既定設定。</param>
    /// <param name="iterations">評価回数。500 程度で十分収束する。</param>
    /// <param name="turns">1評価あたりのターン数。統計量を安定させるため 200 以上を推奨。</param>
    /// <param name="itemCount">1評価あたりの銘柄数。</param>
    /// <param name="seed">乱数シード。</param>
    /// <param name="onProgress">進捗通知（現在, 総数, 現在の最良Loss）。</param>
    public static AbmCalibrationResult Fit(
        AbmCalibrationTarget target, LocalAbmSettings start = null,
        int iterations = 500, int turns = 250, int itemCount = 8, int seed = 4242,
        Action<int, int, float> onProgress = null, AbmSearchBounds bounds = null)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));

        bounds = bounds ?? new AbmSearchBounds();

        var rng = new System.Random(seed);
        var current = (start ?? new LocalAbmSettings()).Clone();

        // Switcher 追加前に保存された設定（5要素）は、そのままだと探索対象から
        // Switcher が漏れてしまう。列挙子の数まで 0 で埋めてから探索を始める。
        int kinds = Enum.GetValues(typeof(TraderArchetype)).Length;
        if (current.archetypeWeights == null || current.archetypeWeights.Length != kinds)
        {
            var padded = new float[kinds];
            if (current.archetypeWeights != null)
            {
                Array.Copy(current.archetypeWeights, padded,
                    Mathf.Min(current.archetypeWeights.Length, kinds));
            }
            current.archetypeWeights = padded;
        }

        bounds.Clamp(current);

        var currentStats = Evaluate(current, turns, itemCount, seed);
        float currentLoss = target.Loss(currentStats);

        var best = current.Clone();
        var bestStats = currentStats;
        float bestLoss = currentLoss;

        var result = new AbmCalibrationResult { InitialStats = currentStats, InitialLoss = currentLoss };

        for (int i = 0; i < iterations; i++)
        {
            // 温度: 序盤は大きく動き、終盤は微調整に絞る
            float temperature = Mathf.Lerp(0.45f, 0.05f, i / (float)Mathf.Max(1, iterations - 1));

            var candidate = Perturb(current, rng, temperature, bounds);
            var stats = Evaluate(candidate, turns, itemCount, seed);
            float loss = target.Loss(stats);

            if (loss < currentLoss)
            {
                current = candidate;
                currentLoss = loss;

                if (loss < bestLoss)
                {
                    best = candidate.Clone();
                    bestStats = stats;
                    bestLoss = loss;
                }
            }

            onProgress?.Invoke(i + 1, iterations, bestLoss);
        }

        result.Best = best;
        result.Stats = bestStats;
        result.Loss = bestLoss;
        result.Evaluations = iterations + 1;
        return result;
    }

    /// <summary>
    /// 設定をランダムに揺らす。各パラメータは物理的に意味のある範囲でクランプする。
    /// </summary>
    private static LocalAbmSettings Perturb(
        LocalAbmSettings source, System.Random rng, float temperature, AbmSearchBounds b)
    {
        var s = source.Clone();

        s.momentumGain = Jitter(s.momentumGain, b.MomentumGain.x, b.MomentumGain.y, rng, temperature);
        s.valueGain = Jitter(s.valueGain, b.ValueGain.x, b.ValueGain.y, rng, temperature);
        s.demandGain = Jitter(s.demandGain, b.DemandGain.x, b.DemandGain.y, rng, temperature);
        s.marketMakerGain = Jitter(s.marketMakerGain, b.MarketMakerGain.x, b.MarketMakerGain.y, rng, temperature);
        s.herdingGain = Jitter(s.herdingGain, b.HerdingGain.x, b.HerdingGain.y, rng, temperature);
        s.inactionBandMax = Jitter(s.inactionBandMax, b.InactionBandMax.x, b.InactionBandMax.y, rng, temperature);
        s.switchingIntensity = Jitter(s.switchingIntensity, b.SwitchingIntensity.x, b.SwitchingIntensity.y, rng, temperature);
        s.switchingMemory = Mathf.RoundToInt(
            Jitter(s.switchingMemory, b.SwitchingMemory.x, b.SwitchingMemory.y, rng, temperature));
        s.impactExponent = Jitter(s.impactExponent, b.ImpactExponent.x, b.ImpactExponent.y, rng, temperature);
        s.lambda = Jitter(s.lambda, b.Lambda.x, b.Lambda.y, rng, temperature);
        s.baseDepth = Jitter(s.baseDepth, b.BaseDepth.x, b.BaseDepth.y, rng, temperature);
        s.capitalParetoAlpha = Jitter(s.capitalParetoAlpha, b.CapitalParetoAlpha.x, b.CapitalParetoAlpha.y, rng, temperature);

        if (s.archetypeWeights != null)
        {
            float sum = 0f;
            for (int i = 0; i < s.archetypeWeights.Length; i++)
            {
                s.archetypeWeights[i] = Mathf.Clamp(
                    s.archetypeWeights[i] + (float)(rng.NextDouble() * 2.0 - 1.0) * temperature * 0.2f,
                    0.01f, 1f);
                sum += s.archetypeWeights[i];
            }
            if (sum > 0f)
            {
                for (int i = 0; i < s.archetypeWeights.Length; i++) s.archetypeWeights[i] /= sum;
            }
        }
        return s;
    }

    /// <summary>現在値の ±(temperature × レンジ幅) だけ揺らしてクランプする。</summary>
    private static float Jitter(float value, float min, float max, System.Random rng, float temperature)
    {
        float span = (max - min) * temperature;
        float moved = value + (float)(rng.NextDouble() * 2.0 - 1.0) * span;
        return Mathf.Clamp(moved, min, max);
    }

    // ================================================================
    // 評価用のミニシミュレータ
    // ================================================================

    /// <summary>ABM へ渡すための最小限の銘柄表現。</summary>
    private sealed class SimItem : IMarketView
    {
        public string Id;
        public int Base, Price, StockValue;
        public float DemandValue, PrevDemandValue, Trend, LastNet;
        public readonly List<int> History = new List<int>();

        public int CurrentPrice => Price;
        public int BasePrice => Base;
        public float Demand => DemandValue;
        public float PreviousDemand => PrevDemandValue;
        public int Stock => StockValue;
        public IReadOnlyList<int> PriceHistory => History;
        public float LastNetOrder => LastNet;
    }

    // JevReferenceRun / JevMarketSimulator と同一の需要モデル定数
    private const float TrendAmplitude = 0.30f;
    private const float TrendConvergenceRate = 0.15f;
    private const float TrendDriftMax = 0.12f;
    private const float TrendDecayRate = 0.10f;
    private const float DemandFloor = 0.05f, DemandCeiling = 1.0f;
    private const float DisplayDemandUp = 0.02f;
    private const float PriceFloorRate = 0.3f, PriceCeilingRate = 3.0f;

    private static List<SimItem> _itemCache;
    private static int _itemCacheCount = -1;

    /// <summary>
    /// 銘柄テンプレートをメインスレッドで読み込んでキャッシュする。
    /// <see cref="FitToFile"/> のようにバックグラウンドで評価を回す前に、必ずメインスレッドから呼ぶこと
    /// （AssetDatabase はメインスレッド専用のため）。
    /// </summary>
    public static void PrewarmItems(int count)
    {
        BuildItems(Mathf.Max(1, count), 0);
    }

    /// <summary>
    /// キャリブレーションをバックグラウンドで実行し、結果をテキストファイルへ書き出す。
    /// 呼び出しは即座に戻る。
    ///
    /// Unity Pipeline の eval はメインスレッドを5秒までしか占有できないため、
    /// 数百回の評価を伴う探索はこの形で投げてファイルをポーリングする。
    /// </summary>
    public static void FitToFile(
        AbmCalibrationTarget target, LocalAbmSettings start, string outputPath,
        int iterations = 400, int turns = 250, int itemCount = 8, int seed = 4242,
        AbmSearchBounds bounds = null)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("outputPath が空です。");

        // メインスレッドのうちに銘柄を読んでおく
        PrewarmItems(itemCount);

        var startCopy = (start ?? new LocalAbmSettings()).Clone();

        var dir = System.IO.Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(outputPath, "RUNNING\n");

        System.Threading.Tasks.Task.Run(() =>
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var result = Fit(target, startCopy, iterations, turns, itemCount, seed, null, bounds);
                sb.AppendLine("INITIAL " + result.InitialStats);
                sb.AppendLine("INITIAL_LOSS " + result.InitialLoss.ToString("F4"));
                sb.AppendLine("BEST " + result.Stats);
                sb.AppendLine("BEST_LOSS " + result.Loss.ToString("F4"));
                sb.AppendLine("EVALS " + result.Evaluations);
                sb.AppendLine("JSON " + JsonUtility.ToJson(result.Best));
                sb.AppendLine("DONE");
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERROR " + ex.GetType().Name + ": " + ex.Message);
                sb.AppendLine("DONE");
            }

            try { System.IO.File.WriteAllText(outputPath, sb.ToString()); }
            catch { /* 書き出し失敗はポーリング側のタイムアウトで検知される */ }
        });
    }

    /// <summary>
    /// 1設定を評価して統計量を返す。
    /// 同じ seed / turns / itemCount なら常に同じ結果になる（探索の再現性のため）。
    /// </summary>
    public static MarketStats Evaluate(LocalAbmSettings settings, int turns, int itemCount, int seed)
    {
        var items = BuildItems(itemCount, seed);
        var market = new LocalAbmMarket(settings, seed);
        var rng = new System.Random(seed);

        for (int turn = 0; turn < turns; turn++)
        {
            foreach (var item in items)
            {
                AdvanceDemand(item, rng);
                float rate = market.Step(item);
                item.LastNet = market.LastNetOrder;
                ApplyPrice(item, rate);
            }
        }

        var stats = new List<MarketStats>(items.Count);
        foreach (var item in items) stats.Add(MarketStatistics.Compute(item.History));
        return MarketStatistics.Aggregate(stats);
    }

    private static List<SimItem> BuildItems(int count, int seed)
    {
        // ItemData の読み込みは AssetDatabase アクセスで重いのでキャッシュする
        if (_itemCache == null || _itemCacheCount != count)
        {
            _itemCache = new List<SimItem>(count);
            foreach (var guid in AssetDatabase.FindAssets("t:ItemData"))
            {
                if (_itemCache.Count >= count) break;
                var data = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.basePrice <= 0) continue;
                _itemCache.Add(new SimItem
                {
                    Id = string.IsNullOrEmpty(data.itemId) ? data.name : data.itemId,
                    Base = data.basePrice,
                    StockValue = Mathf.Max(1, data.initialStock),
                });
            }
            _itemCacheCount = count;
        }

        var rng = new System.Random(seed);
        var items = new List<SimItem>(_itemCache.Count);
        foreach (var template in _itemCache)
        {
            var item = new SimItem
            {
                Id = template.Id,
                Base = template.Base,
                Price = template.Base,
                StockValue = template.StockValue,
                DemandValue = 0.5f,
                PrevDemandValue = 0.5f,
                Trend = (float)(rng.NextDouble() - 0.5),
            };
            item.History.Add(item.Price);
            items.Add(item);
        }
        return items;
    }

    private static void AdvanceDemand(SimItem item, System.Random rng)
    {
        item.PrevDemandValue = item.DemandValue;

        float drift = (float)(rng.NextDouble() * 2.0 - 1.0) * TrendDriftMax;
        item.Trend = Mathf.Clamp(item.Trend + drift - item.Trend * TrendDecayRate, -1f, 1f);

        float natural = Mathf.Clamp01(0.5f + item.Trend * TrendAmplitude);
        item.DemandValue = Mathf.Clamp(
            item.DemandValue + (natural - item.DemandValue) * TrendConvergenceRate + DisplayDemandUp,
            DemandFloor, DemandCeiling);
    }

    private static void ApplyPrice(SimItem item, float rate)
    {
        if (float.IsNaN(rate) || float.IsInfinity(rate)) rate = 1f;

        int price = Mathf.Max(1, Mathf.RoundToInt(item.Price * rate));
        int floor = Mathf.Max(1, Mathf.RoundToInt(item.Base * PriceFloorRate));
        int ceiling = Mathf.Max(floor, Mathf.RoundToInt(item.Base * PriceCeilingRate));

        item.Price = Mathf.Clamp(price, floor, ceiling);
        item.History.Add(item.Price);
    }
}

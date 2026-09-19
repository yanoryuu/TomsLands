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

    /// <summary>
    /// 需要×リターン相関の下限。「需要が高い銘柄は値上がりする」が価格に届いているか。
    /// Legacy は +0.106。これを下回ったときだけ罰する片側ペナルティ。
    /// </summary>
    public float DemandCorrMin = 0.08f;

    /// <summary>需要相関項の重み。既定 0 = 無視。</summary>
    public float WeightDemandCorr;

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

        // 需要相関は「下限を割った分だけ」罰する。高すぎることは問題にならない。
        float shortfall = Mathf.Max(0f, DemandCorrMin - s.DemandReturnCorr);
        float lc = Sq(shortfall / 0.1f) * WeightDemandCorr;

        float loss = lk + la + lr + ls + ld + lc;
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

    /// <summary>適正値への直接の引き寄せ。0.05 未満だと長期で漂流し、0.35 超だと相場の質感が消える。</summary>
    public Vector2 AnchorPull = new Vector2(0.05f, 0.35f);

    /// <summary>逆張りの感度。下限を 0 にすると基準価格アンカーが失われる。2 以上を推奨。</summary>
    public Vector2 ValueGain = new Vector2(0f, 12f);

    public Vector2 DemandGain = new Vector2(0f, 30f);
    public Vector2 MarketMakerGain = new Vector2(0f, 20f);
    public Vector2 HerdingGain = new Vector2(0f, 1f);
    public Vector2 InactionBandMax = new Vector2(0f, 0.99f);

    /// <summary>価格インパクトの指数。0.5=平方根則、1.0=線形。上げるほどテールが太る。</summary>
    public Vector2 ImpactExponent = new Vector2(0.5f, 1.3f);
    public Vector2 Lambda = new Vector2(0.002f, 0.6f);

    /// <summary>Heat の半減期（ターン）。</summary>
    public Vector2 HeatHalfLife = new Vector2(2f, 8f);

    /// <summary>Heat=0 のときの λ 倍率。1 未満で「凪はより静か」。</summary>
    public Vector2 HeatCalmMultiplier = new Vector2(0.3f, 1f);

    /// <summary>Heat=1 のときの λ 倍率。1 超で「荒れはより荒く」。</summary>
    public Vector2 HeatStormMultiplier = new Vector2(1f, 3f);
    public Vector2 BaseDepth = new Vector2(20f, 2000f);
    public Vector2 CapitalParetoAlpha = new Vector2(0.6f, 3f);

    /// <summary>
    /// 出発点の各値を範囲内へ収める。範囲外の開始値から探索するのを防ぐ。
    /// </summary>
    public void Clamp(LocalAbmSettings s)
    {
        if (s == null) return;
        s.momentumGain = Mathf.Clamp(s.momentumGain, MomentumGain.x, MomentumGain.y);
        s.anchorPull = Mathf.Clamp(s.anchorPull, AnchorPull.x, AnchorPull.y);
        s.valueGain = Mathf.Clamp(s.valueGain, ValueGain.x, ValueGain.y);
        s.demandGain = Mathf.Clamp(s.demandGain, DemandGain.x, DemandGain.y);
        s.marketMakerGain = Mathf.Clamp(s.marketMakerGain, MarketMakerGain.x, MarketMakerGain.y);
        s.herdingGain = Mathf.Clamp(s.herdingGain, HerdingGain.x, HerdingGain.y);
        s.inactionBandMax = Mathf.Clamp(s.inactionBandMax, InactionBandMax.x, InactionBandMax.y);
        s.impactExponent = Mathf.Clamp(s.impactExponent, ImpactExponent.x, ImpactExponent.y);
        s.lambda = Mathf.Clamp(s.lambda, Lambda.x, Lambda.y);
        s.heatHalfLife = Mathf.Clamp(s.heatHalfLife, HeatHalfLife.x, HeatHalfLife.y);
        s.heatCalmMultiplier = Mathf.Clamp(s.heatCalmMultiplier, HeatCalmMultiplier.x, HeatCalmMultiplier.y);
        s.heatStormMultiplier = Mathf.Clamp(s.heatStormMultiplier, HeatStormMultiplier.x, HeatStormMultiplier.y);
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
        Action<int, int, float> onProgress = null, AbmSearchBounds bounds = null, int seedCount = 1)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));

        bounds = bounds ?? new AbmSearchBounds();
        seedCount = Mathf.Max(1, seedCount);

        var rng = new System.Random(seed);
        var current = (start ?? new LocalAbmSettings()).Clone();

        // 保存済み設定の archetypeWeights が列挙子の数と違う場合（列挙子の増減後のセーブ）は、
        // 足りない分を 0 で埋め、余った分を落としてから探索を始める。
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

        // Loss は「シードごとの Loss の平均」で評価する。
        // 統計量を平均してから Loss を取ると、良い編成と悪い編成が打ち消し合って
        // 「平均は良いが半分のプレイで破綻する」解が選ばれる（v2 で実測）。
        var currentStats = EvaluateMulti(current, turns, itemCount, seed, seedCount);
        float currentLoss = MeanLoss(target, current, turns, itemCount, seed, seedCount);

        var best = current.Clone();
        var bestStats = currentStats;
        float bestLoss = currentLoss;

        var result = new AbmCalibrationResult { InitialStats = currentStats, InitialLoss = currentLoss };

        for (int i = 0; i < iterations; i++)
        {
            // 温度: 序盤は大きく動き、終盤は微調整に絞る
            float temperature = Mathf.Lerp(0.45f, 0.05f, i / (float)Mathf.Max(1, iterations - 1));

            var candidate = Perturb(current, rng, temperature, bounds);
            float loss = MeanLoss(target, candidate, turns, itemCount, seed, seedCount);
            MarketStats stats = default;
            if (loss < currentLoss) stats = EvaluateMulti(candidate, turns, itemCount, seed, seedCount);

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
        s.anchorPull = Jitter(s.anchorPull, b.AnchorPull.x, b.AnchorPull.y, rng, temperature);
        s.valueGain = Jitter(s.valueGain, b.ValueGain.x, b.ValueGain.y, rng, temperature);
        s.demandGain = Jitter(s.demandGain, b.DemandGain.x, b.DemandGain.y, rng, temperature);
        s.marketMakerGain = Jitter(s.marketMakerGain, b.MarketMakerGain.x, b.MarketMakerGain.y, rng, temperature);
        s.herdingGain = Jitter(s.herdingGain, b.HerdingGain.x, b.HerdingGain.y, rng, temperature);
        s.inactionBandMax = Jitter(s.inactionBandMax, b.InactionBandMax.x, b.InactionBandMax.y, rng, temperature);
        s.impactExponent = Jitter(s.impactExponent, b.ImpactExponent.x, b.ImpactExponent.y, rng, temperature);
        s.lambda = Jitter(s.lambda, b.Lambda.x, b.Lambda.y, rng, temperature);
        s.heatHalfLife = Jitter(s.heatHalfLife, b.HeatHalfLife.x, b.HeatHalfLife.y, rng, temperature);
        s.heatCalmMultiplier = Jitter(s.heatCalmMultiplier, b.HeatCalmMultiplier.x, b.HeatCalmMultiplier.y, rng, temperature);
        s.heatStormMultiplier = Jitter(s.heatStormMultiplier, b.HeatStormMultiplier.x, b.HeatStormMultiplier.y, rng, temperature);
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

    /// <summary>
    /// 銘柄テンプレートをメインスレッドで読み込んでキャッシュする。
    /// <see cref="FitToFile"/> のようにバックグラウンドで評価を回す前に、必ずメインスレッドから呼ぶこと
    /// （AssetDatabase はメインスレッド専用のため）。
    /// </summary>
    public static void PrewarmItems(int count)
    {
        LoadMasters(Mathf.Max(1, count));
        SharedShopSettings();
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
        AbmSearchBounds bounds = null, int seedCount = 1)
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
                var result = Fit(target, startCopy, iterations, turns, itemCount, seed, null, bounds, seedCount);
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
    /// <summary>
    /// トレーダー編成のシードを複数振って平均した統計量を返す。
    /// 単一シードで合わせ込むと「引きの良い編成」に過適合し、実プレイで再現しない（v1 で実測）。
    /// </summary>
    /// <summary>シードごとに Loss を取って平均する。悪い編成を平均で隠さないための評価。</summary>
    public static float MeanLoss(AbmCalibrationTarget target, LocalAbmSettings settings,
        int turns, int itemCount, int seed, int seedCount)
    {
        seedCount = Mathf.Max(1, seedCount);
        float sum = 0f;
        for (int k = 0; k < seedCount; k++)
        {
            float l = target.Loss(Evaluate(settings, turns, itemCount, seed + k * 7919));
            if (float.IsInfinity(l) || float.IsNaN(l) || l >= float.MaxValue) return float.MaxValue;
            sum += l;
        }
        return sum / seedCount;
    }

    public static MarketStats EvaluateMulti(LocalAbmSettings settings, int turns, int itemCount, int seed, int seedCount)
    {
        if (seedCount <= 1) return Evaluate(settings, turns, itemCount, seed);

        var all = new List<MarketStats>(seedCount);
        for (int k = 0; k < seedCount; k++)
        {
            all.Add(Evaluate(settings, turns, itemCount, seed + k * 7919));
        }
        return MarketStatistics.Aggregate(all);
    }

    public static MarketStats Evaluate(LocalAbmSettings settings, int turns, int itemCount, int seed)
    {
        // 実ランタイムと同じ型・同じ手順で回す。
        // 独自のミニシミュレータで代用すると、価格履歴のリングバッファ長や陳列の有無といった
        // 細部がズレて「ハーネスでは良いのに実機では破綻する」解を選んでしまう（実際に起きた）。
        var masters = LoadMasters(itemCount);
        if (masters.Count == 0) return default;

        var shopSettings = SharedShopSettings();
        var engine = new AbmShopPriceEngine(settings, seed);

        var runtimes = new List<RuntimeItemData>(masters.Count);
        var fullHistories = new List<List<int>>(masters.Count);

        // UnityEngine.Random はメインスレッド専用のため使わない（探索はバックグラウンドで走る）
        var rng = new System.Random(seed);

        for (int i = 0; i < masters.Count; i++)
        {
            var m = masters[i];
            var r = new RuntimeItemData(
                m.itemId, m.itemName, m.basePrice, Mathf.Max(1, m.maxStock),
                Mathf.Max(1, m.initialStock), 5, null, null,
                m.itemType, m.itemAttribute, 1, 0.5f, "", 1f, 0,
                initialTrend: (float)(rng.NextDouble() - 0.5));

            // 陳列の有無で需要の向きが反転する。片方だけで合わせ込むと他方で破綻するので混ぜる。
            r.UpdateIsDisplay(i % 2 == 0);
            r.UpdateDisplayStock(5);

            runtimes.Add(r);
            fullHistories.Add(new List<int> { r.CurrentPrice.Value });
        }

        float floorRate = shopSettings.shopPriceFloorRate;
        float ceilingRate = shopSettings.shopPriceCeilingRate;
        float premium = shopSettings.demandPricePremium;

        // 需要×リターン相関の集計用
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        long corrN = 0;

        for (int turn = 0; turn < turns; turn++)
        {
            engine.BeginTurn(turn);
            for (int i = 0; i < runtimes.Count; i++)
            {
                var r = runtimes[i];
                var m = masters[i];

                // --- ItemModel.ApplyShopTurnEconomy と同一の需要更新（status なし相当）---
                r.PreviousDemand = r.Demand.Value;
                r.PreviousPrice = r.CurrentPrice.Value;

                float drift = (float)(rng.NextDouble() * 2.0 - 1.0) * shopSettings.trendDriftMax;
                r.Trend = Mathf.Clamp(r.Trend + drift - r.Trend * shopSettings.trendDecayRate, -1f, 1f);

                bool displaying = r.IsDisplay.Value && r.DisplayStock.Value > 0;
                float natural = Mathf.Clamp01(0.5f + r.Trend * shopSettings.trendAmplitude);
                float convergence = (natural - r.Demand.Value) * shopSettings.trendConvergenceRate;
                float displayDelta = displaying
                    ? shopSettings.displayDemandUp
                    : -shopSettings.notDisplayDemandDown;

                r.UpdateDemand(Mathf.Clamp(
                    r.Demand.Value + convergence + displayDelta,
                    shopSettings.demandFloor, shopSettings.demandCeiling));

                // --- 価格（実ランタイムと同じエンジン・同じクランプ・同じ適正値）---
                int floor = Mathf.Max(1, Mathf.RoundToInt(m.basePrice * floorRate));
                int ceiling = Mathf.Max(floor, Mathf.RoundToInt(m.basePrice * ceilingRate));
                float fair = ShopFairValue.Compute(m.basePrice, r.Demand.Value, premium, floor, ceiling);
                float prevFair = ShopFairValue.Compute(m.basePrice, r.PreviousDemand, premium, floor, ceiling);

                var ctx = new ShopPriceContext(r, m, shopSettings, fair, prevFair, 1f, 0f);
                float rate = engine.GetPriceRate(in ctx);

                int before = r.CurrentPrice.Value;
                int price = Mathf.Max(1, Mathf.RoundToInt(before * rate));
                r.UpdatePrice(Mathf.Clamp(price, floor, ceiling));

                r.RecordShopHistory();
                fullHistories[i].Add(r.CurrentPrice.Value);

                if (before > 0)
                {
                    double ret = (double)r.CurrentPrice.Value / before - 1.0;
                    double dem = r.Demand.Value;
                    sx += dem; sy += ret; sxx += dem * dem; syy += ret * ret; sxy += dem * ret; corrN++;
                }
            }
        }

        var stats = new List<MarketStats>(fullHistories.Count);
        foreach (var h in fullHistories) stats.Add(MarketStatistics.Compute(h));
        var result = MarketStatistics.Aggregate(stats);

        double denom = System.Math.Sqrt(System.Math.Max(1e-12, (corrN * sxx - sx * sx) * (corrN * syy - sy * sy)));
        double corr = corrN > 2 ? (corrN * sxy - sx * sy) / denom : 0.0;
        result.DemandReturnCorr = (float.IsNaN((float)corr) || float.IsInfinity((float)corr)) ? 0f : (float)corr;
        return result;
    }

    private static List<ItemData> _masterCache;
    private static int _masterCacheCount = -1;
    private static ShopEconomySettings _sharedShopSettings;

    /// <summary>既定値の ShopEconomySettings（需要モデルの定数とクランプ率をここから読む）。</summary>
    private static ShopEconomySettings SharedShopSettings()
    {
        if (_sharedShopSettings == null)
        {
            _sharedShopSettings =
                AssetDatabase.LoadAssetAtPath<ShopEconomySettings>("Assets/Resources_moved/ShopEconomySettings.asset")
                ?? ScriptableObject.CreateInstance<ShopEconomySettings>();
        }
        return _sharedShopSettings;
    }

    private static List<ItemData> LoadMasters(int count)
    {
        count = Mathf.Max(1, count);
        if (_masterCache != null && _masterCacheCount == count) return _masterCache;

        var list = new List<ItemData>(count);
        foreach (var guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            if (list.Count >= count) break;
            var data = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null || data.basePrice <= 0) continue;
            list.Add(data);
        }

        _masterCache = list;
        _masterCacheCount = count;
        return list;
    }

}

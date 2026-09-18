using System;
using System.Collections.Generic;

/// <summary>
/// 価格系列から算出した市場統計量。
/// エージェントベース市場シミュレーションの結果を、実市場の定型的事実
/// （stylized facts）と突き合わせて「本物の株価らしいか」を判定するために使う。
/// </summary>
public struct MarketStats
{
    /// <summary>対数リターンのサンプル数（価格系列の長さ - 1）。</summary>
    public int SampleCount;

    /// <summary>対数リターンの平均。トレンドの強さ。</summary>
    public float MeanReturn;

    /// <summary>対数リターンの標準偏差。ボラティリティの水準。</summary>
    public float StdDevReturn;

    /// <summary>
    /// 対数リターンの尖度（kurtosis）。正規分布なら 3。実市場は 5〜10。
    /// 大きいほどファットテール＝たまに大暴落が起きる。
    /// </summary>
    public float Kurtosis;

    /// <summary>
    /// |対数リターン| のラグ1自己相関。ボラティリティ・クラスタリングの指標。
    /// 実市場では 0.1〜0.3。荒れる時期と凪の時期が交互に来るほど大きい。
    /// </summary>
    public float AbsReturnAutocorr1;

    /// <summary>
    /// 対数リターン自体のラグ1自己相関。予測可能性の指標。
    /// 実市場ではほぼ 0。大きいと「読めてしまう」相場になる。
    /// </summary>
    public float ReturnAutocorr1;

    /// <summary>最大ドローダウン（0〜1）。期間中の高値からの最大下落率。</summary>
    public float MaxDrawdown;

    /// <summary>人が読める1行サマリ。</summary>
    public override string ToString()
    {
        // OS ロケールで小数点が "," にならないよう不変カルチャで整形する（ログ・CSV比較用）
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "n={0} σ={1:F4} kurt={2:F2} |r|acf1={3:F3} racf1={4:F3} maxDD={5:F1}%",
            SampleCount,
            StdDevReturn,
            Kurtosis,
            AbsReturnAutocorr1,
            ReturnAutocorr1,
            MaxDrawdown * 100f);
    }
}

/// <summary>
/// 価格系列（ターンごとの価格）から市場統計量を算出するユーティリティ。
/// 数値計算は double（System.Math）で行い、結果を float に丸めて返す。
/// NaN / Infinity は一切返さない（該当時は 0 にフォールバックする）。
/// </summary>
public static class MarketStatistics
{
    /// <summary>分母などが「実質ゼロ」とみなされる閾値。</summary>
    private const double Epsilon = 1e-12;

    /// <summary>空の対数リターン列（毎回の確保を避けるための共有インスタンス）。</summary>
    private static readonly float[] EmptyReturns = new float[0];

    /// <summary>
    /// 価格系列から対数リターン列 ln(p[i+1] / p[i]) を作る。
    /// 価格が 0 以下の要素はスキップせず、1 にクランプして計算する（0 除算・NaN 回避）。
    /// 要素数が 2 未満なら空配列を返す。
    /// </summary>
    public static float[] LogReturns(IReadOnlyList<int> priceSeries)
    {
        if (priceSeries == null || priceSeries.Count < 2)
        {
            return EmptyReturns;
        }

        int n = priceSeries.Count - 1;
        float[] result = new float[n];
        for (int i = 0; i < n; i++)
        {
            double prev = priceSeries[i] < 1 ? 1.0 : priceSeries[i];
            double next = priceSeries[i + 1] < 1 ? 1.0 : priceSeries[i + 1];
            result[i] = Sanitize(Math.Log(next / prev));
        }
        return result;
    }

    /// <summary>
    /// 価格系列から全統計量を算出する。
    /// サンプル数が足りない場合は算出できない項目を 0 にして返す（例外は投げない）。
    /// </summary>
    public static MarketStats Compute(IReadOnlyList<int> priceSeries)
    {
        MarketStats stats = new MarketStats();

        float[] returns = LogReturns(priceSeries);
        int n = returns.Length;
        stats.SampleCount = n;
        if (n == 0)
        {
            return stats;
        }

        // 平均
        double sum = 0.0;
        for (int i = 0; i < n; i++)
        {
            sum += returns[i];
        }
        double mean = sum / n;
        stats.MeanReturn = Sanitize(mean);

        // 中心モーメント m2 / m4
        double m2 = 0.0;
        double m4 = 0.0;
        for (int i = 0; i < n; i++)
        {
            double d = returns[i] - mean;
            double d2 = d * d;
            m2 += d2;
            m4 += d2 * d2;
        }
        double sumSq = m2; // Σ(r-mean)^2（自己相関の分母に使う）
        m2 /= n;
        m4 /= n;

        // 母標準偏差（n で割る）
        stats.StdDevReturn = n < 2 ? 0f : Sanitize(Math.Sqrt(m2));

        // 非超過尖度（正規分布で 3）
        if (n < 4 || m2 < Epsilon)
        {
            stats.Kurtosis = 0f;
        }
        else
        {
            stats.Kurtosis = Sanitize(m4 / (m2 * m2));
        }

        // リターン自体のラグ1自己相関
        stats.ReturnAutocorr1 = Autocorr1(returns, mean, sumSq, n, false);

        // |リターン| のラグ1自己相関（|r| の平均を引く）
        if (n < 3)
        {
            stats.AbsReturnAutocorr1 = 0f;
        }
        else
        {
            double absSum = 0.0;
            for (int i = 0; i < n; i++)
            {
                absSum += Math.Abs(returns[i]);
            }
            double absMean = absSum / n;

            double absSumSq = 0.0;
            for (int i = 0; i < n; i++)
            {
                double d = Math.Abs(returns[i]) - absMean;
                absSumSq += d * d;
            }
            stats.AbsReturnAutocorr1 = Autocorr1(returns, absMean, absSumSq, n, true);
        }

        // 最大ドローダウン
        stats.MaxDrawdown = ComputeMaxDrawdown(priceSeries);

        return stats;
    }

    /// <summary>
    /// 複数の統計量（複数銘柄・複数ラン）を SampleCount で重み付けして平均する。
    /// 空なら既定値（全項目 0）を返す。SampleCount が 0 の要素は無視する。
    /// </summary>
    public static MarketStats Aggregate(IEnumerable<MarketStats> stats)
    {
        MarketStats result = new MarketStats();
        if (stats == null)
        {
            return result;
        }

        long totalWeight = 0;
        double mean = 0.0;
        double stdDev = 0.0;
        double kurtosis = 0.0;
        double absAcf = 0.0;
        double retAcf = 0.0;
        double maxDd = 0.0;

        foreach (MarketStats s in stats)
        {
            if (s.SampleCount <= 0)
            {
                continue;
            }
            double w = s.SampleCount;
            totalWeight += s.SampleCount;
            mean += SafeDouble(s.MeanReturn) * w;
            stdDev += SafeDouble(s.StdDevReturn) * w;
            kurtosis += SafeDouble(s.Kurtosis) * w;
            absAcf += SafeDouble(s.AbsReturnAutocorr1) * w;
            retAcf += SafeDouble(s.ReturnAutocorr1) * w;
            maxDd += SafeDouble(s.MaxDrawdown) * w;
        }

        if (totalWeight <= 0)
        {
            return result;
        }

        double inv = 1.0 / totalWeight;
        result.SampleCount = totalWeight > int.MaxValue ? int.MaxValue : (int)totalWeight;
        result.MeanReturn = Sanitize(mean * inv);
        result.StdDevReturn = Sanitize(stdDev * inv);
        result.Kurtosis = Sanitize(kurtosis * inv);
        result.AbsReturnAutocorr1 = Sanitize(absAcf * inv);
        result.ReturnAutocorr1 = Sanitize(retAcf * inv);
        result.MaxDrawdown = Sanitize(maxDd * inv);
        return result;
    }

    /// <summary>
    /// ラグ1自己相関を計算する。
    /// Σ_{i=0}^{n-2}(x[i]-mean)(x[i+1]-mean) / Σ_{i=0}^{n-1}(x[i]-mean)^2。
    /// useAbs が true なら |r| の系列として扱う。
    /// n &lt; 3 または分母が極小なら 0。
    /// </summary>
    private static float Autocorr1(float[] returns, double mean, double sumSq, int n, bool useAbs)
    {
        if (n < 3 || sumSq < Epsilon)
        {
            return 0f;
        }

        double numerator = 0.0;
        for (int i = 0; i < n - 1; i++)
        {
            double a = useAbs ? Math.Abs(returns[i]) : returns[i];
            double b = useAbs ? Math.Abs(returns[i + 1]) : returns[i + 1];
            numerator += (a - mean) * (b - mean);
        }
        return Sanitize(numerator / sumSq);
    }

    /// <summary>
    /// 最大ドローダウンを計算する。それまでの高値 peak に対する
    /// (peak - p[i]) / peak の最大値を 0〜1 にクランプして返す。要素数 &lt; 2 なら 0。
    /// </summary>
    private static float ComputeMaxDrawdown(IReadOnlyList<int> priceSeries)
    {
        if (priceSeries == null || priceSeries.Count < 2)
        {
            return 0f;
        }

        double peak = double.NegativeInfinity;
        double maxDd = 0.0;
        for (int i = 0; i < priceSeries.Count; i++)
        {
            double p = priceSeries[i];
            if (p > peak)
            {
                peak = p;
            }
            if (peak <= Epsilon)
            {
                // 高値が 0 以下の間はドローダウンを定義できないのでスキップ。
                continue;
            }
            double dd = (peak - p) / peak;
            if (dd > maxDd)
            {
                maxDd = dd;
            }
        }

        if (maxDd < 0.0)
        {
            maxDd = 0.0;
        }
        else if (maxDd > 1.0)
        {
            maxDd = 1.0;
        }
        return Sanitize(maxDd);
    }

    /// <summary>double を float に変換し、NaN / Infinity なら 0 にフォールバックする。</summary>
    private static float Sanitize(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0f;
        }
        float f = (float)value;
        if (float.IsNaN(f) || float.IsInfinity(f))
        {
            return 0f;
        }
        return f;
    }

    /// <summary>float を double に変換し、NaN / Infinity なら 0 にフォールバックする。</summary>
    private static double SafeDouble(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0.0;
        }
        return value;
    }
}

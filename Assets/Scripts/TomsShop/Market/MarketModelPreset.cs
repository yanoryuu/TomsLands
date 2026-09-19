using UnityEngine;

/// <summary>
/// キャリブレーション済みの市場モデル設定。
///
/// Jev（外部API）で作った「お手本」の統計量へローカルABMを合わせ込んだ結果を、
/// この ScriptableObject に焼いて出荷する。ランタイムはこれを読むだけで、
/// 外部APIへは一切アクセスしない。
///
/// 目標値と達成値を両方記録しておくことで、
/// 「どのお手本に対して、どこまで合ったのか」が後から検証できる。
/// </summary>
[CreateAssetMenu(fileName = "MarketModelPreset", menuName = "ScriptableObjects/Market/MarketModelPreset")]
public class MarketModelPreset : ScriptableObject
{
    [Header("ローカルABM のパラメータ（出荷対象）")]
    public LocalAbmSettings abm = new LocalAbmSettings();

    [Header("キャリブレーションの目標（Jev のお手本統計量）")]
    [Tooltip("尖度。実市場は 5〜10。大きいほど「たまに大暴落」が起きる。")]
    public float targetKurtosis = 4.46f;
    [Tooltip("|リターン| のラグ1自己相関。ボラティリティ・クラスタリング。実市場は 0.1〜0.3。")]
    public float targetAbsAutocorr1 = 0.427f;
    [Tooltip("リターンのラグ1自己相関。予測可能性。実市場はほぼ 0。")]
    public float targetAutocorr1 = -0.066f;
    [Tooltip("対数リターンの標準偏差。ボラティリティ水準。")]
    public float targetStdDev = 0.0079f;

    [Header("キャリブレーションの達成値（ABM の実測）")]
    public float achievedKurtosis;
    public float achievedAbsAutocorr1;
    public float achievedAutocorr1;
    public float achievedStdDev;
    [Tooltip("目標との距離。小さいほど良い。")]
    public float loss;

    [Header("記録")]
    [Tooltip("キャリブレーション実行日時。")]
    public string calibratedAt;
    [Tooltip("お手本を作った Jev の設定（ペルソナ比率・λ・ターン数など）。")]
    [TextArea(3, 8)]
    public string referenceNote =
        "Jev 参照ラン: ペルソナ比率 {Momentum .05 / Contrarian .35 / DemandWatcher .05 / MarketMaker .45 / Noise .10}, " +
        "lambda=0.12, 40ターン×8銘柄, seed=1001";

    /// <summary>目標統計量を <see cref="MarketStats"/> の形で返す。</summary>
    public MarketStats TargetStats()
    {
        return new MarketStats
        {
            Kurtosis = targetKurtosis,
            AbsReturnAutocorr1 = targetAbsAutocorr1,
            ReturnAutocorr1 = targetAutocorr1,
            StdDevReturn = targetStdDev,
        };
    }

    /// <summary>キャリブレーション結果を記録する。</summary>
    public void RecordResult(MarketStats achieved, float lossValue, string timestamp)
    {
        achievedKurtosis = achieved.Kurtosis;
        achievedAbsAutocorr1 = achieved.AbsReturnAutocorr1;
        achievedAutocorr1 = achieved.ReturnAutocorr1;
        achievedStdDev = achieved.StdDevReturn;
        loss = lossValue;
        calibratedAt = timestamp;
    }
}

using UnityEngine;

/// <summary>
/// 注文フロー（買い越し／売り越し）を価格変動率へ変換する市場インパクトモデル。
///
/// Jev のトレーダー判断を使う Editor シミュレーション（JevMarketSimulator）と、
/// 将来ランタイムへ載せるローカル簡易 ABM の両方が同じ式を共有できるよう、
/// 「注文 → 価格」の変換だけをここへ閉じ込める。
///
/// 戻り値は ItemModel.ApplyShopTurnEconomy の s1Rate と同じ意味（1.0 = 据え置き）なので、
/// 既存のストップ高／ストップ安クランプにそのまま接続できる。
/// </summary>
public static class OrderFlowPriceEngine
{
    /// <summary>1ターンの対数価格変化幅の既定上限（暴走防止の安全弁）。</summary>
    public const float DefaultMaxLogMove = 0.35f;

    /// <summary>
    /// 平方根マーケットインパクト（Kyle 型）で純注文を価格変動率へ変換する。
    /// 実市場と同様、注文量に対して価格変化は逓減する（4倍の注文で2倍の変動）。
    /// </summary>
    /// <param name="netOrder">純注文量。正が買い越し、負が売り越し。</param>
    /// <param name="depth">板の厚み。大きいほど値が動きにくい。</param>
    /// <param name="lambda">インパクト係数。大きいほどボラティリティが上がる。</param>
    /// <param name="maxLogMove">1ターンの対数変化幅の上限。</param>
    public static float ToPriceRate(float netOrder, float depth, float lambda,
        float maxLogMove = DefaultMaxLogMove)
    {
        float safeDepth = Mathf.Max(1f, depth);
        float impact = lambda * Mathf.Sign(netOrder) * Mathf.Sqrt(Mathf.Abs(netOrder) / safeDepth);
        impact = Mathf.Clamp(impact, -maxLogMove, maxLogMove);
        return Mathf.Exp(impact);
    }

    /// <summary>
    /// 板の厚み。在庫が多く需要が高いほど値が動きにくい（＝流動性が厚い）。
    /// </summary>
    public static float Depth(int stock, float demand, float baseDepth)
    {
        return baseDepth * (1f + stock) * Mathf.Max(0.1f, demand);
    }
}

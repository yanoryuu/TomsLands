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
    /// <param name="exponent">
    /// インパクトの指数。0.5 が平方根則（実市場の大口執行で観測される形）。
    ///
    /// ただし平方根は大きな注文ほど価格変化を圧縮するため、注文フローが正規分布に
    /// 近いとリターン分布は正規分布より<b>テールの細い</b>形になる（尖度が 2 前後に落ちる）。
    /// ファットテールが欲しい場合は 1.0（線形）に近づける。
    /// </param>
    public static float ToPriceRate(float netOrder, float depth, float lambda,
        float maxLogMove = DefaultMaxLogMove, float exponent = 0.5f)
    {
        float safeDepth = Mathf.Max(1f, depth);
        float normalized = Mathf.Abs(netOrder) / safeDepth;
        float magnitude = Mathf.Approximately(exponent, 0.5f)
            ? Mathf.Sqrt(normalized)
            : Mathf.Pow(normalized, Mathf.Clamp(exponent, 0.1f, 2f));

        float impact = lambda * Mathf.Sign(netOrder) * magnitude;
        impact = Mathf.Clamp(impact, -maxLogMove, maxLogMove);
        return Mathf.Exp(impact);
    }

    /// <summary>
    /// 板の厚み。需要が高いほど流動性が厚く、値が動きにくい。
    /// </summary>
    /// <param name="stock">プレイヤーの在庫数。</param>
    /// <param name="demand">需要（0〜1）。</param>
    /// <param name="baseDepth">基準となる板の厚み。</param>
    /// <param name="stockWeight">
    /// 在庫が板の厚みに与える影響の強さ。既定 0 = 影響なし。
    ///
    /// 在庫を線形に掛けてはいけない: 在庫 0 と 99 で厚みが 100 倍変わり、
    /// 「プレイヤーが仕入れるほどその銘柄の価格が凍る」という不自然な挙動になる。
    /// 相場はプレイヤーの手持ちではなく市場の需要で決まるべきなので、既定では無効。
    /// 効かせたい場合も対数で緩やかに効かせる。
    /// </param>
    public static float Depth(int stock, float demand, float baseDepth, float stockWeight = 0f)
    {
        float stockTerm = stockWeight > 0f
            ? 1f + stockWeight * Mathf.Log(1f + Mathf.Max(0, stock))
            : 1f;

        return baseDepth * stockTerm * Mathf.Max(0.1f, demand);
    }
}

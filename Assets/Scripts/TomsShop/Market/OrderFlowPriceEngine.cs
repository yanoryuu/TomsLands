using UnityEngine;

/// <summary>
/// 注文フロー（買い越し／売り越し）を価格変動率へ変換する市場インパクトモデル。
///
/// 戻り値は ItemModel.ApplyShopTurnEconomy の s1Rate と同じ意味（1.0 = 据え置き）なので、
/// 既存のストップ高／ストップ安クランプにそのまま接続できる。
/// </summary>
public static class OrderFlowPriceEngine
{
    /// <summary>1ターンの対数価格変化幅の既定上限（暴走防止の安全弁）。</summary>
    public const float DefaultMaxLogMove = 0.35f;

    /// <summary>
    /// 純注文を価格変動率へ変換する。
    /// </summary>
    /// <param name="netOrder">純注文量。正が買い越し、負が売り越し。</param>
    /// <param name="depth">板の厚み。大きいほど値が動きにくい。</param>
    /// <param name="lambda">インパクト係数。大きいほどボラティリティが上がる。</param>
    /// <param name="maxLogMove">1ターンの対数変化幅の上限。</param>
    /// <param name="exponent">
    /// インパクトの指数。0.5 が平方根則（実市場の大口執行で観測される形）だが、
    /// 大きな注文ほど価格変化を圧縮するため、リターン分布が正規分布よりテールの細い形になり
    /// 尖度が 2 前後に張り付く（v1 で実測）。ファットテールが欲しいゲーム用途では 1.0 前後を使う。
    /// </param>
    public static float ToPriceRate(float netOrder, float depth, float lambda,
        float maxLogMove = DefaultMaxLogMove, float exponent = 1.0f)
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

    /// <summary>板の厚みに対する需要の効き方。需要 0 でこの倍率、需要 1 で 1.0。</summary>
    public const float DepthDemandFloor = 0.5f;

    /// <summary>
    /// 板の厚み。需要が高いほど流動性が厚く、値が動きにくい。
    ///
    /// 需要の効き方は <see cref="DepthDemandFloor"/>〜1.0 の線形補間。
    /// v1 では max(0.1, demand) だったため、需要 0.05 の銘柄が需要 0.5 の銘柄の 10 倍動いていた。
    /// 2 倍程度に抑えて「不人気銘柄がやや荒れる」程度に留める。
    /// </summary>
    /// <param name="stock">プレイヤーの在庫数。</param>
    /// <param name="demand">需要（0〜1）。</param>
    /// <param name="baseDepth">基準となる板の厚み。</param>
    /// <param name="stockWeight">
    /// 在庫の影響。既定 0 = 影響なし。線形に掛けると在庫 0 と 99 で厚みが 100 倍変わり、
    /// 「プレイヤーが仕入れるほど価格が凍る」不自然な挙動になる。効かせる場合も対数で緩やかに。
    /// </param>
    public static float Depth(int stock, float demand, float baseDepth, float stockWeight = 0f)
    {
        float stockTerm = stockWeight > 0f
            ? 1f + stockWeight * Mathf.Log(1f + Mathf.Max(0, stock))
            : 1f;

        float demandTerm = Mathf.Lerp(DepthDemandFloor, 1f, Mathf.Clamp01(demand));
        return baseDepth * stockTerm * demandTerm;
    }
}

/// <summary>
/// 適正値（層1・ファンダメンタルズ）。需要から「この銘柄が本来あるべき価格」を決める。
///
///   fair = clamp( basePrice × (1 + premium × (demand − 0.5) × 2), floor, ceiling )
///
/// 陳列・戦闘の属性波及・バズ・鑑定・マシン・広告ステータスはすべて需要を動かすので、
/// この1本を通せば既存の全システムが価格へ届く。設計: Docs/Market_Price_v2_Design.md §3
/// </summary>
public static class ShopFairValue
{
    /// <param name="basePrice">マスターデータの基準価格。</param>
    /// <param name="demand">需要（0〜1）。0.5 が中立。</param>
    /// <param name="premium">
    /// 需要が価格を押し上げ／押し下げる強さ。0.5 なら需要 0.8 で 1.3 倍、需要 0.2 で 0.7 倍。
    /// </param>
    /// <param name="floor">ストップ安（絶対値）。</param>
    /// <param name="ceiling">ストップ高（絶対値）。</param>
    public static float Compute(int basePrice, float demand, float premium, float floor, float ceiling)
    {
        float multiplier = 1f + premium * (Mathf.Clamp01(demand) - 0.5f) * 2f;
        float fair = basePrice * Mathf.Max(0.05f, multiplier);
        if (ceiling < floor) ceiling = floor;
        return Mathf.Clamp(fair, floor, ceiling);
    }
}

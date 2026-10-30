/// <summary>
/// 占い師の「上げる枠・下げる枠」の数値（Docs/Prophet_Intervention.md）。<b>仮の値。数値の置き場はここ 1 か所</b>（後でマスタへ移す）。
/// 仕組み（ProphetInterventionModel）・需要の計算（ItemModel.ApplyShopTurnEconomy）・AI（ProphetAutoRule）はすべてここを読む。
/// </summary>
public static class ProphetTuning
{
    // ───────── 占い師の力 ─────────

    /// <summary>力の最大。</summary>
    public const int MaxPower = 2;
    /// <summary>ラン開始時（と旧セーブを読んだとき）の力。</summary>
    public const int StartPower = 2;
    /// <summary>配信を 1 回終えると（途中終了でも）戻る力。</summary>
    public const int PowerPerStream = 1;

    // ───────── 代金・長さ ─────────

    /// <summary>1 回の代金（仮 15,000G。取らないなら 0）。村の酒場の値段（VillageEffects.ApplyProphetPrice）を通す。</summary>
    public const int Cost = 15000;
    /// <summary>入れた日から効く日数（1 日＝朝の更新 1 回）。入れた翌朝から EffectTurns 回の朝に効き、最後の朝に枠から外れる。</summary>
    public const int EffectTurns = 3;

    // ───────── 効き方（ニュースの効果と同じ形） ─────────

    /// <summary>
    /// 需要の均衡点へのバイアス（NewsEffectResolver.TrendBias と同じ単位。ニュースの「中」0.6 と同じ）。上げる枠は +、下げる枠は −。
    /// 枠から外れたら消える（反動の上乗せはしない）。需要はふだんの収束（trendConvergenceRate）で戻る。
    /// </summary>
    public const float TrendBias = 0.6f;

    /// <summary>
    /// 効いている間、適正値（相場が寄っていく先）に掛ける倍率の傾き（適正値 × (1 ± FairPerBias × TrendBias)）。
    /// 需要だけを動かすと 3 日では値段にほとんど出ない（News_Spec §7.3 と同じ理由）ので、ニュースの 2 段目と同じく値段側の均衡点も動かす。
    /// </summary>
    public const float FairPerBias = 0.7f;

    /// <summary>
    /// 入れた翌朝（最初に効く朝）に 1 回だけ需要へ直撃する量（ニュースの DemandKick の「小」と同じ 0.03）。
    /// 入れた日のうちには需要も値段も動かさない（陳列・売り注文の途中で値が変わらないように）。客の数が少し増える／減る程度の控えめな手応え。
    /// </summary>
    public const float DemandKick = 0.03f;

    // ───────── AI（ProphetAutoRule） ─────────

    /// <summary>上げる枠に入れるのは、在庫の値打ち（値段 × 在庫）が代金のこの倍以上の品だけ。</summary>
    public const float AutoUpMinStockValueRate = 2f;
    /// <summary>下げる枠に入れるのは、在庫の値打ちが代金のこの倍未満の品だけ（もう持っている品を下げない）。</summary>
    public const float AutoDownMaxStockValueRate = 1f;
    /// <summary>代金を払ったあとに残しておく所持金（仕入れの元手）。</summary>
    public const int AutoReserveMoney = 30000;
}

/// <summary>
/// 金融商品の種別。
/// Bond      = ギルド債（資金ロック・満期に元本+利息で償還）
/// IndexFund = インデックスファンド（口数保有・基準価額は構成銘柄の市場価格連動・いつでも解約）
/// ※ 将来の拡張（証券型配当・空売り等）はここに追加する。
/// </summary>
public enum FinancialProductKind
{
    Bond,
    IndexFund,
}

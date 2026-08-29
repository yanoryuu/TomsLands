using System;

/// <summary>
/// レリックが補正できる数値の識別子。
/// 「①Modifier（常時パッシブ）は StatId を増やす、②Hook はイベントを増やす」の2軸だけで
/// 拡張する（条件型・ルール書き換えに専用機構を作らない）。
/// ※ enum は末尾追加のみ（セーブ・配信データの互換のため並び替え禁止）。
/// </summary>
public enum RelicStatId
{
    /// <summary>営業売上への倍率（SalesCalculator）。</summary>
    ShopRevenueMul,
    /// <summary>需要下限への加算（ApplyShopTurnEconomy）。</summary>
    DemandFloorAdd,
    /// <summary>借金返済額への倍率（DebtPresenter）。軽減は高レア限定を推奨。</summary>
    DebtAmountMul,
    /// <summary>同時陳列銘柄数への加算（店レベル上限に上乗せ）。</summary>
    DisplayKindsAdd,
    /// <summary>仕入れ価格への倍率（未配線・将来用。表示価格との一元化が前提）。</summary>
    ProcurementCostMul,
    /// <summary>バズ発生確率への加算%（未配線・将来用）。</summary>
    BuzzChanceAdd,
    /// <summary>配当収入への倍率（未配線・将来用）。</summary>
    DividendMul,
}

public enum RelicOp
{
    /// <summary>基準値に加算。</summary>
    Add,
    /// <summary>倍率として乗算（1.0 が中立）。</summary>
    Mul,
}

/// <summary>
/// レリック1個が持つ数値補正。JsonUtility 安全（リモート配信可）。
/// 計算順は (base + ΣAdd) × ΠMul。
/// </summary>
[Serializable]
public class RelicModifier
{
    public RelicStatId stat;
    public RelicOp op = RelicOp.Add;
    public float value;
}

/// <summary>
/// 特殊効果（C#実装）への参照。behaviourKey で RelicBehaviourRegistry から解決する。
/// </summary>
[Serializable]
public class RelicBehaviourRef
{
    public string behaviourKey;
    public float param;
}

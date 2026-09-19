using UnityEngine;

// =====================================================================
// ショップのターン価格変動を差し替え可能にするための境界。
//
// ItemModel.ApplyShopTurnEconomy は「1ターンの価格変動率」を求める部分だけを
// このインターフェースへ委譲する。需要・流行度の更新、ストップ高／安のクランプ、
// 広告ステータスの Trust(Floor底上げ) / Spread(需要増幅) / Followers(需要下限)は
// 従来どおり ItemModel 側に残る。
//
// 既定は LegacyShopPriceEngine で、従来の実装と**完全に同一**の結果を返す。
// =====================================================================

/// <summary>
/// 価格変動率を求めるのに必要な情報一式。
/// </summary>
public readonly struct ShopPriceContext
{
    public readonly RuntimeItemData Item;
    public readonly ItemData Master;
    public readonly ShopEconomySettings Settings;

    /// <summary>
    /// 案A2 Attention の増幅係数（1.0 = 無補正）。上振れ側を強める。
    /// </summary>
    public readonly float AttentionFactor;

    /// <summary>
    /// 案A4 Retention の安定化強度（0 = 無補正）。変動率を 1.0 へ引き寄せる Lerp の t。
    /// </summary>
    public readonly float RetentionStability;

    public ShopPriceContext(RuntimeItemData item, ItemData master, ShopEconomySettings settings,
        float attentionFactor, float retentionStability)
    {
        Item = item;
        Master = master;
        Settings = settings;
        AttentionFactor = attentionFactor;
        RetentionStability = retentionStability;
    }
}

/// <summary>
/// 1ターンの価格変動率を決めるエンジン。
/// </summary>
public interface IShopPriceEngine
{
    /// <summary>
    /// このターンの計算を始める前に一度だけ呼ばれる。エンジン側の状態更新に使う。
    /// </summary>
    void BeginTurn();

    /// <summary>
    /// 価格変動率を返す。1.0 が据え置き、1.02 なら 2% 上昇。
    /// 戻り値はこの後 ItemModel 側でストップ高／安にクランプされる。
    /// </summary>
    float GetPriceRate(in ShopPriceContext context);
}

/// <summary>
/// 従来の価格変動（需要帯ごとの一様乱数）。
///
/// 既存セーブとの互換と、ABM との比較基準のために、
/// 元の ApplyShopTurnEconomy と 1 命令ずつ同じ順序・同じ乱数消費で計算する。
/// ここを書き換えると過去のバランス調整が全部ずれるので注意。
/// </summary>
public sealed class LegacyShopPriceEngine : IShopPriceEngine
{
    public void BeginTurn() { }

    public float GetPriceRate(in ShopPriceContext context)
    {
        var settings = context.Settings;
        var item = context.Item;

        float s1Min, s1Max;
        if (item.Demand.Value >= settings.highDemandThreshold)
        {
            s1Min = settings.highDemandPriceRateMin;
            s1Max = settings.highDemandPriceRateMax * context.AttentionFactor;
        }
        else if (item.Demand.Value <= settings.lowDemandThreshold)
        {
            s1Min = settings.lowDemandPriceRateMin;
            s1Max = settings.attentionAffectsLowDemand
                ? settings.lowDemandPriceRateMax * context.AttentionFactor
                : settings.lowDemandPriceRateMax;
        }
        else
        {
            s1Min = settings.normalDemandPriceRateMin;
            s1Max = settings.normalDemandPriceRateMax * context.AttentionFactor;
        }

        // 安全: Attention 増幅で min と max が逆転しないようガード
        if (s1Max < s1Min) s1Max = s1Min;

        float rate = Random.Range(s1Min, s1Max);
        return Mathf.Lerp(rate, 1f, context.RetentionStability);
    }
}

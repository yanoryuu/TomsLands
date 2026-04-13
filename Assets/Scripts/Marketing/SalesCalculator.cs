using UnityEngine;

/// <summary>
/// 売上計算ヘルパー。
/// 基本売上にバズ倍率・フォロワーボーナスを適用した最終売上を計算する。
/// 
/// 【既存システムとの連携】
/// - TurnEndSummaryPresenter で売上計算時に使用する。
/// - ItemModel.SimulateShopSales() の結果に倍率を適用する形で統合する。
/// - BuzzSystem から売上倍率を取得する。
/// - FollowerSystem から売上ボーナス率を取得する。
/// </summary>
public class SalesCalculator
{
    private readonly BuzzSystem _buzzSystem;
    private readonly FollowerSystem _followerSystem;

    /// <summary>
    /// コンストラクタ。VContainer から注入する。
    /// </summary>
    public SalesCalculator(BuzzSystem buzzSystem, FollowerSystem followerSystem)
    {
        _buzzSystem = buzzSystem;
        _followerSystem = followerSystem;
    }

    /// <summary>
    /// 基本売上に各種ボーナスを適用した最終売上を計算する。
    /// 
    /// 計算式:
    ///   最終売上 = 基本売上 × バズ売上倍率 × (1 + フォロワー売上ボーナス率)
    /// 
    /// 例: 基本売上 1000G × バズ倍率 1.5 × (1 + 0.15) = 1725G
    /// </summary>
    /// <param name="baseRevenue">基本売上（アイテム販売のシミュレーション結果）</param>
    /// <returns>ボーナス適用後の最終売上</returns>
    public int CalculateFinalRevenue(int baseRevenue)
    {
        // バズによる売上倍率（バズが非アクティブなら 1.0）
        float buzzMultiplier = _buzzSystem != null ? _buzzSystem.GetCurrentRevenueMultiplier() : 1f;

        // フォロワーマイルストーンによる売上ボーナス率（例: 0.15 = 15%UP）
        float followerBonusRate = _followerSystem != null ? _followerSystem.GetSalesBonusRate() : 0f;

        // 最終売上 = 基本売上 × バズ倍率 × (1 + フォロワーボーナス率)
        float finalRevenue = baseRevenue * buzzMultiplier * (1f + followerBonusRate);

        int result = Mathf.Max(0, Mathf.RoundToInt(finalRevenue));

        Debug.Log($"[SalesCalculator] 売上計算: " +
                  $"基本={baseRevenue}G × バズ倍率={buzzMultiplier:F2} × フォロワーボーナス={(1f + followerBonusRate):F2} " +
                  $"= {result}G");

        return result;
    }

    /// <summary>
    /// 現在の売上倍率の合計を取得する（UI表示用）。
    /// </summary>
    public float GetTotalMultiplier()
    {
        float buzzMultiplier = _buzzSystem != null ? _buzzSystem.GetCurrentRevenueMultiplier() : 1f;
        float followerBonusRate = _followerSystem != null ? _followerSystem.GetSalesBonusRate() : 0f;

        return buzzMultiplier * (1f + followerBonusRate);
    }

    /// <summary>
    /// 売上の内訳情報を取得する（UI表示・デバッグ用）。
    /// </summary>
    public SalesBreakdown GetBreakdown(int baseRevenue)
    {
        float buzzMultiplier = _buzzSystem != null ? _buzzSystem.GetCurrentRevenueMultiplier() : 1f;
        float followerBonusRate = _followerSystem != null ? _followerSystem.GetSalesBonusRate() : 0f;

        return new SalesBreakdown
        {
            BaseRevenue = baseRevenue,
            BuzzMultiplier = buzzMultiplier,
            FollowerBonusRate = followerBonusRate,
            FinalRevenue = CalculateFinalRevenue(baseRevenue)
        };
    }
}

/// <summary>
/// 売上の内訳情報を格納するクラス（UI表示・デバッグ用）。
/// </summary>
public class SalesBreakdown
{
    /// <summary>基本売上</summary>
    public int BaseRevenue;

    /// <summary>バズによる売上倍率</summary>
    public float BuzzMultiplier;

    /// <summary>フォロワーボーナス率</summary>
    public float FollowerBonusRate;

    /// <summary>最終売上</summary>
    public int FinalRevenue;

    public override string ToString()
    {
        return $"基本: {BaseRevenue}G × バズ: {BuzzMultiplier:F2} × フォロワー: {(1f + FollowerBonusRate):F2} = {FinalRevenue}G";
    }
}


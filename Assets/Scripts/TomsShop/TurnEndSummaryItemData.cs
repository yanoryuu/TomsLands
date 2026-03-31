/// <summary>
/// ターン終了サマリーの1行分のデータ
/// </summary>
public class TurnEndSummaryItemData
{
    public string ItemName { get; set; }
    public int SoldCount { get; set; }
    public int Revenue { get; set; }
    public int Price { get; set; }
    public float Demand { get; set; }
    public DemandTrend Trend { get; set; }
}

/// <summary>
/// 需要トレンドの方向
/// </summary>
public enum DemandTrend
{
    Down,
    Flat,
    Up
}


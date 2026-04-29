using UnityEngine;

public class TurnEndSummaryItemData
{
    public string ItemName { get; set; }
    public int SoldCount { get; set; }
    public int Revenue { get; set; }
    public int Price { get; set; }
    public float Demand { get; set; }
    public DemandTrend Trend { get; set; }
    public Sprite ItemIcon { get; set; }
    public DemandChangeCause Cause { get; set; }
    public float DemandDelta { get; set; }
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

/// <summary>
/// 需要変動の主因
/// </summary>
public enum DemandChangeCause
{
    Stable,       // 安定（変動なし）
    Displayed,    // 陳列効果で上昇
    TrendUp,      // 流行が後押し
    TrendDown,    // 流行逆風で下落
}



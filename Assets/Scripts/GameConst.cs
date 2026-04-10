using UnityEngine;

public class GameConst
{
    public const int MaxDungeonLevel = 5;
    
    public const int MaxBlackSmithLevel = 5;
    
    public const int MaxToolShopLevel = 5;
    
    public const int MaxInfoBrokerLevel = 5;

    public const int MaxItemStock = 99;
    
    public const int MinItemStock = 0;
    
    public const int InitMoney = 10000;

    /// <summary>
    /// 鍛冶屋レベルアップコスト（インデックス = 現在レベル → 次のレベルへの費用）
    /// Lv1→2: 3000G, Lv2→3: 6000G, Lv3→4: 12000G, Lv4→5: 20000G
    /// </summary>
    public static readonly int[] BlackSmithLevelUpCosts = { 0, 3000, 6000, 12000, 20000 };

    /// <summary>
    /// 鍛冶屋の現在レベルからレベルアップコストを取得。最大レベルなら -1 を返す。
    /// </summary>
    public static int GetBlackSmithLevelUpCost(int currentLevel)
    {
        if (currentLevel <= 0 || currentLevel >= MaxBlackSmithLevel)
            return -1;
        return BlackSmithLevelUpCosts[currentLevel];
    }
}

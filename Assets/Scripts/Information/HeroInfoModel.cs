using System.Collections.Generic;

public class HeroInfoModel
{
    private HeroRuntimeData _heroRuntimeData;
    
    public List<int> phurchaseCosts { get; private set; }
    
    public int currentPurchaseIndex { get; private set; }
    
    public HeroInfoModel()
    {
        phurchaseCosts = new List<int> { 100, 200, 300, 400, 500 };
        currentPurchaseIndex = 0;
    }
    
    //TODO: 確率計算ロジックを実装(計算式待ち)
    private void CalculateProbabilities()
    {
        
    }
}

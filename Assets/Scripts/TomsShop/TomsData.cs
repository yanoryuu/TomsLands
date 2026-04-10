using System;

[Serializable]
public class TomsData
{
    public int shopMoney;

    public int blacksmithLevel;
    
    public int infoBrokerLevel;
    
    public int currentTurn;
    
    public int gameFlowIndex;

    public TomsData(int shopMoney, int blacksmithLevel, int currentTurn, int gameFlowIndex = 0, int infoBrokerLevel = 1)
    {
        this.shopMoney = shopMoney;
        this.blacksmithLevel = blacksmithLevel;
        this.infoBrokerLevel = infoBrokerLevel;
        this.currentTurn = currentTurn;
        this.gameFlowIndex = gameFlowIndex;
    }
}

using System;

[Serializable]
public class TomsData
{
    public int shopMoney;

    public int blacksmithLevel;
    
    public int currentTurn;

    public TomsData(int shopMoney,int blacksmithLevel,int currentTurn)
    {
        this.shopMoney = shopMoney;
        this.blacksmithLevel = blacksmithLevel;
        this.currentTurn = currentTurn;
    }
}

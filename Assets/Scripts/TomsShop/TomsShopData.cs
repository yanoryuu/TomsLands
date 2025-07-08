using System;

[Serializable]
public class TomsShopData
{
    public int shopMoney;

    public int blacksmithLevel;
    
    public int currentTurn;

    public TomsShopData(int shopMoney,int blacksmithLevel,int currentTurn)
    {
        this.shopMoney = shopMoney;
        this.blacksmithLevel = blacksmithLevel;
        this.currentTurn = currentTurn;
    }
}

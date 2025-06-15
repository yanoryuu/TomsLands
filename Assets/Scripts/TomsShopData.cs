using System;

[Serializable]
public class TomsShopData
{
    public int shopMoney;

    // public int blacksmithLevel;

    public TomsShopData(int shopMoney)
    {
        this.shopMoney = shopMoney;
        // this.blacksmithLevel = blacksmithLevel;
    }
}

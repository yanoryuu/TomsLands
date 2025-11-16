
using System;

[System.Serializable]
public class HeroPurchaseHistory
{
    public string itemId;
    public DateTime purchaseDate;
    public int quantity;
    public int priceAtPurchase;
    public bool wasEquipped;

    public HeroPurchaseHistory(string itemId, int quantity, int price)
    {
        this.itemId = itemId;
        this.quantity = quantity;
        this.priceAtPurchase = price;
        this.purchaseDate = DateTime.Now;
        this.wasEquipped = false;
    }
}
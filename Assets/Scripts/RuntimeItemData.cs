using R3;

[System.Serializable]
public class RuntimeItemData
{
    public string ItemId { get; private set; }
    public ReactiveProperty<int> CurrentPrice { get; private set; }
    public ReactiveProperty<int> Stock { get; private set; }

    public RuntimeItemData(string itemId, int currentPrice, int stock)
    {
        ItemId = itemId;
        CurrentPrice = new ReactiveProperty<int>(currentPrice);
        Stock = new ReactiveProperty<int>(stock);
    }

    public RuntimeItemData(RuntimeItemDataPlain plainData)
    {
        ItemId = plainData.itemId;
        CurrentPrice = new ReactiveProperty<int>(plainData.currentPrice);
        Stock = new ReactiveProperty<int>(plainData.stock);
    }

    public RuntimeItemDataPlain ToPlainData()
    {
        return new RuntimeItemDataPlain
        {
            itemId = ItemId,
            currentPrice = CurrentPrice.Value,
            stock = Stock.Value
        };
    }
}

[System.Serializable]
public class RuntimeItemDataPlain
{
    public string itemId;
    public int currentPrice;
    public int stock;
}
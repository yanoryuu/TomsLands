using System.Collections.Generic;
using UnityEngine;

public class StreamingItemModel
{
    public List<RuntimeItemData> RuntimeStreamingItems { get; private set; }　= new();

    private ItemModel itemModel;

    public StreamingItemModel(ItemModel itemModel)
    {
        this.itemModel = itemModel;
    }
    
    public void LoadData(List<RuntimeItemData> itemDataList)
    {
        RuntimeStreamingItems = itemDataList;
        
        Debug.Log("Item data loaded.");
    }
}

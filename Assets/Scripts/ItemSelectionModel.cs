using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class ItemSelectionModel
{
    private List<RuntimeItemData> displayItemList = new();

    public IReadOnlyList<RuntimeItemData> DisplayItemList => displayItemList.AsReadOnly();

    public void SetSelection(List<RuntimeItemData> items)
    {
        displayItemList = items;
    }

    public void SaveSelection()
    {
        var dataList = new DisplayItemDataList(
            displayItemList.Select(d => new DisplayItemData(d.ItemId, d.Stock.Value)).ToList()
        );

        string json = JsonUtility.ToJson(dataList, true);
        File.WriteAllText(Application.persistentDataPath + "/displayItemData.json", json);
        Debug.Log("Display item list saved.");
    }

    public List<RuntimeItemData> LoadSelection(List<ItemData> masterItems)
    {
        string path = Application.persistentDataPath + "/displayItemData.json";
        if (!File.Exists(path))
        {
            Debug.Log("No display item data found.");
            displayItemList.Clear();
            return displayItemList;
        }

        string json = File.ReadAllText(path);
        var dataList = JsonUtility.FromJson<DisplayItemDataList>(json);

        displayItemList = dataList.displayItems
            .Select(d =>
            {
                var master = masterItems.FirstOrDefault(m => m.itemId == d.itemId);
                if (master != null)
                {
                    return new RuntimeItemData(
                        d.itemId,
                        master.basePrice,
                        master.maxStock,
                        d.stock,
                        master.itemIcon,
                        master.itemType,
                        Random.Range(0.3f, 0.7f)
                    );
                }
                else
                {
                    Debug.LogWarning($"Master item not found: {d.itemId}");
                    return null;
                }
            })
            .Where(d => d != null)
            .ToList();

        Debug.Log("Display item list loaded.");
        return displayItemList;
    }
}

[System.Serializable]
public class DisplayItemData
{
    public string itemId;
    public int stock;

    public DisplayItemData(string itemId, int stock)
    {
        this.itemId = itemId;
        this.stock = stock;
    }
}

[System.Serializable]
public class DisplayItemDataList
{
    public List<DisplayItemData> displayItems;

    public DisplayItemDataList(List<DisplayItemData> displayItems)
    {
        this.displayItems = displayItems;
    }
}
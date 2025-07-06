using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// 最大6つまで選択可能なアイテム＋数量を管理し、
/// JSON で永続化するモデル。
/// </summary>
public class StreamingSettingModel
{
    private const int MaxSelection = 6;
    private const string FileName = "streamingSelection.json";
    private readonly Dictionary<string, int> _selected = new Dictionary<string, int>(MaxSelection);

    public IReadOnlyDictionary<string, int> Selected => _selected;

    public bool TryAdd(string id)
    {
        if (_selected.Count >= MaxSelection || _selected.ContainsKey(id))
            return false;
        _selected[id] = 1;
        return true;
    }

    public void SetQuantity(string id, int qty)
    {
        if (_selected.ContainsKey(id))
            _selected[id] = Mathf.Max(1, qty);
    }

    public void Remove(string id) => _selected.Remove(id);
    public void Clear() => _selected.Clear();

    public void SaveData()
    {
        var plains = _selected.Select(kvp => new StreamingSelectionItemPlain { itemId = kvp.Key, quantity = kvp.Value }).ToList();
        var dto = new StreamingSelectionPlain(plains);
        var json = JsonUtility.ToJson(dto, true);
        File.WriteAllText(Path.Combine(Application.persistentDataPath, FileName), json);
        Debug.Log("Streaming selection saved.");
    }

    public void LoadData()
    {
        var path = Path.Combine(Application.persistentDataPath, FileName);
        if (!File.Exists(path))
        {
            Clear();
            return;
        }
        var json = File.ReadAllText(path);
        var dto = JsonUtility.FromJson<StreamingSelectionPlain>(json);
        _selected.Clear();
        foreach (var item in dto.items)
            _selected[item.itemId] = Mathf.Max(1, item.quantity);
        Debug.Log("Streaming selection loaded.");
    }

    public List<StreamingItemPlain> GetSelectedRuntimeItemData(ItemModel itemModel)
    {
        var selectedRuntimeItems = new List<StreamingItemPlain>();
        foreach (var item in _selected)
        {
            var itemData = itemModel.GetRuntimeItem(item.Key);
            var streamingItemData = new StreamingItemPlain(item.Key,itemData.CurrentPrice.Value,itemData.ItemIcon,item.Value,itemData.Demand.Value);
            selectedRuntimeItems.Add(streamingItemData);
        }
        return selectedRuntimeItems;
    }
}

[Serializable]
public class StreamingSelectionItemPlain
{
    public string itemId;
    public int quantity;
}

/// <summary>
/// JSON 永続化用: 配信設定で選択されたアイテムの一覧
/// </summary>
[Serializable]
public class StreamingSelectionPlain
{
    public List<StreamingSelectionItemPlain> items;
    public StreamingSelectionPlain(List<StreamingSelectionItemPlain> items) => this.items = items;
}
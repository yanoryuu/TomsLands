using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class HeroModel
{
    public List<string> EquippedItemIds { get; private set; } = new List<string>();

    public RuntimeHeroData heroData { get; private set; }
    public void EquipItem(string itemId)
    {
        if (!EquippedItemIds.Contains(itemId))
        {
            EquippedItemIds.Add(itemId);
        }
    }

    public void UnequipItem(string itemId)
    {
        EquippedItemIds.Remove(itemId);
    }

    public void ClearEquippedItems()
    {
        EquippedItemIds.Clear();
    }

    public void SaveHeroData()
    {
        string json = JsonUtility.ToJson(heroData, true);
        File.WriteAllText(Application.persistentDataPath + "/heroData.json", json);
        Debug.Log("Hero data saved.");
    }

    public void LoadHeroData()
    {
        string path = Application.persistentDataPath + "/heroData.json";
        if (!File.Exists(path))
        {
            InitializeRuntimeHeroFromMaster();
            return;
        }

        string json = File.ReadAllText(path);
        var dataList = JsonUtility.FromJson<RuntimeHeroData>(json);
        heroData = dataList;
    }

    public void InitializeRuntimeHeroFromMaster()
    {
        
    }
}


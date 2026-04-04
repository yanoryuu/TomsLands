using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class HeroModel
{
    public List<string> EquippedItemIds { get; private set; } = new List<string>();

    public RuntimeHeroData heroData { get; private set; }

    public HeroModel()
    {
        LoadHeroData();
    }

    /// <summary>
    /// バトルシーン用。ロード済みのヒーローデータに装備だけを上書きする。
    /// </summary>
    public void ApplyEquippedItems(IEnumerable<string> equippedItemIds)
    {
        EquippedItemIds = equippedItemIds != null
            ? new List<string>(equippedItemIds)
            : new List<string>();
    }

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
        var saveData = heroData.ToSaveData();
        string json = JsonUtility.ToJson(saveData, true);
        File.WriteAllText(Application.persistentDataPath + "/heroData.json", json);
        Debug.Log($"[HeroModel] Hero data saved. Lv={saveData.level}, HP={saveData.hp}, AT={saveData.attackPower}, DF={saveData.defensePower}");
    }

    public void LoadHeroData()
    {
        InitializeRuntimeHeroFromMaster();
    }

    public void InitializeRuntimeHeroFromMaster()
    {
        var loader = new HeroLevelDataLoader();
        loader.LoadFromCSV("HeroStatusData");

        var levelData = loader.GetLevelData(1);
        if (levelData != null)
        {
            heroData = RuntimeHeroData.CreateFromLevelData(levelData);
            Debug.Log($"[HeroModel] Initialized hero from CSV: Lv={levelData.Level}, HP={levelData.MaxHp}, AT={levelData.Attack}, DF={levelData.Defense}");
        }
        else
        {
            heroData = RuntimeHeroData.CreateDefault();
            Debug.LogWarning("[HeroModel] HeroStatusData のレベル1が見つかりません。デフォルト値を使用します。");
        }
    }
}

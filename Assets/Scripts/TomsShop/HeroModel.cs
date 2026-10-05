using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class HeroModel
{
    public List<string> EquippedItemIds { get; private set; } = new List<string>();

    public RuntimeHeroData heroData { get; private set; }
    private const string HeroDataFileName = "heroData.json";

    public HeroModel()
    {
        LoadHeroData();
    }

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
        File.WriteAllText(GetSavePath(), json);
        Debug.Log($"[HeroModel] Hero data saved. Lv={saveData.level}, EXP={saveData.experience}/{saveData.expToNextLevel}, HP={saveData.hp}, AT={saveData.attackPower}, DF={saveData.defensePower}");
    }

    public void LoadHeroData()
    {
        string path = GetSavePath();
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            var saveData = JsonUtility.FromJson<HeroSaveData>(json);
            heroData = RuntimeHeroData.CreateFromSaveData(saveData);
            SyncEquippedItemsFromHeroData();
            Debug.Log($"[HeroModel] Hero data loaded. Lv={heroData.level.Value}, EXP={heroData.experience.Value}/{heroData.expToNextLevel.Value}");
            return;
        }

        InitializeRuntimeHeroFromMaster();
        SaveHeroData();
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
            Debug.LogWarning("[HeroModel] HeroStatusData level 1 was not found. Using default values.");
        }

        SyncEquippedItemsFromHeroData();
    }

    public int AddExperience(int experience)
    {
        if (heroData == null || experience <= 0) return 0;

        var loader = new HeroLevelDataLoader();
        loader.LoadFromCSV("HeroStatusData");
        int maxLevel = loader.GetMaxLevel();
        int levelUps = 0;

        heroData.experience.Value += experience;
        while (heroData.level.Value < maxLevel &&
               heroData.experience.Value >= GameConst.GetHeroExpToNextLevel(heroData.level.Value))
        {
            int required = GameConst.GetHeroExpToNextLevel(heroData.level.Value);
            heroData.experience.Value -= required;
            heroData.level.Value++;
            ApplyLevelData(loader.GetLevelData(heroData.level.Value));
            levelUps++;
        }

        heroData.expToNextLevel.Value = heroData.level.Value < maxLevel
            ? GameConst.GetHeroExpToNextLevel(heroData.level.Value)
            : 0;

        if (heroData.level.Value >= maxLevel)
        {
            heroData.experience.Value = 0;
        }

        SaveHeroData();
        Debug.Log($"[HeroModel] Gained {experience} EXP. Lv={heroData.level.Value}, EXP={heroData.experience.Value}/{heroData.expToNextLevel.Value}, LevelUps={levelUps}");
        return levelUps;
    }

    /// <summary>
    /// 経験値に関係なく、指定回数ぶんレベルを上げる（最大レベルで頭打ち）。
    /// 配信（ダンジョン挑戦）後の「最低保証レベルアップ」用。
    /// 持ち越し経験値は新レベルの必要量未満に丸める（保証直後の連続レベルアップ防止）。
    /// 実際に上がった回数を返し、変化があれば保存する。
    /// </summary>
    public int ForceLevelUp(int count)
    {
        if (heroData == null || count <= 0) return 0;

        var loader = new HeroLevelDataLoader();
        loader.LoadFromCSV("HeroStatusData");
        int maxLevel = loader.GetMaxLevel();
        int levelUps = 0;

        while (levelUps < count && heroData.level.Value < maxLevel)
        {
            heroData.level.Value++;
            ApplyLevelData(loader.GetLevelData(heroData.level.Value));
            levelUps++;
        }

        if (levelUps == 0) return 0;

        if (heroData.level.Value >= maxLevel)
        {
            heroData.experience.Value = 0;
            heroData.expToNextLevel.Value = 0;
        }
        else
        {
            int required = GameConst.GetHeroExpToNextLevel(heroData.level.Value);
            heroData.expToNextLevel.Value = required;
            heroData.experience.Value = Mathf.Clamp(heroData.experience.Value, 0, Mathf.Max(0, required - 1));
        }

        SaveHeroData();
        Debug.Log($"[HeroModel] Forced {levelUps} level up(s). Lv={heroData.level.Value}, EXP={heroData.experience.Value}/{heroData.expToNextLevel.Value}");
        return levelUps;
    }

    private void ApplyLevelData(HeroLevelData levelData)
    {
        if (levelData == null) return;

        heroData.hp.Value = levelData.MaxHp;
        heroData.attackPower.Value = levelData.Attack;
        heroData.defensePower.Value = levelData.Defense;
    }

    private string GetSavePath()
    {
        return SaveSlotManager.GetPath(HeroDataFileName);
    }

    private void SyncEquippedItemsFromHeroData()
    {
        EquippedItemIds.Clear();
        if (heroData == null) return;

        if (!string.IsNullOrEmpty(heroData.weaponId.Value))
        {
            EquippedItemIds.Add(heroData.weaponId.Value);
        }

        if (!string.IsNullOrEmpty(heroData.armorId.Value))
        {
            EquippedItemIds.Add(heroData.armorId.Value);
        }
    }
}

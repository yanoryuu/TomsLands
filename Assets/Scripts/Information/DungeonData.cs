
using System.Collections.Generic;

[System.Serializable]
public class DungeonData
{
    public string dungeonId;
    public string dungeonName;
    public int recommendedLevel;
    public ItemTypeData.ItemAttribute requiredAttribute;
    public int difficulty; // 1-10
   

    public DungeonData(string id, string name, int level, ItemTypeData.ItemAttribute attr, int diff)
    {
        dungeonId = id;
        dungeonName = name;
        recommendedLevel = level;
        requiredAttribute = attr;
        difficulty = diff;
        
    }
}
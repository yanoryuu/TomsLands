using System.Collections.Generic;
using UnityEngine;

public class MapModel
{
    public Dictionary<DungeonName,bool> isDungeonInfoUnlocked { get; private set; } = new();
    public MapModel()
    {
        
    }

    public void SetDungeonInfoUnlocked(DungeonName dungeonName, bool isUnlocked)
    {
        isDungeonInfoUnlocked[dungeonName] = isUnlocked;
    }
}

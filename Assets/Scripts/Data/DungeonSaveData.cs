using System;
using System.Collections.Generic;

[Serializable]
public class DungeonSaveData
{
    public string dungeonKey;
    public int currentDungeonLevel;
}

[Serializable]
public class GameSaveData
{
    public List<DungeonSaveData> dungeons = new();
}

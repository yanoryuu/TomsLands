using System;
using System.Collections.Generic;

[Serializable]
public class GameSaveData
{
    public List<DungeonSaveData> dungeons = new();
    public int CurrentDay = 1;
    public int Gold = 0;
}
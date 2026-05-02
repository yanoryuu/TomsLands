using System;
using System.Collections.Generic;

[Serializable]
public class DungeonSaveData
{
    public string dungeonKey;
    public int currentDungeonLevel;
    public DungeonStatus dungeonStatus;
    public bool isShowedInfo;
}



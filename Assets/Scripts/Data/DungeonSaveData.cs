using System;

[Serializable]
public class DungeonSaveData
{
    public string dungeonKey;
    public int currentDungeonLevel;
    public DungeonStatus dungeonStatus;
    public bool isShowedInfo;
    /// <summary>
    /// レベルの数え方。0 = 旧形式（未出資が Lv1）、1 = 未出資が Lv0（2026-10）。
    /// 旧形式の Lv1 は「出資していない」ので Lv0 として読む（DungeonRepository.TryLoadAndRebuild）。
    /// </summary>
    public int levelVersion;
}



using System.Collections.Generic;
using UnityEngine;

public class MapInfoModel
{
    public List<DungeonData> _availableDungeons { get; private set; }
    
    public MapInfoModel()
    {
        
    }

    public void LoadDungeonData()
    {
        if (_availableDungeons == null)
        {
            Debug.Log("Already loaded dungeon data");
            return;
        }
        
        var dungeons = DungeonRepository.Instance.GetAll();
        
        List<DungeonData> dungeonDataList = new List<DungeonData>();
        foreach (var data in dungeons)
        {
            dungeonDataList.Add(data);
        }
        
        _availableDungeons = dungeonDataList;
    }
}

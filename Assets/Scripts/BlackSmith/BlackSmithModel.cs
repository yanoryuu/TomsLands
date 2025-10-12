using System.Collections.Generic;
public class BlackSmithModel
{
    
    public List<RuntimeItemData> armorRuntimeItems{get;private set;}
    public List<RuntimeItemData> weaponRuntimeItems{get;private set;}
    
    public Dictionary<string,int> ItemCount {get;private set;}
    
    public BlackSmithModel()
    {
        
        armorRuntimeItems = new List<RuntimeItemData>();
        weaponRuntimeItems  = new List<RuntimeItemData>();
    }

    public void SetRuntimeItems(List<RuntimeItemData> armorRuntimeItems, List<RuntimeItemData> weaponRuntimeItems)
    {
        armorRuntimeItems = armorRuntimeItems;
        weaponRuntimeItems = weaponRuntimeItems;
    }
}

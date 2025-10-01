using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class ItemSelectionModel
{
   public List<RuntimeItemData> AarmorRuntimeItems{get;private set;}
   public List<RuntimeItemData> WeaponRuntimeItems{get;private set;}
   
   public ItemSelectionModel()
   {
      AarmorRuntimeItems = new List<RuntimeItemData>();
      WeaponRuntimeItems  = new List<RuntimeItemData>();
   }

   public void SetRuntimeItems(List<RuntimeItemData> armorRuntimeItems, List<RuntimeItemData> weaponRuntimeItems)
   {
      AarmorRuntimeItems = armorRuntimeItems;
      WeaponRuntimeItems = weaponRuntimeItems;
   }
}
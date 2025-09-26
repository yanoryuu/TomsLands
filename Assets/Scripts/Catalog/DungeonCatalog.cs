using System.Collections.Generic;
using UnityEngine;

public sealed class DungeonCatalog : IDungeonCatalog
{
    private readonly Dictionary<DungeonName, DungeonInfoScriptableObj> map;

    public DungeonCatalog(IEnumerable<DungeonInfoScriptableObj> dungeonInfos)
    {
        map = new Dictionary<DungeonName, DungeonInfoScriptableObj>();
        if (dungeonInfos == null) return;

        foreach (var so in dungeonInfos)
        {
            if (so == null) continue;

            if (map.ContainsKey(so.key))
            {
                Debug.LogWarning($"[DungeonCatalogService] Duplicate key: {so.key}");
                continue;
            }
            map.Add(so.key, so);
        }
    }

    public DungeonInfoScriptableObj GetDungeon(DungeonName key)
        => map.TryGetValue(key, out var so) ? so : null;
}
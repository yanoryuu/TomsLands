using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class DungeonCatalog : IDungeonCatalog
{
    [SerializeField] private List<DungeonInfoScriptableObj> dungeonInfos = new();

    private Dictionary<string, DungeonInfoScriptableObj> map;

    private void Awake()
    {
        BuildMap();
    }

    private void BuildMap()
    {
        map = new Dictionary<string, DungeonInfoScriptableObj>();
        if (dungeonInfos == null) return;

        foreach (var so in dungeonInfos)
        {
            if (so == null || string.IsNullOrWhiteSpace(so.dungeonId)) continue;

            if (map.ContainsKey(so.dungeonId))
            {
                Debug.LogWarning($"[DungeonCatalog] Duplicate dungeonId: {so.dungeonId}");
                continue;
            }
            map.Add(so.dungeonId, so);
        }
    }

    public DungeonInfoScriptableObj GetDungeon(string dungeonId)
    {
        if (string.IsNullOrWhiteSpace(dungeonId) || map == null) return null;
        return map.TryGetValue(dungeonId, out var so) ? so : null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Editor 上での重複／未設定チェック
        var seen = new HashSet<string>();
        foreach (var so in dungeonInfos)
        {
            if (so == null) continue;

            if (string.IsNullOrWhiteSpace(so.dungeonId))
            {
                Debug.LogWarning($"[DungeonCatalog] dungeonId empty on asset: {so.name}");
                continue;
            }

            if (!seen.Add(so.dungeonId))
            {
                Debug.LogWarning($"[DungeonCatalog] Duplicate dungeonId in list: {so.dungeonId}");
            }
        }
    }
#endif
}
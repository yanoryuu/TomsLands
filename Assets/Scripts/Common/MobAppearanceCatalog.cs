using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// モブ（客・村人）の見た目候補となる SPUM ユニットプレハブの一覧。
/// CustomerManager / VillagerCharacter がここからランダムに選んで見た目を差し替える。
/// 中身は Tools > TomsLands > SPUM > モブを生成して配置 で再生成できる（SpumMobGenerator）。
/// </summary>
[CreateAssetMenu(fileName = "MobAppearanceCatalog", menuName = "TomsLands/Mob Appearance Catalog")]
public class MobAppearanceCatalog : ScriptableObject
{
    [Tooltip("モブの見た目に使う SPUM ユニット（Assets/SPUM/Resources/Units 以下のプレハブ）")]
    [SerializeField] private List<SPUM_Prefabs> units = new();

    public IReadOnlyList<SPUM_Prefabs> Units => units;

    /// <summary>exclude に含まれないものからランダムに1つ選ぶ。全部使用中なら exclude を無視して選ぶ。</summary>
    public SPUM_Prefabs PickRandom(ICollection<SPUM_Prefabs> exclude = null)
    {
        if (units == null || units.Count == 0) return null;

        var candidates = new List<SPUM_Prefabs>(units.Count);
        foreach (var u in units)
        {
            if (u == null) continue;
            if (exclude != null && exclude.Contains(u)) continue;
            candidates.Add(u);
        }
        if (candidates.Count == 0)
        {
            foreach (var u in units) if (u != null) candidates.Add(u);
        }
        return candidates.Count == 0 ? null : candidates[Random.Range(0, candidates.Count)];
    }

#if UNITY_EDITOR
    public void EditorSetUnits(List<SPUM_Prefabs> list) => units = new List<SPUM_Prefabs>(list);
#endif
}

/// <summary>
/// シーン上の SPUM ユニット（子オブジェクト）を別の SPUM ユニットプレハブに差し替える。
/// 位置・回転・指定スケール・レイヤー・SortingGroup／ソーティングレイヤーを引き継ぐ。
/// </summary>
public static class SpumAppearanceUtility
{
    public static SPUM_Prefabs Replace(SPUM_Prefabs current, SPUM_Prefabs prefab, Vector3 baseLocalScale)
    {
        if (current == null || prefab == null) return current;

        Transform oldT = current.transform;
        GameObject go = Object.Instantiate(prefab.gameObject, oldT.parent, false);
        go.name = prefab.name;
        Transform newT = go.transform;
        newT.SetSiblingIndex(oldT.GetSiblingIndex());
        newT.localPosition = oldT.localPosition;
        newT.localRotation = oldT.localRotation;
        newT.localScale = baseLocalScale;

        int layer = current.gameObject.layer;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

        var oldGroup = current.GetComponent<SortingGroup>();
        var newGroup = go.GetComponent<SortingGroup>();
        if (oldGroup != null && newGroup != null)
        {
            newGroup.sortingLayerID = oldGroup.sortingLayerID;
            newGroup.sortingOrder = oldGroup.sortingOrder;
        }

        // 配置先でパーツのソーティングレイヤーを変えている場合に合わせる
        var oldRenderer = current.GetComponentInChildren<SpriteRenderer>(true);
        if (oldRenderer != null)
        {
            int layerId = oldRenderer.sortingLayerID;
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.sortingLayerID != layerId) sr.sortingLayerID = layerId;
        }

        current.gameObject.SetActive(false);
        Object.Destroy(current.gameObject);
        return go.GetComponent<SPUM_Prefabs>();
    }
}

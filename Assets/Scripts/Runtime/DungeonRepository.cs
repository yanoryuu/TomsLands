using System.Collections.Generic;
using UnityEngine;

public class DungeonRepository : MonoBehaviour
{
    // 初期ビルド時のマスター（SO）
    [SerializeField] private List<DungeonInfoScriptableObj> dungeonInfos;

    // 実行時データ（SOの完全コピー + currentDungeonLevel）
    public List<DungeonData> availableDungeons { get; private set; } = new();

    // カタログは FromSave 時に必要
    private IDungeonCatalog catalog; 

    private void Awake()
    {
        // セーブがあればロード復元、無ければ SO から初期化
        if (!TryLoadAndRebuild())
        {
            InitializeFromScriptables(dungeonInfos);
            // 初回保存（不要なら削除可）
            Save();
        }
    }

    public void SetCatalog(IDungeonCatalog catalog)
    {
        this.catalog = catalog;
    }

    public void InitializeFromScriptables(List<DungeonInfoScriptableObj> sources)
    {
        availableDungeons = new List<DungeonData>();
        if (sources == null) return;

        var seen = new HashSet<string>();
        foreach (var so in sources)
        {
            if (so == null || string.IsNullOrWhiteSpace(so.dungeonId)) continue;
            if (!seen.Add(so.dungeonId))
            {
                Debug.LogWarning($"[DungeonRepository] Duplicate dungeonId skipped: {so.dungeonId}");
                continue;
            }
            availableDungeons.Add(new DungeonData(so));
        }
    }

    // ランタイム → 保存
    public void Save() => SaveSystem.Save(availableDungeons);

    // 保存 → ランタイム（SO を注入し直し、currentDungeonLevel のみ上書き）
    public bool TryLoadAndRebuild()
    {
        if (!SaveSystem.TryLoad(out var save)) return false;
        if (catalog == null)
        {
            Debug.LogError("[DungeonRepository] Catalog is null.");
            return false;
        }

        var rebuilt = new List<DungeonData>();
        var seen = new HashSet<string>();

        // セーブにあるダンジョンを優先復元
        foreach (var sd in save.dungeons)
        {
            if (sd == null || string.IsNullOrWhiteSpace(sd.dungeonId)) continue;

            var so = catalog.GetDungeon(sd.dungeonId);
            if (so == null)
            {
                Debug.LogWarning($"[DungeonRepository] SO not found for id in save: {sd.dungeonId}");
                continue;
            }
            var d = new DungeonData(so);
            d.currentDungeonLevel = sd.currentDungeonLevel; // ここだけセーブ値で上書き
            rebuilt.Add(d);
            seen.Add(sd.dungeonId);
        }

        // 新規に追加されたSO（セーブに未登録の分）も含める
        foreach (var so in dungeonInfos)
        {
            if (so == null || string.IsNullOrWhiteSpace(so.dungeonId)) continue;
            if (seen.Contains(so.dungeonId)) continue;

            rebuilt.Add(new DungeonData(so)); // SOの既定値（currentDungeonLevel含む）
        }

        availableDungeons = rebuilt;
        return true;
    }

    // 任意：アプリ停止/中断時の自動保存
    private void OnApplicationPause(bool pause)
    {
        if (pause) Save();
    }

    private void OnApplicationQuit()
    {
        Save();
    }
}
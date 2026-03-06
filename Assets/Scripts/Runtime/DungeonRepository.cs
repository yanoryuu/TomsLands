// csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using System.Linq;

public class DungeonRepository : SingletonMonoBehaviour<DungeonRepository>
{
    // 初期ビルド時のマスター（SO）
    [SerializeField] private List<DungeonInfoScriptableObj> dungeonInfos;

    // 実行時データ（SOの完全コピー + currentDungeonLevel）
    public List<DungeonData> availableDungeons { get; private set; } = new();

    // カタログは FromSave 時に必要
    private IDungeonCatalog catalog;

    private void Awake()
    {
        // カタログが設定されていなければ作成してセット（TryLoadAndRebuild が catalog を使うため）
        if (catalog == null)
        {
            SetCatalog(CreateCatalog());
        }

        // セーブがあればロード復元、無ければ SO から初期化
        if (!TryLoadAndRebuild())
        {
            InitializeFromScriptables(dungeonInfos);
            // 初回保存（不要なら削除可）
            Save();
        }
    }

    public DungeonCatalog CreateCatalog()
    {
        var catalog = new DungeonCatalog(dungeonInfos);
        return catalog;
    }

    public void SetCatalog(IDungeonCatalog catalog)
    {
        this.catalog = catalog;
    }

    public void InitializeFromScriptables(List<DungeonInfoScriptableObj> sources)
    {
        availableDungeons = new List<DungeonData>();
        if (sources == null) return;

        var seen = new HashSet<DungeonName>();
        foreach (var so in sources)
        {
            if (so == null) continue;
            if (!seen.Add(so.key))
            {
                Debug.LogWarning($"[DungeonRepository] Duplicate dungeonId skipped: {so.key}");
                continue;
            }
            availableDungeons.Add(new DungeonData(so));
        }
    }

    // ランタイム → 保存
    public void Save() => SaveSystem.Save(availableDungeons);

    // 保存 → ランタイム（SO を再注入し、currentDungeonLevel だけ上書き）
    public bool TryLoadAndRebuild()
    {
        if (!SaveSystem.TryLoad(out var save)) return false;
        if (catalog == null)
        {
            Debug.LogError("[DungeonRepository] Catalog is null.");
            return false;
        }

        var rebuilt = new List<DungeonData>();
        var seen = new HashSet<DungeonName>();

        // セーブにあるダンジョンを優先復元
        foreach (var sd in save.dungeons)
        {
            if (sd == null || string.IsNullOrWhiteSpace(sd.dungeonKey)) continue;

            // 文字列 → enum へ安全にパース（順序変更に強い）
            if (!Enum.TryParse<DungeonName>(sd.dungeonKey, out var key))
            {
                Debug.LogWarning($"[DungeonRepository] Unknown dungeonKey in save: {sd.dungeonKey}");
                continue;
            }

            var so = catalog.GetDungeon(key);
            if (so == null)
            {
                Debug.LogWarning($"[DungeonRepository] SO not found for key in save: {key}");
                continue;
            }

            if (!seen.Add(key)) continue;

            var d = new DungeonData(so);
            d.currentDungeonLevel = sd.currentDungeonLevel; // ここだけセーブ値で上書き
            rebuilt.Add(d);
        }

        // セーブに無い新規SOも取り込む
        foreach (var so in dungeonInfos)
        {
            if (so == null) continue;
            if (seen.Contains(so.key)) continue;

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

    // すべて（読み取り専用）
    public ReadOnlyCollection<DungeonData> GetAll()
        => availableDungeons.AsReadOnly();

    // idで1件
    public DungeonData GetById(DungeonName key)
        => availableDungeons.FirstOrDefault(d => d.key == key);

    // 安全取得
    public bool TryGetById(DungeonName key, out DungeonData data)
    {
        data = availableDungeons.FirstOrDefault(d => d.key == key);
        return data != null;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 魔王側の配当（防衛報酬）をダンジョンごとに累計して残す（slot_N/demonPayoutHistory.json）。
/// 配当通知書の回収倍率ハンコを「今回 ÷ 支援額」ではなく「受取累計 ÷ 支援額」にするために使う（SUMMARY E-9）。
/// 書き込むのは帰還処理（BattleResultHandler）だけ。配信シーンは読むだけで、今回分は足して表示する。
/// ラン内のデータなので RunSaveCleaner の削除対象に入っている。
/// </summary>
public static class DemonPayoutHistory
{
    public const string FileName = "demonPayoutHistory.json";

    [Serializable]
    private class Entry
    {
        public string dungeonKey;
        public int totalReward;
    }

    [Serializable]
    private class SaveData
    {
        public List<Entry> entries = new();
    }

    /// <summary>このダンジョンでこれまでに受け取った防衛報酬の合計（今回分は含まない）。</summary>
    public static int GetTotal(DungeonName dungeon)
    {
        var data = Load();
        string key = dungeon.ToString();
        foreach (var e in data.entries)
            if (e != null && e.dungeonKey == key) return Mathf.Max(0, e.totalReward);
        return 0;
    }

    /// <summary>今回受け取った防衛報酬を累計に足して保存する。</summary>
    public static void Record(DungeonName dungeon, int reward)
    {
        if (reward <= 0) return;
        var data = Load();
        string key = dungeon.ToString();
        var entry = data.entries.Find(e => e != null && e.dungeonKey == key);
        if (entry == null)
        {
            entry = new Entry { dungeonKey = key };
            data.entries.Add(entry);
        }
        entry.totalReward = Mathf.Max(0, entry.totalReward) + reward;
        if (!SaveFile.WriteAtomic(SaveSlotManager.GetPath(FileName), JsonUtility.ToJson(data, true)))
            Debug.LogWarning("[DemonPayoutHistory] 防衛報酬の累計を保存できませんでした。");
    }

    private static SaveData Load()
        => SaveFile.TryReadJson(SaveSlotManager.GetPath(FileName), out SaveData data) && data?.entries != null
            ? data
            : new SaveData();
}

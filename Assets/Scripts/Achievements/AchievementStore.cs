using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 実績の手元の記録（スロットに依存しない。persistentDataPath/achievements.json）。
/// Steam が無いときも「解除したつもり」をここに残し、次に Steam につながったときに Steam へ送る。
/// </summary>
[Serializable]
public class AchievementGlobalData
{
    public List<string> unlocked = new();          // API 名
    public List<StatEntry> stats = new();
    public List<string> subscribedCompanies = new(); // 全紙購読（ラン跨ぎで数える）

    [Serializable]
    public class StatEntry
    {
        public string name;
        public int value;
    }

    public int GetStat(string name)
    {
        foreach (var s in stats) if (s.name == name) return s.value;
        return 0;
    }

    public void SetStat(string name, int value)
    {
        foreach (var s in stats)
        {
            if (s.name != name) continue;
            s.value = value;
            return;
        }
        stats.Add(new StatEntry { name = name, value = value });
    }
}

/// <summary>
/// 1ラン内で数える実績の途中経過（slot_N/achievementRun.json）。ラン内ファイルなので RunSaveCleaner が消す。
/// シーンを跨いでも、途中で終了して続きから再開しても数え直さないようにファイルに持つ。
/// </summary>
[Serializable]
public class AchievementRunData
{
    // クリアを数えたか（リザルトで終了して続きから戻っても二重に数えない）
    public bool clearReported;

    // 1日の入金（売れた分だけ。配当・レリック換金などは数えない）
    public int incomeTurn = -1;
    public int incomeSum;

    // バズの連続日数
    public int lastBuzzTurn = -1;
    public int buzzStreak;

    // 二枚舌
    public bool heroSideProfit;
    public bool demonSideProfit;

    // 大穴: 配信に出る日のクリア見込み
    public int departureTurn = -1;
    public float departureClearPct = -1f;

    // 収集家: このランで手にしたレリック（入れ替えても減らない）
    public List<string> relicsSeen = new();

    // 読み的中: スクラップした記事の効き先と、貼った時点の値段
    public List<ScrapWatch> scrapWatches = new();

    [Serializable]
    public class ScrapWatch
    {
        public string entryKey;
        public int effectTurn;
        public int settleTurn;
        public List<string> itemIds = new();
        public List<int> pinnedPrices = new();
    }
}

public static class AchievementStore
{
    public const string GlobalFileName = "achievements.json";
    public const string RunFileName = "achievementRun.json";

    /// <summary>
    /// 手元記録の置き場所。エディタの自動テストプレイ（DevRootOverride）中はその隔離フォルダに置き、
    /// 本物の記録にも Steam にも触れない。
    /// </summary>
    public static string GlobalPath
    {
        get
        {
#if UNITY_EDITOR
            if (IsSandboxed) return Path.Combine(SaveSlotManager.DevRootOverride, GlobalFileName);
#endif
            return Path.Combine(Application.persistentDataPath, GlobalFileName);
        }
    }

    /// <summary>自動テストプレイの隔離中か（このときは Steam に送らない）。</summary>
    public static bool IsSandboxed
    {
        get
        {
#if UNITY_EDITOR
            return !string.IsNullOrEmpty(SaveSlotManager.DevRootOverride);
#else
            return false;
#endif
        }
    }

    public static AchievementGlobalData LoadGlobal()
    {
        var path = GlobalPath;
        if (File.Exists(path) && SaveFile.TryReadJson<AchievementGlobalData>(path, out var data))
        {
            data.unlocked ??= new List<string>();
            data.stats ??= new List<AchievementGlobalData.StatEntry>();
            data.subscribedCompanies ??= new List<string>();
            return data;
        }
        return new AchievementGlobalData();
    }

    public static void SaveGlobal(AchievementGlobalData data)
    {
        var path = GlobalPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        SaveFile.WriteAtomic(path, JsonUtility.ToJson(data, true));
    }

    public static AchievementRunData LoadRun()
    {
        var path = SaveSlotManager.GetPath(RunFileName);
        if (File.Exists(path) && SaveFile.TryReadJson<AchievementRunData>(path, out var data))
        {
            data.relicsSeen ??= new List<string>();
            data.scrapWatches ??= new List<AchievementRunData.ScrapWatch>();
            return data;
        }
        return new AchievementRunData();
    }

    public static void SaveRun(AchievementRunData data) =>
        SaveFile.WriteAtomic(SaveSlotManager.GetPath(RunFileName), JsonUtility.ToJson(data, true));

    public static void DeleteGlobal() => SaveFile.DeleteWithBackups(GlobalPath);
}

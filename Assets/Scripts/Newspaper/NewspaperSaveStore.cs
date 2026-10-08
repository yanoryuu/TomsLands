using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 新聞まわりのラン内セーブ（Docs/News_Spec.md §11）。
/// 購読契約・スクラップ・名鑑・社の実測的中率・既読フラグを1ファイルにまとめる。
/// 発行カレンダーはシードから再現できるので保存しない。
///
/// <b>runSeed が今の周のシードと違えば読み捨てる</b>。新規ランでファイルの削除が漏れても
/// 前の周の購読やスクラップが持ち越されないようにするため（RunSaveCleaner にも登録してある）。
/// </summary>
public static class NewspaperSaveStore
{
    public const string FileName = "newspaperData.json";

    [Serializable]
    private class SaveData
    {
        public int runSeed;
        public List<NewspaperSubscriptionModel.Contract> contracts = new();
        public List<ScrapbookModel.Scrap> pinned = new();
        public List<ScrapbookModel.Scrap> archive = new();
        public List<ScrapbookModel.CompanyRecord> records = new();
        public List<string> readIds = new();
    }

    public static void Save(int runSeed, NewspaperSubscriptionModel subscriptions,
        ScrapbookModel scrapbook, NewsModel news)
    {
        try
        {
            var data = new SaveData
            {
                runSeed = runSeed,
                contracts = subscriptions?.ToPlain() ?? new(),
                pinned = scrapbook?.PinnedToPlain() ?? new(),
                archive = scrapbook?.ArchiveToPlain() ?? new(),
                records = scrapbook?.RecordsToPlain() ?? new(),
                readIds = news?.ReadIdsToPlain() ?? new(),
            };
            File.WriteAllText(SaveSlotManager.GetPath(FileName), JsonUtility.ToJson(data, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NewspaperSaveStore] 保存に失敗しました: {ex.Message}");
        }
    }

    /// <summary>読み込む。ファイルが無い・シードが違う場合は空の状態にする。</summary>
    public static void Load(int runSeed, NewspaperSubscriptionModel subscriptions,
        ScrapbookModel scrapbook, NewsModel news)
    {
        subscriptions?.Clear();
        scrapbook?.Clear();

        SaveData data = null;
        try
        {
            string path = SaveSlotManager.GetPath(FileName);
            if (File.Exists(path)) data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NewspaperSaveStore] 読み込みに失敗しました: {ex.Message}");
        }

        if (data == null || data.runSeed != runSeed)
        {
            news?.ReadIdsFromPlain(null);
            return;
        }

        subscriptions?.FromPlain(data.contracts);
        scrapbook?.FromPlain(data.pinned, data.archive, data.records);
        news?.ReadIdsFromPlain(data.readIds);
    }
}

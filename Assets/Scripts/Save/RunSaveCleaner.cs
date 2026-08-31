using System.IO;
using UnityEngine;

/// <summary>
/// 「1ランぶんのセーブデータ」を選択中スロットからまとめて削除する共通ヘルパー。
/// ニューゲーム開始時(GameLifecycleHandler)・ランクリア時(ResultPresenter)・
/// 破産時(GameOverPresenter)の3箇所から呼ばれ、削除対象の一覧をここに一元化する。
/// ※ ラン外に持ち越すファイル(将来の metaData.json 等)はこのリストに入れないこと。
/// </summary>
public static class RunSaveCleaner
{
    // ラン内データのファイル一覧（真実の源）
    private static readonly string[] RunFiles =
    {
        "itemData.json",
        "tomsData.json",
        "displayItemData.json",
        "heroData.json",
        "streamingSelection.json",
        "shopStatusData.json",
        "sellOrderData.json",
        "portfolioData.json",
        "shopMachineData.json",
        "relics.json",
    };

    /// <summary>
    /// 選択中スロット配下のラン内セーブを全て削除する。
    /// ダンジョン進行(save.json/.tmp/.bak)は SaveSystem.Delete() に委譲する。
    /// </summary>
    public static void DeleteRunFiles()
    {
        string dir = SaveSlotManager.CurrentRoot;

        foreach (var filename in RunFiles)
        {
            string path = Path.Combine(dir, filename);
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"[RunSaveCleaner] Deleted save file: {filename}");
            }
        }

        // ダンジョン進行(save.json)は SaveSystem が .tmp/.bak を含めて管理している
        SaveSystem.Delete();
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 情報屋のデータ（ダンジョンの情報の値段と購入）。
/// 以前あった「うわさ」（装備・ダンジョンの見立てメッセージ）と勇者情報の購入は、画面から使われていなかったので消した（2026-10 グループ I-13）。
/// </summary>
public class InfoBrokerModel
{
    public List<DungeonData> availableDungeons { get; private set; } = new();

    private readonly DungeonRepository dungeonRepository;
    private readonly Dictionary<DungeonName, int[]> dungeonInfoCosts;

    // 引数の ItemModel / HeroModel は DI の登録を変えないために残している（使っていない）
    public InfoBrokerModel(ItemModel itemModel, DungeonRepository dungeonRepository, HeroModel heroModel)
    {
        this.dungeonRepository = dungeonRepository;
        dungeonInfoCosts = LoadDungeonInfoCostsFromCSV("DungeonInfoCosts");
        InitializeDungeons();
    }

    /// <summary>
    /// ダンジョンの情報料を CSV から読む。
    /// 形式: DungeonName,Cost1,Cost2,Cost3（配信の回ごとの値段）
    /// </summary>
    private Dictionary<DungeonName, int[]> LoadDungeonInfoCostsFromCSV(string csvFileName)
    {
        var result = new Dictionary<DungeonName, int[]>();

        TextAsset csvFile = AddressableLoader.Load<TextAsset>(csvFileName);
        if (csvFile == null)
        {
            Debug.LogError($"CSV file not found: {csvFileName}");
            return result;
        }

        string[] lines = csvFile.text.Split('\n');

        // 1行目は見出し
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] values = line.Split(',');
            if (values.Length < 2) continue;

            if (!Enum.TryParse<DungeonName>(values[0].Trim(), out var dungeonName))
            {
                Debug.LogWarning($"Unknown DungeonName in CSV row {i}: {values[0]}");
                continue;
            }

            var costs = new List<int>();
            for (int j = 1; j < values.Length; j++)
            {
                if (int.TryParse(values[j].Trim(), out int cost))
                {
                    costs.Add(cost);
                }
            }

            result[dungeonName] = costs.ToArray();
        }

        Debug.Log($"Loaded {result.Count} dungeon info costs from CSV.");
        return result;
    }

    /// <summary>ダンジョンごとの情報料（配信の回ごと）。</summary>
    public Dictionary<DungeonName, int[]> GetDungeonInfoCosts() => dungeonInfoCosts;

    /// <summary>そのダンジョンの情報を売っているか（CSV の情報料テーブルに載っているか）。</summary>
    public bool IsDungeonInfoSellable(DungeonName dungeonName) =>
        dungeonInfoCosts.TryGetValue(dungeonName, out var costs) && costs != null && costs.Length > 0;

    /// <summary>その日の地図の値段。納付の期日が来るたびに上がる（GameConst.GetMapInfoCost）。</summary>
    public int GetDungeonInfoCost(int day) => GameConst.GetMapInfoCost(DebtCalculator.CountDueCycles(day));

    /// <summary>ダンジョンの情報を買う（isShowedInfo を true にして保存する）。支払いは呼び出し側。</summary>
    public void PurchaseDungeonInfo(DungeonName dungeonName)
    {
        var dungeon = dungeonRepository.GetById(dungeonName);
        if (dungeon == null)
        {
            Debug.LogWarning($"Dungeon not found: {dungeonName}");
            return;
        }

        dungeon.isShowedInfo = true;
        // 購入直後に永続化（保存しないと「続きから」で情報が未購入に戻る）
        dungeonRepository.Save();
        Debug.Log($"Purchased dungeon info: {dungeonName}");
    }

    // ダンジョンデータを DungeonRepository から取得
    public void InitializeDungeons()
    {
        if (dungeonRepository != null && dungeonRepository.availableDungeons != null)
        {
            availableDungeons = new List<DungeonData>(dungeonRepository.availableDungeons);
            Debug.Log($"[InfoBrokerModel] {availableDungeons.Count} 件のダンジョンを読み込みました");
        }
        else
        {
            availableDungeons = new List<DungeonData>();
            Debug.LogWarning("[InfoBrokerModel] DungeonRepository が null または空です");
        }
    }
}
